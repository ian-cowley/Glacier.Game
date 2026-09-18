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
}
