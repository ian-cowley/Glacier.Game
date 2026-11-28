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

            // Tier 1: AVX-512 16-element parallel FMA (with 64-byte aligned load/store optimization)
            if (Avx512F.IsSupported && count >= 16)
            {
                var vDt = Vector512.Create(dt);
                bool isAligned = (((nuint)pPosX | (nuint)pPosY | (nuint)pVelX | (nuint)pVelY) & 63) == 0;
                if (isAligned)
                {
                    for (; i <= count - 16; i += 16)
                    {
                        var x = Vector512.LoadAligned(pPosX + i);
                        var y = Vector512.LoadAligned(pPosY + i);
                        var vx = Vector512.LoadAligned(pVelX + i);
                        var vy = Vector512.LoadAligned(pVelY + i);

                        Vector512.StoreAligned(Avx512F.FusedMultiplyAdd(vx, vDt, x), pPosX + i);
                        Vector512.StoreAligned(Avx512F.FusedMultiplyAdd(vy, vDt, y), pPosY + i);
                    }
                }
                else
                {
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
                bool isAligned = (((nuint)pPosF | (nuint)pVelF) & 63) == 0;
                if (isAligned)
                {
                    for (; i <= floatCount - 16; i += 16)
                    {
                        var p = Vector512.LoadAligned(pPosF + i);
                        var v = Vector512.LoadAligned(pVelF + i);
                        Vector512.StoreAligned(Avx512F.FusedMultiplyAdd(v, vDt, p), pPosF + i);
                    }
                }
                else
                {
                    for (; i <= floatCount - 16; i += 16)
                    {
                        var p = Vector512.Load(pPosF + i);
                        var v = Vector512.Load(pVelF + i);
                        Vector512.Store(Avx512F.FusedMultiplyAdd(v, vDt, p), pPosF + i);
                    }
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
                bool isAligned = ((nuint)pVelY & 63) == 0;
                if (isAligned)
                {
                    for (; i <= count - 16; i += 16)
                    {
                        var vy = Vector512.LoadAligned(pVelY + i);
                        Vector512.StoreAligned(Vector512.Add(vy, vDelta), pVelY + i);
                    }
                }
                else
                {
                    for (; i <= count - 16; i += 16)
                    {
                        var vy = Vector512.Load(pVelY + i);
                        Vector512.Store(Vector512.Add(vy, vDelta), pVelY + i);
                    }
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
    public static void ApplyGravityY(Span<Velocity2D> velocities, float gravityY, float dt)
    {
        int count = velocities.Length;
        if (count == 0) return;

        float delta = gravityY * dt;
        fixed (Velocity2D* pVel = velocities)
        {
            float* pF = (float*)pVel;
            int floatCount = count * 2;
            int i = 0;

            if (Avx512F.IsSupported && floatCount >= 16)
            {
                var vAdd = Vector512.Create(0f, delta, 0f, delta, 0f, delta, 0f, delta, 0f, delta, 0f, delta, 0f, delta, 0f, delta);
                for (; i <= floatCount - 16; i += 16)
                {
                    Vector512.Store(Vector512.Add(Vector512.Load(pF + i), vAdd), pF + i);
                }
            }
            else if (Avx2.IsSupported && floatCount >= 8)
            {
                var vAdd = Vector256.Create(0f, delta, 0f, delta, 0f, delta, 0f, delta);
                for (; i <= floatCount - 8; i += 8)
                {
                    Vector256.Store(Vector256.Add(Vector256.Load(pF + i), vAdd), pF + i);
                }
            }
            else if (AdvSimd.IsSupported && floatCount >= 4)
            {
                var vAdd = Vector128.Create(0f, delta, 0f, delta);
                for (; i <= floatCount - 4; i += 4)
                {
                    AdvSimd.Store(pF + i, Vector128.Add(AdvSimd.LoadVector128(pF + i), vAdd));
                }
            }

            for (; i < floatCount; i += 2)
            {
                pF[i + 1] += delta;
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
                bool isAligned = (((nuint)pVx | (nuint)pVy) & 63) == 0;
                if (isAligned)
                {
                    for (; i <= count - 16; i += 16)
                    {
                        Vector512.StoreAligned(Vector512.Multiply(Vector512.LoadAligned(pVx + i), vFac), pVx + i);
                        Vector512.StoreAligned(Vector512.Multiply(Vector512.LoadAligned(pVy + i), vFac), pVy + i);
                    }
                }
                else
                {
                    for (; i <= count - 16; i += 16)
                    {
                        Vector512.Store(Vector512.Multiply(Vector512.Load(pVx + i), vFac), pVx + i);
                        Vector512.Store(Vector512.Multiply(Vector512.Load(pVy + i), vFac), pVy + i);
                    }
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
    /// Vectorized across AVX-512, AVX2, and ARM64 AdvSimd.
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
            int i = 0;
            float negRest = -restitution;

            if (Avx512F.IsSupported && count >= 16)
            {
                var vMinX = Vector512.Create(minX);
                var vMaxX = Vector512.Create(maxX);
                var vMinY = Vector512.Create(minY);
                var vMaxY = Vector512.Create(maxY);
                var vNegRest = Vector512.Create(negRest);

                for (; i <= count - 16; i += 16)
                {
                    var x = Vector512.Load(pX + i);
                    var y = Vector512.Load(pY + i);
                    var vx = Vector512.Load(pVx + i);
                    var vy = Vector512.Load(pVy + i);

                    var hitX = Vector512.BitwiseOr(Vector512.LessThan(x, vMinX), Vector512.GreaterThan(x, vMaxX));
                    var hitY = Vector512.BitwiseOr(Vector512.LessThan(y, vMinY), Vector512.GreaterThan(y, vMaxY));

                    Vector512.Store(Vector512.Min(Vector512.Max(x, vMinX), vMaxX), pX + i);
                    Vector512.Store(Vector512.Min(Vector512.Max(y, vMinY), vMaxY), pY + i);
                    Vector512.Store(Vector512.ConditionalSelect(hitX, Vector512.Multiply(vx, vNegRest), vx), pVx + i);
                    Vector512.Store(Vector512.ConditionalSelect(hitY, Vector512.Multiply(vy, vNegRest), vy), pVy + i);
                }
            }
            else if (Avx2.IsSupported && count >= 8)
            {
                var vMinX = Vector256.Create(minX);
                var vMaxX = Vector256.Create(maxX);
                var vMinY = Vector256.Create(minY);
                var vMaxY = Vector256.Create(maxY);
                var vNegRest = Vector256.Create(negRest);

                for (; i <= count - 8; i += 8)
                {
                    var x = Vector256.Load(pX + i);
                    var y = Vector256.Load(pY + i);
                    var vx = Vector256.Load(pVx + i);
                    var vy = Vector256.Load(pVy + i);

                    var hitX = Vector256.BitwiseOr(Vector256.LessThan(x, vMinX), Vector256.GreaterThan(x, vMaxX));
                    var hitY = Vector256.BitwiseOr(Vector256.LessThan(y, vMinY), Vector256.GreaterThan(y, vMaxY));

                    Vector256.Store(Vector256.Min(Vector256.Max(x, vMinX), vMaxX), pX + i);
                    Vector256.Store(Vector256.Min(Vector256.Max(y, vMinY), vMaxY), pY + i);
                    Vector256.Store(Vector256.ConditionalSelect(hitX, Vector256.Multiply(vx, vNegRest), vx), pVx + i);
                    Vector256.Store(Vector256.ConditionalSelect(hitY, Vector256.Multiply(vy, vNegRest), vy), pVy + i);
                }
            }
            else if (AdvSimd.IsSupported && count >= 4)
            {
                var vMinX = Vector128.Create(minX);
                var vMaxX = Vector128.Create(maxX);
                var vMinY = Vector128.Create(minY);
                var vMaxY = Vector128.Create(maxY);
                var vNegRest = Vector128.Create(negRest);

                for (; i <= count - 4; i += 4)
                {
                    var x = AdvSimd.LoadVector128(pX + i);
                    var y = AdvSimd.LoadVector128(pY + i);
                    var vx = AdvSimd.LoadVector128(pVx + i);
                    var vy = AdvSimd.LoadVector128(pVy + i);

                    var hitX = Vector128.BitwiseOr(Vector128.LessThan(x, vMinX), Vector128.GreaterThan(x, vMaxX));
                    var hitY = Vector128.BitwiseOr(Vector128.LessThan(y, vMinY), Vector128.GreaterThan(y, vMaxY));

                    AdvSimd.Store(pX + i, Vector128.Min(Vector128.Max(x, vMinX), vMaxX));
                    AdvSimd.Store(pY + i, Vector128.Min(Vector128.Max(y, vMinY), vMaxY));
                    AdvSimd.Store(pVx + i, Vector128.ConditionalSelect(hitX, Vector128.Multiply(vx, vNegRest), vx));
                    AdvSimd.Store(pVy + i, Vector128.ConditionalSelect(hitY, Vector128.Multiply(vy, vNegRest), vy));
                }
            }

            for (; i < count; i++)
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
            float* pPF = (float*)pPos;
            float* pVF = (float*)pVel;
            int floatCount = count * 2;
            int i = 0;
            float negRest = -restitution;

            if (Avx512F.IsSupported && floatCount >= 16)
            {
                var vMin = Vector512.Create(minX, minY, minX, minY, minX, minY, minX, minY, minX, minY, minX, minY, minX, minY, minX, minY);
                var vMax = Vector512.Create(maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY);
                var vNegRest = Vector512.Create(negRest);

                for (; i <= floatCount - 16; i += 16)
                {
                    var p = Vector512.Load(pPF + i);
                    var v = Vector512.Load(pVF + i);
                    var hit = Vector512.BitwiseOr(Vector512.LessThan(p, vMin), Vector512.GreaterThan(p, vMax));
                    Vector512.Store(Vector512.Min(Vector512.Max(p, vMin), vMax), pPF + i);
                    Vector512.Store(Vector512.ConditionalSelect(hit, Vector512.Multiply(v, vNegRest), v), pVF + i);
                }
            }
            else if (Avx2.IsSupported && floatCount >= 8)
            {
                var vMin = Vector256.Create(minX, minY, minX, minY, minX, minY, minX, minY);
                var vMax = Vector256.Create(maxX, maxY, maxX, maxY, maxX, maxY, maxX, maxY);
                var vNegRest = Vector256.Create(negRest);

                for (; i <= floatCount - 8; i += 8)
                {
                    var p = Vector256.Load(pPF + i);
                    var v = Vector256.Load(pVF + i);
                    var hit = Vector256.BitwiseOr(Vector256.LessThan(p, vMin), Vector256.GreaterThan(p, vMax));
                    Vector256.Store(Vector256.Min(Vector256.Max(p, vMin), vMax), pPF + i);
                    Vector256.Store(Vector256.ConditionalSelect(hit, Vector256.Multiply(v, vNegRest), v), pVF + i);
                }
            }
            else if (AdvSimd.IsSupported && floatCount >= 4)
            {
                var vMin = Vector128.Create(minX, minY, minX, minY);
                var vMax = Vector128.Create(maxX, maxY, maxX, maxY);
                var vNegRest = Vector128.Create(negRest);

                for (; i <= floatCount - 4; i += 4)
                {
                    var p = AdvSimd.LoadVector128(pPF + i);
                    var v = AdvSimd.LoadVector128(pVF + i);
                    var hit = Vector128.BitwiseOr(Vector128.LessThan(p, vMin), Vector128.GreaterThan(p, vMax));
                    AdvSimd.Store(pPF + i, Vector128.Min(Vector128.Max(p, vMin), vMax));
                    AdvSimd.Store(pVF + i, Vector128.ConditionalSelect(hit, Vector128.Multiply(v, vNegRest), v));
                }
            }

            for (; i < floatCount; i += 2)
            {
                if (pPF[i] < minX)
                {
                    pPF[i] = minX;
                    pVF[i] = -pVF[i] * restitution;
                }
                else if (pPF[i] > maxX)
                {
                    pPF[i] = maxX;
                    pVF[i] = -pVF[i] * restitution;
                }

                if (pPF[i + 1] < minY)
                {
                    pPF[i + 1] = minY;
                    pVF[i + 1] = -pVF[i + 1] * restitution;
                }
                else if (pPF[i + 1] > maxY)
                {
                    pPF[i + 1] = maxY;
                    pVF[i + 1] = -pVF[i + 1] * restitution;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ApplyDamping(Span<Velocity2D> velocities, float drag, float dt)
    {
        int count = velocities.Length;
        if (count == 0) return;

        float factor = Math.Clamp(1.0f - (drag * dt), 0.0f, 1.0f);
        fixed (Velocity2D* pVel = velocities)
        {
            float* pF = (float*)pVel;
            int floatCount = count * 2;
            int i = 0;

            if (Avx512F.IsSupported && floatCount >= 16)
            {
                var vFac = Vector512.Create(factor);
                for (; i <= floatCount - 16; i += 16)
                    Vector512.Store(Vector512.Multiply(Vector512.Load(pF + i), vFac), pF + i);
            }
            else if (Avx2.IsSupported && floatCount >= 8)
            {
                var vFac = Vector256.Create(factor);
                for (; i <= floatCount - 8; i += 8)
                    Vector256.Store(Vector256.Multiply(Vector256.Load(pF + i), vFac), pF + i);
            }
            else if (AdvSimd.IsSupported && floatCount >= 4)
            {
                var vFac = Vector128.Create(factor);
                for (; i <= floatCount - 4; i += 4)
                    AdvSimd.Store(pF + i, Vector128.Multiply(AdvSimd.LoadVector128(pF + i), vFac));
            }

            for (; i < floatCount; i++)
            {
                pF[i] *= factor;
            }
        }
    }
}
