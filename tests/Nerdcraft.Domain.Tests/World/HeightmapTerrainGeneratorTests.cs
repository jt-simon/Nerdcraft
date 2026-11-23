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
    public void GenerateColumn_FullBounds_MatchesGeneratingEachChunkSeparately()
    {
        WorldBounds bounds = WorldBounds.Default;
        HeightmapTerrainGenerator generator = new(1337, bounds);
        int rows = bounds.MaxChunkY - bounds.MinChunkY + 1;
        BlockId[] column = new BlockId[rows * Chunk.Volume];
        column.AsSpan().Fill(BlockTypes.Stone); // ensure every element is overwritten

        generator.GenerateColumn(-5, 7, bounds.MinChunkY, bounds.MaxChunkY, column);

        BlockId[] single = new BlockId[Chunk.Volume];
        for (int row = 0; row < rows; row++)
        {
            generator.Generate(new ChunkPosition(-5, bounds.MinChunkY + row, 7), single);
            Assert.True(
                column.AsSpan(row * Chunk.Volume, Chunk.Volume).SequenceEqual(single),
                $"Row {row} differs from the single-chunk result.");
        }
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
    public void Generate_ChunkHoldingHighestSurface_IsNotSkipped()
    {
        // The early-out for chunks above the terrain must never cut off real terrain.
        HeightmapTerrainGenerator generator = new(3, WorldBounds.Default);
        int highest = int.MinValue;
        int highestX = 0;
        int highestZ = 0;
        for (int z = -3000; z <= 3000; z += 23)
        {
            for (int x = -3000; x <= 3000; x += 23)
            {
                int height = generator.SurfaceHeight(x, z);
                if (height > highest)
                {
                    (highest, highestX, highestZ) = (height, x, z);
                }
            }
        }

        BlockPosition top = new(highestX, highest, highestZ);
        BlockId[] blocks = new BlockId[Chunk.Volume];
        generator.Generate(top.ChunkPosition, blocks);

        Assert.Equal(BlockTypes.Snow, blocks[Chunk.Index(top.LocalX, top.LocalY, top.LocalZ)]);
    }

    [Fact]
    public void Generate_GrassColumn_HasDirtBelowAndAirAbove()
    {
        HeightmapTerrainGenerator generator = new(7, WorldBounds.Default);
        Dictionary<ChunkPosition, BlockId[]> cache = [];
        (int X, int Z, int Surface)? found = null;
        for (int i = 0; i < 400 && found is null; i++)
        {
            int surface = generator.SurfaceHeight(i * 5, 0);
            if (BlockAt(generator, cache, new BlockPosition(i * 5, surface, 0)) == BlockTypes.Grass)
            {
                found = (i * 5, 0, surface);
            }
        }

        Assert.NotNull(found);
        (int x, int z, int top) = found.Value;
        Assert.Equal(BlockTypes.Air, BlockAt(generator, cache, new BlockPosition(x, top + 1, z)));
        Assert.Equal(BlockTypes.Dirt, BlockAt(generator, cache, new BlockPosition(x, top - 1, z)));
    }

    [Fact]
    public void Generate_ColumnBelowSeaLevel_HasWaterUpToSeaLevel()
    {
        HeightmapTerrainGenerator generator = new(7, WorldBounds.Default);
        Dictionary<ChunkPosition, BlockId[]> cache = [];
        int checkedColumns = 0;

        for (int x = -600; x <= 600 && checkedColumns < 20; x += 7)
        {
            int surface = generator.SurfaceHeight(x, 0);
            if (surface >= HeightmapTerrainGenerator.SeaLevel)
            {
                continue;
            }

            checkedColumns++;
            for (int y = surface + 1; y <= HeightmapTerrainGenerator.SeaLevel; y++)
            {
                Assert.Equal(BlockTypes.Water, BlockAt(generator, cache, new BlockPosition(x, y, 0)));
            }

            Assert.Equal(BlockTypes.Air, BlockAt(generator, cache, new BlockPosition(x, HeightmapTerrainGenerator.SeaLevel + 1, 0)));
        }

        Assert.Equal(20, checkedColumns);
    }

    [Fact]
    public void Generate_SurfaceAcrossRegion_UsesEveryHeightMaterial()
    {
        HeightmapTerrainGenerator generator = new(1337, WorldBounds.Default);
        BlockId[] blocks = new BlockId[Chunk.Volume];
        HashSet<BlockId> tops = [];
        int highestSand = int.MinValue;
        int lowestSnow = int.MaxValue;

        for (int z = -1500; z <= 1500; z += 19)
        {
            for (int x = -1500; x <= 1500; x += 19)
            {
                int surface = generator.SurfaceHeight(x, z);
                BlockPosition position = new(x, surface, z);
                generator.Generate(position.ChunkPosition, blocks);
                BlockId top = blocks[Chunk.Index(position.LocalX, position.LocalY, position.LocalZ)];
                tops.Add(top);
                if (top == BlockTypes.Sand)
                {
                    highestSand = Math.Max(highestSand, surface);
                }

                if (top == BlockTypes.Snow)
                {
                    lowestSnow = Math.Min(lowestSnow, surface);
                }
            }
        }

        Assert.Contains(BlockTypes.Grass, tops);
        Assert.Contains(BlockTypes.Sand, tops);
        Assert.Contains(BlockTypes.Gravel, tops);
        Assert.Contains(BlockTypes.Stone, tops);
        Assert.Contains(BlockTypes.Snow, tops);
        Assert.True(highestSand < lowestSnow, $"Sand reaches {highestSand} but snow starts at {lowestSnow}.");
    }

    [Fact]
    public void Generate_SeabedBlocks_AreNeverOpenedByCaves()
    {
        HeightmapTerrainGenerator generator = new(1337, WorldBounds.Default);
        Dictionary<ChunkPosition, BlockId[]> cache = [];
        int seabedColumns = 0;

        for (int z = -400; z < 400; z += 2)
        {
            for (int x = -400; x < 400; x += 2)
            {
                int surface = generator.SurfaceHeight(x, z);
                if (surface >= HeightmapTerrainGenerator.SeaLevel)
                {
                    continue;
                }

                seabedColumns++;
                Assert.False(BlockAt(generator, cache, new BlockPosition(x, surface, z)).IsAir, $"Seabed opened at {x}, {surface}, {z}.");
            }
        }

        Assert.True(seabedColumns > 1000);
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

    // Pins the exact terrain of a fixed region. A failure means the world every seed generates has changed (noise
    // library update, tuning constant, layer order). If that is intended, update ExpectedRegionHash and add a
    // CHANGELOG entry; otherwise fix the regression. Never update it just to make the test pass.
    [Fact]
    public void Generate_FixedSeedRegion_MatchesRecordedHash()
    {
        const ulong ExpectedRegionHash = 789920328402091155UL;
        HeightmapTerrainGenerator generator = new(1337, WorldBounds.Default);
        ChunkPosition[] region =
        [
            new(0, 0, 0), new(5, -1, -3), new(2, -3, 2), new(0, -4, 0), new(40, 2, -17), new(-9, 5, 12),
        ];

        // FNV-1a over every block id of every chunk, in order.
        ulong hash = 14695981039346656037UL;
        BlockId[] blocks = new BlockId[Chunk.Volume];
        foreach (ChunkPosition chunk in region)
        {
            generator.Generate(chunk, blocks);
            foreach (BlockId block in blocks)
            {
                hash = (hash ^ block.Value) * 1099511628211UL;
            }
        }

        Assert.Equal(ExpectedRegionHash, hash);
    }

    [Fact]
    public void Generate_DeepUnderground_HasCavesButStaysMostlySolid()
    {
        WorldBounds bounds = WorldBounds.Default;
        HeightmapTerrainGenerator generator = new(1337, bounds);
        BlockId[] blocks = new BlockId[Chunk.Volume];
        int total = 0;
        int air = 0;

        for (int cz = -3; cz < 3; cz++)
        {
            for (int cx = -3; cx < 3; cx++)
            {
                for (int cy = -3; cy <= -1; cy++)
                {
                    ChunkPosition chunk = new(cx, cy, cz);
                    generator.Generate(chunk, blocks);
                    BlockPosition origin = chunk.Origin;
                    for (int z = 0; z < Chunk.Size; z++)
                    {
                        for (int x = 0; x < Chunk.Size; x++)
                        {
                            int surface = generator.SurfaceHeight(origin.X + x, origin.Z + z);
                            for (int y = 0; y < Chunk.Size; y++)
                            {
                                // Deep enough that a hole here is a cave, not a surface opening or the bedrock floor.
                                if (origin.Y + y >= bounds.MinY + 3 && origin.Y + y <= surface - 9)
                                {
                                    total++;
                                    air += blocks[Chunk.Index(x, y, z)].IsAir ? 1 : 0;
                                }
                            }
                        }
                    }
                }
            }
        }

        Assert.InRange(air / (double)total, 0.02, 0.15);
    }

    [Fact]
    public void Generate_BottomLayersAboveBedrock_AreNeverCarved()
    {
        WorldBounds bounds = WorldBounds.Default;
        HeightmapTerrainGenerator generator = new(1337, bounds);
        Dictionary<ChunkPosition, BlockId[]> cache = [];

        for (int z = -48; z < 48; z++)
        {
            for (int x = -48; x < 48; x++)
            {
                Assert.Equal(BlockTypes.Bedrock, BlockAt(generator, cache, new BlockPosition(x, bounds.MinY, z)));
                Assert.NotEqual(BlockTypes.Air, BlockAt(generator, cache, new BlockPosition(x, bounds.MinY + 1, z)));
                Assert.NotEqual(BlockTypes.Air, BlockAt(generator, cache, new BlockPosition(x, bounds.MinY + 2, z)));
            }
        }
    }

    [Fact]
    public void Generate_SurfaceBlocks_AreOpenedByCavesRarely()
    {
        HeightmapTerrainGenerator generator = new(1337, WorldBounds.Default);
        Dictionary<ChunkPosition, BlockId[]> cache = [];
        int columns = 0;
        int openings = 0;

        for (int z = -256; z < 256; z += 3)
        {
            for (int x = -256; x < 256; x += 3)
            {
                BlockPosition surface = new(x, generator.SurfaceHeight(x, z), z);
                columns++;
                openings += BlockAt(generator, cache, surface).IsAir ? 1 : 0;
            }
        }

        Assert.True(openings > 0, "Caves never reach the surface.");
        Assert.True(openings < columns / 50, $"{openings} of {columns} surface blocks are open; mouths should be occasional.");
    }

    private static BlockId BlockAt(HeightmapTerrainGenerator generator, Dictionary<ChunkPosition, BlockId[]> cache, BlockPosition position)
    {
        if (!cache.TryGetValue(position.ChunkPosition, out BlockId[]? blocks))
        {
            blocks = new BlockId[Chunk.Volume];
            generator.Generate(position.ChunkPosition, blocks);
            cache[position.ChunkPosition] = blocks;
        }

        return blocks[Chunk.Index(position.LocalX, position.LocalY, position.LocalZ)];
    }

    [Fact]
    public void Constructor_MaxYNotAboveMinY_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new HeightmapTerrainGenerator(1, new WorldBounds(5, 5)));
}
