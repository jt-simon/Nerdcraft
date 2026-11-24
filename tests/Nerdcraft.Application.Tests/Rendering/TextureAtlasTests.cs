// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Application.Tests.Rendering;

public class TextureAtlasTests
{
    [Fact]
    public void GetUv_UnknownOrMissingName_ReturnsTheMissingTile()
    {
        TextureAtlas atlas = TextureAtlas.Layout(["stone", "dirt"]);

        UvRect missing = new(0f, 0f, 1f, 1f / 3f);
        Assert.Equal(missing, atlas.GetUv("nope"));
        Assert.Equal(missing, atlas.GetUv(null));
        Assert.NotEqual(missing, atlas.GetUv("stone"));
    }

    [Fact]
    public void Layout_DuplicateName_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextureAtlas.Layout(["stone", "stone"]));
    }

    // The precomputed table covers the first 256 ids; ids beyond it take the slow path. Both must agree.
    [Fact]
    public void GetUv_AcrossTheCachedRange_MatchesTheTextureName()
    {
        for (ushort id = 0; id < 300; id++)
        {
            foreach (ChunkFace face in Enum.GetValues<ChunkFace>())
            {
                BlockId block = new(id);
                Assert.Equal(BlockPalette.Atlas.GetUv(BlockPalette.GetTexture(block, face)), BlockPalette.GetUv(block, face));
            }
        }
    }

    [Fact]
    public void GetTexture_EveryBlockFace_NamesAListedTexture()
    {
        BlockId[] blocks =
        [
            BlockTypes.Stone, BlockTypes.Dirt, BlockTypes.Grass, BlockTypes.Bedrock,
            BlockTypes.Sand, BlockTypes.Gravel, BlockTypes.Snow, BlockTypes.Water,
        ];

        foreach (BlockId block in blocks)
        {
            foreach (ChunkFace face in Enum.GetValues<ChunkFace>())
            {
                Assert.Contains(BlockPalette.GetTexture(block, face), BlockPalette.TextureNames);
            }
        }
    }
}
