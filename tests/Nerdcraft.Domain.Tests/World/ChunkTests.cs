// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.World;

public class ChunkTests
{
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, Chunk.Size, 0)]
    [InlineData(0, 0, Chunk.Size)]
    public void Set_OutOfBounds_Throws(int x, int y, int z)
    {
        Chunk chunk = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.Set(x, y, z, BlockTypes.Stone));
    }

    [Fact]
    public void Set_ChangingBlock_IncrementsVersion()
    {
        Chunk chunk = new();

        chunk.Set(1, 2, 3, BlockTypes.Stone);

        Assert.Equal(1, chunk.Version);
        Assert.Equal(BlockTypes.Stone, chunk.Get(1, 2, 3));
    }

    [Fact]
    public void Set_SameBlock_DoesNotIncrementVersion()
    {
        Chunk chunk = new();

        chunk.Set(1, 2, 3, BlockTypes.Air);

        Assert.Equal(0, chunk.Version);
    }
}
