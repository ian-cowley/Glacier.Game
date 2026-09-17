namespace Glacier.Game.Tests;

using System;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Xunit;

public class EcsTests
{
    [Fact]
    public void Entity_CreationAndRecycling_IncrementsVersion()
    {
        using var world = new World();

        var e1 = world.CreateEntity();
        Assert.Equal(0, e1.Id);
        Assert.Equal(0, e1.Version);
        Assert.True(world.IsAlive(e1));

        world.DestroyEntity(e1);
        Assert.False(world.IsAlive(e1));

        var e2 = world.CreateEntity();
        Assert.Equal(0, e2.Id);
        Assert.Equal(1, e2.Version); // Version incremented on recycling
        Assert.True(world.IsAlive(e2));
    }

    [Fact]
    public void Entity_ComponentLifecycle_SetGetRemove()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        Assert.False(world.HasComponent<Position2D>(entity));

        world.SetComponent(entity, new Position2D(10f, 20f));
        Assert.True(world.HasComponent<Position2D>(entity));

        ref var pos = ref world.GetComponent<Position2D>(entity);
        Assert.Equal(10f, pos.X);
        Assert.Equal(20f, pos.Y);

        pos.X = 50f;
        ref readonly var posUpdated = ref world.GetComponent<Position2D>(entity);
        Assert.Equal(50f, posUpdated.X);

        world.RemoveComponent<Position2D>(entity);
        Assert.False(world.HasComponent<Position2D>(entity));
    }

    [Fact]
    public void Entity_ArchetypeMigration_PreservesExistingData()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        world.SetComponent(entity, new Position2D(100f, 200f));
        world.SetComponent(entity, new Velocity2D(5f, -5f));

        Assert.True(world.HasComponent<Position2D>(entity));
        Assert.True(world.HasComponent<Velocity2D>(entity));

        ref readonly var pos = ref world.GetComponent<Position2D>(entity);
        ref readonly var vel = ref world.GetComponent<Velocity2D>(entity);

        Assert.Equal(100f, pos.X);
        Assert.Equal(200f, pos.Y);
        Assert.Equal(5f, vel.X);
        Assert.Equal(-5f, vel.Y);
    }

    [Fact]
    public void Query_SingleAndMultiComponent_ReturnsContiguousSpans()
    {
        using var world = new World();

        for (int i = 0; i < 100; i++)
        {
            world.CreateEntity(new Position2D(i, i * 2), new Velocity2D(1f, 1f));
        }

        var q2 = world.Query<Position2D, Velocity2D>();
        Assert.Equal(100, q2.Count);
        Assert.Equal(100, q2.Component1Span.Length);
        Assert.Equal(100, q2.Component2Span.Length);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i, q2.Component1Span[i].X);
            Assert.Equal(i * 2, q2.Component1Span[i].Y);
            Assert.Equal(1f, q2.Component2Span[i].X);
        }
    }

    [Fact]
    public void EntityBuilder_FluentSyntax_ConstructsEntity()
    {
        using var world = new World();

        var entity = world.BuildEntity()
            .With(new Position2D(42f, 84f))
            .With(new Velocity2D(1.5f, 2.5f))
            .With(new CircleCollider2D(10f))
            .Build();

        Assert.True(world.HasComponent<Position2D>(entity));
        Assert.True(world.HasComponent<Velocity2D>(entity));
        Assert.True(world.HasComponent<CircleCollider2D>(entity));

        Assert.Equal(42f, world.GetComponent<Position2D>(entity).X);
        Assert.Equal(10f, world.GetComponent<CircleCollider2D>(entity).Radius);
    }

    [Fact]
    public void ArchetypeTable_ComponentsAndEntities_Are64ByteAligned()
    {
        var mask = ComponentMask.Empty.With(ComponentType<Position2D>.Id).With(ComponentType<Velocity2D>.Id);
        var archetype = new Archetype(1, mask);
        using var table = new ArchetypeTable(archetype, 128);

        // Populate table with entities
        for (int i = 0; i < 64; i++)
        {
            int row = table.AddEntity(new Entity(i, 0));
            table.SetComponent(row, new Position2D(i, i * 2));
            table.SetComponent(row, new Velocity2D(10f, 20f));
        }

        // Verify 64-byte alignment
        Assert.True(table.IsColumnAligned<Position2D>(64), "Position2D column must be 64-byte cache-line aligned");
        Assert.True(table.IsColumnAligned<Velocity2D>(64), "Velocity2D column must be 64-byte cache-line aligned");
        Assert.True(table.IsEntitiesAligned(64), "Entity buffer must be 64-byte cache-line aligned");
    }

    [Fact]
    public void ArchetypeTable_CapacityReallocation_Preserves64ByteAlignment()
    {
        var mask = ComponentMask.Empty.With(ComponentType<Position2D>.Id).With(ComponentType<Velocity2D>.Id);
        var archetype = new Archetype(2, mask);
        using var table = new ArchetypeTable(archetype, 16);

        // Add 500 entities to force multiple capacity reallocations
        for (int i = 0; i < 500; i++)
        {
            int row = table.AddEntity(new Entity(i, 0));
            table.SetComponent(row, new Position2D(i, i * 3));
            table.SetComponent(row, new Velocity2D(1f, 2f));
        }

        Assert.True(table.Capacity >= 500);
        Assert.True(table.IsColumnAligned<Position2D>(64), "Position2D column must preserve 64-byte alignment after growth");
        Assert.True(table.IsColumnAligned<Velocity2D>(64), "Velocity2D column must preserve 64-byte alignment after growth");
        Assert.True(table.IsEntitiesAligned(64), "Entities buffer must preserve 64-byte alignment after growth");

        // Verify data integrity
        var posSpan = table.GetSpan<Position2D>();
        var velSpan = table.GetSpan<Velocity2D>();
        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(i, posSpan[i].X);
            Assert.Equal(i * 3, posSpan[i].Y);
            Assert.Equal(1f, velSpan[i].X);
            Assert.Equal(2f, velSpan[i].Y);
        }
    }
}
