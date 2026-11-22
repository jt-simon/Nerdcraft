// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Domain.Tests.World;

public class HeightmapTerrainGeneratorTests
{
    [Fact]
    public void Generate_SameSeedAndPosition_IsDeterministic()
    {
        BlockId[] a = new BlockId[Chunk.Volume];
        BlockId[] b = new BlockId[Chunk.Volume];

        new HeightmapTerrainGenerator(42).Generate(new ChunkPosition(3, 1, -2), a);
        new HeightmapTerrainGenerator(42).Generate(new ChunkPosition(3, 1, -2), b);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentTerrain()
    {
        BlockId[] a = new BlockId[Chunk.Volume];
        BlockId[] b = new BlockId[Chunk.Volume];

        new HeightmapTerrainGenerator(1).Generate(new ChunkPosition(0, 1, 0), a);
        new HeightmapTerrainGenerator(2).Generate(new ChunkPosition(0, 1, 0), b);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Generate_ChunkFarAboveSurface_IsAllAir()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        blocks.AsSpan().Fill(BlockTypes.Stone); // ensure Generate clears stale data

        new HeightmapTerrainGenerator(7).Generate(new ChunkPosition(0, 100, 0), blocks);

        Assert.All(blocks, block => Assert.Equal(BlockTypes.Air, block));
    }

    [Fact]
    public void Generate_Column_HasGrassOverDirtOverStone()
    {
        HeightmapTerrainGenerator generator = new(7);
        int surface = generator.SurfaceHeight(0, 0);
        BlockId[] blocks = new BlockId[Chunk.Volume];
        ChunkPosition chunk = new BlockPosition(0, surface, 0).ChunkPosition;
        generator.Generate(chunk, blocks);
        int baseY = chunk.Origin.Y;

        BlockId At(int worldY) => blocks[Chunk.Index(0, worldY - baseY, 0)];

        Assert.Equal(BlockTypes.Grass, At(surface));
        if (surface - 1 >= baseY)
        {
            Assert.Equal(BlockTypes.Dirt, At(surface - 1));
        }

        if (surface + 1 < baseY + Chunk.Size)
        {
            Assert.Equal(BlockTypes.Air, At(surface + 1));
        }
    }
}
