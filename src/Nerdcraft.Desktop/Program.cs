// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application;
using Nerdcraft.Persistence.Sqlite;
using Nerdcraft.Rendering.Raylib;

// Composition root: the only place the game core, the world storage and a rendering library meet.
// The loop runs on the main thread, which owns the graphics context; background workers never touch it.
string worldDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nerdcraft", "worlds", "default");
GameSettings defaults = new();
SqliteWorld world;

// Ctrl+C asks the game to quit like Esc > Quit does, so edits are saved. While the world opens (the integrity check can take a
// while) it cancels the open instead.
using CancellationTokenSource interrupt = new();
ConsoleCancelEventHandler onInterrupt = (_, e) =>
{
    e.Cancel = true;
    interrupt.Cancel();
};
Console.CancelKeyPress += onInterrupt;
try
{
    try
    {
        world = await SqliteWorld.OpenAsync(worldDirectory, defaults.Seed, defaults.Bounds, cancellationToken: interrupt.Token);
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Startup cancelled.");
        return 1;
    }

    // A saved world keeps the seed and height range it was created with.
    NerdcraftGame game = NerdcraftGame.Create(defaults with { Seed = world.Seed, Bounds = world.Bounds }, world.Chunks(0));

    int exitCode = Guard(() =>
    {
        using RaylibWindow window = new("Nerdcraft", 1280, 720);
        using RaylibRenderer renderer = new();
        while (!window.ShouldClose && !game.ExitRequested && !interrupt.IsCancellationRequested)
        {
            // Clamp so a hitch (window drag, debugger) doesn't fling the camera. HUD timers still get the uncapped frame time.
            game.Update(RaylibInput.Read(), Math.Min(window.FrameSeconds, 0.1f), window.FrameSeconds);
            window.Update(captureCursor: !game.IsPaused);
            game.Render(renderer);
        }
    });

    // Whatever happened above, the game saves its edits and stops its workers first; only then is the world closed.
    exitCode |= await GuardAsync(game.DisposeAsync);
    exitCode |= await GuardAsync(world.DisposeAsync);
    return exitCode;
}
finally
{
    Console.CancelKeyPress -= onInterrupt;
}

// Runs the game; a failure is reported (console and crash.log) instead of ending in an unhandled-exception dump. Returns 1 on failure.
int Guard(Action action)
{
    try
    {
        action();
        return 0;
    }
    catch (Exception exception)
    {
        return Report(exception);
    }
}

async Task<int> GuardAsync(Func<ValueTask> action)
{
    try
    {
        await action();
        return 0;
    }
    catch (Exception exception)
    {
        return Report(exception);
    }
}

// Prints a short message on the console and writes the full details to crash.log in the world's folder, which is
// still there when the game was started without a terminal.
int Report(Exception exception)
{
    string log = Path.Combine(worldDirectory, "crash.log");
    Console.Error.WriteLine($"Nerdcraft stopped because of an error: {exception.Message}");
    try
    {
        File.AppendAllText(log, $"[{DateTimeOffset.UtcNow:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        Console.Error.WriteLine($"Details were written to {log}");
    }
    catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"The details could not be written to {log}: {logFailure.Message}{Environment.NewLine}{exception}");
    }

    return 1;
}
