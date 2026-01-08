using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Utilities;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using Hexa.NET.SDL3;
using Hexa.NET.ImGui.Backends.SDL3;
namespace Alpha.Gui;

public unsafe class ImGuiWrapper : IDisposable {
    private readonly Hexa.NET.SDL3.SDLWindow* window;
    private readonly uint windowId;
    private readonly NativeContext context;
    private readonly GL gl;
    private readonly ImGuiContext* imguiContext;
    private readonly Vector3 backgroundColor;

    public bool Exiting;

    public Vector2 WindowPos {
        get {
            int x;
            int y;
            SDL.GetWindowPosition(this.window, &x, &y);
            return new Vector2(x, y);
        }
    }
    public Vector2 WindowSize {
        get {
            int w;
            int h;
            SDL.GetWindowSize(this.window, &w, &h);
            return new Vector2(w, h);
        }
    }

    public ImGuiWrapper(Config config, string iniPath) {
        //SDL.SetHint(SDL.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");
        SDL.Init(SDLInitFlags.Events | SDLInitFlags.Video);
        const SDLWindowFlags flags = SDLWindowFlags.Opengl
                                  | SDLWindowFlags.Resizable
                                  | SDLWindowFlags.AllowHighdpi;

        this.window = SDL.CreateWindow("Alpha",
            (int) config.WindowSize.X, (int) config.WindowSize.Y,
            flags);
        this.windowId = SDL.GetWindowID(this.window);//this.sdl.GetWindowID(this.window);

        this.context = new NativeContext(this.window);
        this.gl = new GL(this.context);

        this.imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(this.imguiContext);
        ImGuiImplSDL3.SetCurrentContext(this.imguiContext);

        // Apply user themes
        switch (config.Theme) {
            case UiTheme.Light: {
                ImGui.StyleColorsLight();
                this.backgroundColor = new Vector3(0.85f, 0.85f, 0.85f);
                break;
            }

            case UiTheme.Dark:
            default: {
                ImGui.StyleColorsDark();
                this.backgroundColor = new Vector3(0.15f, 0.15f, 0.15f);
                break;
            }
        }

        if (config.BackgroundColor is { } bg) this.backgroundColor = bg;

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        if (config.EnableDocking) io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.IniFilename = (byte*) Marshal.StringToHGlobalAnsi(iniPath + "\0");
        
        // These 3 fields are required for text to render properly
        ImFontConfig imFontConfig = new ImFontConfig() {
            FontDataOwnedByAtlas = 1,
            OversampleV = 1,
            OversampleH = 1,
            GlyphMaxAdvanceX = Single.MaxValue,
            RasterizerDensity = 1f,
            RasterizerMultiply = 1f,
            EllipsisChar = 0,
        };

        imFontConfig.FontLoaderFlags |= (uint) ImGuiFreeTypeLoaderFlags.LoadColor;
        imFontConfig.FontLoaderFlags |= (uint) ImGuiFreeTypeLoaderFlags.Bitmap;

        // Apply user fonts
        // ImFontConfig cannot be changed within functions anymore
        // Glyph ranges are no longer required and it will just use the first font loaded with the corresponding glyphs
        foreach (var font in config.ExtraFonts) {
            if (File.Exists(font.Path) && imFontConfig.MergeMode == 1) {
                io.Fonts.AddFontFromFileTTF(font.Path, font.Size, &imFontConfig);
            } else {
                io.Fonts.AddFontFromFileTTF(font.Path, font.Size, &imFontConfig);
                imFontConfig.MergeMode = 1;
            }
        }

        // Fallback fonts
        io.Fonts.AddFontDefault(&imFontConfig);
        imFontConfig.MergeMode = 1;
        
        // In case the user doesn't provide a font with Japanese glyphs, let's add one for them
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
            const string cjkFont = "C:/Windows/Fonts/msgothic.ttc";
            if (File.Exists(cjkFont)) {
                io.Fonts.AddFontFromFileTTF(cjkFont, 13f, &imFontConfig);
            }
        }

