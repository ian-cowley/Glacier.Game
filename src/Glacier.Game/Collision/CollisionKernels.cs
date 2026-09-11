namespace Glacier.Game.Collision;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Glacier.Game.Physics;

/// <summary>
/// Hardware-accelerated SIMD collision detection kernels for Glacier.Game.
/// Features 16-entity parallel AABB evaluation via AVX-512, AVX2, and ARM64 AdvSimd.
/// </summary>
public static unsafe class CollisionKernels
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void CheckCollisionsAvx512(
        ReadOnlySpan<float> minX1, ReadOnlySpan<float> maxX1,
        ReadOnlySpan<float> minY1, ReadOnlySpan<float> maxY1,
        float targetMinX, float targetMaxX,
        float targetMinY, float targetMaxY,
        Span<ushort> collisionBitmask)
    {
        int count = minX1.Length;
        int wordCount = (count + 15) / 16;
        if (collisionBitmask.Length < wordCount)
            throw new ArgumentException($"Collision bitmask span must have length at least {wordCount}", nameof(collisionBitmask));

        collisionBitmask[..wordCount].Clear();

        fixed (float* pMinX = minX1, pMaxX = maxX1)
        fixed (float* pMinY = minY1, pMaxY = maxY1)
        fixed (ushort* pOut = collisionBitmask)
        {
            int i = 0;
            int maskIdx = 0;

            // Tier 1: AVX-512 16-entity parallel collision detection
            if (Avx512F.IsSupported && count >= 16)
            {
                var vTgtMinX = Vector512.Create(targetMinX);
                var vTgtMaxX = Vector512.Create(targetMaxX);
                var vTgtMinY = Vector512.Create(targetMinY);
                var vTgtMaxY = Vector512.Create(targetMaxY);

                for (; i <= count - 16; i += 16)
                {
                    var c1 = Vector512.LessThanOrEqual(Vector512.Load(pMinX + i), vTgtMaxX);
                    var c2 = Vector512.GreaterThanOrEqual(Vector512.Load(pMaxX + i), vTgtMinX);
                    var c3 = Vector512.LessThanOrEqual(Vector512.Load(pMinY + i), vTgtMaxY);
                    var c4 = Vector512.GreaterThanOrEqual(Vector512.Load(pMaxY + i), vTgtMinY);

                    var overlapX = Vector512.BitwiseAnd(c1, c2);
                    var overlapY = Vector512.BitwiseAnd(c3, c4);
                    var hit = Vector512.BitwiseAnd(overlapX, overlapY);

                    pOut[maskIdx++] = (ushort)hit.ExtractMostSignificantBits();
                }
            }
            // Tier 2: AVX2 16-entity parallel collision detection (two 8-wide Vector256 chunks)
            else if (Avx2.IsSupported && count >= 16)
            {
                var vTgtMinX = Vector256.Create(targetMinX);
                var vTgtMaxX = Vector256.Create(targetMaxX);
                var vTgtMinY = Vector256.Create(targetMinY);
                var vTgtMaxY = Vector256.Create(targetMaxY);

                for (; i <= count - 16; i += 16)
                {
                    // Chunk 0: entities i+0..i+7
                    var c1_0 = Vector256.LessThanOrEqual(Vector256.Load(pMinX + i), vTgtMaxX);
                    var c2_0 = Vector256.GreaterThanOrEqual(Vector256.Load(pMaxX + i), vTgtMinX);
                    var c3_0 = Vector256.LessThanOrEqual(Vector256.Load(pMinY + i), vTgtMaxY);
                    var c4_0 = Vector256.GreaterThanOrEqual(Vector256.Load(pMaxY + i), vTgtMinY);
                    var hit0 = Vector256.BitwiseAnd(Vector256.BitwiseAnd(c1_0, c2_0), Vector256.BitwiseAnd(c3_0, c4_0));
                    uint m0 = (uint)Vector256.ExtractMostSignificantBits(hit0);

                    // Chunk 1: entities i+8..i+15
                    var c1_1 = Vector256.LessThanOrEqual(Vector256.Load(pMinX + i + 8), vTgtMaxX);
                    var c2_1 = Vector256.GreaterThanOrEqual(Vector256.Load(pMaxX + i + 8), vTgtMinX);
                    var c3_1 = Vector256.LessThanOrEqual(Vector256.Load(pMinY + i + 8), vTgtMaxY);
                    var c4_1 = Vector256.GreaterThanOrEqual(Vector256.Load(pMaxY + i + 8), vTgtMinY);
                    var hit1 = Vector256.BitwiseAnd(Vector256.BitwiseAnd(c1_1, c2_1), Vector256.BitwiseAnd(c3_1, c4_1));
                    uint m1 = (uint)Vector256.ExtractMostSignificantBits(hit1);

                    pOut[maskIdx++] = (ushort)(m0 | (m1 << 8));
                }
            }
            // Tier 3: ARM64 AdvSimd 16-entity parallel collision detection (four 4-wide Vector128 chunks)
            else if (AdvSimd.IsSupported && count >= 16)
            {
                var vTgtMinX = Vector128.Create(targetMinX);
                var vTgtMaxX = Vector128.Create(targetMaxX);
                var vTgtMinY = Vector128.Create(targetMinY);
                var vTgtMaxY = Vector128.Create(targetMaxY);

                for (; i <= count - 16; i += 16)
                {
                    uint combinedMask = 0;
                    for (int c = 0; c < 4; c++)
                    {
                        int offset = i + (c * 4);
                        var c1 = Vector128.LessThanOrEqual(Vector128.Load(pMinX + offset), vTgtMaxX);
                        var c2 = Vector128.GreaterThanOrEqual(Vector128.Load(pMaxX + offset), vTgtMinX);
                        var c3 = Vector128.LessThanOrEqual(Vector128.Load(pMinY + offset), vTgtMaxY);
                        var c4 = Vector128.GreaterThanOrEqual(Vector128.Load(pMaxY + offset), vTgtMinY);
                        var hit = Vector128.BitwiseAnd(Vector128.BitwiseAnd(c1, c2), Vector128.BitwiseAnd(c3, c4));
                        uint bits = (uint)Vector128.ExtractMostSignificantBits(hit);
                        combinedMask |= (bits << (c * 4));
                    }
                    pOut[maskIdx++] = (ushort)combinedMask;
                }
            }

            // Remainder tail loop: ADVANCE maskIdx every 16 entities to prevent data loss!
            ushort tailMask = 0;
            int tailBit = 0;
            for (; i < count; i++)
            {
                bool hit = (pMinX[i] <= targetMaxX && pMaxX[i] >= targetMinX) &&
                           (pMinY[i] <= targetMaxY && pMaxY[i] >= targetMinY);
                if (hit)
                {
                    tailMask |= (ushort)(1 << tailBit);
                }
                tailBit++;

                if (tailBit == 16)
                {
                    if (maskIdx < collisionBitmask.Length)
                    {
                        pOut[maskIdx++] = tailMask;
                    }
                    tailMask = 0;
                    tailBit = 0;
                }
            }
            if (tailBit > 0 && maskIdx < collisionBitmask.Length)
            {
                pOut[maskIdx++] = tailMask;
            }
        }
    }

    /// <summary>
    /// Checks collisions for an AoS array of AABB2D against a target bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void CheckCollisions(
        ReadOnlySpan<AABB2D> boxes,
        in AABB2D target,
        Span<ushort> collisionBitmask)
    {
        int count = boxes.Length;
        int wordCount = (count + 15) / 16;
        if (collisionBitmask.Length < wordCount)
            throw new ArgumentException($"Collision bitmask span must have length at least {wordCount}", nameof(collisionBitmask));

        collisionBitmask[..wordCount].Clear();

        fixed (AABB2D* pBoxes = boxes)
        fixed (ushort* pOut = collisionBitmask)
        {
            ushort word = 0;
            int bit = 0;
            int outIdx = 0;

            for (int i = 0; i < count; i++)
            {
                ref readonly AABB2D b = ref pBoxes[i];
                bool hit = (b.MinX <= target.MaxX && b.MaxX >= target.MinX) &&
                           (b.MinY <= target.MaxY && b.MaxY >= target.MinY);
                if (hit)
                {
                    word |= (ushort)(1 << bit);
                }
                bit++;

                if (bit == 16)
                {
                    pOut[outIdx++] = word;
                    word = 0;
                    bit = 0;
                }
            }

            if (bit > 0 && outIdx < collisionBitmask.Length)
            {
                pOut[outIdx++] = word;
            }
        }
    }

    /// <summary>
    /// Pure scalar reference implementation used for parity testing.
    /// </summary>
    public static void ScalarCheckCollisions(
        ReadOnlySpan<float> minX1, ReadOnlySpan<float> maxX1,
        ReadOnlySpan<float> minY1, ReadOnlySpan<float> maxY1,
        float targetMinX, float targetMaxX,
        float targetMinY, float targetMaxY,
        Span<ushort> collisionBitmask)
    {
        int count = minX1.Length;
        int wordCount = (count + 15) / 16;
        collisionBitmask[..wordCount].Clear();

        ushort word = 0;
        int bit = 0;
        int outIdx = 0;

        for (int i = 0; i < count; i++)
        {
            bool hit = (minX1[i] <= targetMaxX && maxX1[i] >= targetMinX) &&
                       (minY1[i] <= targetMaxY && maxY1[i] >= targetMinY);
            if (hit)
            {
                word |= (ushort)(1 << bit);
            }
            bit++;

            if (bit == 16)
            {
                collisionBitmask[outIdx++] = word;
                word = 0;
                bit = 0;
            }
        }

        if (bit > 0 && outIdx < collisionBitmask.Length)
        {
            collisionBitmask[outIdx++] = word;
        }
    }

    /// <summary>
    /// Vectorized circle-to-circle collision test evaluating distance squared vs (r1 + r2)^2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void CheckCircleCollisions(
        ReadOnlySpan<float> posX, ReadOnlySpan<float> posY, ReadOnlySpan<float> radii,
        float targetX, float targetY, float targetRadius,
        Span<ushort> collisionBitmask)
    {
        int count = posX.Length;
        int wordCount = (count + 15) / 16;
        collisionBitmask[..wordCount].Clear();

        fixed (float* pX = posX, pY = posY, pR = radii)
        fixed (ushort* pOut = collisionBitmask)
        {
            int i = 0;
            int maskIdx = 0;

            if (Avx2.IsSupported && count >= 8)
            {
                var vTgtX = Vector256.Create(targetX);
                var vTgtY = Vector256.Create(targetY);
                var vTgtR = Vector256.Create(targetRadius);

                for (; i <= count - 16; i += 16)
                {
                    // Chunk 0
                    var dx0 = Vector256.Subtract(Vector256.Load(pX + i), vTgtX);
                    var dy0 = Vector256.Subtract(Vector256.Load(pY + i), vTgtY);
                    var distSq0 = Vector256.Add(Vector256.Multiply(dx0, dx0), Vector256.Multiply(dy0, dy0));
                    var rSum0 = Vector256.Add(Vector256.Load(pR + i), vTgtR);
                    var hit0 = Vector256.LessThanOrEqual(distSq0, Vector256.Multiply(rSum0, rSum0));
                    uint m0 = (uint)Vector256.ExtractMostSignificantBits(hit0);

                    // Chunk 1
                    var dx1 = Vector256.Subtract(Vector256.Load(pX + i + 8), vTgtX);
                    var dy1 = Vector256.Subtract(Vector256.Load(pY + i + 8), vTgtY);
                    var distSq1 = Vector256.Add(Vector256.Multiply(dx1, dx1), Vector256.Multiply(dy1, dy1));
                    var rSum1 = Vector256.Add(Vector256.Load(pR + i + 8), vTgtR);
                    var hit1 = Vector256.LessThanOrEqual(distSq1, Vector256.Multiply(rSum1, rSum1));
                    uint m1 = (uint)Vector256.ExtractMostSignificantBits(hit1);

                    pOut[maskIdx++] = (ushort)(m0 | (m1 << 8));
                }
            }

            ushort tailWord = 0;
            int bit = 0;
            for (; i < count; i++)
            {
                float dx = pX[i] - targetX;
                float dy = pY[i] - targetY;
                float distSq = (dx * dx) + (dy * dy);
                float rSum = pR[i] + targetRadius;
                if (distSq <= (rSum * rSum))
                {
                    tailWord |= (ushort)(1 << bit);
                }
                bit++;

                if (bit == 16)
                {
                    if (maskIdx < collisionBitmask.Length)
                    {
                        pOut[maskIdx++] = tailWord;
                    }
                    tailWord = 0;
                    bit = 0;
                }
            }

            if (bit > 0 && maskIdx < collisionBitmask.Length)
            {
                pOut[maskIdx++] = tailWord;
            }
        }
    }
}
