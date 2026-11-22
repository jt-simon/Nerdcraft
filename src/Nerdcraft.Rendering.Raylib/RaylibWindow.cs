// SPDX-License-Identifier: AGPL-3.0-or-later

using Raylib_cs;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Owns the raylib window and its graphics context. Esc closes it.</summary>
public sealed class RaylibWindow : IDisposable
{
    /// <summary>Opens a resizable, vsynced window and captures the cursor for mouse look.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="width">Initial width in pixels.</param>
    /// <param name="height">Initial height in pixels.</param>
    /// <param name="targetFps">Frame rate cap.</param>
    public RaylibWindow(string title, int width, int height, int targetFps = 60)
    {
        Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
        Raylib_cs.Raylib.DisableCursor();
    }

    /// <summary>True once the user has asked to close the window (Esc or the close button).</summary>
    public bool ShouldClose => Raylib_cs.Raylib.WindowShouldClose();

    /// <summary>Duration of the previous frame, in seconds.</summary>
    public float FrameSeconds => Raylib_cs.Raylib.GetFrameTime();

    /// <summary>Closes the window and releases the graphics context.</summary>
    public void Dispose() => Raylib_cs.Raylib.CloseWindow();
}
