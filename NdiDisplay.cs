using BigGustave;
using NewTek;
using NewTek.NDI;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tractus.Ndi.Monitor;

public class NdiDisplay
{
    private IWindow? window;
    private GL? gl;

    private nint receiverPtr;
    private nint frameSyncPtr;

    private uint fbo;
    private uint fboTexture;
    public uint shader;
    private uint vao;
    private uint vbo;

    private uint ndiTexture;
    private Matrix4x4 ndiMatrix;

    private int flipYUniformLocation;
    public int transformUniformLocation;
    private int textureSizeUniformLocation;

    private uint RenderWidth { get; set; }
    private uint RenderHeight { get; set; }
    private IInputContext input;

    public void Run(int renderWidth, int renderHeight, string? initialSource = null)
    {
        this.newSourceName = initialSource;
        this.RenderWidth = (uint)renderWidth;
        this.RenderHeight = (uint)renderHeight;

        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(renderWidth, renderHeight);
        options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
        
        this.window = Silk.NET.Windowing.Window.Create(options);
        this.window.Load += this.OnWindowLoad;
        this.window.Render += this.OnWindowRender;

        this.window.Title = "(Awaiting Source) - Tractus Source Viewer for NDI";
        this.window.Run();
    }

    private void OnWindowLoad()
    {
        var rawIcon = this.OpenPNGFile("NdiSourceMonitor.png", out var iconWidth, out var iconHeight);
        var icon = new RawImage(iconWidth, iconHeight, new Memory<byte>(rawIcon));
        this.window.SetWindowIcon(ref icon);

        this.gl = GL.GetApi(this.window);
        var gl = this.gl;

        Console.WriteLine($"OpenGL ES Version: {gl.GetStringS(StringName.Version)}");
        Console.WriteLine($"OpenGL ES Vendor: {gl.GetStringS(StringName.Vendor)}");
        Console.WriteLine($"OpenGL ES Renderer: {gl.GetStringS(StringName.Renderer)}");


        // Init OpenGL with Alpha.
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Viewport(0, 0, 1920, 1080);
        gl.ClearColor(0f, 0f, 0f, 0f);

        // Set up FBOs
        this.fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(GLEnum.Framebuffer, this.fbo);

        // Generate and bind the texture
        this.fboTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, this.fboTexture);

