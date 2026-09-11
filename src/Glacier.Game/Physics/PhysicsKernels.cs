namespace Glacier.Game.Physics;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Hardware-accelerated SIMD physics kernels for Glacier.Game.
/// Supports AVX-512 FMA, AVX2, and ARM64 AdvSimd with zero-allocation tail loops.
/// </summary>
public static unsafe class PhysicsKernels
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void IntegrateEuler(
        Span<float> posX, Span<float> posY,
        ReadOnlySpan<float> velX, ReadOnlySpan<float> velY,
        float dt)
    {
        int count = posX.Length;
        if (count == 0) return;

        fixed (float* pPosX = posX, pPosY = posY)
        fixed (float* pVelX = velX, pVelY = velY)
        {
            int i = 0;

            // Tier 1: AVX-512 16-element parallel FMA
            if (Avx512F.IsSupported && count >= 16)
            {
                var vDt = Vector512.Create(dt);
                for (; i <= count - 16; i += 16)
                {
                    var x = Vector512.Load(pPosX + i);
                    var y = Vector512.Load(pPosY + i);
                    var vx = Vector512.Load(pVelX + i);
                    var vy = Vector512.Load(pVelY + i);

                    Vector512.Store(Avx512F.FusedMultiplyAdd(vx, vDt, x), pPosX + i);
                    Vector512.Store(Avx512F.FusedMultiplyAdd(vy, vDt, y), pPosY + i);
                }
            }
            // Tier 2: AVX2 8-element parallel FMA/Multiply-Add
            else if (Avx2.IsSupported && count >= 8)
            {
                var vDt256 = Vector256.Create(dt);
                for (; i <= count - 8; i += 8)
                {
                    var x = Vector256.Load(pPosX + i);
                    var y = Vector256.Load(pPosY + i);
                    var vx = Vector256.Load(pVelX + i);
                    var vy = Vector256.Load(pVelY + i);

                    if (Fma.IsSupported)
                    {
                        Vector256.Store(Fma.MultiplyAdd(vx, vDt256, x), pPosX + i);
                        Vector256.Store(Fma.MultiplyAdd(vy, vDt256, y), pPosY + i);
                    }
                    else
                    {
                        Vector256.Store(Vector256.Add(x, Vector256.Multiply(vx, vDt256)), pPosX + i);
                        Vector256.Store(Vector256.Add(y, Vector256.Multiply(vy, vDt256)), pPosY + i);
                    }
                }
            }
            // Tier 3: ARM64 AdvSimd 4-element parallel FMA
            else if (AdvSimd.IsSupported && count >= 4)
            {
                var vDt128 = Vector128.Create(dt);
                for (; i <= count - 4; i += 4)
                {
                    var x = AdvSimd.LoadVector128(pPosX + i);
                    var y = AdvSimd.LoadVector128(pPosY + i);
                    var vx = AdvSimd.LoadVector128(pVelX + i);
                    var vy = AdvSimd.LoadVector128(pVelY + i);

                    AdvSimd.Store(pPosX + i, AdvSimd.FusedMultiplyAdd(x, vx, vDt128));
                    AdvSimd.Store(pPosY + i, AdvSimd.FusedMultiplyAdd(y, vy, vDt128));
                }
            }

            // Remainder tail loop
            for (; i < count; i++)
            {
                pPosX[i] += pVelX[i] * dt;
                pPosY[i] += pVelY[i] * dt;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void IntegrateEuler(
        Span<Position2D> positions,
        ReadOnlySpan<Velocity2D> velocities,
        float dt)
    {
        int count = positions.Length;
        if (count == 0) return;

        fixed (Position2D* pPos = positions)
        fixed (Velocity2D* pVel = velocities)
        {
            float* pPosF = (float*)pPos;
            float* pVelF = (float*)pVel;
            int floatCount = count * 2;
            int i = 0;

            if (Avx512F.IsSupported && floatCount >= 16)
            {
                var vDt = Vector512.Create(dt);
                for (; i <= floatCount - 16; i += 16)
                {
                    var p = Vector512.Load(pPosF + i);
                    var v = Vector512.Load(pVelF + i);
                    Vector512.Store(Avx512F.FusedMultiplyAdd(v, vDt, p), pPosF + i);
                }
            }
            else if (Avx2.IsSupported && floatCount >= 8)
            {
                var vDt256 = Vector256.Create(dt);
                for (; i <= floatCount - 8; i += 8)
                {
                    var p = Vector256.Load(pPosF + i);
                    var v = Vector256.Load(pVelF + i);
                    if (Fma.IsSupported)
                    {
                        Vector256.Store(Fma.MultiplyAdd(v, vDt256, p), pPosF + i);
                    }
                    else
                    {
                        Vector256.Store(Vector256.Add(p, Vector256.Multiply(v, vDt256)), pPosF + i);
                    }
                }
            }

            for (; i < floatCount; i += 2)
            {
                pPosF[i] += pVelF[i] * dt;
                pPosF[i + 1] += pVelF[i + 1] * dt;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ApplyAcceleration(
        Span<float> velX, Span<float> velY,
        ReadOnlySpan<float> accX, ReadOnlySpan<float> accY,
        float dt)
    {
        IntegrateEuler(velX, velY, accX, accY, dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ApplyGravity(Span<float> velY, float gravityY, float dt)
    {
        int count = velY.Length;
        if (count == 0) return;

        float delta = gravityY * dt;
        fixed (float* pVelY = velY)
        {
            int i = 0;
            if (Avx512F.IsSupported && count >= 16)
            {
                var vDelta = Vector512.Create(delta);
                for (; i <= count - 16; i += 16)
                {
                    var vy = Vector512.Load(pVelY + i);
                    Vector512.Store(Vector512.Add(vy, vDelta), pVelY + i);
                }
            }
            else if (Avx2.IsSupported && count >= 8)
            {
                var vDelta = Vector256.Create(delta);
                for (; i <= count - 8; i += 8)
                {
                    var vy = Vector256.Load(pVelY + i);
                    Vector256.Store(Vector256.Add(vy, vDelta), pVelY + i);
                }
            }

            for (; i < count; i++)
            {
                pVelY[i] += delta;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ApplyDamping(Span<float> velX, Span<float> velY, float drag, float dt)
    {
        int count = velX.Length;
        if (count == 0) return;

        float factor = Math.Clamp(1.0f - (drag * dt), 0.0f, 1.0f);
        fixed (float* pVx = velX, pVy = velY)
        {
            int i = 0;
            if (Avx512F.IsSupported && count >= 16)
            {
                var vFac = Vector512.Create(factor);
                for (; i <= count - 16; i += 16)
                {
                    Vector512.Store(Vector512.Multiply(Vector512.Load(pVx + i), vFac), pVx + i);
                    Vector512.Store(Vector512.Multiply(Vector512.Load(pVy + i), vFac), pVy + i);
                }
            }
            else if (Avx2.IsSupported && count >= 8)
            {
                var vFac = Vector256.Create(factor);
                for (; i <= count - 8; i += 8)
                {
                    Vector256.Store(Vector256.Multiply(Vector256.Load(pVx + i), vFac), pVx + i);
                    Vector256.Store(Vector256.Multiply(Vector256.Load(pVy + i), vFac), pVy + i);
                }
            }

            for (; i < count; i++)
            {
                pVx[i] *= factor;
                pVy[i] *= factor;
            }
        }
    }

    /// <summary>
    /// Clamps entity positions within [minX, maxX, minY, maxY] and reflects velocities on impact.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ClampAndBounce(
        Span<float> posX, Span<float> posY,
        Span<float> velX, Span<float> velY,
        float minX, float maxX,
        float minY, float maxY,
        float restitution = 0.9f)
    {
        int count = posX.Length;
        fixed (float* pX = posX, pY = posY, pVx = velX, pVy = velY)
        {
            for (int i = 0; i < count; i++)
            {
                if (pX[i] < minX)
                {
                    pX[i] = minX;
                    pVx[i] = -pVx[i] * restitution;
                }
                else if (pX[i] > maxX)
                {
                    pX[i] = maxX;
                    pVx[i] = -pVx[i] * restitution;
                }

                if (pY[i] < minY)
                {
                    pY[i] = minY;
                    pVy[i] = -pVy[i] * restitution;
                }
                else if (pY[i] > maxY)
                {
                    pY[i] = maxY;
                    pVy[i] = -pVy[i] * restitution;
                }
            }
        }
    }

    /// <summary>
    /// Clamps Position2D and reflects Velocity2D within [minX, maxX, minY, maxY].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ClampAndBounce(
        Span<Position2D> positions,
        Span<Velocity2D> velocities,
        float minX, float maxX,
        float minY, float maxY,
        float restitution = 0.9f)
    {
        int count = positions.Length;
        fixed (Position2D* pPos = positions)
        fixed (Velocity2D* pVel = velocities)
        {
            for (int i = 0; i < count; i++)
            {
                if (pPos[i].X < minX)
                {
                    pPos[i].X = minX;
                    pVel[i].X = -pVel[i].X * restitution;
                }
                else if (pPos[i].X > maxX)
                {
                    pPos[i].X = maxX;
                    pVel[i].X = -pVel[i].X * restitution;
                }

                if (pPos[i].Y < minY)
                {
                    pPos[i].Y = minY;
                    pVel[i].Y = -pVel[i].Y * restitution;
                }
                else if (pPos[i].Y > maxY)
                {
                    pPos[i].Y = maxY;
                    pVel[i].Y = -pVel[i].Y * restitution;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ApplyDamping(Span<Velocity2D> velocities, float drag, float dt)
    {
        int count = velocities.Length;
        float factor = Math.Clamp(1.0f - (drag * dt), 0.0f, 1.0f);
        fixed (Velocity2D* pVel = velocities)
        {
            for (int i = 0; i < count; i++)
            {
                pVel[i].X *= factor;
                pVel[i].Y *= factor;
            }
        }
    }
}
