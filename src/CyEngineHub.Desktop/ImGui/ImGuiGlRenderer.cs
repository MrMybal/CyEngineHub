using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using ImGuiNET;
using Silk.NET.OpenGLES;
using SkiaSharp;

namespace CyEngineHub.Desktop.ImGui;

// GLES 3 also runs through ANGLE on Windows; no second native window or swap chain.
internal sealed unsafe class ImGuiGlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vertices;
    private readonly uint _indices;
    private readonly uint _vertexArray;
    private uint _font;
    private readonly int _projection;
    private readonly Dictionary<Bitmap, (uint Texture, long Frame)> _images = new(ReferenceEqualityComparer.Instance);
    private long _frame;
    internal GL Api => _gl;

    public ImGuiGlRenderer(GL gl)
    {
        _gl = gl;
        using var state = new GlState(gl);
        var vertex = Compile(ShaderType.VertexShader, """
            #version 300 es
            precision highp float;
            layout(location=0) in vec2 Position;
            layout(location=1) in vec2 UV;
            layout(location=2) in vec4 Color;
            uniform mat4 Projection;
            out vec2 FragUV;
            out vec4 FragColor;
            void main() { FragUV=UV; FragColor=Color; gl_Position=Projection*vec4(Position,0,1); }
            """);
        var fragment = Compile(ShaderType.FragmentShader, """
            #version 300 es
            precision mediump float;
            in vec2 FragUV;
            in vec4 FragColor;
            uniform sampler2D Texture;
            out vec4 OutColor;
            void main() { OutColor=FragColor*texture(Texture,FragUV); }
            """);
        _program = gl.CreateProgram();
        gl.AttachShader(_program, vertex);
        gl.AttachShader(_program, fragment);
        gl.LinkProgram(_program);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0) throw new InvalidOperationException(gl.GetProgramInfoLog(_program));
        _projection = gl.GetUniformLocation(_program, "Projection");
        _vertices = gl.GenBuffer();
        _indices = gl.GenBuffer();
        _vertexArray = gl.GenVertexArray();
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 20, (void*)0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 20, (void*)8);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 20, (void*)16);
        var io = ImGuiNET.ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        UpdateFont();
    }
    internal void UpdateFont()
    {
        if (_font != 0) _gl.DeleteTexture(_font);
        var atlas = ImGuiNET.ImGui.GetIO().Fonts;
        atlas.GetTexDataAsRGBA32(out nint pixels, out int width, out int height);
        _font = Upload(pixels, width, height); atlas.SetTexID((nint)_font); atlas.ClearTexData();
    }

    private uint Compile(ShaderType type, string source)
    {
        if (!_gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)) source = source.Replace("#version 300 es", "#version 330 core", StringComparison.Ordinal).Replace("precision highp float;", "", StringComparison.Ordinal).Replace("precision mediump float;", "", StringComparison.Ordinal);
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0) throw new InvalidOperationException(_gl.GetShaderInfoLog(shader));
        return shader;
    }

    private uint Upload(nint data, int width, int height)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)data);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return texture;
    }

    public nint Image(Bitmap bitmap)
    {
        if (_images.TryGetValue(bitmap, out var entry))
        {
            _images[bitmap] = (entry.Texture, _frame);
            return (nint)entry.Texture;
        }
        using var pixels = new Avalonia.Media.Imaging.WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        bitmap.CopyPixels(buffer);
        var texture = Upload(buffer.Address, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        _images.Add(bitmap, (texture, _frame));
        return (nint)texture;
    }

    public void Render(ImDrawDataPtr data, int framebuffer, int width, int height)
    {
        _frame++;
        var gl = _gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.ClearColor(.035f, .048f, .065f, 1);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        if (width <= 0 || height <= 0) return;
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.ScissorTest);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "Texture"), 0);
        var l = data.DisplayPos.X;
        var r = l + data.DisplaySize.X;
        var t = data.DisplayPos.Y;
        var b = t + data.DisplaySize.Y;
        float* matrix = stackalloc float[16] { 2/(r-l),0,0,0, 0,2/(t-b),0,0, 0,0,-1,0, (r+l)/(l-r),(t+b)/(b-t),0,1 };
        gl.UniformMatrix4(_projection, 1, false, matrix);
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indices);
        for (var i = 0; i < data.CmdListsCount; i++)
        {
            var list = data.CmdLists[i];
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(list.VtxBuffer.Size * 20), (void*)list.VtxBuffer.Data, BufferUsageARB.StreamDraw);
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(list.IdxBuffer.Size * 2), (void*)list.IdxBuffer.Data, BufferUsageARB.StreamDraw);
            for (var j = 0; j < list.CmdBuffer.Size; j++)
            {
                var command = list.CmdBuffer[j];
                if (command.UserCallback != 0) continue;
                var clip = command.ClipRect;
                var scale = data.FramebufferScale;
                var x = Math.Clamp((clip.X-l)*scale.X, 0, width);
                var y = Math.Clamp((clip.Y-t)*scale.Y, 0, height);
                var right = Math.Clamp((clip.Z-l)*scale.X, 0, width);
                var bottom = Math.Clamp((clip.W-t)*scale.Y, 0, height);
                if (right <= x || bottom <= y) continue;
                gl.Scissor((int)x, height-(int)bottom, (uint)(right-x), (uint)(bottom-y));
                gl.BindTexture(TextureTarget.Texture2D, (uint)command.TextureId);
                var vertexOffset = command.VtxOffset * 20;
                gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 20, (void*)vertexOffset);
                gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 20, (void*)(vertexOffset + 8));
                gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 20, (void*)(vertexOffset + 16));
                gl.DrawElements(PrimitiveType.Triangles, command.ElemCount, DrawElementsType.UnsignedShort, (void*)(command.IdxOffset*2));
            }
        }
        gl.Disable(EnableCap.ScissorTest);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        foreach (var image in _images.OrderBy(pair => pair.Value.Frame).Where((pair, index) => _frame-pair.Value.Frame > 600 || (_images.Count - index > 128 && pair.Value.Frame < _frame)).ToArray())
        {
            gl.DeleteTexture(image.Value.Texture);
            _images.Remove(image.Key);
        }
    }

    public void Dispose()
    {
        foreach (var image in _images.Values) _gl.DeleteTexture(image.Texture);
        _images.Clear();
        _gl.DeleteTexture(_font);
        _gl.DeleteBuffer(_vertices);
        _gl.DeleteBuffer(_indices);
        _gl.DeleteVertexArray(_vertexArray);
        _gl.DeleteProgram(_program);
        _gl.Dispose();
    }
}
