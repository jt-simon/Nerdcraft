// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.World.Generation;

/// <summary>Stateless, seeded 2D simplex noise. Pure and thread-safe.</summary>
internal static class SimplexNoise
{
    private const float Skew = 0.36602540378f; // (sqrt(3) - 1) / 2
    private const float Unskew = 0.21132486540f; // (3 - sqrt(3)) / 6

    // Eight unit-ish gradient directions.
    private static readonly (float X, float Z)[] Gradients =
    [
        (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
        (0.70710678f, 0.70710678f), (-0.70710678f, 0.70710678f),
        (0.70710678f, -0.70710678f), (-0.70710678f, -0.70710678f),
    ];

    /// <summary>
    /// Noise at a point. The same seed and point always give the same value. Because the gradients have unit
    /// length, the output stays within about [-0.71, 0.71], not the full [-1, 1].
    /// </summary>
    public static float Sample(int seed, float x, float z)
    {
        float s = (x + z) * Skew;
        int i = (int)MathF.Floor(x + s);
        int j = (int)MathF.Floor(z + s);

        float t = (i + j) * Unskew;
        float x0 = x - (i - t);
        float z0 = z - (j - t);

        int i1 = x0 > z0 ? 1 : 0;
        int j1 = 1 - i1;

        float x1 = x0 - i1 + Unskew;
        float z1 = z0 - j1 + Unskew;
        float x2 = x0 - 1f + (2f * Unskew);
        float z2 = z0 - 1f + (2f * Unskew);

        float n = Corner(seed, i, j, x0, z0)
            + Corner(seed, i + i1, j + j1, x1, z1)
            + Corner(seed, i + 1, j + 1, x2, z2);

        // 70 is the usual scale for gradients of length sqrt(2). With unit gradients the result peaks near 0.71,
        // which the terrain tuning relies on; the clamp is only a safety net.
        return Math.Clamp(70f * n, -1f, 1f);
    }

    /// <summary>Fractal noise: <paramref name="octaves"/> layers with doubling frequency and halving amplitude, normalised by the total amplitude so the result has the same range as <see cref="Sample"/>.</summary>
    public static float Fbm(int seed, float x, float z, int octaves)
    {
        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;
        float frequency = 1f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amplitude * Sample(seed + (o * 1013), x * frequency, z * frequency);
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum / total;
    }

    private static float Corner(int seed, int i, int j, float dx, float dz)
    {
        float t = 0.5f - (dx * dx) - (dz * dz);
        if (t <= 0f)
        {
            return 0f;
        }

        (float gx, float gz) = Gradients[Hash(seed, i, j) & 7];
        t *= t;
        return t * t * ((gx * dx) + (gz * dz));
    }

    private static int Hash(int seed, int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)z * 0x85EBCA77u;
            h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            h *= 0x27D4EB2Fu;
            h ^= h >> 15;
            return (int)(h >> 8);
        }
    }
}
