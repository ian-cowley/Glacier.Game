namespace Glacier.Game.Ecs;

using System;

/// <summary>
/// Contiguous SoA slice for a single matching archetype table in a 1-component query.
/// </summary>
public readonly ref struct ArchetypeChunk<T1> where T1 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly ReadOnlySpan<Entity> Entities;
    internal readonly ArchetypeTable Table;
    public int Count => Entities.Length;

    internal ArchetypeChunk(ArchetypeTable table, Span<T1> c1, ReadOnlySpan<Entity> entities)
    {
        Table = table;
        Component1Span = c1;
        Entities = entities;
    }
}

/// <summary>
/// Contiguous SoA slice for a single matching archetype table in a 2-component query.
/// </summary>
public readonly ref struct ArchetypeChunk<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly ReadOnlySpan<Entity> Entities;
    internal readonly ArchetypeTable Table;
    public int Count => Entities.Length;

    internal ArchetypeChunk(ArchetypeTable table, Span<T1> c1, Span<T2> c2, ReadOnlySpan<Entity> entities)
    {
        Table = table;
        Component1Span = c1;
        Component2Span = c2;
        Entities = entities;
    }
}

/// <summary>
/// Contiguous SoA slice for a single matching archetype table in a 3-component query.
/// </summary>
public readonly ref struct ArchetypeChunk<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly Span<T3> Component3Span;
    public readonly ReadOnlySpan<Entity> Entities;
    internal readonly ArchetypeTable Table;
    public int Count => Entities.Length;

    internal ArchetypeChunk(ArchetypeTable table, Span<T1> c1, Span<T2> c2, Span<T3> c3, ReadOnlySpan<Entity> entities)
    {
        Table = table;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Entities = entities;
    }
}

/// <summary>
/// Contiguous SoA slice for a single matching archetype table in a 4-component query.
/// </summary>
public readonly ref struct ArchetypeChunk<T1, T2, T3, T4>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
    where T4 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly Span<T3> Component3Span;
    public readonly Span<T4> Component4Span;
    public readonly ReadOnlySpan<Entity> Entities;
    internal readonly ArchetypeTable Table;
    public int Count => Entities.Length;

    internal ArchetypeChunk(ArchetypeTable table, Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4, ReadOnlySpan<Entity> entities)
    {
        Table = table;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Component4Span = c4;
        Entities = entities;
    }
}

/// <summary>
/// Zero-allocation SoA query for entities with a single unmanaged component.
/// Exposes the largest matching archetype span directly and enumerates all matching archetype chunks via <c>foreach</c>.
/// </summary>
public readonly ref struct Query<T1> where T1 : unmanaged
{
    private readonly ReadOnlySpan<ArchetypeTable> _tables;
    private readonly ComponentMask _mask;

    public readonly Span<T1> Component1Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;
    public int TotalCount { get; }

    public Query(Span<T1> c1, ReadOnlySpan<Entity> entities)
    {
        _tables = ReadOnlySpan<ArchetypeTable>.Empty;
        _mask = default;
        Component1Span = c1;
        Entities = entities;
        TotalCount = entities.Length;
    }

    internal Query(Span<T1> c1, ReadOnlySpan<Entity> entities, ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask, int totalCount)
    {
        _tables = tables;
        _mask = mask;
        Component1Span = c1;
        Entities = entities;
        TotalCount = totalCount;
    }

    public Enumerator GetEnumerator() => new(_tables, _mask);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<ArchetypeTable> _tables;
        private readonly ComponentMask _mask;
        private int _index;
        public ArchetypeChunk<T1> Current { get; private set; }

        internal Enumerator(ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask)
        {
            _tables = tables;
            _mask = mask;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            while (++_index < _tables.Length)
            {
                var tbl = _tables[_index];
                if (tbl.Count > 0 && tbl.Archetype.Mask.ContainsAll(_mask))
                {
                    Current = new ArchetypeChunk<T1>(tbl, tbl.GetSpan<T1>(), tbl.GetEntities());
                    return true;
                }
            }
            return false;
        }
    }
}

