namespace Glacier.Game.Physics;

using System.Runtime.CompilerServices;
using Glacier.Game.Ecs;

/// <summary>
/// High-performance ECS physics system executing AVX-512 / AVX2 Euler integration
/// and boundary reflection over contiguous SoA entity buffers.
/// </summary>
public sealed class SimdPhysicsSystem : ISystem
{
    public float BoundsMinX { get; set; } = 0f;
    public float BoundsMaxX { get; set; } = 1920f;
    public float BoundsMinY { get; set; } = 0f;
    public float BoundsMaxY { get; set; } = 1080f;

    public float Restitution { get; set; } = 0.95f;
    public float GravityY { get; set; } = 0f;
    public float Drag { get; set; } = 0.001f;

    public SimdPhysicsSystem() { }

    public SimdPhysicsSystem(float minX, float maxX, float minY, float maxY, float restitution = 0.95f)
    {
        BoundsMinX = minX;
        BoundsMaxX = maxX;
        BoundsMinY = minY;
        BoundsMaxY = maxY;
        Restitution = restitution;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Update(World world, float deltaTime)
    {
        var query = world.Query<Position2D, Velocity2D>();
        if (query.Count == 0) return;

        // 1. SIMD Euler Integration
        PhysicsKernels.IntegrateEuler(query.Component1Span, query.Component2Span, deltaTime);

        // 2. SIMD Boundary Clamping & Bounce Reflection
        PhysicsKernels.ClampAndBounce(query.Component1Span, query.Component2Span, BoundsMinX, BoundsMaxX, BoundsMinY, BoundsMaxY, Restitution);
    }
}
