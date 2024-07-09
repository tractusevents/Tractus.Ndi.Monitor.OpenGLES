using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tractus.Ndi.Monitor;
public class BMFont
{
    public string Face { get; set; }
    public int Size { get; set; }
    public int LineHeight { get; set; }
    public int Base { get; set; }
    public int ScaleW { get; set; }
    public int ScaleH { get; set; }
    public int Pages { get; set; }
    public string PageFile { get; set; }
    public Dictionary<int, Glyph> Glyphs { get; set; } = new Dictionary<int, Glyph>();

    public uint GLTextureID { get; set; }

    public unsafe uint GenerateTitle(GL gl, string text, out int width, out int height)
    {
        var font = this;

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Create a frame buffer and render buffer for the texture
        uint frameBuffer = gl.GenFramebuffer();
        uint renderBuffer = gl.GenRenderbuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);

        // Calculate texture size based on text
        int textureWidth = 0;
        int textureHeight = font.LineHeight;

        foreach (char c in text)
        {
            if (font.Glyphs.ContainsKey(c))
            {
                var glyph = font.Glyphs[c];
                textureWidth += glyph.XAdvance;
            }
        }

        // Create texture
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)textureWidth,
            (uint)textureHeight, 0, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, (void*)0);

        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);

        // Check if frame buffer is complete
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new Exception("Framebuffer not complete");
        }

        // Render the text into the texture
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float x = 0.0f;
        foreach (char c in text)
        {
            if (font.Glyphs.ContainsKey(c))
            {
                var glyph = font.Glyphs[c];
                RenderGlyph(gl, font, glyph, x, 0, textureWidth, textureHeight);
                x += glyph.XAdvance;
            }
        }

        // Unbind the frame buffer
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        height = textureHeight;
        width = textureWidth;
        return texture;
    }

    private unsafe static void RenderGlyph(GL gl, BMFont font, Glyph glyph, float x, float y, int textureWidth, int textureHeight)
    {
        // Render the glyph to the frame buffer
        float x0 = x + glyph.XOffset;
        float y0 = y + glyph.YOffset;
        float x1 = x0 + glyph.Width;
        float y1 = y0 + glyph.Height;

        float u0 = (float)glyph.X / font.ScaleW;
        float v0 = (float)glyph.Y / font.ScaleH;
        float u1 = (float)(glyph.X + glyph.Width) / font.ScaleW;
        float v1 = (float)(glyph.Y + glyph.Height) / font.ScaleH;

        float[] vertices = {
            x0, y0, u0, v0,
            x1, y0, u1, v0,
            x0, y1, u0, v1,
            x1, y1, u1, v1,
        };

        // Create and bind vertex array and buffer
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        var verticesHandle = GCHandle.Alloc(vertices, GCHandleType.Pinned);

        var verticesPtr = verticesHandle.AddrOfPinnedObject();

        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ref verticesPtr, BufferUsageARB.StaticDraw);


        gl.UseProgram(Program.Display.shader);

        // Set the transform uniform
        Matrix4x4 transformMatrix = Matrix4x4.Identity; // Set your transform matrix
        gl.UniformMatrix4(Program.Display.transformUniformLocation, 1, false, ref transformMatrix.M11);

        // Bind the font texture and draw
        gl.BindTexture(TextureTarget.Texture2D, font.GLTextureID);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        // Clean up
        gl.BindVertexArray(0);
        gl.DeleteVertexArray(vao);
        gl.DeleteBuffer(vbo);
        verticesHandle.Free();
    }

    public BMFont(string fontFile)
    {
        using (var reader = new StreamReader(fontFile))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(' ');
                if (parts[0] == "info")
                {
                    // Parse font info
                    this.Face = this.GetValue(parts, "face");
                    this.Size = int.Parse(this.GetValue(parts, "size"));
                }
                else if (parts[0] == "common")
                {
                    // Parse common info
                    this.LineHeight = int.Parse(this.GetValue(parts, "lineHeight"));
                    this.Base = int.Parse(this.GetValue(parts, "base"));
                    this.ScaleW = int.Parse(this.GetValue(parts, "scaleW"));
                    this.ScaleH = int.Parse(this.GetValue(parts, "scaleH"));
                    this.Pages = int.Parse(this.GetValue(parts, "pages"));
                }
                else if (parts[0] == "page")
                {
                    // Parse page info
                    this.PageFile = this.GetValue(parts, "file");
                }
                else if (parts[0] == "char")
                {
                    // Parse character info
                    var glyph = new Glyph
                    {
                        Id = int.Parse(this.GetValue(parts, "id")),
                        X = int.Parse(this.GetValue(parts, "x")),
                        Y = int.Parse(this.GetValue(parts, "y")),
                        Width = int.Parse(this.GetValue(parts, "width")),
                        Height = int.Parse(this.GetValue(parts, "height")),
                        XOffset = int.Parse(this.GetValue(parts, "xoffset")),
                        YOffset = int.Parse(this.GetValue(parts, "yoffset")),
                        XAdvance = int.Parse(this.GetValue(parts, "xadvance"))
                    };
                    this.Glyphs[glyph.Id] = glyph;
                }
            }
        }
    }

    private string? GetValue(string[] parts, string key)
    {
        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue[0] == key)
            {
                return keyValue[1].Trim('"');
            }
        }
        return null;
    }
}
