namespace Glacier.Game.Tests;

using System;
using System.Numerics;
using Glacier.Game.Rendering;
using Xunit;

public class RenderingTests
{
    [Fact]
    public void HeadlessRenderer_TracksDrawCallsAndVertices()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        using var batch = new SpriteBatch(renderer, maxQuads: 1024);

        batch.Begin();
        for (int i = 0; i < 100; i++)
        {
            batch.DrawQuad(i * 10, i * 10, 8, 8, new Color32(255, 0, 0, 255));
        }
        batch.End();

        Assert.Equal(1, renderer.TotalDrawCalls);
        Assert.Equal(400, renderer.TotalVerticesRendered); // 100 quads * 4 vertices
    }

    [Fact]
    public void SpriteBatch_FlushesAutomaticallyWhenFull()
    {
        using var renderer = new HeadlessRenderer(800, 600);
        using var batch = new SpriteBatch(renderer, maxQuads: 50);

        batch.Begin();
        // Drawing 120 quads with maxQuads = 50 should trigger multiple flushes
        for (int i = 0; i < 120; i++)
        {
            batch.DrawQuad(i, i, 4, 4, new Color32(0, 255, 0, 255));
        }
        batch.End();

        Assert.True(renderer.TotalDrawCalls >= 3);
        Assert.Equal(480, renderer.TotalVerticesRendered); // 120 * 4
    }

    [Fact]
    public void SpriteBatch_TextureChange_TriggersBatchSplit()
    {
        using var renderer = new HeadlessRenderer(800, 600);
        using var batch = new SpriteBatch(renderer, maxQuads: 1024);

        batch.Begin();
        batch.DrawSprite(1, 0, 0, 10, 10, new Color32(255, 255, 255, 255));
        batch.DrawSprite(1, 10, 10, 10, 10, new Color32(255, 255, 255, 255));
        // Texture change to 2 should flush the 2 quads from texture 1
        batch.DrawSprite(2, 20, 20, 10, 10, new Color32(255, 255, 255, 255));
        batch.End();

        Assert.Equal(2, renderer.TotalDrawCalls);
        Assert.Equal(12, renderer.TotalVerticesRendered); // 3 quads * 4 vertices
        Assert.Equal(2, renderer.LastTextureId);
    }

    [Fact]
    public void GlacierGraphicsGameRenderer_RendersAndEncodesPng()
    {
        using var renderer = new GlacierGraphicsGameRenderer(640, 480);
        using var batch = new SpriteBatch(renderer, maxQuads: 256);

        batch.Begin();
        batch.DrawQuad(50, 50, 100, 100, Color32.Cyan);
        batch.DrawQuad(200, 150, 80, 80, Color32.Red);
        batch.End();
        renderer.Present();

        Assert.Equal(1, renderer.TotalDrawCalls);
        Assert.Equal(8, renderer.TotalVerticesRendered);
        Assert.Equal(1, renderer.TotalFramesPresented);

        byte[] png = renderer.EncodeToPng();
        Assert.NotNull(png);
        Assert.True(png.Length > 64);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
    }

    [Fact]
    public void GlacierGameWindowFactory_CreatesHeadlessWindow()
    {
        using var win = GlacierGameWindowFactory.CreateHeadlessGameWindow(1280, 720);
        Assert.NotNull(win);
        Assert.Equal(1280, win.Size.Width);
        Assert.Equal(720, win.Size.Height);
    }
}