/// <summary>
/// Zero-allocation SoA query for entities with two unmanaged components.
/// Exposes the largest matching archetype spans directly and enumerates all matching archetype chunks via <c>foreach</c>.
/// </summary>
public readonly ref struct Query<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    private readonly ReadOnlySpan<ArchetypeTable> _tables;
    private readonly ComponentMask _mask;

    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;
    public int TotalCount { get; }

    public Query(Span<T1> c1, Span<T2> c2, ReadOnlySpan<Entity> entities)
    {
        _tables = ReadOnlySpan<ArchetypeTable>.Empty;
        _mask = default;
        Component1Span = c1;
        Component2Span = c2;
        Entities = entities;
        TotalCount = entities.Length;
    }

    internal Query(Span<T1> c1, Span<T2> c2, ReadOnlySpan<Entity> entities, ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask, int totalCount)
    {
        _tables = tables;
        _mask = mask;
        Component1Span = c1;
        Component2Span = c2;
        Entities = entities;
        TotalCount = totalCount;
    }

    public Enumerator GetEnumerator() => new(_tables, _mask);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<ArchetypeTable> _tables;
        private readonly ComponentMask _mask;
        private int _index;
        public ArchetypeChunk<T1, T2> Current { get; private set; }

        internal Enumerator(ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask)
        {
            _tables = tables;
            _mask = mask;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            while (++_index < _tables.Length)
            {
                var tbl = _tables[_index];
                if (tbl.Count > 0 && tbl.Archetype.Mask.ContainsAll(_mask))
                {
                    Current = new ArchetypeChunk<T1, T2>(tbl, tbl.GetSpan<T1>(), tbl.GetSpan<T2>(), tbl.GetEntities());
                    return true;
                }
            }
            return false;
        }
    }
}

/// <summary>
/// Zero-allocation SoA query for entities with three unmanaged components.
/// Exposes the largest matching archetype spans directly and enumerates all matching archetype chunks via <c>foreach</c>.
/// </summary>
public readonly ref struct Query<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    private readonly ReadOnlySpan<ArchetypeTable> _tables;
    private readonly ComponentMask _mask;

    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly Span<T3> Component3Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;
    public int TotalCount { get; }

    public Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, ReadOnlySpan<Entity> entities)
    {
        _tables = ReadOnlySpan<ArchetypeTable>.Empty;
        _mask = default;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Entities = entities;
        TotalCount = entities.Length;
    }

    internal Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, ReadOnlySpan<Entity> entities, ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask, int totalCount)
    {
        _tables = tables;
        _mask = mask;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Entities = entities;
        TotalCount = totalCount;
    }

    public Enumerator GetEnumerator() => new(_tables, _mask);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<ArchetypeTable> _tables;
        private readonly ComponentMask _mask;
        private int _index;
        public ArchetypeChunk<T1, T2, T3> Current { get; private set; }

        internal Enumerator(ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask)
        {
            _tables = tables;
            _mask = mask;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            while (++_index < _tables.Length)
            {
                var tbl = _tables[_index];
                if (tbl.Count > 0 && tbl.Archetype.Mask.ContainsAll(_mask))
                {
                    Current = new ArchetypeChunk<T1, T2, T3>(tbl, tbl.GetSpan<T1>(), tbl.GetSpan<T2>(), tbl.GetSpan<T3>(), tbl.GetEntities());
                    return true;
                }
            }
            return false;
        }
    }
}

/// <summary>
/// Zero-allocation SoA query for entities with four unmanaged components.
/// Exposes the largest matching archetype spans directly and enumerates all matching archetype chunks via <c>foreach</c>.
/// </summary>
public readonly ref struct Query<T1, T2, T3, T4>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
    where T4 : unmanaged
{
    private readonly ReadOnlySpan<ArchetypeTable> _tables;
    private readonly ComponentMask _mask;

    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly Span<T3> Component3Span;
    public readonly Span<T4> Component4Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;
    public int TotalCount { get; }

    public Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4, ReadOnlySpan<Entity> entities)
    {
        _tables = ReadOnlySpan<ArchetypeTable>.Empty;
        _mask = default;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Component4Span = c4;
        Entities = entities;
        TotalCount = entities.Length;
    }

    internal Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4, ReadOnlySpan<Entity> entities, ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask, int totalCount)
    {
        _tables = tables;
        _mask = mask;
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Component4Span = c4;
        Entities = entities;
        TotalCount = totalCount;
    }

    public Enumerator GetEnumerator() => new(_tables, _mask);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<ArchetypeTable> _tables;
        private readonly ComponentMask _mask;
        private int _index;
        public ArchetypeChunk<T1, T2, T3, T4> Current { get; private set; }

        internal Enumerator(ReadOnlySpan<ArchetypeTable> tables, ComponentMask mask)
        {
            _tables = tables;
            _mask = mask;
            _index = -1;
            Current = default;
        }

        public bool MoveNext()
        {
            while (++_index < _tables.Length)
            {
                var tbl = _tables[_index];
                if (tbl.Count > 0 && tbl.Archetype.Mask.ContainsAll(_mask))
                {
                    Current = new ArchetypeChunk<T1, T2, T3, T4>(tbl, tbl.GetSpan<T1>(), tbl.GetSpan<T2>(), tbl.GetSpan<T3>(), tbl.GetSpan<T4>(), tbl.GetEntities());
                    return true;
                }
            }
            return false;
        }
    }
}
