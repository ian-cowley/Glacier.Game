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
        BenchmarkRunner.Run<PhysicsBenchmarks>();
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
