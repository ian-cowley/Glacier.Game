namespace Glacier.Game.Ecs;

using System;

/// <summary>
/// Zero-allocation, contiguous SoA query for entities with a single unmanaged component.
/// </summary>
public readonly ref struct Query<T1> where T1 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;

    public Query(Span<T1> c1, ReadOnlySpan<Entity> entities)
    {
        Component1Span = c1;
        Entities = entities;
    }
}

/// <summary>
/// Zero-allocation, contiguous SoA query for entities with two unmanaged components.
/// </summary>
public readonly ref struct Query<T1, T2> where T1 : unmanaged where T2 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;

    public Query(Span<T1> c1, Span<T2> c2, ReadOnlySpan<Entity> entities)
    {
        Component1Span = c1;
        Component2Span = c2;
        Entities = entities;
    }
}

/// <summary>
/// Zero-allocation, contiguous SoA query for entities with three unmanaged components.
/// </summary>
public readonly ref struct Query<T1, T2, T3> 
    where T1 : unmanaged 
    where T2 : unmanaged 
    where T3 : unmanaged
{
    public readonly Span<T1> Component1Span;
    public readonly Span<T2> Component2Span;
    public readonly Span<T3> Component3Span;
    public readonly ReadOnlySpan<Entity> Entities;
    public int Count => Entities.Length;

    public Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, ReadOnlySpan<Entity> entities)
    {
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Entities = entities;
    }
}

/// <summary>
/// Zero-allocation, contiguous SoA query for entities with four unmanaged components.
/// </summary>
public readonly ref struct Query<T1, T2, T3, T4> 
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
    public int Count => Entities.Length;

    public Query(Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4, ReadOnlySpan<Entity> entities)
    {
        Component1Span = c1;
        Component2Span = c2;
        Component3Span = c3;
        Component4Span = c4;
        Entities = entities;
    }
}
