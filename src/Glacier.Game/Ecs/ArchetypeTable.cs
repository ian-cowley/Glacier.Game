namespace Glacier.Game.Ecs;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Contiguous unmanaged Struct of Arrays (SoA) table for a specific Archetype.
/// Backed by NativeMemory.AllocZeroed with zero GC heap allocations.
/// </summary>
public sealed unsafe class ArchetypeTable : IDisposable
{
    private const int DefaultInitialCapacity = 1024;

    private readonly int[] _typeIdToColumnIndex;
    private readonly int[] _columnSizes;
    private byte** _columns;
    private Entity* _entities;
    private int _capacity;
    private bool _disposed;

    public Archetype Archetype { get; }
    public int Count { get; private set; }
    public int Capacity => _capacity;

    public ArchetypeTable(Archetype archetype, int initialCapacity = DefaultInitialCapacity)
    {
        Archetype = archetype;
        _capacity = Math.Max(16, initialCapacity);

        int maxTypeId = 0;
        foreach (int typeId in archetype.ComponentTypeIds)
        {
            if (typeId > maxTypeId) maxTypeId = typeId;
        }

        _typeIdToColumnIndex = new int[maxTypeId + 1];
        Array.Fill(_typeIdToColumnIndex, -1);

        _columnSizes = new int[archetype.ComponentCount];
        _columns = (byte**)NativeMemory.AllocZeroed((nuint)(sizeof(byte*) * archetype.ComponentCount));
        _entities = (Entity*)NativeMemory.AllocZeroed((nuint)(sizeof(Entity) * _capacity));

        for (int i = 0; i < archetype.ComponentCount; i++)
        {
            int typeId = archetype.ComponentTypeIds[i];
            _typeIdToColumnIndex[typeId] = i;
            // Size will be initialized on first component registration or query
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddEntity(Entity entity)
    {
        EnsureCapacity(Count + 1);
        int index = Count++;
        _entities[index] = entity;
        return index;
    }

    /// <summary>
    /// Removes an entity at the specified row using swap-with-last in O(1).
    /// Returns the entity that was moved into 'row' (or Entity.Null if row was the last element).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity RemoveAt(int row)
    {
        if ((uint)row >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(row));

        int lastIndex = Count - 1;
        Entity movedEntity = Entity.Null;

        if (row != lastIndex)
        {
            // Swap entity
            _entities[row] = _entities[lastIndex];
            movedEntity = _entities[row];

            // Swap components across all columns
            for (int col = 0; col < Archetype.ComponentCount; col++)
            {
                int elemSize = _columnSizes[col];
                if (elemSize > 0 && _columns[col] != null)
                {
                    byte* dst = _columns[col] + (row * elemSize);
                    byte* src = _columns[col] + (lastIndex * elemSize);
                    Buffer.MemoryCopy(src, dst, elemSize, elemSize);
                }
            }
        }

        Count--;
        return movedEntity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int row) where T : unmanaged
    {
        int typeId = ComponentType<T>.Id;
        int col = GetColumnIndex(typeId);
        EnsureColumnAllocated<T>(col);
        return ref Unsafe.AsRef<T>(_columns[col] + (row * sizeof(T)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponent<T>(int row, in T value) where T : unmanaged
    {
        int typeId = ComponentType<T>.Id;
        int col = GetColumnIndex(typeId);
        EnsureColumnAllocated<T>(col);
        Unsafe.AsRef<T>(_columns[col] + (row * sizeof(T))) = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan<T>() where T : unmanaged
    {
        int typeId = ComponentType<T>.Id;
        int col = GetColumnIndex(typeId);
        EnsureColumnAllocated<T>(col);
        return new Span<T>(_columns[col], Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<Entity> GetEntities()
    {
        return new ReadOnlySpan<Entity>(_entities, Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyComponentTo(int srcRow, ArchetypeTable dstTable, int dstRow, int typeId)
    {
        int srcCol = GetColumnIndex(typeId);
        int dstCol = dstTable.GetColumnIndex(typeId);
        int size = _columnSizes[srcCol];
        if (size > 0 && _columns[srcCol] != null)
        {
            dstTable.EnsureColumnAllocatedWithSize(dstCol, size);
            byte* src = _columns[srcCol] + (srcRow * size);
            byte* dst = dstTable._columns[dstCol] + (dstRow * size);
            Buffer.MemoryCopy(src, dst, size, size);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureColumnAllocatedWithSize(int col, int size)
    {
        if (_columnSizes[col] == 0)
        {
            _columnSizes[col] = size;
            _columns[col] = (byte*)NativeMemory.AllocZeroed((nuint)(size * _capacity));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetColumnIndex(int typeId)
    {
        if ((uint)typeId >= (uint)_typeIdToColumnIndex.Length || _typeIdToColumnIndex[typeId] < 0)
        {
            throw new InvalidOperationException($"Archetype {Archetype.Id} does not contain component type ID {typeId}");
        }
        return _typeIdToColumnIndex[typeId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureColumnAllocated<T>(int col) where T : unmanaged
    {
        if (_columnSizes[col] == 0)
        {
            _columnSizes[col] = sizeof(T);
            _columns[col] = (byte*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * _capacity));
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _capacity) return;

        int newCapacity = Math.Max(_capacity * 2, required);
        _entities = (Entity*)NativeMemory.Realloc(_entities, (nuint)(sizeof(Entity) * newCapacity));

        for (int col = 0; col < Archetype.ComponentCount; col++)
        {
            int elemSize = _columnSizes[col];
            if (elemSize > 0 && _columns[col] != null)
            {
                _columns[col] = (byte*)NativeMemory.Realloc(_columns[col], (nuint)(elemSize * newCapacity));
                // Clear newly allocated portion
                NativeMemory.Clear(_columns[col] + (_capacity * elemSize), (nuint)((newCapacity - _capacity) * elemSize));
            }
        }

        _capacity = newCapacity;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_entities != null)
        {
            NativeMemory.Free(_entities);
            _entities = null;
        }

        if (_columns != null)
        {
            for (int col = 0; col < Archetype.ComponentCount; col++)
            {
                if (_columns[col] != null)
                {
                    NativeMemory.Free(_columns[col]);
                    _columns[col] = null;
                }
            }
            NativeMemory.Free(_columns);
            _columns = null;
        }
    }
}
