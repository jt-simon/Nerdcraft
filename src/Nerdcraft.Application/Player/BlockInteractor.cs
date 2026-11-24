// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Items;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Player;

/// <summary>
/// Turns break, place and hotbar input into <see cref="BlockAction"/>s for the local player and applies them with
/// <see cref="BlockInteraction.Apply"/>. It owns the player's <see cref="Inventory"/> and the repeat timer.
/// Also tracks the hotbar hover text shown when the selected slot changes.
/// </summary>
public sealed class BlockInteractor(VoxelWorld world)
{
    /// <summary>Seconds between repeats while break or place stays held.</summary>
    public const float RepeatSeconds = 0.25f;

    private float _cooldown;
    private bool _waitForRelease;
    private float _hoverTimer;

    /// <summary>The local player's hotbar.</summary>
    public Inventory Inventory { get; } = new();

    /// <summary>Current hover text to display above the selected slot, or <see langword="null"/> if none.</summary>
    /// <remarks>Resolved from the current stack while the timer is active, so it updates if the stack changes (e.g., placing the last block).</remarks>
    public string? HoverText
    {
        get
        {
            if (_hoverTimer <= 0f)
            {
                return null;
            }
            ItemStack stack = Inventory.GetSlot(Inventory.SelectedSlot);
            return stack.IsEmpty ? null : BlockPalette.GetDisplayName(stack.Block);
        }
    }

    /// <summary>Seconds the hover text stays visible, counting down from <see cref="Hud.HoverDuration"/> after a slot change; 0 when hidden.</summary>
    public float HoverTimer => _hoverTimer;

    /// <summary>
    /// Applies hotbar selection, then acts on the block the player is looking at. Break wins over place when both are held.
    /// A press acts at once and a held button repeats every <see cref="RepeatSeconds"/>. A refused action is dropped.
    /// Also starts the hover text when the selected slot changes.
    /// </summary>
    /// <remarks>The hover text fades on <see cref="UpdateHover"/>, so a host that clamps the simulation step can keep the HUD on real time.</remarks>
    /// <param name="input">This frame's input.</param>
    /// <param name="deltaSeconds">Simulation time since the previous frame, in seconds.</param>
    /// <param name="player">The player acting; its target, eye and collision box are used.</param>
    public void Update(InputState input, float deltaSeconds, LocalPlayer player)
    {
        int slotBeforeInput = Inventory.SelectedSlot;

        if (input.SelectSlot is { } slot)
        {
            Inventory.Select(slot);
        }

        if (input.HotbarScroll != 0)
        {
            Inventory.Scroll(input.HotbarScroll);
        }

        // A slot change starts the hover text; an empty slot has none, so it cancels any running one.
        if (Inventory.SelectedSlot != slotBeforeInput)
        {
            _hoverTimer = Inventory.GetSlot(Inventory.SelectedSlot).IsEmpty ? 0f : Hud.HoverDuration;
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

    /// <summary>Advances the hover text fade-out by real elapsed time.</summary>
    /// <remarks>
    /// Call once per frame, including while the game is paused, with the uncapped frame time. Then the advertised
    /// <see cref="Hud.HoverDuration"/> holds at any frame rate and the text keeps fading while <see cref="Update"/> is not called.
    /// </remarks>
    /// <param name="uiDeltaSeconds">Real time since the previous frame, in seconds, before any simulation clamp.</param>
    public void UpdateHover(float uiDeltaSeconds)
    {
        if (_hoverTimer > 0f)
        {
            _hoverTimer = MathF.Max(0f, _hoverTimer - uiDeltaSeconds);
        }
    }

    /// <summary>Makes a button that is held now do nothing until it is released, so a click that closed a menu cannot act on the world.</summary>
    public void ClearPendingInput()
    {
        _cooldown = 0f;
        _waitForRelease = true;
    }
}
