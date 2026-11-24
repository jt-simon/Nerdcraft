// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Entities;

/// <summary>What a player asks to do to one block.</summary>
public enum BlockActionKind
{
    /// <summary>Remove the block at the position and collect it.</summary>
    Break,

    /// <summary>Put a block from a hotbar slot at the position.</summary>
    Place,
}

/// <summary>
/// A request to change one block. It holds no device or clock state, so a client can send it to a server, and both can feed it
/// to <see cref="BlockInteraction.Apply"/>.
/// </summary>
/// <param name="Kind">Whether to break or place.</param>
/// <param name="Position">The block to break, or the empty cell to fill.</param>
/// <param name="Slot">Hotbar slot a placed block comes from; ignored for <see cref="BlockActionKind.Break"/>. It is explicit so an authority never relies on its own copy of the selection being in sync.</param>
public readonly record struct BlockAction(BlockActionKind Kind, BlockPosition Position, int Slot = 0);
