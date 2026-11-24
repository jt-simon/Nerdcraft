// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Items;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Entities;

/// <summary>Why a <see cref="BlockAction"/> was or was not carried out.</summary>
public enum BlockActionResult
{
    /// <summary>The action was carried out.</summary>
    Applied,

    /// <summary>The chunk holding the block is not loaded.</summary>
    NotLoaded,

    /// <summary>The block is farther than <see cref="BlockInteraction.ReachBlocks"/> from the eye.</summary>
    OutOfReach,

    /// <summary>The block cannot be broken (air, water or bedrock).</summary>
    Unbreakable,

    /// <summary>The cell is already filled by a block that cannot be replaced.</summary>
    NotReplaceable,

    /// <summary>No solid block touches the cell, so a block placed there would float.</summary>
    NoSupport,

    /// <summary>An entity's box overlaps the cell.</summary>
    Occupied,

    /// <summary>The chosen hotbar slot holds nothing.</summary>
    EmptySlot,
}

/// <summary>
/// The rules for breaking and placing blocks. Pure and deterministic: a refused action changes nothing, so a server can run the
/// same code a client does and send back the <see cref="BlockActionResult"/> as the reason.
/// </summary>
public static class BlockInteraction
{
    /// <summary>How far, in blocks, from the eye a block can be broken or placed.</summary>
    public const float ReachBlocks = 5f;

    /// <summary>
    /// Checks <paramref name="action"/> and, only when every check passes, changes the world and inventory.
    /// Breaking sets the block to air and adds it to <paramref name="inventory"/>; when the inventory is full the block still
    /// breaks and the drop is lost. Placing takes one block from the action's slot and sets it at the position.
    /// </summary>
    /// <param name="world">The world to change.</param>
    /// <param name="inventory">The acting player's inventory.</param>
    /// <param name="eye">The player's eye position; reach is measured from it to the nearest point of the block.</param>
    /// <param name="occupied">Boxes of entities that a placed block must not overlap.</param>
    /// <param name="action">What the player asked for.</param>
    /// <returns><see cref="BlockActionResult.Applied"/>, or the first reason the action was refused.</returns>
    public static BlockActionResult Apply(
        VoxelWorld world, Inventory inventory, Vector3 eye, ReadOnlySpan<Aabb> occupied, BlockAction action)
    {
        BlockPosition position = action.Position;
        if (!world.IsLoaded(position.ChunkPosition))
        {
            return BlockActionResult.NotLoaded;
        }

        if (!InReach(eye, position))
        {
            return BlockActionResult.OutOfReach;
        }

        BlockId current = world.GetBlock(position);
        if (action.Kind == BlockActionKind.Break)
        {
            if (!BlockTypes.IsBreakable(current))
            {
                return BlockActionResult.Unbreakable;
            }

            world.SetBlock(position, BlockId.Air);
            inventory.TryAdd(current);
            return BlockActionResult.Applied;
        }

        if (action.Slot < 0 || action.Slot >= Inventory.SlotCount || inventory.GetSlot(action.Slot).IsEmpty)
        {
            return BlockActionResult.EmptySlot;
        }

        if (!BlockTypes.IsReplaceable(current))
        {
            return BlockActionResult.NotReplaceable;
        }

        if (!HasSolidNeighbor(world, position))
        {
            return BlockActionResult.NoSupport;
        }

        Vector3 corner = new(position.X, position.Y, position.Z);
        Aabb cell = new(corner, corner + Vector3.One);
        foreach (Aabb box in occupied)
        {
            if (box.Intersects(cell))
            {
                return BlockActionResult.Occupied;
            }
        }

        inventory.TryTakeOne(action.Slot, out BlockId block);
        world.SetBlock(position, block);
        return BlockActionResult.Applied;
    }

    private static bool InReach(Vector3 eye, BlockPosition position)
    {
        Vector3 min = new(position.X, position.Y, position.Z);
        Vector3 nearest = Vector3.Clamp(eye, min, min + Vector3.One);
        return Vector3.DistanceSquared(eye, nearest) <= ReachBlocks * ReachBlocks;
    }

    private static bool HasSolidNeighbor(VoxelWorld world, BlockPosition p)
        => BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X + 1, p.Y, p.Z)))
        || BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X - 1, p.Y, p.Z)))
        || BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X, p.Y + 1, p.Z)))
        || BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X, p.Y - 1, p.Z)))
        || BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X, p.Y, p.Z + 1)))
        || BlockTypes.IsSolid(world.GetBlock(new BlockPosition(p.X, p.Y, p.Z - 1)));
}
