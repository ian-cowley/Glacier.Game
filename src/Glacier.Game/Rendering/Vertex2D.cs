namespace Glacier.Game.Rendering;

using System;
using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// 2D render vertex layout for batched quad and sprite pipelines.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vertex2D : IEquatable<Vertex2D>
{
    public Vector2 Position;
    public Vector2 TexCoord;
    public Color32 Color;

    public Vertex2D(Vector2 position, Vector2 texCoord, Color32 color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }

    public Vertex2D(float x, float y, float u, float v, Color32 color)
    {
        Position = new Vector2(x, y);
        TexCoord = new Vector2(u, v);
        Color = color;
    }

    public bool Equals(Vertex2D other) =>
        Position.Equals(other.Position) &&
        TexCoord.Equals(other.TexCoord) &&
        Color.Equals(other.Color);
}
