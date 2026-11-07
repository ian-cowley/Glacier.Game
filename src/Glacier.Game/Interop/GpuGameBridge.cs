namespace Glacier.Game.Interop;

using System;
using Glacier.Game.Physics;

/// <summary>
/// Compute bridge executing SIMD-vectorized particle integration (AVX-512 / AVX2 / AdvSimd).
/// Reports honest hardware status until dedicated GPU compute shaders are bound.
/// </summary>
public sealed class GpuGameBridge : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets whether dedicated GPU kernel execution is active (<c>false</c>; executes on CPU SIMD).
    /// </summary>
    public bool IsGpuAccelerated => false;

    /// <summary>
    /// Description of the active compute backend.
    /// </summary>
    public string BackendName => "CPU SIMD Vectorization (AVX-512 / AVX2 / AdvSimd)";

    /// <summary>
    /// Simulates physics for a batch of particles using AVX-512 / AVX2 / AdvSimd CPU SIMD kernels.
    /// </summary>
    public void SimulateParticles(
        Span<Position2D> positions,
        Span<Velocity2D> velocities,
        float dt,
        float damping = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (positions.Length != velocities.Length)
        {
            throw new ArgumentException("positions and velocities must have matching lengths.", nameof(velocities));
        }

        PhysicsKernels.IntegrateEuler(positions, velocities, dt);
        if (damping < 1.0f)
        {
            PhysicsKernels.ApplyDamping(velocities, 1.0f - damping, dt);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
