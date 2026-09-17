namespace Glacier.Game.Benchmarks;

using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Glacier.Game.Collision;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--standalone")
        {
            RunStandaloneBenchmark();
            return;
        }

        BenchmarkRunner.Run<PhysicsBenchmarks>();
    }

    private static void RunStandaloneBenchmark()
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("  GLACIER.GAME PHYSICAL HARDWARE BENCHMARK SUITE");
        Console.WriteLine("===============================================================================\n");

        const int entityCount = 250000;
        const int iterations = 1000;

        var posX = new float[entityCount];
        var posY = new float[entityCount];
        var velX = new float[entityCount];
        var velY = new float[entityCount];

        var rng = new Random(42);
        for (int i = 0; i < entityCount; i++)
        {
            posX[i] = (float)(rng.NextDouble() * 1920.0);
            posY[i] = (float)(rng.NextDouble() * 1080.0);
            velX[i] = (float)(rng.NextDouble() * 200.0 - 100.0);
            velY[i] = (float)(rng.NextDouble() * 200.0 - 100.0);
        }

        using var world = new World(entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            world.CreateEntity(
                new Position2D(posX[i], posY[i]),
                new Velocity2D(velX[i], velY[i])
            );
        }

        // 1. 250,000 Entities SIMD Euler Integration (Array Spans)
        Console.WriteLine("--- 1. AVX-512 SIMD Euler Integration (250,000 Entities) ---");
        for (int w = 0; w < 50; w++)
            PhysicsKernels.IntegrateEuler(posX.AsSpan(), posY.AsSpan(), velX.AsSpan(), velY.AsSpan(), 0.004166f);

        var swEuler = System.Diagnostics.Stopwatch.StartNew();
        for (int it = 0; it < iterations; it++)
        {
            PhysicsKernels.IntegrateEuler(posX.AsSpan(), posY.AsSpan(), velX.AsSpan(), velY.AsSpan(), 0.004166f);
        }
        swEuler.Stop();
        double msPerEuler = swEuler.Elapsed.TotalMilliseconds / iterations;
        double entitiesPerSecEuler = (double)entityCount * iterations / swEuler.Elapsed.TotalSeconds;
        Console.WriteLine($"[AVX-512 Euler]:      {swEuler.Elapsed.TotalMilliseconds:F2} ms for {iterations:N0} frames | {msPerEuler:F4} ms/frame | Throughput: {entitiesPerSecEuler / 1e6:F2} Million entities/sec");

        // 2. 64-byte Aligned ECS ArchetypeTable Physics System Update
        Console.WriteLine("\n--- 2. 64-Byte Aligned ECS Archetype Table Physics Update (250,000 Entities) ---");
        var physicsSystem = new SimdPhysicsSystem(0f, 1920f, 0f, 1080f, 0.95f);
        for (int w = 0; w < 50; w++)
            physicsSystem.Update(world, 0.004166f);

        var swSys = System.Diagnostics.Stopwatch.StartNew();
        for (int it = 0; it < iterations; it++)
        {
            physicsSystem.Update(world, 0.004166f);
        }
        swSys.Stop();
        double msPerSys = swSys.Elapsed.TotalMilliseconds / iterations;
        double entitiesPerSecSys = (double)entityCount * iterations / swSys.Elapsed.TotalSeconds;
        Console.WriteLine($"[ECS Archetype Table]: {swSys.Elapsed.TotalMilliseconds:F2} ms for {iterations:N0} updates | {msPerSys:F4} ms/frame | Throughput: {entitiesPerSecSys / 1e6:F2} Million entities/sec");

        // 3. ECS SoA Query Traversal
        Console.WriteLine("\n--- 3. ECS SoA Query Traversal (250,000 Entities) ---");
        var q = world.Query<Position2D, Velocity2D>();
        float sum = 0f;
        for (int w = 0; w < 50; w++)
        {
            var p = q.Component1Span;
            var v = q.Component2Span;
            for (int i = 0; i < q.Count; i++) sum += p[i].X + v[i].X;
        }

        var swQuery = System.Diagnostics.Stopwatch.StartNew();
        for (int it = 0; it < iterations; it++)
        {
            var p = q.Component1Span;
            var v = q.Component2Span;
            for (int i = 0; i < q.Count; i++)
            {
                sum += p[i].X + v[i].X;
            }
        }
        swQuery.Stop();
        double msPerQuery = swQuery.Elapsed.TotalMilliseconds / iterations;
        double queriesPerSec = (double)entityCount * iterations / swQuery.Elapsed.TotalSeconds;
        Console.WriteLine($"[ECS SoA Query]:       {swQuery.Elapsed.TotalMilliseconds:F2} ms for {iterations:N0} runs | {msPerQuery:F4} ms/run | Throughput: {queriesPerSec / 1e6:F2} Million entities/sec (Checksum: {sum:E2})");

        Console.WriteLine("\n===============================================================================");
        Console.WriteLine("  GLACIER.GAME PHYSICAL BENCHMARK COMPLETE");
        Console.WriteLine("===============================================================================\n");
    }
}

