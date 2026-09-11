namespace Glacier.Game.Collision;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Glacier.Game.Physics;

/// <summary>
/// High-performance, zero-allocation uniform spatial hash grid for broadphase collision detection.
/// Backed by unmanaged native memory using a cell-head and entity-next linked list structure.
/// </summary>
public sealed unsafe class SpatialGrid2D : IDisposable
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly int _cellCount;
    private readonly int _maxEntities;

    private int* _cellHeads;
    private int* _entityNext;
    private bool _disposed;

    public float CellSize => _cellSize;
    public int CellCount => _cellCount;

    public SpatialGrid2D(float cellSize = 64f, int cellCount = 4096, int maxEntities = 300000)
    {
        _cellSize = cellSize;
        _invCellSize = 1.0f / cellSize;
        _cellCount = cellCount;
        _maxEntities = maxEntities;

        _cellHeads = (int*)NativeMemory.AllocZeroed((nuint)(sizeof(int) * _cellCount));
        _entityNext = (int*)NativeMemory.AllocZeroed((nuint)(sizeof(int) * _maxEntities));

        Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        NativeMemory.Fill(_cellHeads, (nuint)(sizeof(int) * _cellCount), 0xFF); // -1
        NativeMemory.Fill(_entityNext, (nuint)(sizeof(int) * _maxEntities), 0xFF); // -1
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entityId, float x, float y)
    {
        if ((uint)entityId >= (uint)_maxEntities) return;

        int hash = ComputeCellHash(x, y);
        _entityNext[entityId] = _cellHeads[hash];
        _cellHeads[hash] = entityId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int QueryNeighbors(float x, float y, Span<int> results)
    {
        int cellX = (int)MathF.Floor(x * _invCellSize);
        int cellY = (int)MathF.Floor(y * _invCellSize);

        int written = 0;
        int maxResults = results.Length;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int hash = HashCoords(cellX + dx, cellY + dy);
                int curr = _cellHeads[hash];

                while (curr != -1 && written < maxResults)
                {
                    results[written++] = curr;
                    curr = _entityNext[curr];
                }
            }
        }

        return written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputeCellHash(float x, float y)
    {
        int cellX = (int)MathF.Floor(x * _invCellSize);
        int cellY = (int)MathF.Floor(y * _invCellSize);
        return HashCoords(cellX, cellY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HashCoords(int cx, int cy)
    {
        uint h = (uint)(cx * 73856093 ^ cy * 19349663);
        return (int)(h % (uint)_cellCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_cellHeads != null)
        {
            NativeMemory.Free(_cellHeads);
            _cellHeads = null;
        }

        if (_entityNext != null)
        {
            NativeMemory.Free(_entityNext);
            _entityNext = null;
        }
    }
}
