namespace Glacier.Game.Tests;

using System;
using Glacier.Game.Collision;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Xunit;

public class CollisionTests
{
    [Fact]
    public void SIMD_Collision_MatchesScalarParity()
    {
        const int count = 32;
        var minX = new float[count];
        var maxX = new float[count];
        var minY = new float[count];
        var maxY = new float[count];

        for (int i = 0; i < count; i++)
        {
            minX[i] = i * 10f;
            maxX[i] = i * 10f + 5f;
            minY[i] = 0f;
            maxY[i] = 10f;
        }

        float targetMinX = 12f;
        float targetMaxX = 22f;
        float targetMinY = 5f;
        float targetMaxY = 15f;

        var simdMask = new ushort[2];
        var scalarMask = new ushort[2];

        CollisionKernels.CheckCollisionsAvx512(minX, maxX, minY, maxY, targetMinX, targetMaxX, targetMinY, targetMaxY, simdMask);
        CollisionKernels.ScalarCheckCollisions(minX, maxX, minY, maxY, targetMinX, targetMaxX, targetMinY, targetMaxY, scalarMask);

        Assert.Equal(scalarMask[0], simdMask[0]);
        Assert.Equal(scalarMask[1], simdMask[1]);
    }

    [Fact]
    public void SpatialGrid2D_InsertAndQuery_FindsNeighbors()
    {
        using var grid = new SpatialGrid2D(cellSize: 50f, cellCount: 1024, maxEntities: 1000);

        grid.Insert(1, 10f, 10f); // Cell (0, 0)
        grid.Insert(2, 20f, 20f); // Cell (0, 0)
        grid.Insert(3, 500f, 500f); // Distant cell

        Span<int> results = stackalloc int[16];
        int found = grid.QueryNeighbors(15f, 15f, results);

        Assert.True(found >= 2);
        bool found1 = false;
        bool found2 = false;
        for (int i = 0; i < found; i++)
        {
            if (results[i] == 1) found1 = true;
            if (results[i] == 2) found2 = true;
        }
        Assert.True(found1 && found2);
    }
}
