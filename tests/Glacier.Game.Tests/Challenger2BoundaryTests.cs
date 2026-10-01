namespace Glacier.Game.Tests;

using System;
using System.Diagnostics;
using System.Numerics;
using Glacier.Game.Audio;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Windowing;
using Glacier.Windowing.Audio.Simulated;
using Glacier.Windowing.Platform.Headless;
using Glacier.Windowing.Swapchain.Software;
using Xunit;

public class Challenger2BoundaryTests
{
    // =========================================================================
    // OBJECTIVE 1: AUDIO CLIPPING UNDER EXTREME VOLUME / FREQUENCY INPUTS
    // =========================================================================

    [Theory]
    [InlineData(1.5f)]
    [InlineData(10.0f)]
    [InlineData(100.0f)]
    [InlineData(10000.0f)]
    [InlineData(float.MaxValue)]
    public void AudioClip_ExtremeHighVolume_ClampedToUnitInterval(float extremeVolume)
    {
        // 1. Sine wave generation under extreme volume
        var sine = AudioClip.CreateSineWave(440f, 0.02f, volume: extremeVolume, sampleRate: 48000, channels: 2);
        Assert.True(sine.SampleCount > 0);

        var samples = sine.Samples;
        float maxAbs = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            Assert.False(float.IsNaN(s), $"Sample at {i} was NaN");
            Assert.False(float.IsInfinity(s), $"Sample at {i} was Infinity");
            Assert.InRange(s, -1.0f, 1.0f);
            if (MathF.Abs(s) > maxAbs) maxAbs = MathF.Abs(s);
        }

        // Peak volume clamped to 1.0, so maxAbs should approach ~1.0
        Assert.True(maxAbs > 0.95f, $"Expected max amplitude near 1.0, but got {maxAbs}");

