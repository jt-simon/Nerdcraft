// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.Tests.Support;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.Physics;

public class VoxelRaycastTests
{
    private const float Tolerance = 1e-4f;

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    public void Cast_AxisAlignedTowardStone_ReturnsBlockAndEnteredFaceNormal(int dx, int dy, int dz)
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(8 + (3 * dx), 8 + (3 * dy), 8 + (3 * dz)), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(8.5f, 8.5f, 8.5f), new Vector3(dx, dy, dz), 10f));

        Assert.Equal(new BlockPosition(8 + (3 * dx), 8 + (3 * dy), 8 + (3 * dz)), hit.Block);
        Assert.Equal(new Vector3(-dx, -dy, -dz), hit.Normal);
        Assert.Equal(2.5f, hit.Distance, Tolerance);
    }

    [Fact]
    public void Cast_AtNegativeCoordinates_FloorsToContainingBlock()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(-1, 0, -1));
        world.SetBlock(new BlockPosition(-3, 1, -1), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(-0.5f, 1.5f, -0.5f), new Vector3(-1f, 0f, 0f), 10f));

        Assert.Equal(new BlockPosition(-3, 1, -1), hit.Block);
        Assert.Equal(Vector3.UnitX, hit.Normal);
        Assert.Equal(1.5f, hit.Distance, Tolerance);
    }

    [Fact]
    public void Cast_RayCrossesChunkBorder_HitsBlockInNeighbor()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0), new ChunkPosition(-1, 0, 0));
        world.SetBlock(new BlockPosition(-2, 1, 0), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(0.5f, 1.5f, 0.5f), new Vector3(-1f, 0f, 0f), 10f));

        Assert.Equal(new BlockPosition(-2, 1, 0), hit.Block);
        Assert.Equal(1.5f, hit.Distance, Tolerance);
    }

    [Fact]
    public void Cast_StoneBeyondMaxDistance_ReturnsNull()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(8, 1, 2), BlockTypes.Stone);

        Assert.Null(VoxelRaycast.Cast(world, new Vector3(2.5f, 1.5f, 2.5f), Vector3.UnitX, 5f));
    }

    [Fact]
    public void Cast_IntoUnloadedChunk_StopsWithoutHit()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0), new ChunkPosition(2, 0, 0));
        world.SetBlock(new BlockPosition(33, 1, 8), BlockTypes.Stone);

        Assert.Null(VoxelRaycast.Cast(world, new Vector3(8.5f, 1.5f, 8.5f), Vector3.UnitX, 100f));
    }

    [Fact]
    public void Cast_OriginInsideStone_ReturnsThatBlockAtZeroDistance()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(4, 4, 4), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(4.3f, 4.6f, 4.9f), Vector3.UnitX, 5f));

        Assert.Equal(new BlockPosition(4, 4, 4), hit.Block);
        Assert.Equal(Vector3.Zero, hit.Normal);
        Assert.Equal(0f, hit.Distance);
    }

    [Fact]
    public void Cast_OriginInsideStoneIgnoringStartBlock_ReturnsNextSolidBlock()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        TestTerrain.Fill(world, BlockTypes.Stone, (4, 4, 4), (6, 4, 4));

        RaycastHit hit = Assert.NotNull(
            VoxelRaycast.Cast(world, new Vector3(4.5f, 4.5f, 4.5f), Vector3.UnitX, 5f, ignoreStartBlock: true));

        Assert.Equal(new BlockPosition(5, 4, 4), hit.Block);
        Assert.Equal(-Vector3.UnitX, hit.Normal);
        Assert.Equal(0.5f, hit.Distance, Tolerance);
    }

    [Fact]
    public void Cast_ThroughWater_HitsStoneBehind()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        TestTerrain.Fill(world, BlockTypes.Water, (3, 1, 1), (5, 1, 1));
        world.SetBlock(new BlockPosition(6, 1, 1), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(1.5f, 1.5f, 1.5f), Vector3.UnitX, 10f));

        Assert.Equal(new BlockPosition(6, 1, 1), hit.Block);
        Assert.Equal(-Vector3.UnitX, hit.Normal);
    }

    [Fact]
    public void Cast_ObliqueRay_VisitsOnlyCellsOnItsPath()
    {
        // From the middle of cell (8, 8) along (2, 0, 1), the ray visits (8,8), (9,8), (9,9), (10,9). The stones
        // at (10, 8) and (8, 9) lie beside that path and must not be hit.
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(10, 1, 8), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(8, 1, 9), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(10, 1, 9), BlockTypes.Stone);

        RaycastHit hit = Assert.NotNull(VoxelRaycast.Cast(world, new Vector3(8.5f, 1.5f, 8.5f), new Vector3(2f, 0f, 1f), 10f));

        Assert.Equal(new BlockPosition(10, 1, 9), hit.Block);
        Assert.Equal(-Vector3.UnitX, hit.Normal);
        Assert.Equal(0.75f * MathF.Sqrt(5f), hit.Distance, Tolerance);
    }
}
