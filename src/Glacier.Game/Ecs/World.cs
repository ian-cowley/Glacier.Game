namespace Glacier.Game.Ecs;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Record mapping an Entity Id to its current ArchetypeTable and row index.
/// </summary>
internal struct EntityRecord
{
    public ArchetypeTable? Table;
    public int Row;
}

/// <summary>
/// High-performance data-oriented ECS World managing entity lifecycles,
/// Struct of Arrays (SoA) component tables, zero-allocation queries, and systems.
/// </summary>
public sealed class World : IDisposable
{
    private const int InitialEntityCapacity = 65536;

    private int[] _versions;
    private EntityRecord[] _records;
    private readonly Stack<int> _recycledIds = new();
    private int _nextId;
    private int _activeEntityCount;

    private readonly Dictionary<ComponentMask, ArchetypeTable> _tables = new();
    private readonly List<ArchetypeTable> _tableList = new();
    private readonly List<ISystem> _systems = new();

    private bool _disposed;

    public int EntityCount => _activeEntityCount;
    public IReadOnlyList<ISystem> Systems => _systems;

    public World(int initialCapacity = InitialEntityCapacity)
    {
        int cap = Math.Max(1024, initialCapacity);
        _versions = new int[cap];
        _records = new EntityRecord[cap];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity CreateEntity()
    {
        int id;
        if (_recycledIds.Count > 0)
        {
            id = _recycledIds.Pop();
        }
        else
        {
            id = _nextId++;
            EnsureEntityCapacity(id + 1);
        }

        int version = _versions[id];
        _activeEntityCount++;
        _records[id] = new EntityRecord { Table = null, Row = -1 };
        return new Entity(id, version);
    }

    /// <summary>
    /// Fast entity creation with two initial components directly placed into the corresponding archetype table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity CreateEntity<T1, T2>(in T1 component1, in T2 component2) 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        Entity e = CreateEntity();
        var mask = ComponentMask.Empty.With(ComponentType<T1>.Id).With(ComponentType<T2>.Id);
        var table = GetOrCreateTable(mask);
        int row = table.AddEntity(e);
        table.SetComponent(row, component1);
        table.SetComponent(row, component2);
        _records[e.Id] = new EntityRecord { Table = table, Row = row };
        return e;
    }

    /// <summary>
    /// Fast entity creation with three initial components directly placed into the corresponding archetype table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity CreateEntity<T1, T2, T3>(in T1 component1, in T2 component2, in T3 component3)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        Entity e = CreateEntity();
        var mask = ComponentMask.Empty
            .With(ComponentType<T1>.Id)
            .With(ComponentType<T2>.Id)
            .With(ComponentType<T3>.Id);
        var table = GetOrCreateTable(mask);
        int row = table.AddEntity(e);
        table.SetComponent(row, component1);
        table.SetComponent(row, component2);
        table.SetComponent(row, component3);
        _records[e.Id] = new EntityRecord { Table = table, Row = row };
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity CreateEntity<T1, T2, T3, T4>(in T1 component1, in T2 component2, in T3 component3, in T4 component4)
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
    {
        Entity e = CreateEntity();
        var mask = ComponentMask.Empty
            .With(ComponentType<T1>.Id)
            .With(ComponentType<T2>.Id)
            .With(ComponentType<T3>.Id)
            .With(ComponentType<T4>.Id);
        var table = GetOrCreateTable(mask);
        int row = table.AddEntity(e);
        table.SetComponent(row, component1);
        table.SetComponent(row, component2);
        table.SetComponent(row, component3);
        table.SetComponent(row, component4);
        _records[e.Id] = new EntityRecord { Table = table, Row = row };
        return e;
    }

