// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;

using Raylib_cs;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Reads raylib keyboard and mouse state into an <see cref="InputState"/>. Owns the key bindings: WASD move, Space jump, Left Shift sneak, F toggle flight, N toggle noclip, F3 debug overlay, Esc pause menu (mouse picks its buttons). F11 maximize is handled by <see cref="RaylibWindow"/>.</summary>
public static class RaylibInput
{
    /// <summary>Samples the current input. Call once per frame on the window's thread.</summary>
    public static InputState Read()
    {
        Vector2 move = new(
            Axis(KeyboardKey.D, KeyboardKey.A),
            Axis(KeyboardKey.W, KeyboardKey.S));

        return new InputState(
            move,
            Raylib_cs.Raylib.GetMouseDelta(),
            Raylib_cs.Raylib.IsKeyDown(KeyboardKey.Space),
            Raylib_cs.Raylib.IsKeyDown(KeyboardKey.LeftShift),
            Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.F3),
            Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.F),
            Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.N),
            Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.Escape),
            // While the cursor is captured raylib reports a virtual position; park it off-screen so no button looks hovered.
            Raylib_cs.Raylib.IsCursorHidden() ? new Vector2(-1f, -1f) : Raylib_cs.Raylib.GetMousePosition(),
            Raylib_cs.Raylib.IsMouseButtonPressed(MouseButton.Left),
            new Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight()));
    }

    private static float Axis(KeyboardKey positive, KeyboardKey negative)
        => (Raylib_cs.Raylib.IsKeyDown(positive) ? 1f : 0f) - (Raylib_cs.Raylib.IsKeyDown(negative) ? 1f : 0f);
}
