// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Meshing;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Meshing;

public class ChunkVisibilityTests
{
    private static readonly ChunkPosition Origin = new(0, 0, 0);

    private static ChunkVisibility Compute(BlockId[] blocks)
    {
        VoxelWorld world = new();
        world.LoadChunk(Origin, blocks);
        using ChunkSnapshot snapshot = ChunkSnapshot.Create(world, Origin);
        return ChunkVisibility.Compute(snapshot);
    }

    [Fact]
    public void Compute_TunnelThroughSolidChunk_ConnectsOnlyItsTwoEnds()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        Array.Fill(blocks, BlockTypes.Stone);
        for (int x = 0; x < Chunk.Size; x++)
        {
            blocks[Chunk.Index(x, 8, 8)] = BlockTypes.Air;
        }

        ChunkVisibility visibility = Compute(blocks);

        foreach (ChunkFace from in Enum.GetValues<ChunkFace>())
        {
            foreach (ChunkFace to in Enum.GetValues<ChunkFace>())
            {
                bool alongTunnel = from is ChunkFace.PositiveX or ChunkFace.NegativeX && to is ChunkFace.PositiveX or ChunkFace.NegativeX;
                Assert.Equal(alongTunnel, visibility.Connects(from, to));
            }
        }
    }

    [Fact]
    public void Compute_ChunkFullOfWater_ConnectsEveryFace()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        Array.Fill(blocks, BlockTypes.Water);

        Assert.Equal(ChunkVisibility.All, Compute(blocks));
    }
}
