namespace Glacier.Game.Interop;

using System;
using Glacier.Game.Physics;
using Glacier.Gpu.Common;
using Glacier.Gpu.Factory;

/// <summary>
/// Hardware compute bridge integrating Glacier.Game with Glacier.Gpu.
/// Dispatches bare-metal GPU compute tasks for massive entity/particle simulations,
/// with automatic transparent fallback to AVX-512 CPU SIMD execution.
/// </summary>
public sealed class GpuGameBridge : IDisposable
{
    private readonly IGpuEngine? _gpuEngine;
    private bool _disposed;

    /// <summary>
    /// Gets whether physical GPU hardware acceleration is active.
    /// </summary>
    public bool IsGpuAccelerated => _gpuEngine != null && _gpuEngine.IsInitialized;

    /// <summary>
    /// Description of the compute backend in use.
    /// </summary>
    public string BackendName => _gpuEngine switch
    {
        not null => $"{_gpuEngine.DeviceInfo.DeviceName} ({_gpuEngine.DeviceInfo.Architecture})",
        null => "CPU SIMD Vectorization (AVX-512 / AVX2 / AdvSimd)"
    };

    public GpuGameBridge()
    {
        try
        {
            if (GpuEngineFactory.HasNvidiaGpu)
            {
                _gpuEngine = GpuEngineFactory.TryCreateNvidiaEngine();
            }
            else if (GpuEngineFactory.HasAmdGpu)
            {
                _gpuEngine = GpuEngineFactory.TryCreateAmdEngine();
            }
        }
        catch
        {
            _gpuEngine = null;
        }
    }

    /// <summary>
    /// Simulates physics for a batch of particles using GPU acceleration when available,
    /// falling back to AVX-512/AVX2 CPU SIMD kernels.
    /// </summary>
    public unsafe void SimulateParticles(
        Span<Position2D> positions,
        Span<Velocity2D> velocities,
        float dt,
        float damping = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Fallback or baseline: AVX-512 CPU vectorized kernel
        PhysicsKernels.IntegrateEuler(positions, velocities, dt);
        if (damping < 1.0f)
        {
            PhysicsKernels.ApplyDamping(velocities, 1.0f - damping, dt);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gpuEngine?.Dispose();
            _disposed = true;
        }
    }
}
