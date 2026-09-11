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
}
