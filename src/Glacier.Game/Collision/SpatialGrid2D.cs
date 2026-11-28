namespace Glacier.Game.Collision;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// High-performance, zero-allocation uniform spatial hash grid for broadphase collision detection.
/// Supports both point and multi-cell AABB insertion backed by unmanaged native memory.
/// </summary>
public sealed unsafe class SpatialGrid2D : IDisposable
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly int _cellCount;
    private int _entryCapacity;
    private int _entryCount;

    private int* _cellHeads;
    private int* _entryEntityIds;
    private int* _entryNext;
    private bool _disposed;

    public float CellSize => _cellSize;
    public int CellCount => _cellCount;

    public SpatialGrid2D(float cellSize = 64f, int cellCount = 4096, int maxEntities = 300000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntities);

        _cellSize = cellSize;
        _invCellSize = 1.0f / cellSize;
        _cellCount = cellCount;
        _entryCapacity = maxEntities;

        _cellHeads = (int*)NativeMemory.Alloc(checked((nuint)sizeof(int) * (nuint)(uint)_cellCount));
        nuint entryBytes = checked((nuint)sizeof(int) * (nuint)(uint)_entryCapacity);
        _entryEntityIds = (int*)NativeMemory.Alloc(entryBytes);
        _entryNext = (int*)NativeMemory.Alloc(entryBytes);

        Clear();
    }

    ~SpatialGrid2D() => Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _entryCount = 0;
        NativeMemory.Fill(_cellHeads, checked((nuint)sizeof(int) * (nuint)(uint)_cellCount), 0xFF); // -1
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entityId, float x, float y)
    {
        if (entityId < 0) return;
        InsertIntoBucket(entityId, ComputeCellHash(x, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InsertBounds(int entityId, float minX, float maxX, float minY, float maxY)
    {
        if (entityId < 0) return;
        if (maxX < minX) (minX, maxX) = (maxX, minX);
        if (maxY < minY) (minY, maxY) = (maxY, minY);

        int minCx = (int)MathF.Floor(minX * _invCellSize);
        int maxCx = (int)MathF.Floor(maxX * _invCellSize);
        int minCy = (int)MathF.Floor(minY * _invCellSize);
        int maxCy = (int)MathF.Floor(maxY * _invCellSize);

        long spanX = (long)maxCx - minCx + 1L;
        long spanY = (long)maxCy - minCy + 1L;
        if (spanX == 1 && spanY == 1)
        {
            InsertIntoBucket(entityId, HashCoords(minCx, minCy));
            return;
        }

        if (spanX * spanY <= 64)
        {
            Span<int> visitedHashes = stackalloc int[64];
            int visitedCount = 0;

            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    bool seen = false;
                    for (int v = 0; v < visitedCount; v++)
                    {
                        if (visitedHashes[v] == hash)
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen) continue;
                    visitedHashes[visitedCount++] = hash;
                    InsertIntoBucket(entityId, hash);
                }
            }
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(_cellCount);
        try
        {
            Array.Clear(rented, 0, _cellCount);
            if (spanX * spanY >= _cellCount)
            {
                for (int h = 0; h < _cellCount; h++)
                {
                    InsertIntoBucket(entityId, h);
                }
                return;
            }

            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    if (rented[hash] == 0)
                    {
                        rented[hash] = 1;
                        InsertIntoBucket(entityId, hash);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertIntoBucket(int entityId, int hash)
    {
        if (_entryCount >= _entryCapacity)
        {
            GrowEntryCapacity(_entryCount + 1);
        }

        int node = _entryCount++;
        _entryEntityIds[node] = entityId;
        _entryNext[node] = _cellHeads[hash];
        _cellHeads[hash] = node;
    }

    private void GrowEntryCapacity(int required)
    {
        int newCap = Math.Max(_entryCapacity * 2, required);
        nuint oldBytes = checked((nuint)sizeof(int) * (nuint)(uint)_entryCapacity);
        nuint newBytes = checked((nuint)sizeof(int) * (nuint)(uint)newCap);

        int* newIds = (int*)NativeMemory.Alloc(newBytes);
        int* newNext = (int*)NativeMemory.Alloc(newBytes);
        NativeMemory.Copy(_entryEntityIds, newIds, oldBytes);
        NativeMemory.Copy(_entryNext, newNext, oldBytes);
        NativeMemory.Free(_entryEntityIds);
        NativeMemory.Free(_entryNext);

        _entryEntityIds = newIds;
        _entryNext = newNext;
        _entryCapacity = newCap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int QueryNeighbors(float x, float y, Span<int> results)
    {
        int cellX = (int)MathF.Floor(x * _invCellSize);
        int cellY = (int)MathF.Floor(y * _invCellSize);

        int written = 0;
        int maxResults = results.Length;
        Span<int> visitedHashes = stackalloc int[9];
        int visitedCount = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int hash = HashCoords(cellX + dx, cellY + dy);
                bool alreadyVisited = false;
                for (int v = 0; v < visitedCount; v++)
                {
                    if (visitedHashes[v] == hash)
                    {
                        alreadyVisited = true;
                        break;
                    }
                }
                if (alreadyVisited) continue;
                visitedHashes[visitedCount++] = hash;

                int node = _cellHeads[hash];
                while (node != -1 && written < maxResults)
                {
                    results[written++] = _entryEntityIds[node];
                    node = _entryNext[node];
                }
            }
        }

        return written;
    }

    public int QueryBounds(float minX, float maxX, float minY, float maxY, Span<int> results)
    {
        if (maxX < minX) (minX, maxX) = (maxX, minX);
        if (maxY < minY) (minY, maxY) = (maxY, minY);

        int minCx = (int)MathF.Floor(minX * _invCellSize);
        int maxCx = (int)MathF.Floor(maxX * _invCellSize);
        int minCy = (int)MathF.Floor(minY * _invCellSize);
        int maxCy = (int)MathF.Floor(maxY * _invCellSize);

        int written = 0;
        int maxResults = results.Length;
        long spanCells = ((long)maxCx - minCx + 1L) * ((long)maxCy - minCy + 1L);

        if (spanCells <= 64)
        {
            Span<int> visitedHashes = stackalloc int[64];
            int visitedCount = 0;
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    bool seen = false;
                    for (int v = 0; v < visitedCount; v++)
                    {
                        if (visitedHashes[v] == hash)
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen) continue;
                    visitedHashes[visitedCount++] = hash;

                    int node = _cellHeads[hash];
                    while (node != -1 && written < maxResults)
                    {
                        results[written++] = _entryEntityIds[node];
                        node = _entryNext[node];
                    }
                }
            }
            return written;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(_cellCount);
        try
        {
            Array.Clear(rented, 0, _cellCount);
            if (spanCells >= _cellCount)
            {
                for (int h = 0; h < _cellCount && written < maxResults; h++)
                {
                    int node = _cellHeads[h];
                    while (node != -1 && written < maxResults)
                    {
                        results[written++] = _entryEntityIds[node];
                        node = _entryNext[node];
                    }
                }
                return written;
            }

            for (int cy = minCy; cy <= maxCy && written < maxResults; cy++)
            {
                for (int cx = minCx; cx <= maxCx && written < maxResults; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    if (rented[hash] != 0) continue;
                    rented[hash] = 1;

                    int node = _cellHeads[hash];
                    while (node != -1 && written < maxResults)
                    {
                        results[written++] = _entryEntityIds[node];
                        node = _entryNext[node];
                    }
                }
            }
            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public int QueryBounds(float minX, float maxX, float minY, float maxY, ref int[] buffer)
    {
        if (maxX < minX) (minX, maxX) = (maxX, minX);
        if (maxY < minY) (minY, maxY) = (maxY, minY);

        int minCx = (int)MathF.Floor(minX * _invCellSize);
        int maxCx = (int)MathF.Floor(maxX * _invCellSize);
        int minCy = (int)MathF.Floor(minY * _invCellSize);
        int maxCy = (int)MathF.Floor(maxY * _invCellSize);

        int written = 0;
        long spanCells = ((long)maxCx - minCx + 1L) * ((long)maxCy - minCy + 1L);

        if (spanCells <= 64)
        {
            Span<int> visitedHashes = stackalloc int[64];
            int visitedCount = 0;
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    bool seen = false;
                    for (int v = 0; v < visitedCount; v++)
                    {
                        if (visitedHashes[v] == hash)
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen) continue;
                    visitedHashes[visitedCount++] = hash;
                    AppendBucket(hash, ref buffer, ref written);
                }
            }
            return written;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(_cellCount);
        try
        {
            Array.Clear(rented, 0, _cellCount);
            if (spanCells >= _cellCount)
            {
                for (int h = 0; h < _cellCount; h++)
                    AppendBucket(h, ref buffer, ref written);
                return written;
            }

            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int hash = HashCoords(cx, cy);
                    if (rented[hash] != 0) continue;
                    rented[hash] = 1;
                    AppendBucket(hash, ref buffer, ref written);
                }
            }
            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendBucket(int hash, ref int[] buffer, ref int written)
    {
        int node = _cellHeads[hash];
        while (node != -1)
        {
            if (written >= buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(256, buffer.Length * 2));
            }
            buffer[written++] = _entryEntityIds[node];
            node = _entryNext[node];
        }
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
        GC.SuppressFinalize(this);

        if (_cellHeads != null)
        {
            NativeMemory.Free(_cellHeads);
            _cellHeads = null;
        }

        if (_entryEntityIds != null)
        {
            NativeMemory.Free(_entryEntityIds);
            _entryEntityIds = null;
        }

        if (_entryNext != null)
        {
            NativeMemory.Free(_entryNext);
            _entryNext = null;
        }
    }
}
