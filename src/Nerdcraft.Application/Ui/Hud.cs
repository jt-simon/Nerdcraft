// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Items;

namespace Nerdcraft.Application.Ui;

/// <summary>One hotbar slot, ready to draw.</summary>
/// <param name="Bounds">Where the slot sits, in window pixels.</param>
/// <param name="Icon">Name of the texture that depicts the block held (see <see cref="BlockPalette.Atlas"/>), or <see langword="null"/> when the slot is empty. A block with no texture gets a name the atlas does not know, which draws the magenta missing tile.</param>
/// <param name="Count">Blocks in the slot; 0 when empty.</param>
/// <param name="Selected">True for the slot the player will place from.</param>
public readonly record struct HotbarSlotView(UiRect Bounds, string? Icon, int Count, bool Selected);

/// <summary>What a renderer draws for the heads-up display.</summary>
/// <param name="Crosshair">True when a crosshair is drawn at the center of the screen.</param>
/// <param name="Hotbar">The hotbar slots, left to right.</param>
/// <param name="HoverText">Text to show above the selected slot, or <see langword="null"/> for none. Always <see langword="null"/> when <paramref name="HoverOpacity"/> is 0.</param>
/// <param name="HoverOpacity">Opacity for the hover text in [0, 1]; 0 means hidden. Always 0 when <paramref name="HoverText"/> is <see langword="null"/>.</param>
public readonly record struct HudView(bool Crosshair, IReadOnlyList<HotbarSlotView> Hotbar, string? HoverText, float HoverOpacity);

/// <summary>Layout of the heads-up display. Pure arithmetic, so every renderer shows the same HUD.</summary>
public static class Hud
{
    private const string MissingIcon = "missing";
    private const float SlotSize = 44f;
    private const float Gap = 4f;
    private const float BottomMargin = 12f;

    /// <summary>Total time the hover text stays visible.</summary>
    public const float HoverDuration = 3f;

    /// <summary>Time during which the hover text stays at full opacity before fading.</summary>
    public const float HoverFullOpacityDuration = 2f;

    /// <summary>Computes the hover text opacity from the remaining timer.</summary>
    /// <param name="timer">Seconds remaining.</param>
    /// <returns>Opacity in [0, 1]; full for the first <see cref="HoverFullOpacityDuration"/> seconds, then linear fade over the last second.</returns>
    public static float ComputeHoverOpacity(float timer)
    {
        if (timer <= 0f)
        {
            return 0f;
        }

        float fadeDuration = HoverDuration - HoverFullOpacityDuration; // 1f
        if (timer > fadeDuration)
        {
            return 1f; // Full opacity for the first HoverFullOpacityDuration seconds
        }

        // Linear fade over the last fadeDuration seconds
        return timer / fadeDuration;
    }

    /// <summary>Lays the hotbar out centered along the bottom of the window.</summary>
    /// <param name="screenSize">Window size in pixels. Hosts must supply it; a zero size puts the hotbar off-screen.</param>
    /// <param name="inventory">The inventory to show.</param>
    /// <param name="crosshair">Whether the crosshair is drawn.</param>
    /// <param name="hoverText">Text to show above the selected slot, or <see langword="null"/> for none.</param>
    /// <param name="hoverTimer">Seconds remaining for the hover text; controls fade-out.</param>
    public static HudView Layout(Vector2 screenSize, Inventory inventory, bool crosshair, string? hoverText = null, float hoverTimer = 0f)
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
                stack.IsEmpty ? null : BlockPalette.GetTexture(stack.Block, ChunkFace.PositiveZ) ?? MissingIcon,
                stack.IsEmpty ? 0 : stack.Count,
                i == inventory.SelectedSlot);
        }

        float hoverOpacity = hoverText is null ? 0f : ComputeHoverOpacity(hoverTimer);
        return new HudView(crosshair, slots, hoverOpacity > 0f ? hoverText : null, hoverOpacity);
    }
}
