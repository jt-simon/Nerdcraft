// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Player;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application;

/// <summary>
/// The game as seen by a host. The host owns the main loop and calls <see cref="Update"/> then
/// <see cref="Render"/> once per frame, on the thread that owns the graphics context.
/// </summary>
public sealed class NerdcraftGame : IAsyncDisposable
{
    private static readonly Color SkyColor = new(135, 190, 235);

    private readonly GameSettings _settings;
    private readonly LocalPlayer _player;
    private readonly ChunkStreamer _streamer;
    private readonly IAsyncDisposable? _workQueue;
    private readonly float _farPlane;
    private bool _showDebug;
    private int _ignoreLookFrames;
    private Vector2 _screenSize;
    private Vector2 _pointer;

    /// <summary>Creates a game over the supplied work queue and world. If <paramref name="workQueue"/> is <see cref="IAsyncDisposable"/>, this game disposes it.</summary>
    /// <param name="settings">Configuration for streaming and the camera.</param>
    /// <param name="workQueue">Background worker port used for terrain generation and meshing.</param>
    /// <param name="world">The world to populate and draw.</param>
    /// <param name="spawnHeight">World Y of the player's feet at the start, above the block column at X = 8, Z = 8. Must clear the terrain there; the player holds still until the chunk under them has loaded, then falls.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public NerdcraftGame(GameSettings settings, IChunkWorkQueue workQueue, VoxelWorld world, float spawnHeight = 56f)
    {
        settings.Validate();
        _settings = settings;
        _workQueue = workQueue as IAsyncDisposable;
        _streamer = new ChunkStreamer(world, workQueue, settings);
        _player = new LocalPlayer(
            PlayerState.Spawn(new Vector3(8.5f, spawnHeight, 8.5f)), world, settings.Bounds, new PlayerAbilities(CanFly: true, CanNoclip: true));
        _farPlane = (settings.RenderDistance + 1) * Chunk.Size * 1.5f;
    }

    /// <summary>
    /// Creates a game with the default wiring: heightmap terrain, culled meshing, and a threaded <see cref="ChunkPipeline"/>.
    /// The player starts standing on the terrain surface, or on the sea if that is higher, at X = 8, Z = 8.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public static NerdcraftGame Create(GameSettings settings)
    {
        settings.Validate();
        HeightmapTerrainGenerator generator = new(settings.Seed, settings.Bounds);
        ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), settings.WorkerCount);
        return new NerdcraftGame(settings, pipeline, new VoxelWorld(), Math.Max(generator.SurfaceHeight(8, 8), HeightmapTerrainGenerator.SeaLevel) + 1);
    }

    /// <summary>True while the pause menu is open. The host should release the cursor then, and capture it otherwise.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True once the player chose Quit in the pause menu. The host should then stop its loop.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>Advances the player by <paramref name="deltaSeconds"/> and requests any chunks they now need. Does not touch the renderer.</summary>
    /// <param name="input">
    /// This frame's input. Toggles the debug overlay when <see cref="InputState.ToggleDebug"/> is set, and flight and noclip likewise.
    /// <see cref="InputState.TogglePause"/> opens or closes the pause menu; while it is open the player stands still, chunks keep
    /// streaming, and a <see cref="InputState.Click"/> on a button acts on it. Look input is ignored on the frame the menu closes and the two after it,
    /// because the host re-captures the cursor then and the pointer jumps.
    /// </param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    public void Update(InputState input, float deltaSeconds)
    {
        if (input.ToggleDebug)
        {
            _showDebug = !_showDebug;
        }

        _screenSize = input.ScreenSize;
        _pointer = input.Pointer;

        if (input.TogglePause)
        {
            SetPaused(!IsPaused);
        }
        else if (IsPaused && input.Click)
        {
            PauseMenuView menu = PauseMenu.Layout(_screenSize, _pointer);
            if (menu.Buttons[PauseMenu.ResumeIndex].Bounds.Contains(_pointer))
            {
                SetPaused(false);
            }
            else if (menu.Buttons[PauseMenu.QuitIndex].Bounds.Contains(_pointer))
            {
                ExitRequested = true;
            }
        }

        if (!IsPaused)
        {
            if (_ignoreLookFrames > 0)
            {
                _ignoreLookFrames--;
                input = input with { LookDelta = Vector2.Zero };
            }

            _player.Update(input, deltaSeconds);
        }

        _streamer.Update(_player.EyePosition);
    }

    /// <summary>Uploads finished meshes and draws one frame. Must run on the thread that owns the graphics context.</summary>
    public void Render(IRenderer renderer)
    {
        _streamer.UploadCompleted(renderer);

        CameraView view = _player.ToView(_settings.FovYDegrees, nearPlane: 0.1f, _farPlane);
        Frustum frustum = Frustum.FromViewProjection(view.CreateViewProjection(renderer.AspectRatio));

        renderer.BeginFrame(view, SkyColor);
        DrawStats stats = _streamer.Draw(renderer, frustum, view.Position);
        renderer.EndFrame(
            _showDebug ? new DebugOverlay(_player.EyePosition, stats) : null,
            IsPaused ? PauseMenu.Layout(_screenSize, _pointer) : null);
    }

    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        if (!paused)
        {
            _ignoreLookFrames = 3;
            _player.ClearPendingInput();
        }
    }

    /// <summary>Stops background workers (when the work queue is disposable) and waits for them to finish.</summary>
    public ValueTask DisposeAsync() => _workQueue?.DisposeAsync() ?? ValueTask.CompletedTask;
}
