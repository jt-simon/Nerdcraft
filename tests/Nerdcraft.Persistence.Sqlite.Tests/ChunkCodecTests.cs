// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Persistence.Sqlite.Tests;

public class ChunkCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(4096)]
    public void Decode_EncodedChunkWithPaletteOfSize_ReturnsTheSameBlocks(int paletteSize)
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new BlockId((ushort)(((i * 7) % paletteSize) * 13));
        }

        Assert.Equal(blocks, RoundTrip(blocks));
    }

    [Fact]
    public void Decode_EncodedRandomChunk_ReturnsTheSameBlocks()
    {
        // Random ids do not compress, which exercises the stored-uncompressed form.
        Random random = new(7);
        BlockId[] blocks = new BlockId[Chunk.Volume];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new BlockId((ushort)random.Next(ushort.MaxValue + 1));
        }

        Assert.Equal(blocks, RoundTrip(blocks));
    }

    [Fact]
    public void Encode_ChunkOfOneBlockType_IsTiny()
    {
        using ChunkCodec codec = new();

        byte[] encoded = codec.Encode(new BlockId[Chunk.Volume]);

        Assert.True(encoded.Length <= 8);
    }

    [Fact]
    public void Decode_TruncatedBlob_Throws()
    {
        using ChunkCodec codec = new();
        BlockId[] blocks = new BlockId[Chunk.Volume];
        blocks[5] = BlockTypes.Stone;
        byte[] encoded = codec.Encode(blocks);

        Assert.Throws<InvalidDataException>(() => codec.Decode(encoded.AsSpan(0, encoded.Length - 1), new BlockId[Chunk.Volume]));
    }

    [Fact]
    public void Decode_UnknownFormat_Throws()
    {
        using ChunkCodec codec = new();

        Assert.Throws<InvalidDataException>(() => codec.Decode([2, 0, 1, 0, 0, 0], new BlockId[Chunk.Volume]));
    }

    private static BlockId[] RoundTrip(BlockId[] blocks)
    {
        using ChunkCodec codec = new();
        BlockId[] decoded = new BlockId[Chunk.Volume];
        codec.Decode(codec.Encode(blocks), decoded);
        return decoded;
    }
}
