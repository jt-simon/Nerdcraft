// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Tests.Support;

/// <summary>Chunks at Y=0 are stone in their lower half; everything else is air. Every Y=0 chunk meshes to a non-empty mesh.</summary>
public sealed class HalfSlabGenerator : ITerrainGenerator
{
    public void Generate(ChunkPosition position, Span<BlockId> blocks)
    {
        blocks.Clear();
        if (position.Y != 0)
        {
            return;
        }

        for (int y = 0; y < Chunk.Size / 2; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    blocks[Chunk.Index(x, y, z)] = BlockTypes.Stone;
                }
            }
        }
    }
}
