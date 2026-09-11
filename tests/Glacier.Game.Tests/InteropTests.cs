namespace Glacier.Game.Tests;

using System;
using Glacier.Game.Ecs;
using Glacier.Game.Interop;
using Glacier.Game.Physics;
using Glacier.Polaris;
using Glacier.Tensor.Core;
using Xunit;

public class InteropTests
{
    [Fact]
    public void Polaris_ExportAndImport_PreservesState()
    {
        using var world1 = new World();
        var e1 = world1.CreateEntity(new Position2D(10f, 20f), new Velocity2D(1f, -1f));
        var e2 = world1.CreateEntity(new Position2D(30f, 40f), new Velocity2D(2f, -2f));

        var entities = new Entity[] { e1, e2 };
        var df = world1.ExportToDataFrame(entities);

        Assert.Equal(2, df.RowCount);
        Assert.Equal(5, df.Columns.Count);

        using var world2 = new World();
        int importedCount = world2.ImportFromDataFrame(df);
        Assert.Equal(2, importedCount);

        var query = world2.Query<Position2D, Velocity2D>();
        Assert.Equal(2, query.Count);
        Assert.Equal(10f, query.Component1Span[0].X);
        Assert.Equal(20f, query.Component1Span[0].Y);
        Assert.Equal(1f, query.Component2Span[0].X);
    }

    [Fact]
    public void Tensor_ObservationAndAction_RoundTrips()
    {
        using var world = new World();
        var e1 = world.CreateEntity(new Position2D(1.5f, 2.5f), new Velocity2D(3.5f, 4.5f));
        var entities = new Entity[] { e1 };

        using var obs = world.ToObservationTensor(entities);
        Assert.Equal(1, obs.Shape[0]);
        Assert.Equal(4, obs.Shape[1]);

        unsafe
        {
            Assert.Equal(1.5f, obs[0, 0]);
            Assert.Equal(2.5f, obs[0, 1]);
            Assert.Equal(3.5f, obs[0, 2]);
            Assert.Equal(4.5f, obs[0, 3]);
        }

        // Apply actions
        using var actions = new Tensor<float>(1, 2);
        unsafe
        {
            actions[0, 0] = -10f;
            actions[0, 1] = 20f;
        }

        world.ApplyActionTensor(entities, actions);

        ref readonly var updatedVel = ref world.GetComponent<Velocity2D>(e1);
        Assert.Equal(-10f, updatedVel.X);
        Assert.Equal(20f, updatedVel.Y);
    }

    [Fact]
    public void GpuGameBridge_SimulateParticles_RunsSuccessfully()
    {
        using var bridge = new GpuGameBridge();
        Assert.NotNull(bridge.BackendName);

        var positions = new Position2D[] { new(0f, 0f), new(10f, 10f) };
        var velocities = new Velocity2D[] { new(5f, 5f), new(-2f, -2f) };

        bridge.SimulateParticles(positions, velocities, dt: 0.1f, damping: 1.0f);

        Assert.Equal(0.5f, positions[0].X, 3);
        Assert.Equal(0.5f, positions[0].Y, 3);
        Assert.Equal(9.8f, positions[1].X, 3);
        Assert.Equal(9.8f, positions[1].Y, 3);
    }
}
