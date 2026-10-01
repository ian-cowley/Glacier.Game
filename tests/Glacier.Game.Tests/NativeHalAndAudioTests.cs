namespace Glacier.Game.Tests;

using System;
using System.Numerics;
using System.Reflection;
using Glacier.Game.Audio;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Windowing;
using Glacier.Windowing.Platform.Headless;
using Glacier.Windowing.Swapchain.Software;
using Xunit;

public class NativeHalAndAudioTests
{
    [Fact]
    public void GameEngine_DefaultRenderer_IsGlacierGraphicsGameRenderer()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        Assert.IsType<GlacierGraphicsGameRenderer>(engine.Renderer);
        Assert.Equal(800, engine.Renderer.Width);
        Assert.Equal(600, engine.Renderer.Height);
    }

    [Fact]
    public void GameEngine_HeadlessWindowAndSwapchain_PresentsFrames()
    {
        var config = new WindowConfig { Headless = true, Width = 640, Height = 480 };
        using var engine = new GameEngine(config);

        Assert.NotNull(engine.Window);
        Assert.IsType<HeadlessWindow>(engine.Window);

        // Render first frame
        engine.Render();

        // Check swapchain creation and presentation
        Assert.NotNull(engine.Swapchain);
        Assert.IsType<SoftwareSwapchain>(engine.Swapchain);
        Assert.Equal(640, engine.Swapchain.Width);
        Assert.Equal(480, engine.Swapchain.Height);

        var sw = (SoftwareSwapchain)engine.Swapchain;
        Assert.Equal(1UL, sw.PresentCount);

        // Render second frame
        engine.Render();
        Assert.Equal(2UL, sw.PresentCount);
    }

    [Fact]
    public void GlacierGraphicsGameRenderer_BlitsToSoftwareSwapchain()
    {
        using var renderer = new GlacierGraphicsGameRenderer(320, 240);
        var desc = new SwapchainDescription(320, 240, BufferCount: 2);
        using var swapchain = new SoftwareSwapchain(desc);
        renderer.Swapchain = swapchain;

        using var batch = new SpriteBatch(renderer, maxQuads: 64);
        batch.Begin();
        batch.DrawQuad(10, 10, 50, 50, new Color32(255, 128, 0, 255));
        batch.End();

        IntPtr backBufferBefore = swapchain.CurrentBackBuffer;
        renderer.Present();

        // Verify pixel was rendered into LinearFramebuffer and copied to swapchain backbuffer
        var fb = renderer.Framebuffer;
        var pixel = fb.GetPixel(30, 20);
        Assert.Equal(255, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(0, pixel.B);

        unsafe
        {
            byte* ptr = (byte*)backBufferBefore;
            int offset = (20 * 320 + 30) * 4;
            Assert.Equal(255, ptr[offset + 0]);
            Assert.Equal(128, ptr[offset + 1]);
            Assert.Equal(0, ptr[offset + 2]);
            Assert.Equal(255, ptr[offset + 3]);
        }
    }

    [Fact]
    public void GameEngine_Sub3msWasapiAudio_InitializedAndPlaysClips()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        Assert.NotNull(engine.AudioDevice);
        Assert.NotNull(engine.Audio);
        Assert.True(engine.AudioDevice.SampleRate > 0);
        Assert.True(engine.AudioDevice.Channels > 0);

        // Test master volume
        engine.SetMasterVolume(0.75f);
        Assert.Equal(0.75f, engine.Audio.MasterVolume, 2);

        // Test sine wave synthesis
        var sine = AudioClip.CreateSineWave(440f, 0.05f, 0.5f, engine.AudioDevice.SampleRate, engine.AudioDevice.Channels);
        Assert.True(sine.SampleCount > 0);
        Assert.True(sine.DurationSec > 0.04f);
        engine.PlaySound(sine);

        // Test noise burst synthesis
        var noise = AudioClip.CreateNoise(0.02f, 0.3f, engine.AudioDevice.SampleRate, engine.AudioDevice.Channels);
        Assert.True(noise.SampleCount > 0);
        engine.PlaySound(noise);

        // Test direct tone playback
        engine.PlayTone(880f, 0.02f, 0.25f);
    }

    [Fact]
    public void GameEngine_AudioSystem_TickingEcsEmitters()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        var audioSys = new AudioSystem(engine.Audio!);
        engine.AddSystem(audioSys);

        var clip = AudioClip.CreateSineWave(523.25f, 0.01f);
        int clipId = engine.Audio!.RegisterClip(clip);

        var entity = engine.World.CreateEntity(
            new Position2D(10f, 10f),
            new AudioEmitter(clipId, PlayOnSpawn: true, Volume: 0.5f)
        );

        engine.Step(0.016f);

        ref readonly var emitter = ref engine.World.GetComponent<AudioEmitter>(entity);
        Assert.False(emitter.PlayOnSpawn);
    }

    [Fact]
    public void GameEngine_InputPipeline_ProcessesKeyboardAndMouseEvents()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        Key pressedKey = Key.Unknown;
        engine.KeyDown += k => pressedKey = k;

        var headlessWin = (HeadlessWindow)engine.Window;
        headlessWin.EnqueueInput(new InputEvent(InputEventType.KeyDown, 32, 0, 0, 100)); // Space
        headlessWin.EnqueueInput(new InputEvent(InputEventType.MouseMove, 0, 150f, 200f, 101));
        headlessWin.EnqueueInput(new InputEvent(InputEventType.MouseDown, 0, 150f, 200f, 102)); // Left button
        headlessWin.PollEvents();

        Assert.Equal(Key.Space, pressedKey);
        Assert.Equal(new Vector2(150f, 200f), engine.MousePosition);
        Assert.True(engine.IsLeftMouseDown);
        Assert.True(engine.Input.Keyboard.IsKeyDown(32));
        Assert.True(engine.Input.Mouse.LeftButton);

        // Test release
        headlessWin.EnqueueInput(new InputEvent(InputEventType.KeyUp, 32, 0, 0, 103));
        headlessWin.EnqueueInput(new InputEvent(InputEventType.MouseUp, 0, 150f, 200f, 104));
        headlessWin.PollEvents();

        Assert.False(engine.IsLeftMouseDown);
        Assert.False(engine.Input.Keyboard.IsKeyDown(32));
        Assert.False(engine.Input.Mouse.LeftButton);
    }

    [Fact]
    public void GameEngine_HeadlessRun_SimulationLoopAndStop()
    {
        var config = new WindowConfig { Headless = true, Width = 640, Height = 480, FixedTimeStepHz = 120 };
        using var engine = new GameEngine(config);

        int updateCount = 0;
        engine.Update += t =>
        {
            updateCount++;
            if (updateCount >= 5)
            {
                engine.Stop();
            }
        };

        engine.Run();

        Assert.False(engine.IsRunning);
        Assert.True(updateCount >= 5);
        Assert.True(engine.Time.FrameCount >= 5);
    }

    [Fact]
    public void ZeroSilkNetAssemblies_ReferencedByGlacierGame()
    {
        var gameAssembly = typeof(GameEngine).Assembly;
        var referencedAssemblies = gameAssembly.GetReferencedAssemblies();

        foreach (var asm in referencedAssemblies)
        {
            Assert.DoesNotContain("Silk", asm.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw", asm.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sdl", asm.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
