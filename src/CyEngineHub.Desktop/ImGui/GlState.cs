using Silk.NET.OpenGLES;

namespace CyEngineHub.Desktop.ImGui;

// Browser chrome surfaces share the platform context: restore all modified GL state.
internal sealed class GlState : IDisposable
{
    private readonly GL _gl;
    private readonly int _program, _vao, _array, _element, _activeTexture, _texture, _framebuffer, _readFramebuffer;
    private readonly int _sourceRgb, _destinationRgb, _sourceAlpha, _destinationAlpha, _equationRgb, _equationAlpha;
    private readonly int[] _viewport = new int[4], _scissor = new int[4];
    private readonly bool _blend, _cull, _depth, _scissorEnabled;
    private readonly int _pack, _unpack;

    public GlState(GL gl)
    {
        _gl = gl;
        _program = gl.GetInteger(GetPName.CurrentProgram);
        _vao = gl.GetInteger(GetPName.VertexArrayBinding);
        _array = gl.GetInteger(GetPName.ArrayBufferBinding);
        _element = gl.GetInteger(GetPName.ElementArrayBufferBinding);
        _activeTexture = gl.GetInteger(GetPName.ActiveTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        _texture = gl.GetInteger(GetPName.TextureBinding2D);
        _framebuffer = gl.GetInteger(GetPName.DrawFramebufferBinding);
        _readFramebuffer = gl.GetInteger(GetPName.ReadFramebufferBinding);
        _sourceRgb = gl.GetInteger(GetPName.BlendSrcRgb);
        _destinationRgb = gl.GetInteger(GetPName.BlendDstRgb);
        _sourceAlpha = gl.GetInteger(GetPName.BlendSrcAlpha);
        _destinationAlpha = gl.GetInteger(GetPName.BlendDstAlpha);
        _equationRgb = gl.GetInteger(GetPName.BlendEquationRgb);
        _equationAlpha = gl.GetInteger(GetPName.BlendEquationAlpha);
        _pack = gl.GetInteger(GetPName.PackAlignment);
        _unpack = gl.GetInteger(GetPName.UnpackAlignment);
        _blend = gl.IsEnabled(EnableCap.Blend);
        _cull = gl.IsEnabled(EnableCap.CullFace);
        _depth = gl.IsEnabled(EnableCap.DepthTest);
        _scissorEnabled = gl.IsEnabled(EnableCap.ScissorTest);
        gl.GetInteger(GetPName.Viewport, _viewport);
        gl.GetInteger(GetPName.ScissorBox, _scissor);
    }

    public void Dispose()
    {
        var gl = _gl;
        gl.UseProgram((uint)_program);
        gl.BindVertexArray((uint)_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_array);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, (uint)_element);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_framebuffer);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_readFramebuffer);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, (uint)_texture);
        gl.ActiveTexture((TextureUnit)_activeTexture);
        gl.BlendFuncSeparate((BlendingFactor)_sourceRgb, (BlendingFactor)_destinationRgb, (BlendingFactor)_sourceAlpha, (BlendingFactor)_destinationAlpha);
        gl.BlendEquationSeparate((BlendEquationModeEXT)_equationRgb, (BlendEquationModeEXT)_equationAlpha);
        gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
        gl.Scissor(_scissor[0], _scissor[1], (uint)_scissor[2], (uint)_scissor[3]);
        gl.PixelStore(PixelStoreParameter.PackAlignment, _pack);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, _unpack);
        Restore(EnableCap.Blend, _blend);
        Restore(EnableCap.CullFace, _cull);
        Restore(EnableCap.DepthTest, _depth);
        Restore(EnableCap.ScissorTest, _scissorEnabled);
    }

    private void Restore(EnableCap cap, bool enabled)
    {
        if (enabled) _gl.Enable(cap);
        else _gl.Disable(cap);
    }
}
