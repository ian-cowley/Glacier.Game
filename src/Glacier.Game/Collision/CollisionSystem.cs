namespace Glacier.Game.Collision;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;

/// <summary>
/// ECS system coordinating broadphase spatial hash acceleration and SIMD narrowphase world-space AABB intersection tests.
/// </summary>
public sealed class CollisionSystem : ISystem, IDisposable
{
    private readonly SpatialGrid2D _grid;
    private int _collisionCount;
    private int[] _neighborBuffer = new int[512];
    private int[] _seenStamp = new int[1024];
    private int _stampCounter;
    private float[] _candMinX = new float[256];
    private float[] _candMaxX = new float[256];
    private float[] _candMinY = new float[256];
    private float[] _candMaxY = new float[256];
    private ushort[] _bitmask = new ushort[32];

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

        // 1. Broadphase: Insert entities across all overlapped grid cells for world-space AABB
        foreach (var chunk in query)
        {
            var positions = chunk.Component1Span;
            var boxes = chunk.Component2Span;
            var entities = chunk.Entities;

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Position2D pos = ref positions[i];
                ref readonly AABB2D box = ref boxes[i];
                _grid.InsertBounds(
                    entities[i].Id,
                    pos.X + box.MinX,
                    pos.X + box.MaxX,
                    pos.Y + box.MinY,
                    pos.Y + box.MaxY);
            }
        }

        // 2. Narrowphase: Gather unique candidate boxes across covered cells and evaluate AABB overlap (SIMD when >=16 candidates)
        foreach (var chunk in query)
        {
            var positions = chunk.Component1Span;
            var boxes = chunk.Component2Span;
            var entities = chunk.Entities;

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Position2D posA = ref positions[i];
                ref readonly AABB2D boxA = ref boxes[i];
                float minXA = posA.X + boxA.MinX;
                float maxXA = posA.X + boxA.MaxX;
                float minYA = posA.Y + boxA.MinY;
                float maxYA = posA.Y + boxA.MaxY;

                int neighborCount = _grid.QueryBounds(minXA, maxXA, minYA, maxYA, ref _neighborBuffer);
                if (neighborCount <= 1) continue;

                if (_stampCounter == int.MaxValue)
                {
                    Array.Clear(_seenStamp);
                    _stampCounter = 0;
                }
                int stamp = ++_stampCounter;
                int currentId = entities[i].Id;
                int candCount = 0;

                for (int n = 0; n < neighborCount; n++)
                {
                    int otherId = _neighborBuffer[n];
                    if (otherId <= currentId) continue;

                    if (otherId >= _seenStamp.Length)
                    {
                        Array.Resize(ref _seenStamp, Math.Max(_seenStamp.Length * 2, otherId + 1));
                    }
                    if (_seenStamp[otherId] == stamp) continue;
                    _seenStamp[otherId] = stamp;

                    if (world.TryGetPositionAndBounds(otherId, out Position2D posB, out AABB2D boxB))
                    {
                        if (candCount >= _candMinX.Length)
                        {
                            int newCap = _candMinX.Length * 2;
                            Array.Resize(ref _candMinX, newCap);
                            Array.Resize(ref _candMaxX, newCap);
                            Array.Resize(ref _candMinY, newCap);
                            Array.Resize(ref _candMaxY, newCap);
                        }

                        _candMinX[candCount] = posB.X + boxB.MinX;
                        _candMaxX[candCount] = posB.X + boxB.MaxX;
                        _candMinY[candCount] = posB.Y + boxB.MinY;
                        _candMaxY[candCount] = posB.Y + boxB.MaxY;
                        candCount++;
                    }
                }

                if (candCount >= 16)
                {
                    int wordCount = (candCount + 15) / 16;
                    if (_bitmask.Length < wordCount)
                    {
                        Array.Resize(ref _bitmask, wordCount);
                    }

                    CollisionKernels.CheckCollisionsAvx512(
                        _candMinX.AsSpan(0, candCount),
                        _candMaxX.AsSpan(0, candCount),
                        _candMinY.AsSpan(0, candCount),
                        _candMaxY.AsSpan(0, candCount),
                        minXA, maxXA, minYA, maxYA,
                        _bitmask);

                    for (int w = 0; w < wordCount; w++)
                    {
                        _collisionCount += BitOperations.PopCount(_bitmask[w]);
                    }
                }
                else
                {
                    for (int c = 0; c < candCount; c++)
                    {
                        if (minXA <= _candMaxX[c] && maxXA >= _candMinX[c] &&
                            minYA <= _candMaxY[c] && maxYA >= _candMinY[c])
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
