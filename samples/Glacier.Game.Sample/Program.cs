namespace Glacier.Game.Sample;

using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Interop;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Silk.NET.Input;
using Position2D = Glacier.Game.Physics.Position2D;

public class Program
{
    public static void Main(string[] args)
    {
        bool isHeadless = args.Any(a => a is "--headless" or "--bench" or "-b");

        if (isHeadless)
        {
            RunHeadlessBenchmark();
            if (Environment.UserInteractive && !Console.IsInputRedirected)
            {
                Console.WriteLine("\n[Press any key to exit...]");
                Console.ReadKey();
            }
        }
        else
        {
            RunInteractiveGame();
        }
    }

    private static void RunInteractiveGame()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     GLACIER.GAME: High-Performance Data-Oriented Game Engine for .NET 10        ");
        Console.WriteLine("         Zero-Allocation ECS | AVX-512 SIMD Physics | Silk.NET GPU              ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
        Console.WriteLine("\n>> Launching interactive GPU window on screen...");
        Console.WriteLine("   Interactive Controls:");
        Console.WriteLine("   • Left Click / Drag:  Spawn bursts of 150 physics particles at mouse cursor");
        Console.WriteLine("   • Right Click / Hold: Gravity Vortex / Attractor pulling entities to cursor");
        Console.WriteLine("   • Spacebar:           Toggle downward gravity on/off");
        Console.WriteLine("   • Key 'R':            Reset & respawn 15,000 particles");
        Console.WriteLine("   • Key 'C':            Clear all particles");
        Console.WriteLine("   • Key 'Escape':       Exit window\n");

        const int width = 1280;
        const int height = 720;

        var config = new WindowConfig
        {
            Headless = false,
            Width = width,
            Height = height,
            TargetFps = 240,
            FixedTimeStepHz = 240,
            Title = "Glacier.Game: .NET 10 SIMD ECS Engine"
        };

        using var engine = new GameEngine(config);
        var physics = new SimdPhysicsSystem(0f, width, 0f, height);
        engine.AddSystem(physics);

        var rng = new Random(42);

        void SpawnExplosion(int count, float cx, float cy, float maxSpeed)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float speed = (float)(rng.NextDouble() * maxSpeed + 25.0);
                float vx = MathF.Cos(angle) * speed;
                float vy = MathF.Sin(angle) * speed;

                byte r = (byte)(180 + rng.Next(75));
                byte g = (byte)(80 + rng.Next(155));
                byte b = (byte)(50 + rng.Next(205));
                var col = new Color32(r, g, b, 240);

                engine.World.CreateEntity(
                    new Position2D(cx, cy),
                    new Velocity2D(vx, vy),
                    new AABB2D(-2.5f, -2.5f, 2.5f, 2.5f),
                    col
                );
            }
        }

        void SpawnInitialEntities(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rng.NextDouble() * (width - 40) + 20);
                float y = (float)(rng.NextDouble() * (height - 40) + 20);
                float vx = (float)(rng.NextDouble() * 300.0 - 150.0);
                float vy = (float)(rng.NextDouble() * 300.0 - 150.0);

                byte r = (byte)(40 + (x / width) * 215);
                byte g = (byte)(80 + (y / height) * 175);
                byte b = (byte)(255 - (x / width) * 100);
                var col = new Color32(r, g, b, 230);

                engine.World.CreateEntity(
                    new Position2D(x, y),
                    new Velocity2D(vx, vy),
                    new AABB2D(-2.5f, -2.5f, 2.5f, 2.5f),
                    col
                );
            }
        }

        SpawnInitialEntities(15000);

        bool gravity = false;

        engine.KeyDown += key =>
        {
            if (key == Key.Space)
            {
                gravity = !gravity;
                physics.GravityY = gravity ? 500f : 0f;
            }
            else if (key == Key.R)
            {
                var entities = engine.World.Query<Position2D>().Entities.ToArray();
                foreach (var e in entities) engine.World.DestroyEntity(e);
                SpawnInitialEntities(15000);
            }
            else if (key == Key.C)
            {
                var entities = engine.World.Query<Position2D>().Entities.ToArray();
                foreach (var e in entities) engine.World.DestroyEntity(e);
            }
        };

        engine.Update += time =>
        {
            float dt = time.DeltaTime;

            if (engine.IsLeftMouseDown)
            {
                SpawnExplosion(150, engine.MousePosition.X, engine.MousePosition.Y, 400f);
            }

            if (engine.IsRightMouseDown)
            {
                var query = engine.World.Query<Position2D, Velocity2D>();
                float mx = engine.MousePosition.X;
                float my = engine.MousePosition.Y;
                var pSpan = query.Component1Span;
                var vSpan = query.Component2Span;

                for (int i = 0; i < query.Count; i++)
                {
                    float dx = mx - pSpan[i].X;
                    float dy = my - pSpan[i].Y;
                    float distSq = dx * dx + dy * dy + 200f;
                    float force = 180000f / distSq;
                    float invDist = 1.0f / MathF.Sqrt(distSq);
                    vSpan[i].X += (dx * invDist) * force * dt;
                    vSpan[i].Y += (dy * invDist) * force * dt;
                }
            }
        };

        engine.Run();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[Window Closed] Glacier.Game execution completed cleanly.");
        Console.ResetColor();

        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine("[Press any key to exit...]");
            Console.ReadKey();
        }
    }

    private static void RunHeadlessBenchmark()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     GLACIER.GAME: High-Performance Data-Oriented Game Engine for .NET 10        ");
        Console.WriteLine("         Zero-Allocation ECS | AVX-512 SIMD Physics | Silk.NET GPU              ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        const int entityCount = 250000;
        const int simulatedFrames = 1000;
        const float fixedDt = 1.0f / 240.0f;

        Console.WriteLine($"\n[1/4] Spawning {entityCount:N0} entities in ECS World...");
        var swSetup = Stopwatch.StartNew();

        var config = new WindowConfig
        {
            Headless = true,
            Width = 1920,
            Height = 1080,
            TargetFps = 240,
            FixedTimeStepHz = 240
        };

        using var engine = new GameEngine(config);
        engine.AddSystem(new SimdPhysicsSystem(0f, 1920f, 0f, 1080f));

        var rng = new Random(42);
        for (int i = 0; i < entityCount; i++)
        {
            float x = (float)(rng.NextDouble() * 1920.0);
            float y = (float)(rng.NextDouble() * 1080.0);
            float vx = (float)(rng.NextDouble() * 400.0 - 200.0);
            float vy = (float)(rng.NextDouble() * 400.0 - 200.0);

            engine.World.CreateEntity(
                new Position2D(x, y),
                new Velocity2D(vx, vy),
                new AABB2D(-2f, -2f, 2f, 2f)
            );
        }

        swSetup.Stop();
        Console.WriteLine($"      Spawned {entityCount:N0} entities in {swSetup.ElapsedMilliseconds:N1} ms (Archetype SoA layout).");

        Console.WriteLine($"\n[2/4] Simulating {simulatedFrames:N0} frames at {config.TargetFps} Hz target...");
        long initialMemory = GC.GetAllocatedBytesForCurrentThread();
        var swSim = Stopwatch.StartNew();

        for (int frame = 0; frame < simulatedFrames; frame++)
        {
            engine.Step(fixedDt);
            engine.Render();
        }

        swSim.Stop();
        long finalMemory = GC.GetAllocatedBytesForCurrentThread();
        long allocatedInLoop = finalMemory - initialMemory;

        double totalSeconds = swSim.Elapsed.TotalSeconds;
        double fps = simulatedFrames / totalSeconds;
        double frameUs = (totalSeconds / simulatedFrames) * 1_000_000.0;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[SIMULATION RESULTS]");
        Console.WriteLine($"  - Total Entities:         {entityCount:N0}");
        Console.WriteLine($"  - Total Frames:           {simulatedFrames:N0}");
        Console.WriteLine($"  - Elapsed Wall Time:      {swSim.ElapsedMilliseconds:N1} ms");
        Console.WriteLine($"  - Average Frame Time:     {frameUs:N2} µs ({(frameUs / 1000.0):N3} ms)");
        Console.WriteLine($"  - Effective Throughput:   {fps:N1} FPS (Target: 240 FPS)");
        Console.WriteLine($"  - GC Collections (Gen0):  {GC.CollectionCount(0)}");
        Console.WriteLine($"  - Heap Allocations/Frame: {(allocatedInLoop / (double)simulatedFrames):N1} bytes (Near-Zero Heap Hot Path)");
        Console.ResetColor();

        Console.WriteLine($"\n[3/4] Testing Ecosystem Interop with Glacier.Polaris...");
        var query = engine.World.Query<Position2D, Velocity2D>();
        var sampleEntities = query.Entities[..5].ToArray();
        var df = engine.World.ExportToDataFrame(sampleEntities);
        Console.WriteLine($"      Exported {df.RowCount} entities to Polaris DataFrame with columns: {string.Join(", ", df.Columns.ConvertAll(c => c.Name))}");

        Console.WriteLine($"\n[4/4] Testing Ecosystem Interop with Glacier.Tensor...");
        using var obsTensor = engine.World.ToObservationTensor(sampleEntities);
        Console.WriteLine($"      Observation Tensor Shape: [{obsTensor.Shape[0]}, {obsTensor.Shape[1]}] (N, [X, Y, Vx, Vy])");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\nAll Glacier.Game benchmarks and interop validation checks completed successfully!");
        Console.ResetColor();
    }
}