        // 2. Noise generation under extreme volume
        var noise = AudioClip.CreateNoise(0.02f, volume: extremeVolume, sampleRate: 48000, channels: 2);
        Assert.True(noise.SampleCount > 0);
        var noiseSamples = noise.Samples;
        for (int i = 0; i < noiseSamples.Length; i++)
        {
            float s = noiseSamples[i];
            Assert.False(float.IsNaN(s), $"Noise sample at {i} was NaN");
            Assert.False(float.IsInfinity(s), $"Noise sample at {i} was Infinity");
            Assert.InRange(s, -1.0f, 1.0f);
        }
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(-1.0f)]
    [InlineData(-100.0f)]
    [InlineData(-10000.0f)]
    public void AudioClip_NegativeVolume_ClampedToSilence(float negativeVolume)
    {
        var sine = AudioClip.CreateSineWave(440f, 0.02f, volume: negativeVolume, sampleRate: 48000, channels: 2);
        var samples = sine.Samples;

        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(0.0f, samples[i]);
        }

        var noise = AudioClip.CreateNoise(0.02f, volume: negativeVolume, sampleRate: 48000, channels: 2);
        var noiseSamples = noise.Samples;

        for (int i = 0; i < noiseSamples.Length; i++)
        {
            Assert.Equal(0.0f, noiseSamples[i]);
        }
    }

    [Theory]
    [InlineData(0.001f)]        // Sub-audible / DC-near frequency
    [InlineData(20.0f)]         // Lower human hearing threshold
    [InlineData(20000.0f)]      // Upper human hearing threshold
    [InlineData(24000.0f)]      // Exact Nyquist limit for 48kHz
    [InlineData(24001.0f)]      // Above Nyquist (aliasing regime)
    [InlineData(100000.0f)]     // Extreme ultrasonic
    [InlineData(1000000.0f)]    // 1 MHz
    [InlineData(-440.0f)]       // Negative frequency
    public void AudioClip_ExtremeFrequencies_ProducesBoundedFiniteSamples(float freqHz)
    {
        var sine = AudioClip.CreateSineWave(freqHz, 0.02f, volume: 0.8f, sampleRate: 48000, channels: 2);
        Assert.True(sine.SampleCount > 0);

        var samples = sine.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            Assert.False(float.IsNaN(s), $"Sample at {i} for freq {freqHz} was NaN");
            Assert.False(float.IsInfinity(s), $"Sample at {i} for freq {freqHz} was Infinity");
            Assert.InRange(s, -1.0f, 1.0f);
        }
    }

    [Fact]
    public void AudioManager_MasterVolume_BoundaryClampingAndTonePlayback()
    {
        using var simDevice = new SimulatedAudioDevice(48000, 2, bufferCapacity: 32768);
        using var audioManager = new AudioManager(simDevice);

        // Extreme positive volume clamps to 1.0f
        audioManager.SetMasterVolume(9999.0f);
        Assert.Equal(1.0f, audioManager.MasterVolume);
        Assert.Equal(1.0f, simDevice.MasterVolume);

        // Extreme negative volume clamps to 0.0f
        audioManager.SetMasterVolume(-500.0f);
        Assert.Equal(0.0f, audioManager.MasterVolume);
        Assert.Equal(0.0f, simDevice.MasterVolume);

        // Tone playback with extreme volume
        audioManager.SetMasterVolume(1.0f);
        audioManager.PlayTone(frequencyHz: 80000.0f, durationSec: 0.01f, volume: 50.0f);

        // Verify samples were written to the simulated device
        Assert.True(simDevice.TotalSamplesWritten > 0);
    }

    [Fact]
    public void AudioStream_RingBufferSaturation_HandlesUnderrunsGracefully()
    {
        // Buffer capacity = 256 samples
        using var simDevice = new SimulatedAudioDevice(48000, 2, bufferCapacity: 256);
        using var audioManager = new AudioManager(simDevice, stream: simDevice);

        float[] largeBuffer = new float[1024];
        Array.Fill(largeBuffer, 0.5f);

        // Streaming 1024 samples into a 256 capacity ring buffer should saturate without crashing
        bool wrote = audioManager.StreamSamples(largeBuffer);
        Assert.False(wrote); // Failed to write because capacity exceeded
        Assert.True(simDevice.UnderrunCount > 0);

        // Drain simulated device
        int drained = simDevice.Drain(256);
        Assert.True(drained >= 0);
    }

    // =========================================================================
    // OBJECTIVE 2: SWAPCHAIN RESIZE & FRAME PRESENTATION (1x1, 4096x4096)
    // =========================================================================

    [Fact]
    public void SoftwareSwapchain_BoundaryDimension_1x1_LifecycleAndPresentation()
    {
        var desc = new SwapchainDescription(1, 1, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);

        Assert.Equal(1, swapchain.Width);
        Assert.Equal(1, swapchain.Height);
        Assert.Equal(2, swapchain.BufferCount);
        Assert.Equal((nuint)4, swapchain.BufferByteSize); // 1 * 1 * 4 bytes
        Assert.NotEqual(IntPtr.Zero, swapchain.CurrentBackBuffer);

        // Present 10 frames at 1x1
        for (ulong i = 1; i <= 10; i++)
        {
            swapchain.Present();
            Assert.Equal(i, swapchain.PresentCount);
            Assert.NotEqual(IntPtr.Zero, swapchain.CurrentBackBuffer);
        }
    }

    [Fact]
    public void SoftwareSwapchain_BoundaryDimension_4096x4096_LifecycleAndPresentation()
    {
        var desc = new SwapchainDescription(4096, 4096, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);

        Assert.Equal(4096, swapchain.Width);
        Assert.Equal(4096, swapchain.Height);
        Assert.Equal((nuint)(4096 * 4096 * 4), swapchain.BufferByteSize); // 67,108,864 bytes (64 MB)
        Assert.NotEqual(IntPtr.Zero, swapchain.CurrentBackBuffer);

        // Present 5 frames at 4096x4096
        for (ulong i = 1; i <= 5; i++)
        {
            swapchain.Present();
            Assert.Equal(i, swapchain.PresentCount);
            Assert.NotEqual(IntPtr.Zero, swapchain.CurrentBackBuffer);
        }
    }

    [Fact]
    public void SoftwareSwapchain_DynamicResize_1x1_To_4096x4096_Cycle()
    {
        var desc = new SwapchainDescription(1, 1, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);

        Assert.Equal((nuint)4, swapchain.BufferByteSize);
        swapchain.Present();
        Assert.Equal(1UL, swapchain.PresentCount);

        // Resize up to 4096x4096
        swapchain.Resize(4096, 4096);
        Assert.Equal(4096, swapchain.Width);
        Assert.Equal(4096, swapchain.Height);
        Assert.Equal((nuint)(4096 * 4096 * 4), swapchain.BufferByteSize);
        swapchain.Present();
        Assert.Equal(2UL, swapchain.PresentCount);

        // Resize back down to 1x1
        swapchain.Resize(1, 1);
        Assert.Equal(1, swapchain.Width);
        Assert.Equal(1, swapchain.Height);
        Assert.Equal((nuint)4, swapchain.BufferByteSize);
        swapchain.Present();
        Assert.Equal(3UL, swapchain.PresentCount);
    }

    [Fact]
    public void GlacierGraphicsGameRenderer_Boundary1x1_RendersAndPresentsToSwapchain()
    {
        using var renderer = new GlacierGraphicsGameRenderer(1, 1);
        var desc = new SwapchainDescription(1, 1, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);
        renderer.Swapchain = swapchain;

        using var batch = new SpriteBatch(renderer, maxQuads: 16);
        batch.Begin();
        batch.DrawQuad(0, 0, 1, 1, new Color32(255, 0, 0, 255));
        batch.End();

        IntPtr backBuffer = swapchain.CurrentBackBuffer;
        renderer.Present();
        swapchain.Present();

        Assert.Equal(1L, renderer.TotalDrawCalls);
        Assert.Equal(4L, renderer.TotalVerticesRendered);
        Assert.Equal(1L, renderer.TotalFramesPresented);
        Assert.Equal(1UL, swapchain.PresentCount);

        // Verify the single pixel in swapchain backbuffer received the rasterized quad
        unsafe
        {
            byte* ptr = (byte*)backBuffer;
            Assert.True(ptr[0] >= 190, $"Expected red >= 190, actual was {ptr[0]}"); // R (anti-aliased seam coverage)
            Assert.Equal(0, ptr[1]); // G
            Assert.Equal(0, ptr[2]); // B
            Assert.True(ptr[3] >= 190, $"Expected alpha >= 190, actual was {ptr[3]}"); // A
        }
    }

    [Fact]
    public void GlacierGraphicsGameRenderer_Boundary4096x4096_RendersAndPresentsToSwapchain()
    {
        using var renderer = new GlacierGraphicsGameRenderer(4096, 4096);
        var desc = new SwapchainDescription(4096, 4096, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);
        renderer.Swapchain = swapchain;

        using var batch = new SpriteBatch(renderer, maxQuads: 16);
        batch.Begin();
        batch.DrawQuad(2000, 2000, 10, 10, new Color32(0, 255, 128, 255));
        batch.End();

        IntPtr backBuffer = swapchain.CurrentBackBuffer;
        renderer.Present();
        swapchain.Present();

        Assert.Equal(1L, renderer.TotalDrawCalls);
        Assert.Equal(4L, renderer.TotalVerticesRendered);
        Assert.Equal(1L, renderer.TotalFramesPresented);
        Assert.Equal(1UL, swapchain.PresentCount);

        // Verify sampled interior pixel in the 64MB swapchain backbuffer
        unsafe
        {
            byte* ptr = (byte*)backBuffer;
            // Sample interior of triangle (2002, 2006) well inside the quad bounds
            int offset = (2006 * 4096 + 2002) * 4;
            Assert.Equal(0, ptr[offset + 0]);   // R
            Assert.Equal(255, ptr[offset + 1]); // G
            Assert.Equal(128, ptr[offset + 2]); // B
            Assert.Equal(255, ptr[offset + 3]); // A
        }
    }

    [Fact]
    public void GameEngine_HeadlessResize_BoundaryCycle_1x1_and_4096x4096()
    {
        var config = new WindowConfig { Headless = true, Width = 1, Height = 1 };
        using var engine = new GameEngine(config);

        // Initial render at 1x1
        engine.Render();
        Assert.NotNull(engine.Swapchain);
        Assert.Equal(1, engine.Swapchain.Width);
        Assert.Equal(1, engine.Swapchain.Height);
        Assert.Equal(1UL, ((SoftwareSwapchain)engine.Swapchain).PresentCount);

        // Resize window to 4096x4096
        var headlessWin = (HeadlessWindow)engine.Window;
        headlessWin.Size = new WindowSize(4096, 4096);

        engine.Render();
        Assert.Equal(4096, engine.Swapchain.Width);
        Assert.Equal(4096, engine.Swapchain.Height);
        Assert.Equal(4096, engine.Renderer.Width);
        Assert.Equal(4096, engine.Renderer.Height);
        Assert.Equal(2UL, ((SoftwareSwapchain)engine.Swapchain).PresentCount);

        // Resize window back to 1x1
        headlessWin.Size = new WindowSize(1, 1);

        engine.Render();
        Assert.Equal(1, engine.Swapchain.Width);
        Assert.Equal(1, engine.Swapchain.Height);
        Assert.Equal(1, engine.Renderer.Width);
        Assert.Equal(1, engine.Renderer.Height);
        Assert.Equal(3UL, ((SoftwareSwapchain)engine.Swapchain).PresentCount);
    }

    // =========================================================================
    // OBJECTIVE 3: SPRITE BATCH RENDERING WITH 0 SPRITES AND 100,000 SPRITES
    // =========================================================================

    [Fact]
    public void SpriteBatch_ZeroSprites_ProducesZeroDrawCallsAndZeroVertices()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        using var batch = new SpriteBatch(renderer, maxQuads: 1024);

        batch.Begin();
        // Zero sprites drawn
        batch.End();

        Assert.Equal(0L, renderer.TotalDrawCalls);
        Assert.Equal(0L, renderer.TotalVerticesRendered);
        Assert.Equal(0, batch.QuadCount);
    }

    [Fact]
    public void SpriteBatch_MultipleZeroSpriteBatches_MaintainsPristineState()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        using var batch = new SpriteBatch(renderer, maxQuads: 1024);

        for (int i = 0; i < 20; i++)
        {
            batch.Begin();
            batch.End();
        }

        Assert.Equal(0L, renderer.TotalDrawCalls);
        Assert.Equal(0L, renderer.TotalVerticesRendered);
        Assert.Equal(0, batch.QuadCount);
    }

    [Fact]
    public void GameEngine_RenderWorld_ZeroEntities_PresentsCleanFrame()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        Assert.Equal(0, engine.World.EntityCount);

        engine.Render();

        var ggr = Assert.IsType<GlacierGraphicsGameRenderer>(engine.Renderer);
        Assert.Equal(0L, ggr.TotalDrawCalls);
        Assert.Equal(0L, ggr.TotalVerticesRendered);
        Assert.Equal(1L, ggr.TotalFramesPresented);
        Assert.Equal(1UL, ((SoftwareSwapchain)engine.Swapchain!).PresentCount);
    }

    [Fact]
    public void SpriteBatch_100kSprites_HeadlessRenderer_DefaultBatchSize()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        // Default maxQuads = 65,536
        using var batch = new SpriteBatch(renderer, maxQuads: 65536);

        const int spriteCount = 100_000;
        var sw = Stopwatch.StartNew();

        batch.Begin();
        for (int i = 0; i < spriteCount; i++)
        {
            batch.DrawQuad(i % 1920, (i / 1920) % 1080, 2f, 2f, new Color32(255, 255, 255, 255));
        }
        batch.End();

        sw.Stop();

        // 100,000 quads with maxQuads 65,536:
        // Batch 1: 65,536 quads -> Flush()
        // Batch 2: 34,464 quads -> Flush() at End()
        Assert.Equal(2L, renderer.TotalDrawCalls);
        Assert.Equal(spriteCount * 4L, renderer.TotalVerticesRendered); // 400,000 vertices
        Assert.Equal(0, batch.QuadCount);

        // Throughput sanity check: 100,000 sprites should batch in < 100 ms on modern hardware
        Assert.True(sw.ElapsedMilliseconds < 1000, $"100,000 sprites took {sw.ElapsedMilliseconds} ms, expected < 1000 ms");
    }

    [Fact]
    public void SpriteBatch_100kSprites_SmallBatchSize_StressFlushing()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        // maxQuads = 1,000 -> 100 flushes for 100,000 sprites
        using var batch = new SpriteBatch(renderer, maxQuads: 1000);

        const int spriteCount = 100_000;
        batch.Begin();
        for (int i = 0; i < spriteCount; i++)
        {
            batch.DrawQuad(i % 100, i % 100, 1f, 1f, Color32.White);
        }
        batch.End();

        Assert.Equal(100L, renderer.TotalDrawCalls);
        Assert.Equal(spriteCount * 4L, renderer.TotalVerticesRendered);
        Assert.Equal(0, batch.QuadCount);
    }

    [Fact]
    public void SpriteBatch_100kSprites_LargeBatchSize_SingleBatch()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        // maxQuads = 131,072 -> all 100,000 sprites fit in 1 batch
        using var batch = new SpriteBatch(renderer, maxQuads: 131072);

        const int spriteCount = 100_000;
        batch.Begin();
        for (int i = 0; i < spriteCount; i++)
        {
            batch.DrawQuad(i % 100, i % 100, 1f, 1f, Color32.White);
        }
        batch.End();

        Assert.Equal(1L, renderer.TotalDrawCalls);
        Assert.Equal(spriteCount * 4L, renderer.TotalVerticesRendered);
        Assert.Equal(0, batch.QuadCount);
    }

    [Fact]
    public void SpriteBatch_100kSprites_TextureSwitching_FlushesPerTexture()
    {
        using var renderer = new HeadlessRenderer(1920, 1080);
        using var batch = new SpriteBatch(renderer, maxQuads: 131072);

        const int spriteCount = 100_000;
        batch.Begin();
        for (int i = 0; i < spriteCount; i++)
        {
            int textureId = i < 50_000 ? 1 : 2;
            batch.DrawSprite(textureId, i % 100, i % 100, 1f, 1f, Color32.White);
        }
        batch.End();

        // Should flush once at texture change (at i = 50,000) and once at End()
        Assert.Equal(2L, renderer.TotalDrawCalls);
        Assert.Equal(spriteCount * 4L, renderer.TotalVerticesRendered);
        Assert.Equal(2, renderer.LastTextureId);
    }

    [Fact]
    public void SpriteBatch_100kSprites_GlacierGraphicsGameRenderer_Execution()
    {
        // Test 100,000 sprites through GlacierGraphicsGameRenderer (pure C# CPU rasterizer)
        using var renderer = new GlacierGraphicsGameRenderer(640, 480);
        // Use batch size of 65536
        using var batch = new SpriteBatch(renderer, maxQuads: 65536);

        const int spriteCount = 100_000;
        var sw = Stopwatch.StartNew();

        batch.Begin();
        for (int i = 0; i < spriteCount; i++)
        {
            // Small 1x1 quads placed across framebuffer
            batch.DrawQuad(i % 640, (i / 640) % 480, 1f, 1f, new Color32(100, 150, 200, 255));
        }
        batch.End();
        renderer.Present();

        sw.Stop();

        Assert.Equal(2L, renderer.TotalDrawCalls);
        Assert.Equal(spriteCount * 4L, renderer.TotalVerticesRendered);
        Assert.Equal(1L, renderer.TotalFramesPresented);
    }
}
