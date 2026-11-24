// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Items;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.Tests.Support;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.Entities;

public class BlockInteractionTests
{
    private static readonly Vector3 Eye = new(8.5f, 2.62f, 8.5f);

    private static BlockActionResult Apply(VoxelWorld world, Inventory inventory, BlockAction action, params Aabb[] occupied)
        => BlockInteraction.Apply(world, inventory, Eye, occupied, action);

    [Fact]
    public void Apply_Break_SetsAirAndAddsBlockToInventory()
    {
        VoxelWorld world = TestTerrain.Flat();
        Inventory inventory = new();
        BlockPosition target = new(8, 0, 8);

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Break, target));

        Assert.Equal(BlockActionResult.Applied, result);
        Assert.Equal(BlockId.Air, world.GetBlock(target));
        Assert.Equal(new ItemStack(BlockTypes.Stone, 1), inventory.GetSlot(0));
    }

    [Fact]
    public void Apply_BreakBedrock_RefusesAndChangesNothing()
    {
        VoxelWorld world = TestTerrain.Flat();
        BlockPosition target = new(8, 0, 8);
        world.SetBlock(target, BlockTypes.Bedrock);
        Inventory inventory = new();

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Break, target));

        Assert.Equal(BlockActionResult.Unbreakable, result);
        Assert.Equal(BlockTypes.Bedrock, world.GetBlock(target));
        Assert.True(inventory.GetSlot(0).IsEmpty);
    }

    [Fact]
    public void Apply_BeyondReach_Refuses()
    {
        VoxelWorld world = TestTerrain.Flat();
        BlockPosition target = new(8, 0, 8);

        BlockActionResult result = BlockInteraction.Apply(
            world, new Inventory(), new Vector3(8.5f, 10f, 8.5f), [], new BlockAction(BlockActionKind.Break, target));

        Assert.Equal(BlockActionResult.OutOfReach, result);
        Assert.Equal(BlockTypes.Stone, world.GetBlock(target));
    }

    [Fact]
    public void Apply_Place_TakesOneAndSetsBlock()
    {
        VoxelWorld world = TestTerrain.Flat();
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Dirt);
        inventory.TryAdd(BlockTypes.Dirt);
        BlockPosition cell = new(8, 1, 8);

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Place, cell, Slot: 0));

        Assert.Equal(BlockActionResult.Applied, result);
        Assert.Equal(BlockTypes.Dirt, world.GetBlock(cell));
        Assert.Equal(1, inventory.GetSlot(0).Count);
    }

    [Fact]
    public void Apply_PlaceIntoWater_ReplacesIt()
    {
        VoxelWorld world = TestTerrain.Flat();
        BlockPosition cell = new(8, 1, 8);
        world.SetBlock(cell, BlockTypes.Water);
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Sand);

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Place, cell));

        Assert.Equal(BlockActionResult.Applied, result);
        Assert.Equal(BlockTypes.Sand, world.GetBlock(cell));
    }

    [Fact]
    public void Apply_PlaceIntersectingEntity_RefusesAndKeepsItem()
    {
        VoxelWorld world = TestTerrain.Flat();
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Dirt);
        BlockPosition cell = new(8, 1, 8);
        Aabb player = new(new Vector3(8.2f, 1f, 8.2f), new Vector3(8.8f, 2.8f, 8.8f));

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Place, cell), player);

        Assert.Equal(BlockActionResult.Occupied, result);
        Assert.Equal(BlockId.Air, world.GetBlock(cell));
        Assert.Equal(1, inventory.GetSlot(0).Count);
    }

    [Fact]
    public void Apply_PlaceWithoutSolidNeighbor_Refuses()
    {
        VoxelWorld world = TestTerrain.Flat();
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Dirt);

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Place, new BlockPosition(8, 4, 8)));

        Assert.Equal(BlockActionResult.NoSupport, result);
        Assert.Equal(1, inventory.GetSlot(0).Count);
    }

    [Fact]
    public void Apply_PlaceOnSolidBlock_Refuses()
    {
        VoxelWorld world = TestTerrain.Flat();
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Dirt);

        BlockActionResult result = Apply(world, inventory, new BlockAction(BlockActionKind.Place, new BlockPosition(8, 0, 8)));

        Assert.Equal(BlockActionResult.NotReplaceable, result);
        Assert.Equal(BlockTypes.Stone, world.GetBlock(new BlockPosition(8, 0, 8)));
    }

    [Fact]
    public void Apply_PlaceFromEmptySlot_Refuses()
    {
        VoxelWorld world = TestTerrain.Flat();
        BlockPosition cell = new(8, 1, 8);

        BlockActionResult result = Apply(world, new Inventory(), new BlockAction(BlockActionKind.Place, cell, Slot: 3));

        Assert.Equal(BlockActionResult.EmptySlot, result);
        Assert.Equal(BlockId.Air, world.GetBlock(cell));
    }

    [Fact]
    public void Apply_BlockInUnloadedChunk_Refuses()
    {
        VoxelWorld world = TestTerrain.Flat();

        BlockActionResult result = Apply(world, new Inventory(), new BlockAction(BlockActionKind.Break, new BlockPosition(8, 100, 8)));

        Assert.Equal(BlockActionResult.NotLoaded, result);
    }
}
