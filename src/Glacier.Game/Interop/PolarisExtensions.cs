namespace Glacier.Game.Interop;

using System;
using System.Linq;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Polaris;
using Glacier.Polaris.Data;

/// <summary>
/// High-performance interoperability extensions between Glacier.Game ECS and Glacier.Polaris DataFrames.
/// Enables zero-copy/low-overhead telemetry logging, game state snapshots, and tabular entity loading.
/// </summary>
public static class PolarisExtensions
{
    /// <summary>
    /// Exports entity positions and velocities into a columnar Glacier.Polaris DataFrame.
    /// </summary>
    public static DataFrame ExportToDataFrame(this World world, ReadOnlySpan<Entity> entities)
    {
        int count = entities.Length;
        var idSeries = new Int32Series("EntityId", count);
        var xSeries = new Float32Series("X", count);
        var ySeries = new Float32Series("Y", count);
        var vxSeries = new Float32Series("Vx", count);
        var vySeries = new Float32Series("Vy", count);

        for (int i = 0; i < count; i++)
        {
            Entity e = entities[i];
            idSeries[i] = e.Id;

            if (world.HasComponent<Position2D>(e))
            {
                ref readonly Position2D pos = ref world.GetComponent<Position2D>(e);
                xSeries[i] = pos.X;
                ySeries[i] = pos.Y;
            }

            if (world.HasComponent<Velocity2D>(e))
            {
                ref readonly Velocity2D vel = ref world.GetComponent<Velocity2D>(e);
                vxSeries[i] = vel.X;
                vySeries[i] = vel.Y;
            }
        }

        return new DataFrame([idSeries, xSeries, ySeries, vxSeries, vySeries]);
    }

    /// <summary>
    /// Spawns entities into the ECS World from a Glacier.Polaris DataFrame.
    /// </summary>
    public static int ImportFromDataFrame(this World world, DataFrame df)
    {
        var xCol = df.Columns.FirstOrDefault(c => c.Name == "X") 
            ?? throw new ArgumentException("DataFrame is missing 'X' column.");
        var yCol = df.Columns.FirstOrDefault(c => c.Name == "Y")
            ?? throw new ArgumentException("DataFrame is missing 'Y' column.");
        var vxCol = df.Columns.FirstOrDefault(c => c.Name == "Vx");
        var vyCol = df.Columns.FirstOrDefault(c => c.Name == "Vy");

        int rowCount = xCol.Length;

        for (int i = 0; i < rowCount; i++)
        {
            float x = Convert.ToSingle(xCol.Get(i));
            float y = Convert.ToSingle(yCol.Get(i));
            float vx = vxCol != null ? Convert.ToSingle(vxCol.Get(i)) : 0f;
            float vy = vyCol != null ? Convert.ToSingle(vyCol.Get(i)) : 0f;

            world.CreateEntity(new Position2D(x, y), new Velocity2D(vx, vy));
        }

        return rowCount;
    }
}
