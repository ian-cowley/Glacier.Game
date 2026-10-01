namespace Glacier.Game.Rendering;

using System;
using System.IO;
using System.Numerics;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Vector;
using Glacier.Windowing;
using Glacier.Windowing.Platform;
using Glacier.Windowing.Swapchain.Software;

/// <summary>
/// Hardware/software renderer backed by Glacier.Graphics and Glacier.Windowing.
/// Provides pure managed C# .NET 10 rasterization and zero third-party native dependencies.
/// </summary>
public sealed class GlacierGraphicsGameRenderer : IRenderer
{
    private LinearFramebuffer _framebuffer;
    private CpuGraphicsCanvas _canvas;
    private System.Numerics.Matrix3x2 _transform = System.Numerics.Matrix3x2.Identity;
    private bool _disposed;
    private readonly VectorPath _scratchPath = new VectorPath();
    private readonly VectorPath _scratchTrianglePath;

    public int Width => _framebuffer.Width;
    public int Height => _framebuffer.Height;
    public LinearFramebuffer Framebuffer => _framebuffer;
    public IGraphicsCanvas Canvas => _canvas;

    public long TotalDrawCalls { get; private set; }
    public long TotalVerticesRendered { get; private set; }
    public long TotalFramesPresented { get; private set; }
    public ISwapchain? Swapchain { get; set; }

    public GlacierGraphicsGameRenderer(int width = 1280, int height = 720)
    {
        _framebuffer = new LinearFramebuffer(width, height);
        _canvas = new CpuGraphicsCanvas(_framebuffer);
        _scratchTrianglePath = _scratchPath;
    }

    public void Initialize(int width, int height)
    {
        if (width != Width || height != Height)
        {
            _canvas.Dispose();
            _framebuffer.Dispose();
            _framebuffer = new LinearFramebuffer(width, height);
            _canvas = new CpuGraphicsCanvas(_framebuffer);
            Swapchain?.Resize(width, height);
        }
    }

    public void Begin(in System.Numerics.Matrix3x2 transform)
    {
        _transform = transform;
        _canvas.Clear(Rgba32.Black);
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        DrawBatch(vertices, indices, 0);
    }

    public void DrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int textureId)
    {
        TotalDrawCalls++;
        TotalVerticesRendered += vertices.Length;

        // Render indexed triangles into vector paths
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];

            if (i0 < vertices.Length && i1 < vertices.Length && i2 < vertices.Length)
            {
                var v0 = vertices[(int)i0];
                var v1 = vertices[(int)i1];
                var v2 = vertices[(int)i2];

                var p0 = Vector2.Transform(v0.Position, _transform);
                var p1 = Vector2.Transform(v1.Position, _transform);
                var p2 = Vector2.Transform(v2.Position, _transform);

                _scratchPath.Clear();
                _scratchPath.MoveTo(p0.X, p0.Y);
                _scratchPath.LineTo(p1.X, p1.Y);
                _scratchPath.LineTo(p2.X, p2.Y);
                _scratchPath.Close();

                var c = v0.Color;
                _canvas.FillPath(_scratchPath, new Paint(new Rgba32(c.R, c.G, c.B, c.A), PaintStyle.Fill));
            }
        }
    }

    public void End()
    {
        _canvas.Flush();
    }

    public void Present()
    {
        TotalFramesPresented++;
        if (Swapchain != null)
        {
            if (Swapchain is SoftwareSwapchain sw && sw.CurrentBackBuffer != IntPtr.Zero)
            {
                unsafe
                {
                    var src = _framebuffer.AsByteSpan();
                    var dst = new Span<byte>((void*)sw.CurrentBackBuffer, (int)sw.BufferByteSize);
                    src.CopyTo(dst);
                }
            }
        }
    }

    public byte[] EncodeToPng()
    {
        return PngEncoder.Encode(_framebuffer);
    }

    public void SavePng(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, EncodeToPng());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _canvas.Dispose();
            _framebuffer.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Facilitates hosting Glacier.Game engine using Glacier.Windowing platform windows.
/// </summary>
public static class GlacierGameWindowFactory
{
    public static IWindow CreateGameWindow(string title = "Glacier Game", int width = 1280, int height = 720)
    {
        var options = new WindowOptions
        {
            Title = title,
            Width = width,
            Height = height,
            Resizable = true,
            IsVisible = true,
            EnableRawInput = true
        };
        return WindowFactory.CreateWindow(options);
    }

    public static IWindow CreateHeadlessGameWindow(int width = 1280, int height = 720)
    {
        var options = new WindowOptions
        {
            Title = "Glacier Game Headless",
            Width = width,
            Height = height,
            Resizable = false,
            IsVisible = false
        };
        return WindowFactory.CreateHeadless(options);
    }
}
