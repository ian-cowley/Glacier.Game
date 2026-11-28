namespace Glacier.Game.Tests;

using System;
using System.Collections.Generic;
using Glacier.Desktop.Audio;
using Glacier.Game.Audio;
using Glacier.Game.Collision;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Graphics;
using Xunit;

public sealed class GameHardeningTests
{
    [Fact]
    public void CollisionSystem_WideSpanningEntityAndDenseCluster_DetectsAllPairs()
    {
        // 1. Wide entity spanning multiple 64px cells collides with a small entity near its far edge
        using (var world = new World())
        using (var collision = new CollisionSystem(cellSize: 64f))
        {
            world.CreateEntity(new Position2D(0f, 32f), new AABB2D(0f, -10f, 320f, 10f));
            world.CreateEntity(new Position2D(310f, 32f), new AABB2D(-5f, -5f, 5f, 5f));

            collision.Update(world, 0.016f);
            Assert.Equal(1, collision.CollisionCount);
        }

        // 2. Dense 300-entity cluster (>256 neighbors + triggers SIMD CheckCollisionsAvx512)
        using (var world = new World())
        using (var collision = new CollisionSystem(cellSize: 64f))
        {
            const int n = 300;
            for (int i = 0; i < n; i++)
            {
                world.CreateEntity(new Position2D(100f + (i % 5) * 0.5f, 100f), new AABB2D(-10f, -10f, 10f, 10f));
            }

            collision.Update(world, 0.016f);
            Assert.Equal(n * (n - 1) / 2, collision.CollisionCount);
        }
    }

    [Fact]
    public void PhysicsKernels_VectorizedClampAndBounce_Damping_AndGravity_MatchScalar()
    {
        const int count = 67; // Exercises SIMD vector chunks + scalar remainder tail
        var posX = new float[count];
        var posY = new float[count];
        var velX = new float[count];
        var velY = new float[count];
        var velocities = new Velocity2D[count];
        var positions = new Position2D[count];

        for (int i = 0; i < count; i++)
        {
            posX[i] = i % 3 == 0 ? -5f : (i % 3 == 1 ? 105f : 50f);
            posY[i] = i % 4 == 0 ? -2f : (i % 4 == 1 ? 210f : 100f);
            velX[i] = (i % 5 - 2) * 10f;
            velY[i] = (i % 7 - 3) * 8f;
            positions[i] = new Position2D(posX[i], posY[i]);
            velocities[i] = new Velocity2D(velX[i], velY[i]);
        }

        PhysicsKernels.ClampAndBounce(posX, posY, velX, velY, 0f, 100f, 0f, 200f, restitution: 0.8f);
        PhysicsKernels.ClampAndBounce(positions, velocities, 0f, 100f, 0f, 200f, restitution: 0.8f);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(posX[i], positions[i].X, 4);
            Assert.Equal(posY[i], positions[i].Y, 4);
            Assert.Equal(velX[i], velocities[i].X, 4);
            Assert.Equal(velY[i], velocities[i].Y, 4);
            Assert.InRange(posX[i], 0f, 100f);
            Assert.InRange(posY[i], 0f, 200f);
        }

        PhysicsKernels.ApplyGravityY(velocities, gravityY: 10f, dt: 0.5f);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(velY[i] + 5.0f, velocities[i].Y, 4);
        }

        using var world = new World();
        var sys = new SimdPhysicsSystem(0f, 1000f, 0f, 1000f) { Drag = 0.2f };
        var e = world.CreateEntity(new Position2D(50f, 50f), new Velocity2D(100f, 0f));
        sys.Update(world, 1.0f);
        Assert.Equal(80f, world.GetComponent<Velocity2D>(e).X, 3);
    }

    [Fact]
    public void GlacierGraphicsGameRenderer_ScanlineRasterizer_RendersFlatAndInterpolatedColors()
    {
        using var renderer = new GlacierGraphicsGameRenderer(64, 64);

        renderer.Begin(System.Numerics.Matrix3x2.Identity);

        // Flat solid green quad at [4..24] x [4..24]
        Vertex2D[] quadVerts =
        [
            new Vertex2D(4f, 4f, 0f, 0f, new Color32(10, 220, 40, 255)),
            new Vertex2D(24f, 4f, 1f, 0f, new Color32(10, 220, 40, 255)),
            new Vertex2D(24f, 24f, 1f, 1f, new Color32(10, 220, 40, 255)),
            new Vertex2D(4f, 24f, 0f, 1f, new Color32(10, 220, 40, 255)),
        ];
        uint[] quadIdx = [0, 1, 2, 2, 3, 0];
        renderer.DrawBatch(quadVerts, quadIdx, textureId: 0);

        // Per-vertex colored triangle across [30..60] x [10..55]
        Vertex2D[] triVerts =
        [
            new Vertex2D(45f, 10f, 0f, 0f, Color32.Red),
            new Vertex2D(30f, 55f, 0f, 1f, Color32.Green),
            new Vertex2D(60f, 55f, 1f, 1f, Color32.Blue),
        ];
        uint[] triIdx = [0, 1, 2];
        renderer.DrawBatch(triVerts, triIdx, textureId: 1);
        renderer.End();

        var flatPixel = renderer.Framebuffer.GetRowSpan(14)[14];
        Assert.Equal(10, flatPixel.R);
        Assert.Equal(220, flatPixel.G);
        Assert.Equal(40, flatPixel.B);

        // Centroid of (45,10), (30,55), (60,55) is (45, 40) -> blended RGB near (85, 85, 85)
        var blendedPixel = renderer.Framebuffer.GetRowSpan(40)[45];
        Assert.InRange(blendedPixel.R, 60, 115);
        Assert.InRange(blendedPixel.G, 60, 115);
        Assert.InRange(blendedPixel.B, 60, 115);
    }

    [Fact]
    public void AudioSystem_RespectsEmitterVolumeAndLoop()
    {
        using var mockDevice = new CapturingAudioDevice();
        using var audioMgr = new AudioManager(mockDevice);

        // 0.1s clip at 100 Hz mono = 10 samples of 1.0f
        float[] samples = new float[10];
        Array.Fill(samples, 1.0f);
        int clipId = audioMgr.RegisterClip(new AudioClip(samples, sampleRate: 100, channels: 1));

        using var world = new World();
        world.CreateEntity(new Position2D(0f, 0f), new AudioEmitter(clipId, PlayOnSpawn: true, Loop: true, Volume: 0.5f));

        var sys = new AudioSystem(audioMgr);

        // First tick: PlayOnSpawn triggers immediately with 0.5x scaling
        sys.Update(world, 0.016f);
        Assert.Single(mockDevice.PlayedBatches);
        Assert.All(mockDevice.PlayedBatches[0], s => Assert.Equal(0.5f, s, 4));

        // Before 0.1s elapses: no replay yet
        sys.Update(world, 0.05f);
        Assert.Single(mockDevice.PlayedBatches);

        // Once >= 0.1s elapses: loop re-triggers with 0.5x volume
        sys.Update(world, 0.06f);
        Assert.Equal(2, mockDevice.PlayedBatches.Count);
        Assert.All(mockDevice.PlayedBatches[1], s => Assert.Equal(0.5f, s, 4));
    }

    private sealed class CapturingAudioDevice : IAudioDevice
    {
        public int SampleRate => 100;
        public int Channels => 1;
        public List<float[]> PlayedBatches { get; } = new();

        public void Play(ReadOnlySpan<float> pcmSamples) => PlayedBatches.Add(pcmSamples.ToArray());
        public void SetMasterVolume(float volume) { }
        public void Dispose() { }
    }
}
