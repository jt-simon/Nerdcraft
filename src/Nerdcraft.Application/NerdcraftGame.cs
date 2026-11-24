// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Player;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application;

/// <summary>
/// The game as seen by a host. The host owns the main loop and calls <see cref="Update(InputState, float, float)"/> then
/// <see cref="Render"/> once per frame, on the thread that owns the graphics context.
/// </summary>
public sealed class NerdcraftGame : IAsyncDisposable
{
    private static readonly Histogram<double> FrameTime = StreamingMetrics.Meter.CreateHistogram<double>("nerdcraft.frame.duration", "ms");
    private static readonly Histogram<double> UpdateTime = StreamingMetrics.Meter.CreateHistogram<double>("nerdcraft.update.duration", "ms");
    private static readonly Histogram<double> UploadTime = StreamingMetrics.Meter.CreateHistogram<double>("nerdcraft.upload.duration", "ms");
    private static readonly Histogram<double> DrawTime = StreamingMetrics.Meter.CreateHistogram<double>("nerdcraft.draw.duration", "ms");

    private static readonly Color SkyColor = new(135, 190, 235);
    private static readonly Color OutlineColor = new(0, 0, 0, 160);

    // Grows the outline a little past the block so its lines do not fight the block's own faces for depth.
    private const float OutlineMargin = 0.002f;

    private readonly GameSettings _settings;
    private readonly LocalPlayer _player;
    private readonly BlockInteractor _interactor;
    private readonly ChunkStreamer _streamer;
    private readonly IAsyncDisposable? _workQueue;
    private readonly float _farPlane;
    private bool _showDebug;
    private int _ignoreLookFrames;
    private Vector2 _screenSize;
    private Vector2 _pointer;
    private long _previousFrame;
    private float _secondsSinceSave;

    /// <summary>Creates a game over the supplied work queue and world. If <paramref name="workQueue"/> is <see cref="IAsyncDisposable"/>, this game disposes it.</summary>
    /// <param name="settings">Configuration for streaming and the camera.</param>
    /// <param name="workQueue">Background worker port used for terrain generation and meshing.</param>
    /// <param name="world">The world to populate and draw.</param>
    /// <param name="spawnHeight">World Y of the player's feet at the start, above the block column at X = 8, Z = 8. Must clear the terrain there; the player holds still until the chunk under them has loaded, then falls.</param>
    /// <param name="store">
    /// Optional chunk storage. Edited chunks are saved to it every <see cref="GameSettings.AutosaveSeconds"/>, when they unload, and on
    /// <see cref="DisposeAsync"/>. The caller owns the store and disposes it after this game; it should be the same store the work queue reads.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public NerdcraftGame(GameSettings settings, IChunkWorkQueue workQueue, VoxelWorld world, float spawnHeight = 56f, IChunkStore? store = null)
    {
        settings.Validate();
        _settings = settings;
        _workQueue = workQueue as IAsyncDisposable;
        _streamer = new ChunkStreamer(world, workQueue, settings, store);
        _player = new LocalPlayer(
            PlayerState.Spawn(new Vector3(8.5f, spawnHeight, 8.5f)), world, settings.Bounds, new PlayerAbilities(CanFly: true, CanNoclip: true));
        _interactor = new BlockInteractor(world);
        _farPlane = (settings.RenderDistance + 1) * Chunk.Size * 1.5f;
    }

