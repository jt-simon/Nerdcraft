// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.Support;

public static class TestTerrain
{
    /// <summary>One chunk row (Y = 0 to 15), so blocks below Y = 0 are outside the world.</summary>
    public static readonly WorldBounds Bounds = new(0, Chunk.Size - 1);

    /// <summary>A world of 3 x 3 loaded chunks around the origin whose Y = 0 layer is stone, so feet at Y = 1 stand on it.</summary>
    public static VoxelWorld Flat()
    {
        VoxelWorld world = new();
        BlockId[] blocks = new BlockId[Chunk.Volume];
        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                blocks[Chunk.Index(x, 0, z)] = BlockTypes.Stone;
            }
        }

        for (int cz = -1; cz <= 1; cz++)
        {
            for (int cx = -1; cx <= 1; cx++)
            {
                world.LoadChunk(new ChunkPosition(cx, 0, cz), blocks);
            }
        }

        return world;
    }

    /// <summary>A world whose given chunks are loaded and all air.</summary>
    public static VoxelWorld Empty(params ChunkPosition[] positions)
    {
        VoxelWorld world = new();
        foreach (ChunkPosition position in positions)
        {
            world.LoadChunk(position, new BlockId[Chunk.Volume]);
        }

        return world;
    }

    /// <summary>Fills the inclusive block range with <paramref name="block"/>.</summary>
    public static void Fill(VoxelWorld world, BlockId block, (int X, int Y, int Z) min, (int X, int Y, int Z) max)
    {
        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    world.SetBlock(new BlockPosition(x, y, z), block);
                }
            }
        }
    }
}
