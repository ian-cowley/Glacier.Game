namespace Glacier.Game.Rendering;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// High-throughput, zero-allocation batched quad and sprite renderer.
/// Manages unmanaged vertex and index arrays capable of rendering 250,000+ entities.
/// </summary>
public sealed unsafe class SpriteBatch : IDisposable
{
    private const int DefaultMaxQuads = 65536;

    private readonly IRenderer _renderer;
    private readonly int _maxQuads;
    private Vertex2D* _vertices;
    private uint* _indices;
    private int _quadCount;
    private bool _inBatch;
    private bool _disposed;

    public int QuadCount => _quadCount;
    public int MaxQuads => _maxQuads;

    public SpriteBatch(IRenderer renderer, int maxQuads = DefaultMaxQuads)
    {
        _renderer = renderer;
        _maxQuads = Math.Max(16, maxQuads);

        nuint vertexBytes = (nuint)(sizeof(Vertex2D) * _maxQuads * 4);
        nuint indexBytes = (nuint)(sizeof(uint) * _maxQuads * 6);

        _vertices = (Vertex2D*)NativeMemory.AllocZeroed(vertexBytes);
        _indices = (uint*)NativeMemory.AllocZeroed(indexBytes);

        // Pre-populate index buffer with standard quad topology (0,1,2, 2,3,0)
        for (int i = 0; i < _maxQuads; i++)
        {
            uint vBase = (uint)(i * 4);
            int idx = i * 6;
            _indices[idx + 0] = vBase + 0;
            _indices[idx + 1] = vBase + 1;
            _indices[idx + 2] = vBase + 2;
            _indices[idx + 3] = vBase + 2;
            _indices[idx + 4] = vBase + 3;
            _indices[idx + 5] = vBase + 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Begin(in Matrix3x2? transform = null)
    {
        if (_inBatch) throw new InvalidOperationException("Begin() called while already in a batch.");
        _inBatch = true;
        _quadCount = 0;
        _renderer.Begin(transform ?? Matrix3x2.Identity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(float x, float y, float width, float height, Color32 color)
    {
        if (!_inBatch) throw new InvalidOperationException("DrawQuad called outside Begin/End block.");
        if (_quadCount >= _maxQuads)
        {
            Flush();
        }

        int vIdx = _quadCount * 4;
        _vertices[vIdx + 0] = new Vertex2D(x, y, 0f, 0f, color);
        _vertices[vIdx + 1] = new Vertex2D(x + width, y, 1f, 0f, color);
        _vertices[vIdx + 2] = new Vertex2D(x + width, y + height, 1f, 1f, color);
        _vertices[vIdx + 3] = new Vertex2D(x, y + height, 0f, 1f, color);

        _quadCount++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(int textureId, float x, float y, float width, float height, Color32 color)
    {
        // For basic sprite batching without multi-texture splits, route to DrawQuad
        DrawQuad(x, y, width, height, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        if (_quadCount == 0) return;

        var vSpan = new ReadOnlySpan<Vertex2D>(_vertices, _quadCount * 4);
        var iSpan = new ReadOnlySpan<uint>(_indices, _quadCount * 6);
        _renderer.DrawBatch(vSpan, iSpan);

        _quadCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void End()
    {
        if (!_inBatch) throw new InvalidOperationException("End() called without preceding Begin().");
        Flush();
        _renderer.End();
        _inBatch = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vertices != null)
        {
            NativeMemory.Free(_vertices);
            _vertices = null;
        }

        if (_indices != null)
        {
            NativeMemory.Free(_indices);
            _indices = null;
        }
    }
}
