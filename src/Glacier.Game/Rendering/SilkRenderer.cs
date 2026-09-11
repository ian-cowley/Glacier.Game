namespace Glacier.Game.Rendering;

using System;
using System.Numerics;
using Silk.NET.OpenGL;

/// <summary>
/// Hardware GPU renderer using Silk.NET OpenGL for modern shader-based batched 2D rendering.
/// </summary>
public sealed unsafe class SilkRenderer : IRenderer
{
    private readonly GL? _gl;
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private bool _initialized;
    private bool _disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public SilkRenderer(GL? gl = null, int width = 1920, int height = 1080)
    {
        _gl = gl;
        Width = width;
        Height = height;

        if (_gl != null)
        {
            Initialize(width, height);
        }
    }

    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;

        if (_gl == null || _initialized) return;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        // Pre-allocate 4MB dynamic vertex buffer
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(4 * 1024 * 1024), null, BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(1024 * 1024), null, BufferUsageARB.DynamicDraw);

        // Vertex layout: Position(2 floats), TexCoord(2 floats), Color(4 bytes normalized)
        uint stride = (uint)sizeof(Vertex2D);

        // Location 0: Position
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        // Location 1: TexCoord
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(sizeof(float) * 2));

        // Location 2: Color (packed 4 bytes normalized)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)(sizeof(float) * 4));

        _gl.BindVertexArray(0);

        _initialized = true;
    }

    public void Begin(in Matrix3x2 transform)
    {
        if (_gl == null) return;
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.BindVertexArray(_vao);
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        if (_gl == null || vertices.Length == 0) return;

        fixed (Vertex2D* pV = vertices)
        fixed (uint* pI = indices)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(sizeof(Vertex2D) * vertices.Length), pV);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(sizeof(uint) * indices.Length), pI);

            _gl.DrawElements(PrimitiveType.Triangles, (uint)indices.Length, DrawElementsType.UnsignedInt, (void*)0);
        }
    }

    public void End()
    {
        if (_gl == null) return;
        _gl.BindVertexArray(0);
    }

    public void Present()
    {
        // Buffers are swapped by the host window lifecycle
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_gl != null && _initialized)
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
        }
    }
}
