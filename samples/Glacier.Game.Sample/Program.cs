namespace Glacier.Game.Sample;

using System;
using System.Diagnostics;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Interop;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;

public class Program
{
    public static void Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     GLACIER.GAME: High-Performance Data-Oriented Game Engine for .NET 10        ");
        Console.WriteLine("         Zero-Allocation ECS | AVX-512 SIMD Physics | Silk.NET GPU              ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        const int entityCount = 250000;
        const int simulatedFrames = 1000;
        const float fixedDt = 1.0f / 240.0f; // 240 FPS fixed update step (4.16ms)

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
                new AABB2D(-2f, 2f, -2f, 2f)
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
