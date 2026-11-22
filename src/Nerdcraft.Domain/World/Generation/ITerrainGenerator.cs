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
}
