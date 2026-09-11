namespace Glacier.Game.Interop;

using System;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Tensor.Core;

/// <summary>
/// High-performance interoperability extensions between Glacier.Game ECS and Glacier.Tensor.
/// Delivers zero-allocation batch tensor observation extraction and policy action application
/// for reinforcement learning (RL) and deep learning agents.
/// </summary>
public static unsafe class TensorExtensions
{
    /// <summary>
    /// Extracts entity positions and velocities into a 2D float tensor of shape [N, 4] (X, Y, Vx, Vy).
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entities">Span of entities to observe.</param>
    /// <returns>A new Tensor&lt;float&gt; with shape [N, 4].</returns>
    public static Tensor<float> ToObservationTensor(this World world, ReadOnlySpan<Entity> entities)
    {
        int count = entities.Length;
        var tensor = new Tensor<float>(count, 4);
        float* ptr = tensor.DataPointer;

        for (int i = 0; i < count; i++)
        {
            Entity e = entities[i];
            float x = 0f, y = 0f, vx = 0f, vy = 0f;

            if (world.HasComponent<Position2D>(e))
            {
                ref readonly Position2D pos = ref world.GetComponent<Position2D>(e);
                x = pos.X;
                y = pos.Y;
            }

            if (world.HasComponent<Velocity2D>(e))
            {
                ref readonly Velocity2D vel = ref world.GetComponent<Velocity2D>(e);
                vx = vel.X;
                vy = vel.Y;
            }

            int rowOffset = i * 4;
            ptr[rowOffset + 0] = x;
            ptr[rowOffset + 1] = y;
            ptr[rowOffset + 2] = vx;
            ptr[rowOffset + 3] = vy;
        }

        return tensor;
    }

    /// <summary>
    /// Applies action velocities from a 2D tensor of shape [N, 2] directly to entity Velocity2D components.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entities">Span of entities to control.</param>
    /// <param name="actions">Action tensor of shape [N, 2] containing target [Vx, Vy].</param>
    public static void ApplyActionTensor(this World world, ReadOnlySpan<Entity> entities, Tensor<float> actions)
    {
        int count = entities.Length;
        if (actions.Shape[0] < count || actions.Shape[1] < 2)
        {
            throw new ArgumentException($"Action tensor shape [{actions.Shape[0]}, {actions.Shape[1]}] is insufficient for {count} entities with 2 action channels.");
        }

        float* ptr = actions.DataPointer;

        for (int i = 0; i < count; i++)
        {
            Entity e = entities[i];
            int rowOffset = i * 2;
            float vx = ptr[rowOffset + 0];
            float vy = ptr[rowOffset + 1];

            if (world.HasComponent<Velocity2D>(e))
            {
                ref Velocity2D vel = ref world.GetComponent<Velocity2D>(e);
                vel.X = vx;
                vel.Y = vy;
            }
            else
            {
                world.AddComponent(e, new Velocity2D(vx, vy));
            }
        }
    }
}
