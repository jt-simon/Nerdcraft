// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;

using Raylib_cs;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Reads raylib keyboard and mouse state into an <see cref="InputState"/>. Owns the key bindings: WASD, Space/Left Ctrl, Left Shift, F3.</summary>
public static class RaylibInput
{
    /// <summary>Samples the current input. Call once per frame on the window's thread.</summary>
    public static InputState Read()
    {
        Vector3 move = new(
            Axis(KeyboardKey.D, KeyboardKey.A),
            Axis(KeyboardKey.Space, KeyboardKey.LeftControl),
            Axis(KeyboardKey.W, KeyboardKey.S));

        return new InputState(
            move,
            Raylib_cs.Raylib.GetMouseDelta(),
            Raylib_cs.Raylib.IsKeyDown(KeyboardKey.LeftShift),
            Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.F3));
    }

    private static float Axis(KeyboardKey positive, KeyboardKey negative)
        => (Raylib_cs.Raylib.IsKeyDown(positive) ? 1f : 0f) - (Raylib_cs.Raylib.IsKeyDown(negative) ? 1f : 0f);
}
