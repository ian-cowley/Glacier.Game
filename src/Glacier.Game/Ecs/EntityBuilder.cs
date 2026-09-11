namespace Glacier.Game.Ecs;

using System.Runtime.CompilerServices;

/// <summary>
/// Fluent, zero-allocation builder for assembling entity components.
/// </summary>
public readonly ref struct EntityBuilder
{
    public readonly World World;
    public readonly Entity Entity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityBuilder(World world, Entity entity)
    {
        World = world;
        Entity = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityBuilder With<T>(in T component) where T : unmanaged
    {
        World.SetComponent(Entity, component);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Build() => Entity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Entity(EntityBuilder builder) => builder.Entity;
}

public static class WorldEntityExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EntityBuilder BuildEntity(this World world)
    {
        var entity = world.CreateEntity();
        return new EntityBuilder(world, entity);
    }
}
