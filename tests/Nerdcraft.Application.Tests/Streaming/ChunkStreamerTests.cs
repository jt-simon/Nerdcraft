// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Streaming;

public class ChunkStreamerTests
{
    // Radius 1 gives five columns around the camera chunk, one chunk tall, so five meshes in total.
    private const int ChunksInWorld = 5;

    private static readonly Vector3 Camera = new(8f, 4f, 8f);

    private readonly VoxelWorld _world = new();
    private readonly FakeRenderer _renderer = new();
    private readonly ChunkStreamer _streamer;
    private readonly GameSettings _settings;

    public ChunkStreamerTests()
    {
        _settings = new GameSettings { RenderDistance = 1, WorldHeightInChunks = 1, MaxUploadsPerFrame = 100 };
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        _streamer = new ChunkStreamer(_world, queue, _settings);
    }

    [Fact]
    public void Update_CameraInWorld_LoadsChunksWithinRenderDistance()
    {
        Settle();

        Assert.True(_world.IsLoaded(new ChunkPosition(0, 0, 0)));
        Assert.True(_world.IsLoaded(new ChunkPosition(1, 0, 0)));
        Assert.True(_world.IsLoaded(new ChunkPosition(0, 0, -1)));
        Assert.False(_world.IsLoaded(new ChunkPosition(1, 0, 1))); // Corner lies outside the circular radius.
        Assert.False(_world.IsLoaded(new ChunkPosition(2, 0, 0)));
    }

    [Fact]
    public void UploadCompleted_AfterSettling_UploadsEachChunkMeshOnce()
    {
        Settle();

        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
        _streamer.Update(Camera);
        _streamer.UploadCompleted(_renderer);
        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
    }

    [Fact]
    public void UploadCompleted_MoreMeshesThanBudget_UploadsAtMostTheBudgetPerFrame()
    {
        ChunkStreamer limited = new(
            _world,
            new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()),
            _settings with { MaxUploadsPerFrame = 2 });
        limited.Update(Camera);
        limited.Update(Camera);

        limited.UploadCompleted(_renderer);
        Assert.Equal(2, _renderer.Uploads.Count);

        limited.UploadCompleted(_renderer);
        limited.UploadCompleted(_renderer);
        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
    }

    [Fact]
    public void UploadCompleted_DiscardsMeshBuiltFromOutdatedBlocks_ThenRemeshes()
    {
        _streamer.Update(Camera);
        _streamer.Update(Camera); // Meshes are complete but not yet uploaded.

        _world.SetBlock(new BlockPosition(8, 8, 8), BlockTypes.Stone); // Edits chunk (0,0,0) mid-flight.
        _streamer.UploadCompleted(_renderer);
        Assert.Equal(ChunksInWorld - 1, _renderer.Uploads.Count);

        _streamer.Update(Camera);
        _streamer.UploadCompleted(_renderer);
        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
        Assert.Empty(_renderer.Releases);
    }

    [Fact]
    public void UploadCompleted_BlockEditedAfterUpload_ReplacesTheMeshAndReleasesTheOldOne()
    {
        Settle();
        MeshHandle original = _renderer.Uploads[0]; // Nearest chunk (0,0,0) uploads first.

        _world.SetBlock(new BlockPosition(8, 8, 8), BlockTypes.Stone);
        Settle();

        Assert.Equal(ChunksInWorld + 1, _renderer.Uploads.Count);
        Assert.Single(_renderer.Releases);
        Assert.Equal(original, _renderer.Releases[0]);
    }

    [Fact]
    public void Update_CameraMovesAway_UnloadsChunksAndReleasesTheirMeshes()
    {
        Settle();

        Vector3 far = new(16_000f, 4f, 0f);
        _streamer.Update(far);
        _streamer.UploadCompleted(_renderer);

        Assert.False(_world.IsLoaded(new ChunkPosition(0, 0, 0)));
        Assert.Equal(ChunksInWorld, _renderer.Releases.Count);
    }

    [Fact]
    public void UploadCompleted_ChunkReloadedAfterUnload_DiscardsMeshesFromTheEarlierLoad()
    {
        _streamer.Update(Camera);
        _streamer.Update(Camera); // Meshes are complete but not yet uploaded.

        _streamer.Update(new Vector3(16_000f, 4f, 0f)); // Unloads every chunk while those meshes are still queued.
        _streamer.Update(Camera); // Brings the same chunks back.
        Settle();

        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
        Assert.Empty(_renderer.Releases);
    }

    [Fact]
    public void Draw_ChunksOutsideTheFrustum_AreSkipped()
    {
        Settle();
        Frustum towardWorld = FrustumFrom(new Vector3(8, 40, 8), new Vector3(8, 0, 8), Vector3.UnitZ * -1);
        Frustum awayFromWorld = FrustumFrom(new Vector3(8, 40, 8), new Vector3(8, 80, 8), Vector3.UnitZ * -1);

        _streamer.Draw(_renderer, awayFromWorld);
        Assert.Empty(_renderer.Draws);

        _streamer.Draw(_renderer, towardWorld);
        Assert.NotEmpty(_renderer.Draws);
    }

    // Enough frames for generation, then meshing, then upload to complete with the inline queue.
    private void Settle()
    {
        for (int frame = 0; frame < 4; frame++)
        {
            _streamer.Update(Camera);
            _streamer.UploadCompleted(_renderer);
        }
    }

    private static Frustum FrustumFrom(Vector3 position, Vector3 target, Vector3 up)
        => Frustum.FromViewProjection(
            new CameraView(position, target, up, 70f, 0.1f, 500f).CreateViewProjection(16f / 9f));
}
