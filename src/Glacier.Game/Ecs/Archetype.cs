namespace Glacier.Game.Ecs;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// A 128-bit bitmask identifying component compositions for an archetype.
/// Supports up to 128 distinct component types with zero heap allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct ComponentMask : IEquatable<ComponentMask>
{
    public readonly ulong Low;
    public readonly ulong High;

    public static readonly ComponentMask Empty = new(0, 0);

    public ComponentMask(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentMask With(int typeId)
    {
        if ((uint)typeId < 64)
            return new ComponentMask(Low | (1UL << typeId), High);
        if ((uint)typeId < 128)
            return new ComponentMask(Low, High | (1UL << (typeId - 64)));
        throw new ArgumentOutOfRangeException(nameof(typeId), "Glacier.Game ECS supports up to 128 component types.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentMask Without(int typeId)
    {
        if ((uint)typeId < 64)
            return new ComponentMask(Low & ~(1UL << typeId), High);
        if ((uint)typeId < 128)
            return new ComponentMask(Low, High & ~(1UL << (typeId - 64)));
        throw new ArgumentOutOfRangeException(nameof(typeId), "Glacier.Game ECS supports up to 128 component types.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(int typeId)
    {
        if ((uint)typeId < 64)
            return (Low & (1UL << typeId)) != 0;
        if ((uint)typeId < 128)
            return (High & (1UL << (typeId - 64))) != 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsAll(in ComponentMask required)
    {
        return (Low & required.Low) == required.Low &&
               (High & required.High) == required.High;
    }

    public int ComponentCount => BitOperations.PopCount(Low) + BitOperations.PopCount(High);

    public bool Equals(ComponentMask other) => Low == other.Low && High == other.High;
    public override bool Equals(object? obj) => obj is ComponentMask other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Low, High);

    public static bool operator ==(ComponentMask left, ComponentMask right) => left.Equals(right);
    public static bool operator !=(ComponentMask left, ComponentMask right) => !left.Equals(right);
}

/// <summary>
/// Represents a unique combination of component types in Glacier.Game ECS.
/// </summary>
public sealed class Archetype : IEquatable<Archetype>
{
    public int Id { get; }
    public ComponentMask Mask { get; }
    public int[] ComponentTypeIds { get; }
    public int ComponentCount => ComponentTypeIds.Length;

    public Archetype(int id, ComponentMask mask)
    {
        Id = id;
        Mask = mask;

        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 128; i++)
        {
            if (mask.Has(i))
                list.Add(i);
        }
        ComponentTypeIds = list.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Matches(in ComponentMask queryMask) => Mask.ContainsAll(queryMask);

    public bool Equals(Archetype? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Archetype);
    public override int GetHashCode() => Id;
}
