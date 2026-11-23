// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Application.Input;

/// <summary>Per-frame input snapshot. Adapters fill it in; key bindings live in adapters.</summary>
/// <param name="Move">X = strafe right, Y = up, Z = forward; each in [-1, 1].</param>
/// <param name="LookDelta">Pointer movement since the last frame, in pixels.</param>
/// <param name="Sprint">True while the sprint modifier is held.</param>
/// <param name="ToggleDebug">True only on the frame the debug overlay toggle was pressed.</param>
public readonly record struct InputState(Vector3 Move, Vector2 LookDelta, bool Sprint, bool ToggleDebug = false);
