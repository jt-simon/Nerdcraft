// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World.Generation;

/// <summary>Fills chunks with terrain. Pure and thread-safe: implementations must not hold mutable state.</summary>
public interface ITerrainGenerator
{
    /// <summary>Writes the terrain for one chunk, overwriting every element of <paramref name="blocks"/>.</summary>
    /// <param name="position">The chunk to generate.</param>
    /// <param name="blocks">Destination of length <see cref="Chunk.Volume"/>, indexed by <see cref="Chunk.Index"/>.</param>
    void Generate(ChunkPosition position, Span<BlockId> blocks);

    /// <summary>
    /// Writes the terrain for a vertical stack of chunks sharing one X and Z, overwriting every element of
    /// <paramref name="blocks"/>. The result is identical to calling <see cref="Generate"/> for each row, but an
    /// implementation may share work between rows. The default implementation calls <see cref="Generate"/> per row.
    /// </summary>
    /// <param name="chunkX">Chunk X of the column.</param>
    /// <param name="chunkZ">Chunk Z of the column.</param>
    /// <param name="minChunkY">Chunk Y of the lowest row, inclusive.</param>
    /// <param name="maxChunkY">Chunk Y of the highest row, inclusive. Nothing is written when this is below <paramref name="minChunkY"/>.</param>
    /// <param name="blocks">
    /// Destination of length <c>(maxChunkY - minChunkY + 1) * <see cref="Chunk.Volume"/></c>. Rows are stacked from
    /// <paramref name="minChunkY"/> upward, each laid out as in <see cref="Generate"/>.
    /// </param>
    void GenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks)
    {
        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            Generate(new ChunkPosition(chunkX, y, chunkZ), blocks.Slice((y - minChunkY) * Chunk.Volume, Chunk.Volume));
        }
    }
}
