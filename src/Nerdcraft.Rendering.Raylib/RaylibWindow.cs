// SPDX-License-Identifier: AGPL-3.0-or-later

using Raylib_cs;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Owns the raylib window and its graphics context. Esc does not close it; F11 toggles maximize.</summary>
public sealed class RaylibWindow : IDisposable
{
    private bool _cursorCaptured = true;

    /// <summary>Opens a resizable, vsynced window and captures the cursor for mouse look. Disables raylib's Esc-to-close.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="width">Initial width in pixels.</param>
    /// <param name="height">Initial height in pixels.</param>
    /// <param name="targetFps">Frame rate cap.</param>
    public RaylibWindow(string title, int width, int height, int targetFps = 60)
    {
        Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
        Raylib_cs.Raylib.SetExitKey(KeyboardKey.Null);
        Raylib_cs.Raylib.DisableCursor();
    }

    /// <summary>True once the user has asked to close the window (the close button).</summary>
    public bool ShouldClose => Raylib_cs.Raylib.WindowShouldClose();

    /// <summary>Duration of the previous frame, in seconds.</summary>
    public float FrameSeconds => Raylib_cs.Raylib.GetFrameTime();

    /// <summary>Applies window-level input once per frame: F11 toggles maximize, and the cursor is captured or released to match.</summary>
    /// <param name="captureCursor">True to hide and lock the cursor for mouse look; false to free it for menus.</param>
    public void Update(bool captureCursor)
    {
        if (Raylib_cs.Raylib.IsKeyPressed(KeyboardKey.F11))
        {
            if (Raylib_cs.Raylib.IsWindowMaximized())
            {
                Raylib_cs.Raylib.RestoreWindow();
            }
            else
            {
                Raylib_cs.Raylib.MaximizeWindow();
            }
        }

        if (captureCursor == _cursorCaptured)
        {
            return;
        }

        _cursorCaptured = captureCursor;
        if (captureCursor)
        {
            Raylib_cs.Raylib.DisableCursor();
        }
        else
        {
            Raylib_cs.Raylib.EnableCursor();
        }
    }

    /// <summary>Closes the window and releases the graphics context.</summary>
    public void Dispose() => Raylib_cs.Raylib.CloseWindow();
}
