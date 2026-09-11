namespace Glacier.Game.Rendering;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Packed 32-bit RGBA color representation.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 4)]
public readonly struct Color32 : IEquatable<Color32>
{
    [FieldOffset(0)] public readonly uint Rgba;
    [FieldOffset(0)] public readonly byte R;
    [FieldOffset(1)] public readonly byte G;
    [FieldOffset(2)] public readonly byte B;
    [FieldOffset(3)] public readonly byte A;

    public static readonly Color32 White = new(255, 255, 255, 255);
    public static readonly Color32 Black = new(0, 0, 0, 255);
    public static readonly Color32 Transparent = new(0, 0, 0, 0);
    public static readonly Color32 Red = new(255, 0, 0, 255);
    public static readonly Color32 Green = new(0, 255, 0, 255);
    public static readonly Color32 Blue = new(0, 0, 255, 255);
    public static readonly Color32 Yellow = new(255, 255, 0, 255);
    public static readonly Color32 Cyan = new(0, 255, 255, 255);
    public static readonly Color32 Magenta = new(255, 0, 255, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        Rgba = 0; // Silences compiler
        R = r;
        G = g;
        B = b;
        A = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32(uint rgba)
    {
        R = 0; G = 0; B = 0; A = 0; // Silences compiler
        Rgba = rgba;
    }

    public static Color32 FromRgb(float r, float g, float b, float a = 1.0f)
    {
        byte bR = (byte)Math.Clamp((int)(r * 255f), 0, 255);
        byte bG = (byte)Math.Clamp((int)(g * 255f), 0, 255);
        byte bB = (byte)Math.Clamp((int)(b * 255f), 0, 255);
        byte bA = (byte)Math.Clamp((int)(a * 255f), 0, 255);
        return new Color32(bR, bG, bB, bA);
    }

    public bool Equals(Color32 other) => Rgba == other.Rgba;
    public override bool Equals(object? obj) => obj is Color32 other && Equals(other);
    public override int GetHashCode() => Rgba.GetHashCode();

    public static bool operator ==(Color32 left, Color32 right) => left.Rgba == right.Rgba;
    public static bool operator !=(Color32 left, Color32 right) => left.Rgba != right.Rgba;

    public override string ToString() => $"Color32(R:{R}, G:{G}, B:{B}, A:{A})";
}