    /// <summary>
    /// Destroys the entity, recycling its identifier and swapping components in O(1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DestroyEntity(Entity entity)
    {
        ValidateEntity(entity);

        ref EntityRecord record = ref _records[entity.Id];
        if (record.Table != null)
        {
            Entity moved = record.Table.RemoveAt(record.Row);
            if (!moved.IsNull)
            {
                _records[moved.Id].Row = record.Row;
            }
            record.Table = null;
            record.Row = -1;
        }

        _versions[entity.Id]++;
        _recycledIds.Push(entity.Id);
        _activeEntityCount--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity)
    {
        return (uint)entity.Id < (uint)_versions.Length && _versions[entity.Id] == entity.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(Entity entity) where T : unmanaged
    {
        ValidateEntity(entity);
        var table = _records[entity.Id].Table;
        return table != null && table.Archetype.Mask.Has(ComponentType<T>.Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(Entity entity) where T : unmanaged
    {
        ValidateEntity(entity);
        ref EntityRecord record = ref _records[entity.Id];
        if (record.Table == null || !record.Table.Archetype.Mask.Has(ComponentType<T>.Id))
        {
            throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");
        }
        return ref record.Table.GetComponent<T>(record.Row);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
    {
        SetComponent(entity, in component);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
    {
        ValidateEntity(entity);
        ref EntityRecord record = ref _records[entity.Id];

        if (record.Table == null)
        {
            var mask = ComponentMask.Empty.With(ComponentType<T>.Id);
            var table = GetOrCreateTable(mask);
            int row = table.AddEntity(entity);
            table.SetComponent(row, component);
            record.Table = table;
            record.Row = row;
            return;
        }

        int typeId = ComponentType<T>.Id;
        if (record.Table.Archetype.Mask.Has(typeId))
        {
            record.Table.SetComponent(record.Row, component);
            return;
        }

        // Migrate entity to new archetype with added component
        var newMask = record.Table.Archetype.Mask.With(typeId);
        var newTable = GetOrCreateTable(newMask);

        int newRow = newTable.AddEntity(entity);
        // Copy existing components
        foreach (int existingTypeId in record.Table.Archetype.ComponentTypeIds)
        {
            CopyComponentData(record.Table, record.Row, newTable, newRow, existingTypeId);
        }
        newTable.SetComponent(newRow, component);

        Entity moved = record.Table.RemoveAt(record.Row);
        if (!moved.IsNull)
        {
            _records[moved.Id].Row = record.Row;
        }

        record.Table = newTable;
        record.Row = newRow;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
        ValidateEntity(entity);
        ref EntityRecord record = ref _records[entity.Id];
        if (record.Table == null || !record.Table.Archetype.Mask.Has(ComponentType<T>.Id))
            return;

        int typeId = ComponentType<T>.Id;
        var newMask = record.Table.Archetype.Mask.Without(typeId);

        if (newMask == ComponentMask.Empty)
        {
            Entity moved = record.Table.RemoveAt(record.Row);
            if (!moved.IsNull) _records[moved.Id].Row = record.Row;
            record.Table = null;
            record.Row = -1;
            return;
        }

        var newTable = GetOrCreateTable(newMask);
        int newRow = newTable.AddEntity(entity);
        foreach (int existingTypeId in newTable.Archetype.ComponentTypeIds)
        {
            CopyComponentData(record.Table, record.Row, newTable, newRow, existingTypeId);
        }

        Entity movedEntity = record.Table.RemoveAt(record.Row);
        if (!movedEntity.IsNull)
        {
            _records[movedEntity.Id].Row = record.Row;
        }

        record.Table = newTable;
        record.Row = newRow;
    }

    /// <summary>
    /// Gets a zero-allocation query over the primary archetype table matching T1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Query<T1> Query<T1>() where T1 : unmanaged
    {
        var mask = ComponentMask.Empty.With(ComponentType<T1>.Id);
        var table = FindPrimaryTable(mask);
        if (table == null || table.Count == 0)
        {
            return new Query<T1>(Span<T1>.Empty, ReadOnlySpan<Entity>.Empty);
        }
        return new Query<T1>(table.GetSpan<T1>(), table.GetEntities());
    }

    /// <summary>
    /// Gets a zero-allocation query over the primary archetype table matching T1 and T2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Query<T1, T2> Query<T1, T2>() 
        where T1 : unmanaged 
        where T2 : unmanaged
    {
        var mask = ComponentMask.Empty
            .With(ComponentType<T1>.Id)
            .With(ComponentType<T2>.Id);
        var table = FindPrimaryTable(mask);
        if (table == null || table.Count == 0)
        {
            return new Query<T1, T2>(Span<T1>.Empty, Span<T2>.Empty, ReadOnlySpan<Entity>.Empty);
        }
        return new Query<T1, T2>(table.GetSpan<T1>(), table.GetSpan<T2>(), table.GetEntities());
    }

    /// <summary>
    /// Gets a zero-allocation query over the primary archetype table matching T1, T2, and T3.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Query<T1, T2, T3> Query<T1, T2, T3>()
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        var mask = ComponentMask.Empty
            .With(ComponentType<T1>.Id)
            .With(ComponentType<T2>.Id)
            .With(ComponentType<T3>.Id);
        var table = FindPrimaryTable(mask);
        if (table == null || table.Count == 0)
        {
            return new Query<T1, T2, T3>(Span<T1>.Empty, Span<T2>.Empty, Span<T3>.Empty, ReadOnlySpan<Entity>.Empty);
        }
        return new Query<T1, T2, T3>(table.GetSpan<T1>(), table.GetSpan<T2>(), table.GetSpan<T3>(), table.GetEntities());
    }

    /// <summary>
    /// Gets a zero-allocation query over the primary archetype table matching T1, T2, T3, and T4.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Query<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
    {
        var mask = ComponentMask.Empty
            .With(ComponentType<T1>.Id)
            .With(ComponentType<T2>.Id)
            .With(ComponentType<T3>.Id)
            .With(ComponentType<T4>.Id);
        var table = FindPrimaryTable(mask);
        if (table == null || table.Count == 0)
        {
            return new Query<T1, T2, T3, T4>(Span<T1>.Empty, Span<T2>.Empty, Span<T3>.Empty, Span<T4>.Empty, ReadOnlySpan<Entity>.Empty);
        }
        return new Query<T1, T2, T3, T4>(table.GetSpan<T1>(), table.GetSpan<T2>(), table.GetSpan<T3>(), table.GetSpan<T4>(), table.GetEntities());
    }

    public void AddSystem(ISystem system)
    {
        _systems.Add(system);
    }

    public void RunSystems(float dt)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].Update(this, dt);
        }
    }

    public void RunSystem<TSystem>(float dt) where TSystem : ISystem, new()
    {
        var sys = new TSystem();
        sys.Update(this, dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArchetypeTable? FindPrimaryTable(in ComponentMask mask)
    {
        ArchetypeTable? best = null;
        int maxCount = -1;
        for (int i = 0; i < _tableList.Count; i++)
        {
            var tbl = _tableList[i];
            if (tbl.Archetype.Mask.ContainsAll(mask) && tbl.Count > maxCount)
            {
                best = tbl;
                maxCount = tbl.Count;
            }
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArchetypeTable GetOrCreateTable(in ComponentMask mask)
    {
        if (_tables.TryGetValue(mask, out var existing))
            return existing;

        var archetype = new Archetype(_tables.Count, mask);
        var table = new ArchetypeTable(archetype);
        _tables[mask] = table;
        _tableList.Add(table);
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyComponentData(ArchetypeTable src, int srcRow, ArchetypeTable dst, int dstRow, int typeId)
    {
        src.CopyComponentTo(srcRow, dst, dstRow, typeId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateEntity(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is invalid or destroyed.");
        }
    }

    private void EnsureEntityCapacity(int required)
    {
        if (required <= _versions.Length) return;

        int newCap = Math.Max(_versions.Length * 2, required);
        Array.Resize(ref _versions, newCap);
        Array.Resize(ref _records, newCap);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _tableList.Count; i++)
        {
            _tableList[i].Dispose();
        }
        _tableList.Clear();
        _tables.Clear();
    }
}
