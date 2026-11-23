// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.World;

public class VoxelWorldTests
{
    private static VoxelWorld WorldWith(params ChunkPosition[] positions)
    {
        VoxelWorld world = new();
        foreach (ChunkPosition position in positions)
        {
            world.LoadChunk(position, new BlockId[Chunk.Volume]);
        }

        return world;
    }

    [Fact]
    public void LoadChunk_BlocksWithOneSolid_ChunkIsNotEmpty()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        blocks[Chunk.Index(3, 3, 3)] = BlockTypes.Stone;
        VoxelWorld world = new();

        world.LoadChunk(new ChunkPosition(0, 0, 0), blocks);

        Assert.True(world.TryGetChunk(new ChunkPosition(0, 0, 0), out Chunk chunk));
        Assert.False(chunk.IsEmpty);
    }

    [Fact]
    public void GetBlock_InMissingChunk_ReturnsAir()
    {
        VoxelWorld world = new();

        Assert.Equal(BlockTypes.Air, world.GetBlock(new BlockPosition(100, 5, -100)));
    }

    [Fact]
    public void SetBlock_AcrossNegativeBoundary_ReadsBack()
    {
        VoxelWorld world = WorldWith(new ChunkPosition(-1, 0, 0));

        Assert.True(world.SetBlock(new BlockPosition(-1, 0, 0), BlockTypes.Dirt));

        Assert.Equal(BlockTypes.Dirt, world.GetBlock(new BlockPosition(-1, 0, 0)));
    }

    [Fact]
    public void SetBlock_InUnloadedChunk_ReturnsFalseAndLoadsNothing()
    {
        VoxelWorld world = new();

        bool changed = world.SetBlock(new BlockPosition(5, 5, 5), BlockTypes.Stone);

        Assert.False(changed);
        Assert.False(world.IsLoaded(new ChunkPosition(0, 0, 0)));
    }

    [Fact]
    public void SetBlock_OnChunkBorder_BumpsNeighborVersion()
    {
        VoxelWorld world = WorldWith(new ChunkPosition(0, 0, 0), new ChunkPosition(1, 0, 0));
        world.TryGetChunk(new ChunkPosition(1, 0, 0), out Chunk neighbor);
        int before = neighbor.Version;

        world.SetBlock(new BlockPosition(15, 0, 0), BlockTypes.Stone); // +X border of chunk (0,0,0)

        Assert.Equal(before + 1, neighbor.Version);
    }

    [Fact]
    public void SetBlock_OnChunkCorner_BumpsDiagonalNeighborVersions()
    {
        ChunkPosition edgeNeighbor = new(1, 1, 0);
        ChunkPosition cornerNeighbor = new(1, 1, 1);
        VoxelWorld world = WorldWith(new ChunkPosition(0, 0, 0), edgeNeighbor, cornerNeighbor);
        world.TryGetChunk(edgeNeighbor, out Chunk edge);
        world.TryGetChunk(cornerNeighbor, out Chunk corner);
        int edgeBefore = edge.Version;
        int cornerBefore = corner.Version;

        world.SetBlock(new BlockPosition(15, 15, 15), BlockTypes.Stone); // The +X +Y +Z corner of chunk (0,0,0).

        Assert.Equal(edgeBefore + 1, edge.Version);
        Assert.Equal(cornerBefore + 1, corner.Version);
    }

    [Fact]
    public void SetBlock_InChunkInterior_LeavesNeighborVersionUnchanged()
    {
        VoxelWorld world = WorldWith(new ChunkPosition(0, 0, 0), new ChunkPosition(1, 0, 0));
        world.TryGetChunk(new ChunkPosition(1, 0, 0), out Chunk neighbor);
        int before = neighbor.Version;

        world.SetBlock(new BlockPosition(7, 7, 7), BlockTypes.Stone);

        Assert.Equal(before, neighbor.Version);
    }

    [Fact]
    public void LoadChunk_AfterUnloadingTheSamePosition_StartsAboveTheEarlierVersion()
    {
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = WorldWith(position);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);
        world.TryGetChunk(position, out Chunk first);
        int earlierVersion = first.Version;

        world.UnloadChunk(position);
        world.LoadChunk(position, new BlockId[Chunk.Volume]);
        world.TryGetChunk(position, out Chunk second);

        Assert.True(second.Version > earlierVersion);
    }

    [Fact]
    public void LoadChunk_BumpsLoadedNeighborVersions()
    {
        VoxelWorld world = new();
        world.LoadChunk(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);
        world.TryGetChunk(new ChunkPosition(0, 0, 0), out Chunk first);
        int before = first.Version;

        world.LoadChunk(new ChunkPosition(0, 0, 1), new BlockId[Chunk.Volume]);

        Assert.Equal(before + 1, first.Version);
    }
}
