namespace Glacier.Game.Rendering;

using System;
using System.Numerics;

/// <summary>
/// Headless software renderer for deterministic unit testing, benchmarking, and CI runners.
/// Produces zero managed heap garbage and tracks draw statistics.
/// </summary>
public sealed class HeadlessRenderer : IRenderer
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    public long TotalDrawCalls { get; private set; }
    public long TotalVerticesRendered { get; private set; }
    public long TotalFramesPresented { get; private set; }

    public HeadlessRenderer(int width = 1920, int height = 1080)
    {
        Width = width;
        Height = height;
    }

    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Begin(in Matrix3x2 transform)
    {
        // No-op in headless
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        TotalDrawCalls++;
        TotalVerticesRendered += vertices.Length;
    }

    public void End()
    {
        // No-op in headless
    }

    public void Present()
    {
        TotalFramesPresented++;
    }

    public void ResetStats()
    {
        TotalDrawCalls = 0;
        TotalVerticesRendered = 0;
        TotalFramesPresented = 0;
    }

    public void Dispose()
    {
        // No native handles to release
    }
}
