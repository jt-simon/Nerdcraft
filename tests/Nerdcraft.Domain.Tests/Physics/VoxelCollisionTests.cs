// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.Tests.Support;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.Physics;

public class VoxelCollisionTests
{
    [Fact]
    public void Collides_OverlapsStoneAtLargeX_FindsSolidBlock()
    {
        BlockPosition block = new(500000, 1, 0);
        VoxelWorld world = TestTerrain.Empty(block.ChunkPosition);
        world.SetBlock(block, BlockTypes.Stone);
        Aabb box = PlayerState.Spawn(new Vector3(500000.5f, 1f, 1f)).Bounds;

        Assert.True(VoxelCollision.Collides(world, TestTerrain.Bounds, box));
    }

    [Fact]
    public void OverlapsLiquid_OverlapsWaterAtLargeX_FindsLiquidBlock()
    {
        BlockPosition block = new(500000, 1, 0);
        VoxelWorld world = TestTerrain.Empty(block.ChunkPosition);
        world.SetBlock(block, BlockTypes.Water);
        Aabb box = PlayerState.Spawn(new Vector3(500000.5f, 1f, 1f)).Bounds;

        Assert.True(VoxelCollision.OverlapsLiquid(world, box));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlapsLiquid_AtLargeX_RequiresPositiveOverlap(bool overlaps)
    {
        BlockPosition block = new(500000, 1, 0);
        VoxelWorld world = TestTerrain.Empty(block.ChunkPosition);
        world.SetBlock(block, BlockTypes.Water);
        float edge = overlaps ? MathF.BitIncrement(500000f) : 500000f;
        Aabb box = new(new Vector3(499999.5f, 1f, 0.25f), new Vector3(edge, 1.75f, 0.75f));

        Assert.Equal(overlaps, VoxelCollision.OverlapsLiquid(world, box));
    }

    [Fact]
    public void Move_AgainstWallWithNarrowOverlapAtLargeX_StopsOnFace()
    {
        BlockPosition block = new(500000, 1, 2);
        VoxelWorld world = TestTerrain.Empty(block.ChunkPosition, block.ChunkPosition.Offset(-1, 0, 0));
        world.SetBlock(block, BlockTypes.Stone);
        Aabb box = PlayerState.Spawn(new Vector3(499999.9f, 1f, 1.5f)).Bounds;

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(0f, 0f, 1f));

        Assert.Equal(0.2f, allowed.Z, 3);
    }

    [Fact]
    public void Move_FallOntoFloor_StopsOnTopFace()
    {
        VoxelWorld world = TestTerrain.Flat();
        Aabb box = PlayerState.Spawn(new Vector3(0.5f, 3f, 0.5f)).Bounds;

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(0f, -5f, 0f));

        Assert.Equal(-2f, allowed.Y, 3);
    }

    [Fact]
    public void Move_LargeDelta_DoesNotTunnelThroughBlock()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Stone, (5, 1, 0), (5, 1, 0));
        Aabb box = PlayerState.Spawn(new Vector3(0.5f, 1f, 0.5f)).Bounds;

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(50f, 0f, 0f));

        Assert.Equal(4.2f, allowed.X, 3);
    }

    [Fact]
    public void Move_IntoUnloadedChunkInsideBounds_IsBlocked()
    {
        VoxelWorld world = new();
        world.LoadChunk(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);
        Aabb box = PlayerState.Spawn(new Vector3(15.5f, 1f, 8.5f)).Bounds;

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(5f, 0f, 0f));

        Assert.Equal(0.2f, allowed.X, 3);
    }

    [Fact]
    public void Move_BoxAlreadyInsideBlock_CanLeave()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Stone, (0, 1, 0), (0, 1, 0));
        Aabb box = PlayerState.Spawn(new Vector3(0.5f, 1f, 0.5f)).Bounds;

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(2f, 0f, 0f));

        Assert.Equal(2f, allowed.X, 3);
    }

    [Fact]
    public void Move_BoxOneFloatStepIntoWallFarFromOrigin_StaysBlocked()
    {
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(187, 0, 0));
        world.SetBlock(new BlockPosition(3000, 1, 8), BlockTypes.Stone);

        // Rounding the position can leave the box's far face one float step inside the wall; it must still not pass.
        Aabb box = new(new Vector3(2999.4f, 1f, 8.2f), new Vector3(MathF.BitIncrement(3000f), 2.8f, 8.8f));

        Vector3 allowed = VoxelCollision.Move(world, TestTerrain.Bounds, box, new Vector3(1f, 0f, 0f));

        Assert.Equal(0f, allowed.X);
    }

    [Fact]
    public void Move_BoxTwoFloatStepsIntoCeilingHighUp_StaysBlocked()
    {
        WorldBounds tall = new(0, 255);
        VoxelWorld world = TestTerrain.Empty(new ChunkPosition(0, 9, 0));
        world.SetBlock(new BlockPosition(8, 152, 8), BlockTypes.Stone);
        Aabb box = new(new Vector3(8.2f, 150.2f, 8.2f), new Vector3(8.8f, MathF.BitIncrement(MathF.BitIncrement(152f)), 8.8f));

        Vector3 allowed = VoxelCollision.Move(world, tall, box, new Vector3(0f, 1f, 0f));

        Assert.Equal(0f, allowed.Y);
    }
}