        unsafe
        {
            gl.TexImage2D(TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgba,
                (uint)this.RenderWidth,
                (uint)this.RenderHeight,
                0,
                GLEnum.Rgba,
                GLEnum.UnsignedByte,
                null);
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        // Attach the texture to the framebuffer
        gl.FramebufferTexture2D(
            GLEnum.Framebuffer,
            GLEnum.ColorAttachment0,
            TextureTarget.Texture2D,
            this.fboTexture,
            0);

        // Check if framebuffer is complete
        if (gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Console.WriteLine("Framebuffer is not complete!");
        }

        // Unbind the framebuffer
        gl.BindFramebuffer(GLEnum.Framebuffer, 0);


        // Create shaders
        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, VertexCoordShader);
        gl.CompileShader(vertexShader);

        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, FragmentShader);
        gl.CompileShader(fragmentShader);

        this.shader = gl.CreateProgram();
        gl.AttachShader(this.shader, vertexShader);
        gl.AttachShader(this.shader, fragmentShader);
        gl.LinkProgram(this.shader);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        // Create vertex objects
        float[] vertices = {
            // Position     // Texture coordinates
            -1.0f, -1.0f, 0.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 0.0f, 1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
             1.0f,  1.0f, 0.0f, 1.0f, 1.0f
        };

        this.vao = gl.GenVertexArray();
        gl.BindVertexArray(this.vao);

        this.vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, this.vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)vertices, BufferUsageARB.StaticDraw);

        unsafe
        {
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        }

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        this.flipYUniformLocation = gl.GetUniformLocation(this.shader, "uFlipY");
        this.transformUniformLocation = gl.GetUniformLocation(this.shader, "uTransform");
        this.textureSizeUniformLocation = gl.GetUniformLocation(this.shader, "uTextureSize");

        this.font = new BMFont("font.fnt");
        this.font.GLTextureID = this.CreateTextureFromPNGFile("font1.png");

        this.title = this.font.GenerateTitle(this.gl, "Hello world.", out var titleWidth, out var titleHeight);
        this.titleMatrix = this.CreateTransformMatrix(1920, 1080, titleWidth, titleHeight, 100, 100);

        this.noSourcePng = this.CreateTextureFromPNGFile("nosource.png");
        this.noSourcePngMatrix = this.CreateTransformMatrix(1920, 1080, 1920, 1080, 0, 0);

        this.connectingPng = this.CreateTextureFromPNGFile("connecting.png");
        this.connectingPngMatrix = this.CreateTransformMatrix(1920, 1080, 1920, 1080, 0, 0);

        this.input = this.window.CreateInput();

        this.input.Mice[0].MouseDown += this.OnMouseDown;
    }

    private void OnMouseDown(IMouse arg1, MouseButton arg2)
    {
        if(arg2 == MouseButton.Right)
        {
            this.window.WindowState = 
                this.window.WindowState == WindowState.Fullscreen
                ? WindowState.Normal
                : WindowState.Fullscreen;
        }
    }

    private uint noSourcePng;
    private Matrix4x4 noSourcePngMatrix;

    private uint connectingPng;
    private Matrix4x4 connectingPngMatrix;


    private BMFont font;
    private uint title;
    private Matrix4x4 titleMatrix;

    private void OnWindowRender(double obj)
    {
        var gl = this.gl;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        if (!string.IsNullOrEmpty(this.newSourceName))
        {
            // Handle new source.
            if (this.frameSyncPtr != IntPtr.Zero)
            {
                NDIlib.framesync_destroy(this.frameSyncPtr);
                this.frameSyncPtr = IntPtr.Zero;
            }

            if (this.receiverPtr != IntPtr.Zero)
            {
                NDIlib.recv_destroy(this.receiverPtr);
                this.receiverPtr = nint.Zero;
            }

            if (this.ndiTexture != 0)
            {
                gl.DeleteTexture(this.ndiTexture);
                this.ndiTexture = 0;
            }

            var createSettings = new NDIlib.recv_create_v3_t()
            {
                allow_video_fields = true,
                bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest,
                color_format = NDIlib.recv_color_format_e.recv_color_format_e_RGBX_RGBA,
                source_to_connect_to = new NDIlib.source_t
                {
                    p_ndi_name = UTF.StringToUtf8(this.newSourceName)
                },
                p_ndi_recv_name = UTF.StringToUtf8("OpenGL")
            };

            var receiver = NDIlib.recv_create_v3(ref createSettings);

            var frameSyncApi = NDIlib.framesync_create(receiver);

            this.receiverPtr = receiver;
            this.frameSyncPtr = frameSyncApi;

            Marshal.FreeHGlobal(createSettings.source_to_connect_to.p_ndi_name);
            Marshal.FreeHGlobal(createSettings.p_ndi_recv_name);


            this.window.Title = $"{this.newSourceName} - Tractus Source Viewer for NDI";
            this.newSourceName = null;
        }


        gl.BindFramebuffer(GLEnum.Framebuffer, this.fbo);
        gl.Viewport(0, 0, this.RenderWidth, this.RenderHeight);

        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        gl.UseProgram(this.shader);
        gl.BindVertexArray(this.vao);
        gl.Uniform1(this.flipYUniformLocation, 0);

        // Do render of source.
        if (this.frameSyncPtr != IntPtr.Zero)
        {
            var videoData = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(this.frameSyncPtr, ref videoData, NDIlib.frame_format_type_e.frame_format_type_progressive);

            if (videoData.p_data != nint.Zero)
            {
                unsafe
                {
                    var ptrToData = videoData.p_data.ToPointer();

                    if (this.ndiTexture == 0)
                    {
                        this.ndiTexture = this.CreateTexture(videoData.xres, videoData.yres);
                        this.ndiMatrix = this.CreateTransformMatrix(
                            (int)this.RenderWidth,
                            (int)this.RenderHeight,
                            (int)this.RenderWidth,
                            (int)this.RenderHeight,
                            0,
                            0);
                    }

                    gl.Uniform2(this.textureSizeUniformLocation, (float)videoData.xres, (float)videoData.yres);

                    gl.BindTexture(TextureTarget.Texture2D, this.ndiTexture);

                    gl.PixelStore(GLEnum.UnpackAlignment, 1);

                    gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                        (uint)videoData.xres,
                        (uint)videoData.yres,
                        Silk.NET.OpenGL.PixelFormat.Rgba,
                        Silk.NET.OpenGL.PixelType.UnsignedByte,
                        ptrToData);

                    gl.BindTexture(TextureTarget.Texture2D, 0);

                    NDIlib.framesync_free_video(this.frameSyncPtr, ref videoData);
                }
            }
        }

        if (this.ndiTexture != 0)
        {
            gl.ActiveTexture(GLEnum.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, this.ndiTexture);


            var transformMatrix = this.ndiMatrix;
            Span<float> matrixSpan = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref transformMatrix, 1));
            gl.UniformMatrix4(this.transformUniformLocation, 1, false, matrixSpan);

            gl.DrawArrays(GLEnum.TriangleStrip, 0, 4);
        }
        else if(this.frameSyncPtr != nint.Zero)
        {
            gl.ActiveTexture(GLEnum.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, this.connectingPng);

            var transformMatrix = this.connectingPngMatrix;
            Span<float> matrixSpan = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref transformMatrix, 1));
            gl.UniformMatrix4(this.transformUniformLocation, 1, false, matrixSpan);

            gl.DrawArrays(GLEnum.TriangleStrip, 0, 4);
        }
        else
        {
            gl.ActiveTexture(GLEnum.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, this.noSourcePng);

            var transformMatrix = this.noSourcePngMatrix;
            Span<float> matrixSpan = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref transformMatrix, 1));
            gl.UniformMatrix4(this.transformUniformLocation, 1, false, matrixSpan);

            gl.DrawArrays(GLEnum.TriangleStrip, 0, 4);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);


        // Something about the way I'm trying to generate fonts doesn't work.
        // I just get a black bar instead of a nicely rendered title.
        // Not surprised since I absolutely suck at OpenGL.

        //if (this.title != 0)
        //{
        //    gl.ActiveTexture(GLEnum.Texture0);
        //    gl.BindTexture(TextureTarget.Texture2D, this.title);

        //    var transformMatrix = this.titleMatrix;
        //    Span<float> matrixSpan = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref transformMatrix, 1));
        //    gl.UniformMatrix4(this.transformUniformLocation, 1, false, matrixSpan);

        //    gl.DrawArrays(GLEnum.TriangleStrip, 0, 4);
        //}

        gl.BindTexture(TextureTarget.Texture2D, 0);

        gl.BindVertexArray(0);
        gl.UseProgram(0);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)this.window.Size.X, (uint)this.window.Size.Y);

        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.UseProgram(this.shader);
        gl.BindVertexArray(this.vao);
        gl.Uniform1(this.flipYUniformLocation, 1);

        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(GLEnum.Texture2D, this.fboTexture);

        // In your render loop, when rendering FBO to screen:
        var screenTransform = CreateScreenTransformMatrix(this.window.Size.X, this.window.Size.Y);
        Span<float> screenMatrixSpan = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref screenTransform, 1));
        gl.UniformMatrix4(this.transformUniformLocation, 1, false, screenMatrixSpan);

        gl.DrawArrays(GLEnum.TriangleStrip, 0, 4);

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private Matrix4x4 CreateTransformMatrix(int fboWidth, int fboHeight, int sourceWidth, int sourceHeight, int posX, int posY)
    {
        // Calculate scale factors
        float scaleX = (float)sourceWidth / fboWidth;
        float scaleY = (float)sourceHeight / fboHeight;

        int adjustedPosY = fboHeight - posY - sourceHeight;

        // Calculate position in NDC
        float ndcX = (2.0f * posX / fboWidth) - 1.0f + scaleX;
        float ndcY = 1.0f - (2.0f * adjustedPosY / fboHeight) - scaleY;

        // Create transformation matrix
        return Matrix4x4.CreateScale(scaleX, scaleY, 1) *
               Matrix4x4.CreateTranslation(ndcX, ndcY, 0);
    }

    public unsafe uint CreateTexture(int width, int height)
    {
        var gl = this.gl;
        uint textureId = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, textureId);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba,
            (uint)width,
            (uint)height,
            0,
            Silk.NET.OpenGL.PixelFormat.Rgba,
            Silk.NET.OpenGL.PixelType.UnsignedByte,
            (void*)0);

        gl.BindTexture(TextureTarget.Texture2D, 0);

        return textureId;
    }

    public byte[] OpenPNGFile(string fileName, out int width, out int height)
    {
        using var stream = File.OpenRead(fileName);

        var pngFile = Png.Open(stream);

        var emptyData = new byte[pngFile.Width * pngFile.Height * 4];

        var pixelIndex = 0;

        for (var i = 0; i < (pngFile.Width * pngFile.Height); i++)
        {
            var x = i % pngFile.Width;
            var y = i / pngFile.Width;
            var pixel = pngFile.GetPixel(x, y);

            emptyData[pixelIndex] = pixel.R;
            emptyData[pixelIndex + 1] = pixel.G;
            emptyData[pixelIndex + 2] = pixel.B;
            emptyData[pixelIndex + 3] = pixel.A;

            pixelIndex += 4;
        }

        width = pngFile.Width;
        height = pngFile.Height;
        return emptyData;
    }

    public unsafe uint CreateTextureFromPNGFile(string fileName)
    {
        var gl = this.gl;

        var emptyData = this.OpenPNGFile(fileName, out var width, out var height);

        var textureId = this.CreateTexture(width, height);

        gl.BindTexture(TextureTarget.Texture2D, textureId);

        fixed (byte* pEmptyData = emptyData)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, pEmptyData);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);

        return textureId;
    }

    private static Matrix4x4 CreateScreenTransformMatrix(int windowWidth, int windowHeight)
    {
        float targetAspectRatio = 16.0f / 9.0f;
        float windowAspectRatio = (float)windowWidth / windowHeight;
        float scaleX = 1.0f, scaleY = 1.0f;

        if (windowAspectRatio > targetAspectRatio)
        {
            scaleX = targetAspectRatio / windowAspectRatio;
        }
        else
        {
            scaleY = windowAspectRatio / targetAspectRatio;
        }

        return Matrix4x4.CreateScale(scaleX, scaleY, 1.0f);
    }

    public void RequestUpdateNdiSource(string sourceName)
    {
        this.newSourceName = sourceName;
    }

    private string? newSourceName;

    private const string VertexCoordShader = @"#version 300 es
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;
uniform mat4 uTransform;
uniform bool uFlipY;

