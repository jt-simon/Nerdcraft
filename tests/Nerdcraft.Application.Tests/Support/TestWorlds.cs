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
}
