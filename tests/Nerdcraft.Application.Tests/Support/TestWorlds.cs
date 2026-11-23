// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Support;

public static class TestWorlds
{
    /// <summary>A world whose given chunks are loaded and all air, ready for <see cref="VoxelWorld.SetBlock"/>.</summary>
    public static VoxelWorld WithEmptyChunks(params ChunkPosition[] positions)
    {
        VoxelWorld world = new();
        foreach (ChunkPosition position in positions)
        {
            world.LoadChunk(position, new BlockId[Chunk.Volume]);
        }

        return world;
    }

    /// <summary>One chunk row (Y = 0 to 15), matching <see cref="Flat"/>.</summary>
    public static readonly WorldBounds OneRow = new(0, Chunk.Size - 1);

    /// <summary>A world of 3 x 3 loaded chunks around the origin whose Y = 0 layer is stone, so feet at Y = 1 stand on it.</summary>
    public static VoxelWorld Flat()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                blocks[Chunk.Index(x, 0, z)] = BlockTypes.Stone;
            }
        }

        VoxelWorld world = new();
        for (int cz = -1; cz <= 1; cz++)
        {
            for (int cx = -1; cx <= 1; cx++)
            {
                world.LoadChunk(new ChunkPosition(cx, 0, cz), blocks);
            }
        }

        return world;
    }
}
