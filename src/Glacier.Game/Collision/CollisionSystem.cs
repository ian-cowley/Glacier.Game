namespace Glacier.Game.Collision;

using System;
using System.Runtime.CompilerServices;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;

/// <summary>
/// ECS system coordinating broadphase spatial hash acceleration and narrowphase world-space AABB intersection tests.
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
        _collisionCount = 0;
        var query = world.Query<Position2D, AABB2D>();
        if (query.TotalCount == 0) return;

        _grid.Clear();

        // 1. Broadphase: Insert entities across all matching archetypes into spatial hash grid by world AABB center
        foreach (var chunk in query)
        {
            var positions = chunk.Component1Span;
            var boxes = chunk.Component2Span;
            var entities = chunk.Entities;

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Position2D pos = ref positions[i];
                ref readonly AABB2D box = ref boxes[i];
                float cx = pos.X + (box.MinX + box.MaxX) * 0.5f;
                float cy = pos.Y + (box.MinY + box.MaxY) * 0.5f;
                _grid.Insert(entities[i].Id, cx, cy);
            }
        }

        // 2. Narrowphase: Query spatial neighbors and test exact world-space AABB overlap
        Span<int> neighbors = stackalloc int[256];

        foreach (var chunk in query)
        {
            var positions = chunk.Component1Span;
            var boxes = chunk.Component2Span;
            var entities = chunk.Entities;

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Position2D posA = ref positions[i];
                ref readonly AABB2D boxA = ref boxes[i];
                float cxA = posA.X + (boxA.MinX + boxA.MaxX) * 0.5f;
                float cyA = posA.Y + (boxA.MinY + boxA.MaxY) * 0.5f;

                int neighborCount = _grid.QueryNeighbors(cxA, cyA, neighbors);
                if (neighborCount <= 1) continue;

                float minXA = posA.X + boxA.MinX;
                float maxXA = posA.X + boxA.MaxX;
                float minYA = posA.Y + boxA.MinY;
                float maxYA = posA.Y + boxA.MaxY;
                int currentId = entities[i].Id;

                for (int n = 0; n < neighborCount; n++)
                {
                    int otherId = neighbors[n];
                    if (otherId <= currentId) continue; // Avoid self and duplicate unordered pairs

                    if (world.TryGetPositionAndBounds(otherId, out Position2D posB, out AABB2D boxB))
                    {
                        float minXB = posB.X + boxB.MinX;
                        float maxXB = posB.X + boxB.MaxX;
                        float minYB = posB.Y + boxB.MinY;
                        float maxYB = posB.Y + boxB.MaxY;

                        if (minXA <= maxXB && maxXA >= minXB && minYA <= maxYB && maxYA >= minYB)
                        {
                            _collisionCount++;
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _grid.Dispose();
    }
}