        ImGuiImplSDL3.InitForOpenGL(new SDLWindowPtr((Hexa.NET.ImGui.Backends.SDL3.SDLWindow*) this.window), (void*) this.context.Handle);

        ImGuiImplOpenGL3.SetCurrentContext(ImGui.GetCurrentContext());
        ImGuiImplOpenGL3.Init((string) null!);

        ImGuiImplOpenGL3.NewFrame();
    }

    public void DoEvents() {
        Hexa.NET.SDL3.SDLEvent @event;
        SDL.PumpEvents();
        while (SDL.PollEvent(&@event) == true) {
            var type = (SDLEventType) @event.Type;
            if (type == SDLEventType.WindowCloseRequested) {
                var windowEvent = @event.Window;
                if (windowEvent.WindowID == this.windowId) {
                    this.Exiting = true;
                }
            }

            ImGuiImplSDL3.ProcessEvent((Hexa.NET.ImGui.Backends.SDL3.SDLEvent*) &@event);
        }
    }

    public void Render(Action draw) {
        ImGui.SetCurrentContext(this.imguiContext);
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        draw();

        SDL.GLMakeCurrent(this.window, this.context.Handle);
        this.gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);

        this.gl.ClearColor(this.backgroundColor.X, this.backgroundColor.Y, this.backgroundColor.Z, 1);
        // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
        this.gl.Clear(GLClearBufferMask.ColorBufferBit | GLClearBufferMask.DepthBufferBit);

        ImGui.Render();
        ImGui.EndFrame();

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

        SDL.GLSwapWindow(this.window);
        SDL.GLSetSwapInterval(1);
    }

    public void Dispose() {
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGuiImplSDL3.SetCurrentContext(null);
        ImGuiImplOpenGL3.SetCurrentContext(null);
        ImGui.SetCurrentContext(null);
        ImGui.DestroyContext(this.imguiContext);

        this.context.Dispose();
        SDL.DestroyWindow(this.window);
        SDL.Quit();
    }

    public nint CreateTexture(byte[] data, int width, int height) {
        // Swap ARGB to RGBA
        data = data.ToArray();
        for (var i = 0; i < data.Length; i += 4) {
            (data[i], data[i + 2]) = (data[i + 2], data[i]);
        }

        fixed (byte* dataPtr = data) {
            var texture = this.gl.GenTexture();
            this.gl.BindTexture(GLTextureTarget.Texture2D, texture);

            this.gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter,
                (int) GLTextureMinFilter.Linear);
            this.gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter,
                (int) GLTextureMagFilter.Linear);

            this.gl.PixelStorei(GLPixelStoreParameter.UnpackRowLength, 0);
            this.gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, width, height, 0,
                GLPixelFormat.Rgba,
                GLPixelType.UnsignedByte, (nint) dataPtr);

            return (nint) texture;
        }
    }

    public void DestroyTexture(nint texture) {
        this.gl.DeleteTexture((uint) texture);
    }

    private class NativeContext(Hexa.NET.SDL3.SDLWindow* window) : IGLContext {
        private SDLGLContext glContext = SDL.GLCreateContext(window);
        public nint Handle => this.glContext.Handle;
        public bool IsCurrent => SDL.GLGetCurrentContext() == this.glContext;

        public void Dispose() {
            SDL.GLDestroyContext(this.glContext);
        }

        public bool TryGetProcAddress(string procName, out nint procAddress) {
            procAddress = (nint) SDL.GLGetProcAddress(procName);
            return procAddress != 0;
        }

        public nint GetProcAddress(string procName)
            => (nint) SDL.GLGetProcAddress(procName);

        public bool IsExtensionSupported(string extensionName)
            => SDL.GLExtensionSupported(extensionName) != false;

        public void MakeCurrent() => SDL.GLMakeCurrent(window, this.glContext);
        public void SwapBuffers() => SDL.GLSwapWindow(window);
        public void SwapInterval(int interval) => SDL.GLSetSwapInterval(interval);
    }
}
