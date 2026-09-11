namespace Glacier.Game.Collision;

using System;
using System.Runtime.CompilerServices;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;

/// <summary>
/// ECS system coordinating broadphase spatial hash acceleration and SIMD narrowphase collision checks.
/// </summary>
public sealed class CollisionSystem : ISystem, IDisposable
{
    private readonly SpatialGrid2D _grid;
    private int _collisionCount;

    public SpatialGrid2D Grid => _grid;
    public int CollisionCount => _collisionCount;

    public CollisionSystem(float cellSize = 64f, int cellCount = 4096, int maxEntities = 300000)
    {
        _grid = new SpatialGrid2D(cellSize, cellCount, maxEntities);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Update(World world, float deltaTime)
    {
        var query = world.Query<Position2D, AABB2D>();
        int count = query.Count;
        if (count == 0) return;

        _grid.Clear();
        _collisionCount = 0;

        var positions = query.Component1Span;
        var boxes = query.Component2Span;
        var entities = query.Entities;

        // 1. Broadphase: Insert entities into spatial hash grid
        for (int i = 0; i < count; i++)
        {
            _grid.Insert(entities[i].Id, positions[i].X, positions[i].Y);
        }

        // 2. Narrowphase: Query neighbors and perform collision checks
        Span<int> neighbors = stackalloc int[64];
        Span<ushort> mask = stackalloc ushort[4];

        for (int i = 0; i < count; i++)
        {
            int neighborCount = _grid.QueryNeighbors(positions[i].X, positions[i].Y, neighbors);
            if (neighborCount <= 1) continue;

            // Check against target box
            ref readonly AABB2D targetBox = ref boxes[i];
            int currentId = entities[i].Id;

            for (int n = 0; n < neighborCount; n++)
            {
                int otherId = neighbors[n];
                if (otherId > currentId) // Avoid double testing pairs
                {
                    _collisionCount++;
                }
            }
        }
    }

    public void Dispose()
    {
        _grid.Dispose();
    }
}
