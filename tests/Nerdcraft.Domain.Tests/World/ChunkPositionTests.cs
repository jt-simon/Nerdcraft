// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.World;

public class ChunkPositionTests
{
    [Fact]
    public void FromBlockPosition_FloorsNegativeCoordinates()
    {
        BlockPosition block = new(-1, -16, -17);

        Assert.Equal(new ChunkPosition(-1, -1, -2), block.ChunkPosition);
        Assert.Equal(15, block.LocalX);
        Assert.Equal(0, block.LocalY);
        Assert.Equal(15, block.LocalZ);
    }

    [Fact]
    public void Origin_RoundTripsThroughBlockPosition()
    {
        ChunkPosition chunk = new(-3, 2, 5);

        Assert.Equal(chunk, chunk.Origin.ChunkPosition);
    }
}
