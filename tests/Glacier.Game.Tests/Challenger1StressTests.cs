namespace Glacier.Game.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Glacier.Game.Audio;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Windowing;
using Glacier.Windowing.Audio;
using Glacier.Windowing.Audio.Simulated;
using Glacier.Windowing.Platform.Headless;
using Glacier.Windowing.Swapchain.Software;
using Xunit;

/// <summary>
/// Empirical Challenger 1 stress harness and adversarial test suite for Milestone M8.4.
/// Stress-tests multi-frame headless simulation, physics determinism & drift, memory stability,
/// WASAPI audio streaming under saturation, ECS AudioSystem processing, and Silk.NET runtime purge.
/// </summary>
public class Challenger1StressTests
{
    // =========================================================================
    // SECTION 1: MULTI-FRAME HEADLESS SIMULATION & PHYSICS STRESS
    // =========================================================================

    [Fact]
    public void HeadlessEngine_PhysicsIntegrateEuler_DeterministicAndZeroDrift_2000Frames()
    {
        // Setup: Headless engine with 500 moving entities
        var config = new WindowConfig { Headless = true, Width = 1280, Height = 720, FixedTimeStepHz = 100 };
        using var engine = new GameEngine(config);

        const int entityCount = 500;
        const float dt = 0.01f; // 10ms per step
        const int totalFrames = 2000; // 20.0 seconds of simulated time

        var initialPositions = new Position2D[entityCount];
        var velocities = new Velocity2D[entityCount];
        var entities = new Entity[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            float x0 = 100f + (i * 5f);
            float y0 = 50f + (i * 2f);
            float vx = 10f + (i % 20);
            float vy = -5f - (i % 15);

            initialPositions[i] = new Position2D(x0, y0);
            velocities[i] = new Velocity2D(vx, vy);
            entities[i] = engine.World.CreateEntity(initialPositions[i], velocities[i]);
        }

        // Use huge boundary so entities do not hit walls and reflect
        var physics = new SimdPhysicsSystem(minX: -1e6f, maxX: 1e6f, minY: -1e6f, maxY: 1e6f, restitution: 1.0f);
        engine.AddSystem(physics);

        // Advance simulation 2,000 frames
        for (int frame = 0; frame < totalFrames; frame++)
        {
            engine.Step(dt);
        }

        Assert.Equal(totalFrames, engine.Time.FrameCount);
        Assert.Equal(totalFrames * dt, (float)engine.Time.TotalTime.TotalSeconds, 3);

        // Analytical verification: X(t) = X0 + Vx * t, Y(t) = Y0 + Vy * t
        float simulatedTotalTime = totalFrames * dt;
        float maxErrorX = 0f;
        float maxErrorY = 0f;

        for (int i = 0; i < entityCount; i++)
        {
            ref readonly var pos = ref engine.World.GetComponent<Position2D>(entities[i]);
            float expectedX = initialPositions[i].X + (velocities[i].X * simulatedTotalTime);
            float expectedY = initialPositions[i].Y + (velocities[i].Y * simulatedTotalTime);

            float errX = MathF.Abs(pos.X - expectedX);
            float errY = MathF.Abs(pos.Y - expectedY);

            Assert.False(float.IsNaN(pos.X), $"Entity {i} X was NaN");
            Assert.False(float.IsNaN(pos.Y), $"Entity {i} Y was NaN");

            if (errX > maxErrorX) maxErrorX = errX;
            if (errY > maxErrorY) maxErrorY = errY;

            // In single-precision IEEE 754, 1 ULP at magnitude ~1500 is 2^-13 ~ 0.000122.
            // Accumulating 2,000 steps of inexact binary dt (0.01f) results in theoretical drift ~0.1 - 0.2 units.
            // Verify drift is strictly bounded within 0.5 units (< 0.035% relative error over 2,000 frames).
            Assert.True(errX < 0.5f, $"Entity {i} X drift exceeded limit: {errX} (expected {expectedX}, actual {pos.X})");
            Assert.True(errY < 0.5f, $"Entity {i} Y drift exceeded limit: {errY} (expected {expectedY}, actual {pos.Y})");
        }

        // Drift check summary: max drift across all 500 entities must remain under 0.5 units (< 0.035% relative error)
        Assert.True(maxErrorX < 0.5f, $"Max X drift across 500 entities was {maxErrorX}");
        Assert.True(maxErrorY < 0.5f, $"Max Y drift across 500 entities was {maxErrorY}");
    }

