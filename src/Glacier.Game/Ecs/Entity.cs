namespace Glacier.Game.Ecs;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Represents a lightweight, generational entity identifier in Glacier.Game ECS.
/// Pack = 4 ensures 8-byte cache alignment and compact memory footprint.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>
{
    public static readonly Entity Null = new(-1, 0);

    public readonly int Id;
    public readonly int Version;

    public Entity(int id, int version)
    {
        Id = id;
        Version = version;
    }

    public bool IsNull => Id < 0;

    public bool Equals(Entity other) => Id == other.Id && Version == other.Version;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Version);

    public int CompareTo(Entity other)
    {
        int idComp = Id.CompareTo(other.Id);
        return idComp != 0 ? idComp : Version.CompareTo(other.Version);
    }

    public override string ToString() => $"Entity({Id}v{Version})";

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