void main()
{
    gl_Position = uTransform * vec4(aPosition, 1.0);
    vTexCoord = uFlipY ? vec2(aTexCoord.x, 1.0 - aTexCoord.y) : aTexCoord;
}";

    private const string FragmentShader = @"
        #version 300 es
        precision mediump float;
        in vec2 vTexCoord;
        out vec4 FragColor;
        uniform sampler2D uTexture;
        void main()
        {
            FragColor = texture(uTexture, vTexCoord);
        }";

    /*
     * I can't make YUV to RGB conversion work properly. Shaders aren't my strong point.
     * AI suggested this, ChatGPT is broken, Claude provides something
     * less broken.
     */
    private const string UYVYToRgbaFragmentShader = @"
#version 300 es
precision mediump float;

uniform sampler2D uTexture;
uniform vec2 uTextureSize;
in vec2 vTexCoord;
out vec4 FragColor;

void main()
{
    vec2 texelSize = vec2(1.0) / uTextureSize;
    vec2 coord = vTexCoord * uTextureSize;

    // Calculate the texel coordinate of the even pixel
    vec2 evenTexCoord = vec2(floor(coord.x / 2.0) * 2.0, floor(coord.y));
    evenTexCoord *= texelSize;

    // Fetch the UYVY macropixel
    vec4 uyvy = texture(uTexture, evenTexCoord);

    // Determine if the current pixel is even or odd
    bool isEven = mod(coord.x, 2.0) == 0.0;

    // Extract Y value
    float y = isEven ? uyvy.g : uyvy.a;

    // Output the luma value as grayscale
    FragColor = vec4(vec3(y), 1.0);
}


";

}
