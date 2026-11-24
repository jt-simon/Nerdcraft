// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>
/// Packs a chunk position into the 64-bit key of the <c>chunks</c> table: dimension (8 bits), X (22), Z (22), Y (12), most
/// significant first, each coordinate offset to be non-negative. Rows sort by dimension, X, Z, then Y, so one column of chunks is
/// a single contiguous key range. The sign bit is flipped so SQLite's signed ordering matches the unsigned bit pattern.
/// </summary>
internal static class ChunkKey
{
    /// <summary>X and Z chunk coordinates are accepted from <c>-HorizontalBias</c> up to <c>HorizontalBias - 1</c>.</summary>
    public const int HorizontalBias = 1 << 21;

    /// <summary>Y chunk coordinates are accepted from <c>-VerticalBias</c> up to <c>VerticalBias - 1</c>.</summary>
    public const int VerticalBias = 1 << 11;

    private const ulong SignBit = 1UL << 63;

    /// <summary>Returns the key of the chunk at the given position in <paramref name="dimension"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is outside the range the key can hold.</exception>
    public static long Pack(byte dimension, int chunkX, int chunkY, int chunkZ)
    {
        ulong x = Offset(chunkX, HorizontalBias, nameof(chunkX));
        ulong y = Offset(chunkY, VerticalBias, nameof(chunkY));
        ulong z = Offset(chunkZ, HorizontalBias, nameof(chunkZ));
        return unchecked((long)(((ulong)dimension << 56) | (x << 34) | (z << 12) | y) ^ (long)SignBit);
    }

    private static ulong Offset(int value, int bias, string name)
    {
        long offset = (long)value + bias;
        if ((ulong)offset >= (ulong)bias * 2)
        {
            throw new ArgumentOutOfRangeException(name, value, $"Chunk coordinates must be from {-bias} to {bias - 1}.");
        }

        return (ulong)offset;
    }
}
