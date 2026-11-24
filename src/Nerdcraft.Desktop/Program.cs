// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application;
using Nerdcraft.Rendering.Raylib;

// Composition root: the only place the game core and a rendering library meet.
// The loop runs on the main thread, which owns the graphics context; background workers never touch it.
NerdcraftGame game = NerdcraftGame.Create(new GameSettings());

try
{
    using RaylibWindow window = new("Nerdcraft", 1280, 720);
    using RaylibRenderer renderer = new();
    while (!window.ShouldClose && !game.ExitRequested)
    {
        // Clamp so a hitch (window drag, debugger) doesn't fling the camera. HUD timers still get the uncapped frame time.
        game.Update(RaylibInput.Read(), Math.Min(window.FrameSeconds, 0.1f), window.FrameSeconds);
        window.Update(captureCursor: !game.IsPaused);
        game.Render(renderer);
    }
}
finally
{
    await game.DisposeAsync();
}