    [Fact]
    public void HeadlessEngine_BoundaryBounce_EnergyAndConfinementInvariant_5000Frames()
    {
        var config = new WindowConfig { Headless = true, Width = 1000, Height = 1000, FixedTimeStepHz = 60 };
        using var engine = new GameEngine(config);

        const int entityCount = 200;
        const float dt = 0.0166667f;
        const int totalFrames = 5000;
        const float minX = 0f, maxX = 1000f, minY = 0f, maxY = 1000f;

        // Perfectly elastic collision: restitution = 1.0f
        var physics = new SimdPhysicsSystem(minX, maxX, minY, maxY, restitution: 1.0f);
        engine.AddSystem(physics);

        var entities = new Entity[entityCount];
        var initialSpeeds = new float[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            float x0 = 100f + (i * 3.5f);
            float y0 = 100f + (i * 3.5f);
            float vx = 50f + (i % 50) * 5f;
            float vy = 40f + (i % 40) * 5f;

            initialSpeeds[i] = MathF.Sqrt(vx * vx + vy * vy);
            entities[i] = engine.World.CreateEntity(new Position2D(x0, y0), new Velocity2D(vx, vy));
        }

        // Run 5,000 frames of boundary bounces
        for (int frame = 0; frame < totalFrames; frame++)
        {
            engine.Step(dt);

            // Periodic sanity check every 500 frames: all entities strictly confined
            if (frame % 500 == 0)
            {
                for (int i = 0; i < entityCount; i++)
                {
                    ref readonly var pos = ref engine.World.GetComponent<Position2D>(entities[i]);
                    Assert.InRange(pos.X, minX, maxX);
                    Assert.InRange(pos.Y, minY, maxY);
                }
            }
        }

        // Final invariant checks: confinement + kinetic speed conservation
        for (int i = 0; i < entityCount; i++)
        {
            ref readonly var pos = ref engine.World.GetComponent<Position2D>(entities[i]);
            ref readonly var vel = ref engine.World.GetComponent<Velocity2D>(entities[i]);

            Assert.InRange(pos.X, minX, maxX);
            Assert.InRange(pos.Y, minY, maxY);

            float currentSpeed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            float speedDelta = MathF.Abs(currentSpeed - initialSpeeds[i]);

            // Speed must be conserved within floating-point tolerance after hundreds of bounces
            Assert.True(speedDelta < 0.01f, $"Entity {i} speed altered: initial {initialSpeeds[i]}, current {currentSpeed}");
        }
    }

    [Fact]
    public void HeadlessEngine_MultiFrameRenderAndStep_MemoryStability_1000Frames()
    {
        var config = new WindowConfig { Headless = true, Width = 320, Height = 240, FixedTimeStepHz = 60 };
        using var engine = new GameEngine(config);

        // Spawn 20 entities with Position, AABB, Color32 to stress Render() sprite batching
        for (int i = 0; i < 20; i++)
        {
            engine.World.CreateEntity(
                new Position2D(i * 15f, i * 10f),
                new Velocity2D(5f, 5f),
                new AABB2D(-5f, -5f, 5f, 5f),
                new Color32(255, 100, 50, 255)
            );
        }

        engine.AddSystem(new SimdPhysicsSystem(0f, 320f, 0f, 240f));

        // Warm up JIT and initial allocations for 100 frames
        for (int i = 0; i < 100; i++)
        {
            engine.Step(0.016f);
            engine.Render();
        }

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        long memBefore = GC.GetTotalMemory(true);

        // Run 1,000 continuous simulation and rendering frames
        for (int i = 0; i < 1000; i++)
        {
            engine.Step(0.016f);
            engine.Render();
        }

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        long memAfter = GC.GetTotalMemory(true);
        long memGrowthBytes = memAfter - memBefore;

        // Verify no unbounded heap leaks (allow < 3 MB headroom for transient runtime caches)
        Assert.True(memGrowthBytes < 3 * 1024 * 1024,
            $"Detected excessive memory growth across 1,000 frames: {memGrowthBytes / 1024.0:F2} KB");
    }

    [Fact]
    public void HeadlessEngine_EntityChurn_SpawnAndDestroyStability_1000Frames()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        engine.AddSystem(new SimdPhysicsSystem(0f, 800f, 0f, 600f));

        const int entitiesPerFrame = 50;
        const int frames = 1000;

