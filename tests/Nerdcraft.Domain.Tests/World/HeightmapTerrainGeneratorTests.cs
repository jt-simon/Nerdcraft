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

        new HeightmapTerrainGenerator(42, WorldBounds.Default).Generate(new ChunkPosition(3, 0, -2), a);
        new HeightmapTerrainGenerator(42, WorldBounds.Default).Generate(new ChunkPosition(3, 0, -2), b);

        Assert.Equal(a, b);
    }

    [Fact]
    public void SurfaceHeight_DifferentSeeds_ProduceDifferentTerrain()
    {
        HeightmapTerrainGenerator first = new(1, WorldBounds.Default);
        HeightmapTerrainGenerator second = new(2, WorldBounds.Default);

        bool differs = false;
        for (int i = 0; i < 64 && !differs; i++)
        {
            differs = first.SurfaceHeight(i * 37, i * 11) != second.SurfaceHeight(i * 37, i * 11);
        }

        Assert.True(differs);
    }

    [Fact]
    public void Generate_ChunkFarAboveSurface_IsAllAir()
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        blocks.AsSpan().Fill(BlockTypes.Stone); // ensure Generate clears stale data

        new HeightmapTerrainGenerator(7, WorldBounds.Default).Generate(new ChunkPosition(0, 100, 0), blocks);

        Assert.All(blocks, block => Assert.Equal(BlockTypes.Air, block));
    }

    [Fact]
    public void Generate_Column_HasGrassOverDirtOverStone()
    {
        HeightmapTerrainGenerator generator = new(7, WorldBounds.Default);
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

    [Fact]
    public void Generate_ChunkAtWorldBottom_HasBedrockLayerWithAirBelow()
    {
        WorldBounds bounds = WorldBounds.Default;
        BlockId[] blocks = new BlockId[Chunk.Volume];
        ChunkPosition chunk = new(0, bounds.MinChunkY, 0);
        new HeightmapTerrainGenerator(7, bounds).Generate(chunk, blocks);
        int bedrockY = bounds.MinY - chunk.Origin.Y;

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                Assert.Equal(BlockTypes.Bedrock, blocks[Chunk.Index(x, bedrockY, z)]);
                Assert.Equal(BlockTypes.Air, blocks[Chunk.Index(x, bedrockY - 1, z)]);
                Assert.NotEqual(BlockTypes.Air, blocks[Chunk.Index(x, bedrockY + 1, z)]);
            }
        }
    }

    [Theory]
    [InlineData(-50, 200)]
    [InlineData(-10, 20)]
    public void SurfaceHeight_AnyColumn_StaysWithinBounds(int minY, int maxY)
    {
        HeightmapTerrainGenerator generator = new(99, new WorldBounds(minY, maxY));

        for (int z = -2000; z <= 2000; z += 97)
        {
            for (int x = -2000; x <= 2000; x += 97)
            {
                int height = generator.SurfaceHeight(x, z);
                Assert.InRange(height, minY + 1, maxY);
            }
        }
    }

    [Fact]
    public void Constructor_MaxYNotAboveMinY_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new HeightmapTerrainGenerator(1, new WorldBounds(5, 5)));
}
