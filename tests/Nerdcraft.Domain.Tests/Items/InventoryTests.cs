// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Items;

namespace Nerdcraft.Domain.Tests.Items;

public class InventoryTests
{
    [Fact]
    public void TryAdd_MatchingStack_FillsItBeforeAnEmptySlot()
    {
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Dirt);
        inventory.TryAdd(BlockTypes.Stone);

        inventory.TryAdd(BlockTypes.Dirt);

        Assert.Equal(new ItemStack(BlockTypes.Dirt, 2), inventory.GetSlot(0));
        Assert.Equal(new ItemStack(BlockTypes.Stone, 1), inventory.GetSlot(1));
        Assert.True(inventory.GetSlot(2).IsEmpty);
    }

    [Fact]
    public void TryAdd_FullStack_StartsANewSlot()
    {
        Inventory inventory = new();
        for (int i = 0; i < Inventory.MaxStack + 1; i++)
        {
            inventory.TryAdd(BlockTypes.Dirt);
        }

        Assert.Equal(Inventory.MaxStack, inventory.GetSlot(0).Count);
        Assert.Equal(1, inventory.GetSlot(1).Count);
    }

    [Fact]
    public void TryAdd_AllSlotsFull_ReturnsFalse()
    {
        Inventory inventory = new();
        for (ushort i = 1; i <= Inventory.SlotCount; i++)
        {
            Assert.True(inventory.TryAdd(new BlockId(i)));
        }

        Assert.False(inventory.TryAdd(new BlockId(100)));
    }

    [Fact]
    public void TryTakeOne_LastBlock_EmptiesSlot()
    {
        Inventory inventory = new();
        inventory.TryAdd(BlockTypes.Sand);

        Assert.True(inventory.TryTakeOne(0, out BlockId taken));

        Assert.Equal(BlockTypes.Sand, taken);
        Assert.True(inventory.GetSlot(0).IsEmpty);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(8, 1, 0)]
    [InlineData(0, -1, 8)]
    [InlineData(2, -12, 8)]
    public void Scroll_PastEitherEnd_Wraps(int start, int steps, int expected)
    {
        Inventory inventory = new();
        inventory.Select(start);

        inventory.Scroll(steps);

        Assert.Equal(expected, inventory.SelectedSlot);
    }
}
