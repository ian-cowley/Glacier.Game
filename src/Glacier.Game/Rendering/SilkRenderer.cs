namespace Glacier.Game.Rendering;

using System;
using System.Numerics;

/// <summary>
/// Deprecated renderer retained for backward compatibility.
/// Superseded by <see cref="GlacierGraphicsGameRenderer"/> and native HAL Direct3D 12 / Vulkan swapchains.
/// </summary>
[Obsolete("SilkRenderer is deprecated in Milestone M8. Use GlacierGraphicsGameRenderer backed by Glacier.Graphics and Glacier.Windowing.", false)]
public sealed class SilkRenderer : IRenderer
{
    private readonly GlacierGraphicsGameRenderer _inner;

    public int Width => _inner.Width;
    public int Height => _inner.Height;

    public SilkRenderer(int width = 1920, int height = 1080)
    {
        _inner = new GlacierGraphicsGameRenderer(width, height);
    }

    public void Initialize(int width, int height) => _inner.Initialize(width, height);
    public void Begin(in Matrix3x2 transform) => _inner.Begin(transform);
    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices) => _inner.DrawBatch(vertices, indices);
    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int textureId) => _inner.DrawBatch(vertices, indices, textureId);
    public void End() => _inner.End();
    public void Present() => _inner.Present();
    public void Dispose() => _inner.Dispose();
}
