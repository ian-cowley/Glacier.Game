namespace Glacier.Game.Rendering;

using System;
using System.Numerics;

/// <summary>
/// Rendering abstraction supporting headless testing and hardware GPU backends.
/// </summary>
public interface IRenderer : IDisposable
{
    int Width { get; }
    int Height { get; }

    void Initialize(int width, int height);
    void Begin(in Matrix3x2 transform);
    void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices);
    void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int textureId)
    {
        DrawBatch(vertices, indices);
    }
    void End();
    void Present();
}