[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class PhysicsBenchmarks
{
    private const int EntityCount = 250000;

    private float[] _posX = null!;
    private float[] _posY = null!;
    private float[] _velX = null!;
    private float[] _velY = null!;

    private float[] _minX = null!;
    private float[] _maxX = null!;
    private float[] _minY = null!;
    private float[] _maxY = null!;
    private ushort[] _collisionMask = null!;

    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _posX = new float[EntityCount];
        _posY = new float[EntityCount];
        _velX = new float[EntityCount];
        _velY = new float[EntityCount];

        _minX = new float[100000];
        _maxX = new float[100000];
        _minY = new float[100000];
        _maxY = new float[100000];
        _collisionMask = new ushort[(100000 + 15) / 16];

        var rng = new Random(42);
        for (int i = 0; i < EntityCount; i++)
        {
            _posX[i] = (float)(rng.NextDouble() * 1920.0);
            _posY[i] = (float)(rng.NextDouble() * 1080.0);
            _velX[i] = (float)(rng.NextDouble() * 200.0 - 100.0);
            _velY[i] = (float)(rng.NextDouble() * 200.0 - 100.0);
        }

        for (int i = 0; i < 100000; i++)
        {
            _minX[i] = (float)(rng.NextDouble() * 1920.0);
            _maxX[i] = _minX[i] + 10f;
            _minY[i] = (float)(rng.NextDouble() * 1080.0);
            _maxY[i] = _minY[i] + 10f;
        }

        _world = new World(EntityCount);
        for (int i = 0; i < EntityCount; i++)
        {
            _world.CreateEntity(
                new Position2D(_posX[i], _posY[i]),
                new Velocity2D(_velX[i], _velY[i])
            );
        }
    }

    [Benchmark(Description = "Euler Integration (250,000 Entities SIMD)")]
    public void Benchmark_IntegrateEuler_SIMD()
    {
        PhysicsKernels.IntegrateEuler(_posX, _posY, _velX, _velY, 0.004166f); // 240 FPS dt
    }

    [Benchmark(Description = "ECS SoA Query Traversal (250,000 Entities)")]
    public float Benchmark_EcsQuery()
    {
        var q = _world.Query<Position2D, Velocity2D>();
        var p = q.Component1Span;
        var v = q.Component2Span;
        float total = 0f;
        for (int i = 0; i < q.Count; i++)
        {
            total += p[i].X + v[i].X;
        }
        return total;
    }

    [Benchmark(Description = "Collision AABB 16-Wide SIMD (100,000 Targets)")]
    public void Benchmark_Collision_SIMD()
    {
        CollisionKernels.CheckCollisionsAvx512(
            _minX, _maxX, _minY, _maxY,
            targetMinX: 500f, targetMaxX: 550f,
            targetMinY: 500f, targetMaxY: 550f,
            _collisionMask
        );
    }
}
