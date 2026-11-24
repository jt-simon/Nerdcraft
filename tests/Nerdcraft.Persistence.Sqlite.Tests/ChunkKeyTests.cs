// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Persistence.Sqlite.Tests;

public class ChunkKeyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(-ChunkKey.HorizontalBias, ChunkKey.HorizontalBias - 2)]
    [InlineData(ChunkKey.HorizontalBias - 1, -ChunkKey.HorizontalBias)]
    public void Pack_ColumnRows_AreContiguousAndAscending(int x, int z)
    {
        long first = ChunkKey.Pack(0, x, -ChunkKey.VerticalBias, z);

        for (int y = -ChunkKey.VerticalBias; y < ChunkKey.VerticalBias; y++)
        {
            Assert.Equal(first + y + ChunkKey.VerticalBias, ChunkKey.Pack(0, x, y, z));
        }

        // The next column starts after this one ends, so a column is one range of keys.
        long next = ChunkKey.Pack(0, x, -ChunkKey.VerticalBias, z + 1 < ChunkKey.HorizontalBias ? z + 1 : z);
        Assert.True(z + 1 >= ChunkKey.HorizontalBias || next > ChunkKey.Pack(0, x, ChunkKey.VerticalBias - 1, z));
    }

    [Fact]
    public void Pack_DifferentDimensionsAndColumns_SortByDimensionThenXThenZ()
    {
        long[] expected =
        [
            ChunkKey.Pack(0, -5, 0, 7),
            ChunkKey.Pack(0, -5, 0, 8),
            ChunkKey.Pack(0, 4, 0, -9),
            ChunkKey.Pack(1, -ChunkKey.HorizontalBias, 0, -ChunkKey.HorizontalBias),
        ];

        Assert.Equal(expected, expected.Order());
    }

    [Theory]
    [InlineData(ChunkKey.HorizontalBias, 0, 0)]
    [InlineData(-ChunkKey.HorizontalBias - 1, 0, 0)]
    [InlineData(0, ChunkKey.VerticalBias, 0)]
    [InlineData(0, -ChunkKey.VerticalBias - 1, 0)]
    [InlineData(0, 0, ChunkKey.HorizontalBias)]
    [InlineData(0, 0, -ChunkKey.HorizontalBias - 1)]
    public void Pack_CoordinateOutOfRange_Throws(int x, int y, int z)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ChunkKey.Pack(0, x, y, z));
}