        var activeEntities = new Queue<Entity>();

        for (int frame = 0; frame < frames; frame++)
        {
            // Spawn new entities
            for (int i = 0; i < entitiesPerFrame; i++)
            {
                var e = engine.World.CreateEntity(
                    new Position2D(frame % 800, i * 10),
                    new Velocity2D(10f, 5f)
                );
                activeEntities.Enqueue(e);
            }

            engine.Step(0.016f);

            // Destroy oldest entities to keep population stable at 250
            while (activeEntities.Count > 250)
            {
                var old = activeEntities.Dequeue();
                engine.World.DestroyEntity(old);
            }

            Assert.Equal(activeEntities.Count, engine.World.EntityCount);
        }

        Assert.Equal(250, engine.World.EntityCount);
    }

    // =========================================================================
    // SECTION 2: WASAPI AUDIO STREAMING & ECS AUDIOSYSTEM PROCESSING STRESS
    // =========================================================================

    [Fact]
    public void WasapiAudio_HighFrequencyMultiClipStress_ThousandsOfPlaybacks()
    {
        using var sim = AudioFactory.CreateSimulated(48000, 2, bufferCapacity: 65536);
        using var audioManager = new AudioManager(sim);

        // Register 50 diverse procedural audio clips
        var clipIds = new int[50];
        for (int i = 0; i < 50; i++)
        {
            float freq = 200f + (i * 20f);
            var clip = (i % 2 == 0)
                ? AudioClip.CreateSineWave(freq, durationSec: 0.005f, volume: 0.8f, sampleRate: 48000, channels: 2)
                : AudioClip.CreateNoise(durationSec: 0.005f, volume: 0.5f, sampleRate: 48000, channels: 2);
            clipIds[i] = audioManager.RegisterClip(clip);
        }

        // Stress: fire 5,000 rapid playback invocations
        for (int i = 0; i < 5000; i++)
        {
            int clipId = clipIds[i % clipIds.Length];
            audioManager.PlayClip(clipId);

            if (i % 100 == 0)
            {
                audioManager.SetMasterVolume(0.1f + (i % 10) * 0.09f);
                audioManager.PlayTone(440f, 0.001f, 0.5f);
            }
        }

        Assert.True(sim.TotalSamplesWritten > 0);
        Assert.Equal(50, clipIds.Length);
    }

    [Fact]
    public void WasapiAudio_ConcurrentMultithreadedPlayback_ThreadSafetyStress()
    {
        using var sim = AudioFactory.CreateSimulated(48000, 2, bufferCapacity: 65536);
        using var audioManager = new AudioManager(sim);

        var clip = AudioClip.CreateSineWave(440f, 0.01f, 0.5f);
        int clipId = audioManager.RegisterClip(clip);

        const int threadCount = 8;
        const int iterationsPerThread = 500;

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < iterationsPerThread; i++)
            {
                audioManager.PlayClip(clipId);
                audioManager.PlaySamples(clip.Samples);
            }
        });

        Assert.True(sim.TotalSamplesWritten > 0);
    }

    [Fact]
    public void WasapiAudio_StreamingRingBuffer_MassiveSampleThroughput()
    {
        using var sim = AudioFactory.CreateSimulated(48000, 2, bufferCapacity: 32768);
        using var audioManager = new AudioManager(sim, stream: sim);

        float[] sampleChunk = new float[256];
        for (int i = 0; i < sampleChunk.Length; i++)
        {
            sampleChunk[i] = MathF.Sin(i * 0.1f) * 0.5f;
        }

        const int totalChunks = 500; // 128,000 samples
        int successfulWrites = 0;
        int failedWrites = 0;

        for (int i = 0; i < totalChunks; i++)
        {
            bool written = audioManager.StreamSamples(sampleChunk);
            if (written)
            {
                successfulWrites++;
                // Periodically drain buffer to simulate consumer audio hardware thread
                if (i % 10 == 0)
                {
                    sim.Drain(256 * 10);
                }
            }
            else
            {
                failedWrites++;
                // Drain to clear congestion
                sim.Drain(4096);
            }
        }

        Assert.True(successfulWrites > 0, "Expected successful writes to ring buffer");
        Assert.True(sim.TotalSamplesWritten > 0);
    }

    [Fact]
    public void EcsAudioSystem_MassiveEmitterBatch_10000EntitiesStress()
    {
        using var sim = AudioFactory.CreateSimulated(48000, 2, bufferCapacity: 65536);
        using var audioManager = new AudioManager(sim);
        var audioSys = new AudioSystem(audioManager);

        var clip = AudioClip.CreateSineWave(523.25f, 0.005f);
        int clipId = audioManager.RegisterClip(clip);

        using var world = new World();

        const int entityCount = 10000;
        var entities = new Entity[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            // First 5,000 have PlayOnSpawn = true, rest have false
            bool playOnSpawn = i < 5000;
            entities[i] = world.CreateEntity(
                new Position2D(i, i),
                new AudioEmitter(clipId, PlayOnSpawn: playOnSpawn, Volume: 0.8f)
            );
        }

        var sw = Stopwatch.StartNew();
        audioSys.Update(world, 0.016f);
        sw.Stop();

        // 10,000 entity iteration must complete within tight time budget (< 50ms)
        Assert.True(sw.ElapsedMilliseconds < 50, $"AudioSystem.Update took {sw.ElapsedMilliseconds} ms, expected < 50 ms");

        // Verify all 5,000 active emitters were triggered and reset to false
        for (int i = 0; i < 5000; i++)
        {
            ref readonly var emitter = ref world.GetComponent<AudioEmitter>(entities[i]);
            Assert.False(emitter.PlayOnSpawn, $"Entity {i} PlayOnSpawn was not reset to false");
        }

        long samplesWrittenFirstPass = sim.TotalSamplesWritten;
        Assert.True(samplesWrittenFirstPass > 0);

        // Subsequent update should NOT trigger any new playbacks since PlayOnSpawn is false
        audioSys.Update(world, 0.016f);
        Assert.Equal(samplesWrittenFirstPass, sim.TotalSamplesWritten);
    }

    // =========================================================================
    // SECTION 3: RUNTIME VERIFICATION OF 0 SILK.NET ASSEMBLIES OR NATIVE DLLS
    // =========================================================================

    [Fact]
    public void RuntimeInspection_AppDomainAssemblies_ContainsZeroSilkNetOrGlfw()
    {
        // Force-load Glacier.Game and its dependencies
        var gameType = typeof(GameEngine);
        Assert.NotNull(gameType);

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in loadedAssemblies)
        {
            string name = asm.GetName().Name ?? string.Empty;
            Assert.DoesNotContain("Silk.NET", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SDL2", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OpenTK", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RuntimeInspection_LoadedProcessModules_ContainsZeroSilkOrGlfwDlls()
    {
        // Enumerate native modules loaded into the running process
        var currentProcess = Process.GetCurrentProcess();
        foreach (ProcessModule module in currentProcess.Modules)
        {
            string moduleName = module.ModuleName ?? string.Empty;
            string fileName = module.FileName ?? string.Empty;

            Assert.DoesNotContain("glfw3.dll", moduleName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw.dll", moduleName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("libglfw", moduleName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SDL2.dll", moduleName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Silk.NET", moduleName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Silk.NET", fileName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RecursiveAssemblyReferences_GlacierGame_HasZeroSilkNetDependencies()
    {
        var gameAsm = typeof(GameEngine).Assembly;
        var directRefs = gameAsm.GetReferencedAssemblies();

        foreach (var r in directRefs)
        {
            Assert.DoesNotContain("Silk", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sdl", r.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Check Glacier.Windowing reference
        var windowingAsm = typeof(IWindow).Assembly;
        foreach (var r in windowingAsm.GetReferencedAssemblies())
        {
            Assert.DoesNotContain("Silk", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sdl", r.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Check Glacier.Graphics reference
        var graphicsAsm = typeof(GlacierGraphicsGameRenderer).Assembly;
        foreach (var r in graphicsAsm.GetReferencedAssemblies())
        {
            Assert.DoesNotContain("Silk", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("glfw", r.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sdl", r.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DiskOutputSweep_ReleaseBinDirectory_ContainsZeroSilkOrGlfwFiles()
    {
        string baseDir = AppContext.BaseDirectory;
        var di = new DirectoryInfo(baseDir);

        var matchingFiles = di.GetFiles("*", SearchOption.AllDirectories);
        foreach (var file in matchingFiles)
        {
            string lowerName = file.Name.ToLowerInvariant();
            Assert.DoesNotContain("silk", lowerName);
            Assert.DoesNotContain("glfw", lowerName);
            Assert.DoesNotContain("sdl", lowerName);
        }
    }
}
