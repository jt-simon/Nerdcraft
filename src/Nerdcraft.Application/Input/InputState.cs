// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Application.Input;

/// <summary>Per-frame input snapshot. Adapters fill it in; key bindings live in adapters.</summary>
/// <param name="Move">X = strafe right, Y = forward; each in [-1, 1].</param>
/// <param name="LookDelta">Pointer movement since the last frame, in pixels.</param>
/// <param name="Jump">True while the jump control is held; also swims and flies up.</param>
/// <param name="Sneak">True while the sneak control is held; also swims and flies down.</param>
/// <param name="ToggleDebug">True only on the frame the debug overlay toggle was pressed.</param>
/// <param name="ToggleFly">True only on the frame the flight toggle was pressed.</param>
/// <param name="ToggleNoclip">True only on the frame the noclip toggle was pressed.</param>
/// <param name="TogglePause">True only on the frame the pause control was pressed; opens or closes the pause menu.</param>
/// <param name="Pointer">Pointer position in pixels from the top-left of the window; used only by menus.</param>
/// <param name="Click">True only on the frame the primary pointer button was pressed.</param>
/// <param name="Break">True while the break control (primary button) is held.</param>
/// <param name="Place">True while the place control (secondary button) is held.</param>
/// <param name="SelectSlot">Hotbar slot chosen directly this frame, or <see langword="null"/>.</param>
/// <param name="HotbarScroll">Hotbar steps requested this frame; positive selects the next slot.</param>
/// <param name="ScreenSize">Window size in the same pixel space as <paramref name="Pointer"/>; used only to lay out menus. Hosts must fill it, or the pause menu cannot be clicked.</param>
public readonly record struct InputState(
    Vector2 Move,
    Vector2 LookDelta,
    bool Jump,
    bool Sneak,
    bool ToggleDebug = false,
    bool ToggleFly = false,
    bool ToggleNoclip = false,
    bool TogglePause = false,
    Vector2 Pointer = default,
    bool Click = false,
    Vector2 ScreenSize = default,
    bool Break = false,
    bool Place = false,
    int? SelectSlot = null,
    int HotbarScroll = 0);
