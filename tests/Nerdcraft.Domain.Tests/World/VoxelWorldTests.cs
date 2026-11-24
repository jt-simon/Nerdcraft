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

        Assert.True(world.TryGetChunk(new ChunkPosition(0, 0, 0), out ChunkView chunk));
        Assert.False(chunk.IsEmpty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Chunk.Volume - 1)]
    [InlineData(Chunk.Volume + 1)]
    public void LoadChunk_InvalidLengthForNewChunk_LeavesWorldUnchanged(int length)
    {
        ChunkPosition target = new(0, 0, 0);
        ChunkPosition adjacent = new(1, 0, 0);
        VoxelWorld world = WorldWith(adjacent);
        Assert.True(world.TryGetChunk(adjacent, out ChunkView neighbor));
        int neighborVersion = neighbor.Version;
        int revision = world.Revision;

        Assert.Throws<ArgumentException>("blocks", () => world.LoadChunk(target, new BlockId[length]));

        Assert.False(world.IsLoaded(target));
        Assert.Equal(revision, world.Revision);
        Assert.Equal(neighborVersion, neighbor.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Chunk.Volume - 1)]
    [InlineData(Chunk.Volume + 1)]
    public void LoadChunk_InvalidLengthForExistingChunk_PreservesOriginal(int length)
    {
        ChunkPosition target = new(0, 0, 0);
        ChunkPosition adjacent = new(1, 0, 0);
        VoxelWorld world = WorldWith(target, adjacent);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);
        Assert.True(world.TryGetChunk(target, out ChunkView chunk));
        Assert.True(world.TryGetChunk(adjacent, out ChunkView neighbor));
        int version = chunk.Version;
        int neighborVersion = neighbor.Version;
        int revision = world.Revision;
        BlockId[] original = chunk.Blocks.ToArray();

        Assert.Throws<ArgumentException>("blocks", () => world.LoadChunk(target, new BlockId[length]));

        Assert.True(world.IsLoaded(target));
        Assert.Equal(revision, world.Revision);
        Assert.Equal(version, chunk.Version);
        Assert.Equal(neighborVersion, neighbor.Version);
        Assert.Equal(original, chunk.Blocks.ToArray());
        Assert.False(chunk.IsEmpty);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 1)]
    public void UnloadChunk_TouchesEveryKindOfNeighbor(int x, int y, int z)
    {
        ChunkPosition removed = new(0, 0, 0);
        ChunkPosition adjacent = new(x, y, z);
        VoxelWorld world = WorldWith(removed, adjacent);
        Assert.True(world.TryGetChunk(adjacent, out ChunkView neighbor));
        int before = neighbor.Version;

        Assert.True(world.UnloadChunk(removed));

        Assert.Equal(before + 1, neighbor.Version);
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
        world.TryGetChunk(new ChunkPosition(1, 0, 0), out ChunkView neighbor);
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
        world.TryGetChunk(edgeNeighbor, out ChunkView edge);
        world.TryGetChunk(cornerNeighbor, out ChunkView corner);
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
        world.TryGetChunk(new ChunkPosition(1, 0, 0), out ChunkView neighbor);
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
        world.TryGetChunk(position, out ChunkView first);
        int earlierVersion = first.Version;

        world.UnloadChunk(position);
        world.LoadChunk(position, new BlockId[Chunk.Volume]);
        world.TryGetChunk(position, out ChunkView second);

        Assert.True(second.Version > earlierVersion);
    }

    [Fact]
    public void LoadChunk_BumpsLoadedNeighborVersions()
    {
        VoxelWorld world = new();
        world.LoadChunk(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);
        world.TryGetChunk(new ChunkPosition(0, 0, 0), out ChunkView first);
        int before = first.Version;

        world.LoadChunk(new ChunkPosition(0, 0, 1), new BlockId[Chunk.Volume]);

        Assert.Equal(before + 1, first.Version);
    }

    [Fact]
    public void SetBlock_ChangedBlock_MarksOnlyThatChunkModified()
    {
        ChunkPosition edited = new(0, 0, 0);
        ChunkPosition neighbor = new(1, 0, 0);
        VoxelWorld world = WorldWith(edited, neighbor);

        world.SetBlock(new BlockPosition(Chunk.Mask, 1, 1), BlockTypes.Stone);

        Assert.Equal([edited], world.ModifiedChunks);
    }

    [Fact]
    public void SetBlock_SameBlock_DoesNotMarkModified()
    {
        VoxelWorld world = WorldWith(new ChunkPosition(0, 0, 0));

        world.SetBlock(new BlockPosition(1, 1, 1), BlockId.Air);

        Assert.Empty(world.ModifiedChunks);
    }

    [Fact]
    public void MarkSaved_ModifiedChunk_ClearsTheMarkUntilTheNextEdit()
    {
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = WorldWith(position);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);

        world.MarkSaved(position);
        Assert.Empty(world.ModifiedChunks);

        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Dirt);
        Assert.Equal([position], world.ModifiedChunks);
    }

    [Fact]
    public void UnloadChunk_ModifiedChunk_ClearsTheMark()
    {
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = WorldWith(position);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);

        world.UnloadChunk(position);

        Assert.Empty(world.ModifiedChunks);
    }
}
