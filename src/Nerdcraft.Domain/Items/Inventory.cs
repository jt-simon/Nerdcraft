// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.Items;

/// <summary>A stack of one block type held in an inventory slot.</summary>
/// <param name="Block">The block type; air marks an empty slot.</param>
/// <param name="Count">How many blocks are in the stack; 0 for an empty slot.</param>
public readonly record struct ItemStack(BlockId Block, int Count)
{
    /// <summary>True when the slot holds nothing.</summary>
    public bool IsEmpty => Count <= 0 || Block.IsAir;
}

/// <summary>
/// A player's hotbar: <see cref="SlotCount"/> slots of up to <see cref="MaxStack"/> blocks each, and one selected slot.
/// It is plain per-player state with no device or UI dependency, so a server can hold one per player. Not thread-safe.
/// </summary>
public sealed class Inventory
{
    /// <summary>Number of slots.</summary>
    public const int SlotCount = 9;

    /// <summary>Most blocks one slot holds.</summary>
    public const int MaxStack = 64;

    private readonly ItemStack[] _slots = new ItemStack[SlotCount];

    /// <summary>Index of the selected slot, in [0, <see cref="SlotCount"/>).</summary>
    public int SelectedSlot { get; private set; }

    /// <summary>Returns the stack in <paramref name="slot"/>; an empty stack when the slot is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside [0, <see cref="SlotCount"/>).</exception>
    public ItemStack GetSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotCount);
        return _slots[slot];
    }

    /// <summary>Selects <paramref name="slot"/>; an index outside the hotbar is ignored.</summary>
    public void Select(int slot)
    {
        if (slot >= 0 && slot < SlotCount)
        {
            SelectedSlot = slot;
        }
    }

    /// <summary>Moves the selection by <paramref name="steps"/> slots (positive is the next slot), wrapping around the ends.</summary>
    public void Scroll(int steps) => SelectedSlot = (((SelectedSlot + steps) % SlotCount) + SlotCount) % SlotCount;

    /// <summary>Adds one block, topping up a matching stack that has room before using the first empty slot.</summary>
    /// <returns>False, changing nothing, when <paramref name="block"/> is air or every slot is full of other blocks.</returns>
    public bool TryAdd(BlockId block)
    {
        if (block.IsAir)
        {
            return false;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!_slots[i].IsEmpty && _slots[i].Block == block && _slots[i].Count < MaxStack)
            {
                _slots[i] = new ItemStack(block, _slots[i].Count + 1);
                return true;
            }
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i].IsEmpty)
            {
                _slots[i] = new ItemStack(block, 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes one block from <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot to take from.</param>
    /// <param name="block">The block taken; air when nothing was.</param>
    /// <returns>False when the slot is empty or outside the hotbar.</returns>
    public bool TryTakeOne(int slot, out BlockId block)
    {
        block = BlockId.Air;
        if (slot < 0 || slot >= SlotCount || _slots[slot].IsEmpty)
        {
            return false;
        }

        ItemStack stack = _slots[slot];
        block = stack.Block;
        _slots[slot] = stack.Count == 1 ? default : stack with { Count = stack.Count - 1 };
        return true;
    }
}
