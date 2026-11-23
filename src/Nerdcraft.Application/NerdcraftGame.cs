// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Camera;
using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
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

    // Blocks between the terrain surface and the camera at the spawn column.
    private const int SpawnClearance = 20;

    private readonly GameSettings _settings;
    private readonly FlyCamera _camera;
    private readonly ChunkStreamer _streamer;
    private readonly IAsyncDisposable? _workQueue;
    private readonly float _farPlane;

    /// <summary>Creates a game over the supplied work queue and world. If <paramref name="workQueue"/> is <see cref="IAsyncDisposable"/>, this game disposes it.</summary>
    /// <param name="settings">Configuration for streaming and the camera.</param>
    /// <param name="workQueue">Background worker port used for terrain generation and meshing.</param>
    /// <param name="world">The world to populate and draw.</param>
    /// <param name="spawnHeight">World Y at which the camera starts, above the block column at X = 8, Z = 8. Must clear the terrain there.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public NerdcraftGame(GameSettings settings, IChunkWorkQueue workQueue, VoxelWorld world, float spawnHeight = 56f)
    {
        settings.Validate();
        _settings = settings;
        _workQueue = workQueue as IAsyncDisposable;
        _streamer = new ChunkStreamer(world, workQueue, settings);
        _camera = new FlyCamera(new Vector3(8f, spawnHeight, 8f), yawDegrees: 0f, pitchDegrees: -25f);
        _farPlane = (settings.RenderDistance + 1) * Chunk.Size * 1.5f;
    }

    /// <summary>
    /// Creates a game with the default wiring: heightmap terrain, culled meshing, and a threaded <see cref="ChunkPipeline"/>.
    /// The camera starts 20 blocks above the terrain surface at X = 8, Z = 8.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value in <paramref name="settings"/> is out of range.</exception>
    public static NerdcraftGame Create(GameSettings settings)
    {
        settings.Validate();
        HeightmapTerrainGenerator generator = new(settings.Seed, settings.Bounds);
        ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), settings.WorkerCount);
        return new NerdcraftGame(settings, pipeline, new VoxelWorld(), generator.SurfaceHeight(8, 8) + SpawnClearance);
    }

    /// <summary>Advances the camera by <paramref name="deltaSeconds"/> and requests any chunks it now needs. Does not touch the renderer.</summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    public void Update(InputState input, float deltaSeconds)
    {
        _camera.Apply(input, deltaSeconds);
        _streamer.Update(_camera.Position);
    }

    /// <summary>Uploads finished meshes and draws one frame. Must run on the thread that owns the graphics context.</summary>
    public void Render(IRenderer renderer)
    {
        _streamer.UploadCompleted(renderer);

        CameraView view = _camera.ToView(_settings.FovYDegrees, nearPlane: 0.1f, _farPlane);
        Frustum frustum = Frustum.FromViewProjection(view.CreateViewProjection(renderer.AspectRatio));

        renderer.BeginFrame(view, SkyColor);
        _streamer.Draw(renderer, frustum);
        renderer.EndFrame();
    }

    /// <summary>Stops background workers (when the work queue is disposable) and waits for them to finish.</summary>
    public ValueTask DisposeAsync() => _workQueue?.DisposeAsync() ?? ValueTask.CompletedTask;
}
