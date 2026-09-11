namespace Glacier.Game.Physics;

using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Position2D : IEquatable<Position2D>
{
    public float X;
    public float Y;

    public Position2D(float x, float y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(Position2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override string ToString() => $"Position2D({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Velocity2D : IEquatable<Velocity2D>
{
    public float X;
    public float Y;

    public Velocity2D(float x, float y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(Velocity2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override string ToString() => $"Velocity2D({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Acceleration2D : IEquatable<Acceleration2D>
{
    public float X;
    public float Y;

    public Acceleration2D(float x, float y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(Acceleration2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override string ToString() => $"Acceleration2D({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AABB2D : IEquatable<AABB2D>
{
    public float MinX;
    public float MinY;
    public float MaxX;
    public float MaxY;

    public AABB2D(float minX, float minY, float maxX, float maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public static AABB2D FromCenterAndExtents(float cx, float cy, float halfW, float halfH)
    {
        return new AABB2D(cx - halfW, cy - halfH, cx + halfW, cy + halfH);
    }

    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;

    public bool Overlaps(in AABB2D other)
    {
        return MinX <= other.MaxX && MaxX >= other.MinX &&
               MinY <= other.MaxY && MaxY >= other.MinY;
    }

    public bool Equals(AABB2D other) =>
        MinX.Equals(other.MinX) && MinY.Equals(other.MinY) &&
        MaxX.Equals(other.MaxX) && MaxY.Equals(other.MaxY);

    public override string ToString() => $"AABB2D([{MinX}, {MinY}] to [{MaxX}, {MaxY}])";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CircleCollider2D : IEquatable<CircleCollider2D>
{
    public float Radius;

    public CircleCollider2D(float radius)
    {
        Radius = radius;
    }

    public bool Equals(CircleCollider2D other) => Radius.Equals(other.Radius);
    public override string ToString() => $"CircleCollider2D(Radius: {Radius})";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RigidBody2D : IEquatable<RigidBody2D>
{
    public float Mass;
    public float InvMass;
    public float Restitution;
    public float Drag;
    public byte IsStatic; // 0 = dynamic, 1 = static

    public RigidBody2D(float mass, float restitution = 0.8f, float drag = 0.01f, bool isStatic = false)
    {
        Mass = mass;
        InvMass = (isStatic || mass <= 0f) ? 0f : 1f / mass;
        Restitution = restitution;
        Drag = drag;
        IsStatic = (byte)(isStatic ? 1 : 0);
    }

    public bool Equals(RigidBody2D other) =>
        Mass.Equals(other.Mass) &&
        InvMass.Equals(other.InvMass) &&
        Restitution.Equals(other.Restitution) &&
        Drag.Equals(other.Drag) &&
        IsStatic == other.IsStatic;
}
