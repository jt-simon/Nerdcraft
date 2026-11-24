// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Items;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Player;

/// <summary>
/// Turns break, place and hotbar input into <see cref="BlockAction"/>s for the local player and applies them with
/// <see cref="BlockInteraction.Apply"/>. It owns the player's <see cref="Inventory"/> and the repeat timer.
/// </summary>
public sealed class BlockInteractor(VoxelWorld world)
{
    /// <summary>Seconds between repeats while break or place stays held.</summary>
    public const float RepeatSeconds = 0.25f;

    private float _cooldown;
    private bool _waitForRelease;

    /// <summary>The local player's hotbar.</summary>
    public Inventory Inventory { get; } = new();

    /// <summary>
    /// Applies hotbar selection, then acts on the block the player is looking at. Break wins over place when both are held.
    /// A press acts at once and a held button repeats every <see cref="RepeatSeconds"/>. A refused action is dropped.
    /// </summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    /// <param name="player">The player acting; its target, eye and collision box are used.</param>
    public void Update(InputState input, float deltaSeconds, LocalPlayer player)
    {
        if (input.SelectSlot is { } slot)
        {
            Inventory.Select(slot);
        }

        if (input.HotbarScroll != 0)
        {
            Inventory.Scroll(input.HotbarScroll);
        }

        bool breaking = input.Break;
        bool placing = input.Place && !breaking;
        if (_waitForRelease)
        {
            if (input.Break || input.Place)
            {
                return;
            }

            _waitForRelease = false;
        }

        if (!breaking && !placing)
        {
            _cooldown = 0f;
            return;
        }

        _cooldown -= deltaSeconds;
        if (_cooldown > 0f || player.Target() is not { } hit)
        {
            return;
        }

        BlockAction action;
        if (breaking)
        {
            action = new BlockAction(BlockActionKind.Break, hit.Block);
        }
        else if (hit.Normal == Vector3.Zero)
        {
            return;
        }
        else
        {
            BlockPosition cell = new(
                hit.Block.X + (int)hit.Normal.X, hit.Block.Y + (int)hit.Normal.Y, hit.Block.Z + (int)hit.Normal.Z);
            action = new BlockAction(BlockActionKind.Place, cell, Inventory.SelectedSlot);
        }

        BlockActionResult result = BlockInteraction.Apply(world, Inventory, player.EyePosition, [player.State.Bounds], action);
        if (result == BlockActionResult.Applied)
        {
            _cooldown = RepeatSeconds;
        }
    }

    /// <summary>Makes a button that is held now do nothing until it is released, so a click that closed a menu cannot act on the world.</summary>
    public void ClearPendingInput()
    {
        _cooldown = 0f;
        _waitForRelease = true;
    }
}
