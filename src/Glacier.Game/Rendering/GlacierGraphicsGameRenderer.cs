namespace Glacier.Game.Rendering;

using System;
using System.IO;
using System.Numerics;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Desktop.Windowing;
using Glacier.Desktop.Windowing.Platform;

/// <summary>
/// Hardware/software renderer backed by Glacier.Graphics and Glacier.Desktop.
/// Provides pure managed C# .NET 10 scanline rasterization with zero third-party native dependencies.
/// </summary>
public sealed class GlacierGraphicsGameRenderer : IRenderer
{
    private LinearFramebuffer _framebuffer;
    private CpuGraphicsCanvas _canvas;
    private System.Numerics.Matrix3x2 _transform = System.Numerics.Matrix3x2.Identity;
    private bool _disposed;

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

        int fbWidth = _framebuffer.Width;
        int fbHeight = _framebuffer.Height;
        bool isIdentity = _transform.IsIdentity;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];

            if (i0 < (uint)vertices.Length && i1 < (uint)vertices.Length && i2 < (uint)vertices.Length)
            {
                Vertex2D v0 = vertices[(int)i0];
                Vertex2D v1 = vertices[(int)i1];
                Vertex2D v2 = vertices[(int)i2];

                Vector2 p0 = isIdentity ? v0.Position : Vector2.Transform(v0.Position, _transform);
                Vector2 p1 = isIdentity ? v1.Position : Vector2.Transform(v1.Position, _transform);
                Vector2 p2 = isIdentity ? v2.Position : Vector2.Transform(v2.Position, _transform);

                RasterizeTriangle(p0, v0.Color, p1, v1.Color, p2, v2.Color, fbWidth, fbHeight);
            }
        }
    }

    private void RasterizeTriangle(
        Vector2 p0, Color32 c0,
        Vector2 p1, Color32 c1,
        Vector2 p2, Color32 c2,
        int fbWidth, int fbHeight)
    {
        float area = EdgeFunction(p0, p1, p2);
        if (MathF.Abs(area) < 1e-5f) return;

        if (area < 0f)
        {
            (p1, p2) = (p2, p1);
            (c1, c2) = (c2, c1);
            area = -area;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int maxX = Math.Min(fbWidth - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int maxY = Math.Min(fbHeight - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));

        if (minX > maxX || minY > maxY) return;

        bool flatColor =
            c0.R == c1.R && c0.G == c1.G && c0.B == c1.B && c0.A == c1.A &&
            c0.R == c2.R && c0.G == c2.G && c0.B == c2.B && c0.A == c2.A;

        float dx0 = p2.Y - p1.Y, dy0 = p1.X - p2.X;
        float dx1 = p0.Y - p2.Y, dy1 = p2.X - p0.X;
        float dx2 = p1.Y - p0.Y, dy2 = p0.X - p1.X;

        float startX = minX + 0.5f;
        float startY = minY + 0.5f;
        var startPt = new Vector2(startX, startY);
        float w0Row = EdgeFunction(p1, p2, startPt);
        float w1Row = EdgeFunction(p2, p0, startPt);
        float w2Row = EdgeFunction(p0, p1, startPt);

        float invArea = 1.0f / area;

        for (int y = minY; y <= maxY; y++)
        {
            Span<Rgba32> row = _framebuffer.GetRowSpan(y);
            float w0 = w0Row;
            float w1 = w1Row;
            float w2 = w2Row;

            if (flatColor)
            {
                var flatRgba = new Rgba32(c0.R, c0.G, c0.B, c0.A);
                if (flatRgba.A == 0)
                    return;

                for (int x = minX; x <= maxX; x++)
                {
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                    {
                        row[x] = flatRgba.A == 255 ? flatRgba : BlendPixel(row[x], flatRgba);
                    }
                    w0 += dx0;
                    w1 += dx1;
                    w2 += dx2;
                }
            }
            else
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                    {
                        float b0 = w0 * invArea;
                        float b1 = w1 * invArea;
                        float b2 = 1.0f - b0 - b1;
                        byte a = (byte)Math.Clamp((int)(c0.A * b0 + c1.A * b1 + c2.A * b2 + 0.5f), 0, 255);
                        if (a > 0)
                        {
                            byte r = (byte)Math.Clamp((int)(c0.R * b0 + c1.R * b1 + c2.R * b2 + 0.5f), 0, 255);
                            byte g = (byte)Math.Clamp((int)(c0.G * b0 + c1.G * b1 + c2.G * b2 + 0.5f), 0, 255);
                            byte b = (byte)Math.Clamp((int)(c0.B * b0 + c1.B * b1 + c2.B * b2 + 0.5f), 0, 255);
                            var src = new Rgba32(r, g, b, a);
                            row[x] = a == 255 ? src : BlendPixel(row[x], src);
                        }
                    }
                    w0 += dx0;
                    w1 += dx1;
                    w2 += dx2;
                }
            }

            w0Row += dy0;
            w1Row += dy1;
            w2Row += dy2;
        }
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

    private static Rgba32 BlendPixel(Rgba32 dst, Rgba32 src)
    {
        int invA = 255 - src.A;
        byte r = (byte)((src.R * src.A + dst.R * invA + 127) / 255);
        byte g = (byte)((src.G * src.A + dst.G * invA + 127) / 255);
        byte b = (byte)((src.B * src.A + dst.B * invA + 127) / 255);
        byte a = (byte)(src.A + (dst.A * invA + 127) / 255);
        return new Rgba32(r, g, b, a);
    }

    public void End()
    {
        _canvas.Flush();
    }

    public void Present()
    {
        TotalFramesPresented++;
        if (Swapchain != null && Swapchain.CurrentBackBuffer != IntPtr.Zero &&
            Swapchain.Width == Width && Swapchain.Height == Height)
        {
            unsafe
            {
                ReadOnlySpan<byte> src = _framebuffer.AsByteSpan();
                var dst = new Span<byte>((void*)Swapchain.CurrentBackBuffer, src.Length);
                src.CopyTo(dst);
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
