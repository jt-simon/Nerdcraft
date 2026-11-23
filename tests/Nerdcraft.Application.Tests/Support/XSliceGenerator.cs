// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Tests.Support;

/// <summary>Chunks at X=<paramref name="sliceX"/> are filled with <paramref name="fill"/>; chunks at Y=0 elsewhere are stone in their lower half. A solid slice is a wall across the world.</summary>
public sealed class XSliceGenerator(int sliceX, BlockId fill) : ITerrainGenerator
{
    public void Generate(ChunkPosition position, Span<BlockId> blocks)
    {
        if (position.X == sliceX)
        {
            blocks.Fill(fill);
            return;
        }

        new HalfSlabGenerator().Generate(position, blocks);
    }
}
