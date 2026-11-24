// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Items;

namespace Nerdcraft.Application.Ui;

/// <summary>One hotbar slot, ready to draw.</summary>
/// <param name="Bounds">Where the slot sits, in window pixels.</param>
/// <param name="Swatch">Color of the block held, or <see langword="null"/> when the slot is empty.</param>
/// <param name="Count">Blocks in the slot; 0 when empty.</param>
/// <param name="Selected">True for the slot the player will place from.</param>
public readonly record struct HotbarSlotView(UiRect Bounds, Color? Swatch, int Count, bool Selected);

/// <summary>What a renderer draws for the heads-up display.</summary>
/// <param name="Crosshair">True when a crosshair is drawn at the center of the screen.</param>
/// <param name="Hotbar">The hotbar slots, left to right.</param>
public readonly record struct HudView(bool Crosshair, IReadOnlyList<HotbarSlotView> Hotbar);

/// <summary>Layout of the heads-up display. Pure arithmetic, so every renderer shows the same HUD.</summary>
public static class Hud
{
    private const float SlotSize = 44f;
    private const float Gap = 4f;
    private const float BottomMargin = 12f;

    /// <summary>Lays the hotbar out centered along the bottom of the window.</summary>
    /// <param name="screenSize">Window size in pixels. Hosts must supply it; a zero size puts the hotbar off-screen.</param>
    /// <param name="inventory">The inventory to show.</param>
    /// <param name="crosshair">Whether the crosshair is drawn.</param>
    public static HudView Layout(Vector2 screenSize, Inventory inventory, bool crosshair)
    {
        float totalWidth = (Inventory.SlotCount * SlotSize) + ((Inventory.SlotCount - 1) * Gap);

        // Whole pixels, so every renderer draws the same rectangles.
        float left = MathF.Floor((screenSize.X - totalWidth) / 2f);
        float top = MathF.Floor(screenSize.Y - BottomMargin - SlotSize);

        HotbarSlotView[] slots = new HotbarSlotView[Inventory.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack stack = inventory.GetSlot(i);
            UiRect bounds = new(left + (i * (SlotSize + Gap)), top, SlotSize, SlotSize);
            slots[i] = new HotbarSlotView(
                bounds,
                stack.IsEmpty ? null : BlockPalette.GetColor(stack.Block),
                stack.IsEmpty ? 0 : stack.Count,
                i == inventory.SelectedSlot);
        }

        return new HudView(crosshair, slots);
    }
}
