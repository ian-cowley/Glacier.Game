namespace Glacier.Game.Tests;

using System;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Xunit;

public class PhysicsTests
{
    [Fact]
    public void IntegrateEuler_MatchesAnalyticalSolution()
    {
        const int count = 64;
        var posX = new float[count];
        var posY = new float[count];
        var velX = new float[count];
        var velY = new float[count];

        for (int i = 0; i < count; i++)
        {
            posX[i] = 10.0f;
            posY[i] = 20.0f;
            velX[i] = 2.0f;
            velY[i] = -3.0f;
        }

        const float dt = 0.5f;
        PhysicsKernels.IntegrateEuler(posX, posY, velX, velY, dt);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(11.0f, posX[i], 4);
            Assert.Equal(18.5f, posY[i], 4);
        }
    }

    [Fact]
    public void ApplyGravity_IncrementsVerticalVelocity()
    {
        const int count = 32;
        var velY = new float[count];
        for (int i = 0; i < count; i++) velY[i] = 0f;

        PhysicsKernels.ApplyGravity(velY, 9.81f, 1.0f);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(9.81f, velY[i], 4);
        }
    }

    [Fact]
    public void ApplyDamping_ReducesVelocityMagnitude()
    {
        var vel = new Velocity2D[] { new(10f, 20f) };

        PhysicsKernels.ApplyDamping(vel, 0.1f, 1.0f);

        Assert.Equal(9.0f, vel[0].X, 4);
        Assert.Equal(18.0f, vel[0].Y, 4);
    }

    [Fact]
    public void ClampAndBounce_ReflectsVelocityAtBoundaries()
    {
        var pos = new Position2D[] { new(-5f, 105f) };
        var vel = new Velocity2D[] { new(-10f, 20f) };

        PhysicsKernels.ClampAndBounce(pos, vel, 0f, 100f, 0f, 100f, restitution: 1.0f);

        Assert.Equal(0f, pos[0].X);
        Assert.Equal(10f, vel[0].X); // Reflected
        Assert.Equal(100f, pos[0].Y);
        Assert.Equal(-20f, vel[0].Y); // Reflected
    }

    [Fact]
    public void SimdPhysicsSystem_UpdatesEntitiesInWorld()
    {
        using var world = new World();
        var sys = new SimdPhysicsSystem(minX: 0f, maxX: 100f, minY: 0f, maxY: 100f);

        var e = world.CreateEntity(new Position2D(10f, 10f), new Velocity2D(5f, 2f));

        sys.Update(world, 1.0f);

        ref readonly var pos = ref world.GetComponent<Position2D>(e);
        Assert.Equal(15f, pos.X, 3);
        Assert.Equal(12f, pos.Y, 3);
    }
}