    /// <summary>
    /// Creates a game with the default wiring: heightmap terrain, culled meshing, and a threaded <see cref="ChunkPipeline"/>.
    /// The player starts standing on the terrain surface, or on the sea if that is higher, at X = 8, Z = 8.
    /// </summary>
    /// <param name="settings">Configuration for streaming and the camera.</param>
    /// <param name="store">Optional chunk storage to load chunks from and save them to; the caller owns it and disposes it after the game.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public static NerdcraftGame Create(GameSettings settings, IChunkStore? store = null)
    {
        settings.Validate();
        HeightmapTerrainGenerator generator = new(settings.Seed, settings.Bounds);
        ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), settings.WorkerCount, settings.Bounds, store: store);
        return new NerdcraftGame(settings, pipeline, new VoxelWorld(), Math.Max(generator.SurfaceHeight(8, 8), HeightmapTerrainGenerator.SeaLevel) + 1, store);
    }

    /// <summary>True while the pause menu is open. The host should release the cursor then, and capture it otherwise.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True once the player chose Quit in the pause menu. The host should then stop its loop.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Advances the player by <paramref name="deltaSeconds"/> and requests any chunks they now need. Does not touch the renderer.
    /// Uses <paramref name="deltaSeconds"/> for HUD timers too; a host that clamps the simulation step must call the overload
    /// that also takes the uncapped elapsed time.
    /// </summary>
    /// <param name="input">This frame's input. See the three-argument overload for how each field is used.</param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    public void Update(InputState input, float deltaSeconds) => Update(input, deltaSeconds, deltaSeconds);

    /// <summary>
    /// Advances the player by <paramref name="deltaSeconds"/> and requests any chunks they now need. Every <see cref="GameSettings.AutosaveSeconds"/>
    /// of real time it also saves edited chunks to the chunk store, if there is one. Does not touch the renderer.
    /// </summary>
    /// <param name="input">
    /// This frame's input. Toggles the debug overlay when <see cref="InputState.ToggleDebug"/> is set, and flight and noclip likewise.
    /// <see cref="InputState.TogglePause"/> opens or closes the pause menu; while it is open the player stands still, chunks keep
    /// streaming, and a <see cref="InputState.Click"/> on a button acts on it. Otherwise <see cref="InputState.Break"/> and
    /// <see cref="InputState.Place"/> change the targeted block and the hotbar keys change the selected slot; a button held
    /// when the menu closes does nothing until released. Look input is ignored on the frame the menu closes and the two after it,
    /// because the host re-captures the cursor then and the pointer jumps.
    /// </param>
    /// <param name="deltaSeconds">Simulation time since the previous frame, in seconds. The host may clamp it so a hitch does not fling the camera.</param>
    /// <param name="uiDeltaSeconds">
    /// Real time since the previous frame, in seconds, before any clamp. Drives HUD timers such as the hotbar hover text and the
    /// autosave interval, so they keep real time while paused or when <paramref name="deltaSeconds"/> is clamped. A host that always
    /// passes 0 never autosaves.
    /// </param>
    /// <exception cref="Exception">Rethrows a failure of the chunk store (for example a full disk) when saving or unloading chunks.</exception>
    public void Update(InputState input, float deltaSeconds, float uiDeltaSeconds)
    {
        long started = StreamingMetrics.Start(UpdateTime);
        if (FrameTime.Enabled)
        {
            long now = Stopwatch.GetTimestamp();
            if (_previousFrame != 0)
            {
                FrameTime.Record(Stopwatch.GetElapsedTime(_previousFrame, now).TotalMilliseconds);
            }

            _previousFrame = now;
        }
        else
        {
            _previousFrame = 0;
        }

        if (input.ToggleDebug)
        {
            _showDebug = !_showDebug;
        }

        _screenSize = input.ScreenSize;
        _pointer = input.Pointer;

        // HUD timers use real time, so they keep running while the simulation is paused or clamped.
        _interactor.UpdateHover(uiDeltaSeconds);

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
            _interactor.Update(input, deltaSeconds, _player);
        }

        _streamer.Update(_player.EyePosition);

        // Real time, so edits are saved while paused and a long frame does not skip a save.
        _secondsSinceSave += uiDeltaSeconds;
        if (_secondsSinceSave >= _settings.AutosaveSeconds)
        {
            _secondsSinceSave = 0f;
            _streamer.SaveModified();
        }

        StreamingMetrics.RecordElapsed(UpdateTime, started);
    }

    /// <summary>Uploads finished meshes and draws one frame. Must run on the thread that owns the graphics context.</summary>
    public void Render(IRenderer renderer)
    {
        long uploadStarted = StreamingMetrics.Start(UploadTime);
        _streamer.UploadCompleted(renderer);
        StreamingMetrics.RecordElapsed(UploadTime, uploadStarted);

        long drawStarted = StreamingMetrics.Start(DrawTime);

        CameraView view = _player.ToView(_settings.FovYDegrees, nearPlane: 0.1f, _farPlane);
        Frustum frustum = Frustum.FromViewProjection(view.CreateViewProjection(renderer.AspectRatio));

        renderer.BeginFrame(view, SkyColor);
        DrawStats stats = _streamer.Draw(renderer, frustum, view.Position, countMeshedChunks: _showDebug);
        if (!IsPaused && _player.Target() is { } hit)
        {
            Vector3 corner = new(hit.Block.X, hit.Block.Y, hit.Block.Z);
            renderer.DrawBoxOutline(
                new Aabb(corner - new Vector3(OutlineMargin), corner + Vector3.One + new Vector3(OutlineMargin)), OutlineColor);
        }

        renderer.EndFrame(
            Hud.Layout(_screenSize, _interactor.Inventory, crosshair: !IsPaused, _interactor.HoverText, _interactor.HoverTimer),
            _showDebug ? new DebugOverlay(_player.EyePosition, stats) : null,
            IsPaused ? PauseMenu.Layout(_screenSize, _pointer) : null);
        StreamingMetrics.RecordElapsed(DrawTime, drawStarted);
    }

    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        if (!paused)
        {
            _ignoreLookFrames = 3;
            _player.ClearPendingInput();
            _interactor.ClearPendingInput();
        }
    }

    /// <summary>
    /// Saves edited chunks to the store, discards any staged CPU mesh, stops background workers and waits for them to finish.
    /// Call when frames have stopped. The renderer's owner is responsible for releasing its remaining GPU resources, and the
    /// store's owner for disposing the store afterwards.
    /// </summary>
    /// <exception cref="Exception">Rethrows a failure of the chunk store while saving, after the workers have stopped.</exception>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _streamer.SaveModified();
        }
        finally
        {
            // Workers stop even when the store failed; the store's exception is still thrown to the caller.
            _streamer.DiscardStagedMesh();
            if (_workQueue is not null)
            {
                await _workQueue.DisposeAsync();
            }
        }
    }
}
