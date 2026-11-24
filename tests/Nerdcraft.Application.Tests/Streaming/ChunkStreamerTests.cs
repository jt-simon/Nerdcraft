// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Ports;
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

    private static readonly Vector3 SliceCamera = new(8f, 12f, 8f);

    private readonly VoxelWorld _world = new();
    private readonly FakeRenderer _renderer = new();
    private readonly ChunkStreamer _streamer;
    private readonly GameSettings _settings;

    public ChunkStreamerTests()
    {
        _settings = new GameSettings { RenderDistance = 1, Bounds = new WorldBounds(0, Chunk.Size - 1), MaxUploadsPerFrame = 100 };
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
    public void Update_BoundsBelowZero_LoadsNegativeChunkRows()
    {
        GameSettings settings = new() { RenderDistance = 0, Bounds = new WorldBounds(-Chunk.Size, Chunk.Size - 1), MaxUploadsPerFrame = 100 };
        VoxelWorld world = new();
        ChunkStreamer streamer = new(world, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()), settings);

        streamer.Update(Camera); // Requests generation; the next update installs the results.
        streamer.Update(Camera);

        Assert.True(world.IsLoaded(new ChunkPosition(0, -1, 0)));
        Assert.True(world.IsLoaded(new ChunkPosition(0, 0, 0)));
        Assert.False(world.IsLoaded(new ChunkPosition(0, 1, 0)));
        Assert.False(world.IsLoaded(new ChunkPosition(0, -2, 0)));
    }

    [Fact]
    public void Update_EditedChunkUnloadedAndReloaded_KeepsEdit()
    {
        InMemoryChunkStore store = new();
        VoxelWorld world = new();
        ChunkStreamer streamer = new(world, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher(), store), _settings, store);
        streamer.Update(Camera);
        streamer.Update(Camera);
        BlockPosition edit = new(1, Chunk.Size - 1, 1); // Air in the half slab.
        Assert.True(world.SetBlock(edit, BlockTypes.Gravel));

        Vector3 far = new(10 * Chunk.Size, 4f, 8f);
        streamer.Update(far);
        Assert.False(world.IsLoaded(new ChunkPosition(0, 0, 0)));
        streamer.Update(Camera);
        streamer.Update(Camera);

        Assert.Equal(BlockTypes.Gravel, world.GetBlock(edit));
    }

    [Fact]
    public void Update_EmptyChunks_RequestNoMesh()
    {
        // Two chunk rows: row 0 holds the slab, row 1 is all air. Five columns means five meshes, not ten.
        GameSettings settings = _settings with { Bounds = new WorldBounds(0, (2 * Chunk.Size) - 1) };
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        ChunkStreamer streamer = new(_world, queue, settings);

        for (int frame = 0; frame < 4; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.True(_world.IsLoaded(new ChunkPosition(0, 1, 0)));
        Assert.Equal(ChunksInWorld, queue.MeshRequests);
    }

    [Fact]
    public void UploadCompleted_BlockPlacedInEmptyChunk_UploadsItsMesh()
    {
        GameSettings settings = _settings with { Bounds = new WorldBounds(0, (2 * Chunk.Size) - 1) };
        ChunkStreamer streamer = new(_world, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()), settings);
        for (int frame = 0; frame < 4; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);

        _world.SetBlock(new BlockPosition(8, Chunk.Size + 8, 8), BlockTypes.Stone); // Inside the empty chunk (0,1,0).
        for (int frame = 0; frame < 4; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.Equal(ChunksInWorld + 1, _renderer.Uploads.Count);
    }

    [Fact]
    public void UploadCompleted_ChunkEmptiedAfterUpload_ReleasesItsMesh()
    {
        Settle();
        MeshHandle original = _renderer.Uploads[0]; // Nearest chunk (0,0,0) uploads first.

        for (int y = 0; y < Chunk.Size / 2; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    _world.SetBlock(new BlockPosition(x, y, z), BlockTypes.Air);
                }
            }
        }

        Settle();

        Assert.Contains(original, _renderer.Releases);
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

    [Theory]
    [InlineData(0)] // Generation requests are refused.
    [InlineData(1)] // Mesh requests are refused.
    public void Update_QueueRefusesRequests_RetriesOnLaterFrames(int framesBeforeRefusing)
    {
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        ChunkStreamer streamer = new(_world, queue, _settings);

        for (int frame = 0; frame < 10; frame++)
        {
            if (frame == framesBeforeRefusing)
            {
                queue.RefusedRequests = 2;
            }

            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.Equal(ChunksInWorld, _renderer.Uploads.Count);
    }

    [Fact]
    public void Update_MoreGeneratedChunksThanBudget_InstallsAtMostTheBudgetPerFrame()
    {
        ChunkStreamer limited = new(
            _world,
            new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()),
            _settings with { MaxChunkInstallsPerFrame = 2 });

        limited.Update(Camera); // Requests generation.
        limited.Update(Camera);
        Assert.Equal(2, _world.LoadedChunks.Count);

        limited.Update(Camera);
        limited.Update(Camera);
        Assert.Equal(ChunksInWorld, _world.LoadedChunks.Count);
    }

    [Fact]
    public void Update_MoreMeshableChunksThanBudget_RequestsAtMostTheBudgetPerFrame()
    {
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        ChunkStreamer limited = new(_world, queue, _settings with { MaxMeshRequestsPerFrame = 2 });

        limited.Update(Camera); // Requests generation.
        limited.Update(Camera);
        Assert.Equal(2, queue.MeshRequests);

        limited.Update(Camera);
        limited.Update(Camera);
        Assert.Equal(ChunksInWorld, queue.MeshRequests);
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
    public void UploadCompleted_MixedMeshWithOneUploadPerFrame_EventuallyDrawsBothMaterials()
    {
        ChunkStreamer streamer = new(
            _world,
            new InlineChunkWorkQueue(new FloodedSlabGenerator(), new CulledChunkMesher()),
            _settings with { RenderDistance = 0, MaxUploadsPerFrame = 1 });
        Frustum towardWorld = FrustumFrom(new Vector3(8, 40, 8), new Vector3(8, 0, 8), -Vector3.UnitZ);
        streamer.Update(Camera);
        streamer.Update(Camera);

        streamer.UploadCompleted(_renderer);
        Assert.Single(_renderer.Uploads);
        streamer.Draw(_renderer, towardWorld, Camera);
        Assert.Empty(_renderer.AllDraws);

        streamer.UploadCompleted(_renderer);
        streamer.Draw(_renderer, towardWorld, Camera);
        Assert.Equal(new[] { (_renderer.Uploads[0], false), (_renderer.Uploads[1], true) }, _renderer.AllDraws);
    }

    [Fact]
    public void UploadCompleted_BurstOfEvictions_SpreadsReleasesAcrossFrames()
    {
        ChunkStreamer streamer = new(
            _world,
            new InlineChunkWorkQueue(new FloodedSlabGenerator(), new CulledChunkMesher()),
            _settings with { MaxUploadsPerFrame = 1 });
        for (int frame = 0; frame <= 2 * ChunksInWorld; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        MeshHandle[] handles = _renderer.Uploads.ToArray();
        Assert.Equal(2 * ChunksInWorld, handles.Length);

        streamer.Update(new Vector3(16_000f, 4f, 0f));
        for (int frame = 0; frame < ChunksInWorld; frame++)
        {
            streamer.UploadCompleted(_renderer);
            Assert.Equal(2 * (frame + 1), _renderer.Releases.Count);
        }

        Assert.All(handles, handle => Assert.Contains(handle, _renderer.Releases));
    }

    [Fact]
    public void UploadCompleted_StagedMixedMeshInvalidatedByEdit_ReleasesItsPartialUpload()
    {
        ChunkStreamer streamer = new(
            _world,
            new InlineChunkWorkQueue(new FloodedSlabGenerator(), new CulledChunkMesher()),
            _settings with { RenderDistance = 0, MaxUploadsPerFrame = 1 });
        streamer.Update(Camera);
        streamer.Update(Camera);
        streamer.UploadCompleted(_renderer);
        MeshHandle obsolete = Assert.Single(_renderer.Uploads);

        Assert.True(_world.SetBlock(new BlockPosition(8, 8, 8), BlockTypes.Stone));
        for (int frame = 0; frame < 4; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.Equal(obsolete, Assert.Single(_renderer.Releases));
        Assert.Equal(3, _renderer.Uploads.Count);
    }

    [Fact]
    public void UploadCompleted_StagedMixedMeshEvicted_ReleasesItsPartialUpload()
    {
        ChunkStreamer streamer = new(
            _world,
            new InlineChunkWorkQueue(new FloodedSlabGenerator(), new CulledChunkMesher()),
            _settings with { RenderDistance = 0, MaxUploadsPerFrame = 1 });
        streamer.Update(Camera);
        streamer.Update(Camera);
        streamer.UploadCompleted(_renderer);
        MeshHandle staged = Assert.Single(_renderer.Uploads);

        streamer.Update(new Vector3(16_000f, 4f, 0f));
        streamer.UploadCompleted(_renderer);

        Assert.Equal(staged, Assert.Single(_renderer.Releases));
        Assert.Single(_renderer.Uploads);
    }

    [Fact]
    public void UploadCompleted_NeighborUnloaded_ExposesTheSurvivorsBorderFace()
    {
        ChunkStreamer streamer = new(
            _world,
            new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()),
            _settings with { RenderDistance = 0 });
        BlockId[] adjacent = new BlockId[Chunk.Volume];
        adjacent[Chunk.Index(0, 4, 8)] = BlockTypes.Stone;
        ChunkPosition east = new(1, 0, 0);
        _world.LoadChunk(east, adjacent);

        streamer.Update(Camera);
        streamer.Update(Camera);
        BlockId[] survivor = new BlockId[Chunk.Volume];
        survivor[Chunk.Index(Chunk.Mask, 4, 8)] = BlockTypes.Stone;
        _world.LoadChunk(new ChunkPosition(0, 0, 0), survivor);
        for (int frame = 0; frame < 3; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(_renderer);
        }

        Assert.Equal(20, _renderer.VertexCounts[_renderer.Uploads[^1]]);

        Assert.True(_world.UnloadChunk(east));
        streamer.Update(Camera);
        streamer.UploadCompleted(_renderer);

        Assert.Equal(24, _renderer.VertexCounts[_renderer.Uploads[^1]]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // The scan already saw the edit, and skipped the chunk because its mesh was outstanding.
    public void UploadCompleted_DiscardsMeshBuiltFromOutdatedBlocks_ThenRemeshes(bool scanBeforeUpload)
    {
        _streamer.Update(Camera);
        _streamer.Update(Camera); // Meshes are complete but not yet uploaded.

        _world.SetBlock(new BlockPosition(8, 8, 8), BlockTypes.Stone); // Edits chunk (0,0,0) mid-flight.
        if (scanBeforeUpload)
        {
            _streamer.Update(Camera);
        }

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
    public void UploadCompleted_StationaryBorderEdit_ReplacesBothMeshes()
    {
        Settle();

        Assert.True(_world.SetBlock(new BlockPosition(Chunk.Mask, 4, 8), BlockTypes.Air));
        Settle();

        Assert.Equal(ChunksInWorld + 2, _renderer.Uploads.Count);
        Assert.Equal(2, _renderer.Releases.Count);
    }

    [Theory]
    [InlineData(16_000f)]
    [InlineData(1_048_576f)]
    public void Update_CameraMovesAway_UnloadsChunksAndReleasesTheirMeshes(float distance)
    {
        Settle();

        Vector3 far = new(distance, 4f, 0f);
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

        DrawStats away = _streamer.Draw(_renderer, awayFromWorld, Vector3.Zero);
        Assert.Empty(_renderer.Draws);
        Assert.Equal(new DrawStats(0, ChunksInWorld, 0), away);

        DrawStats toward = _streamer.Draw(_renderer, towardWorld, Vector3.Zero);
        Assert.NotEmpty(_renderer.Draws);
        Assert.Equal(_renderer.Draws.Count, toward.ChunksDrawn);
        Assert.Equal(ChunksInWorld, toward.ChunksMeshed);
        Assert.Equal(_renderer.Draws.Sum(handle => (long)_renderer.VertexCounts[handle]), toward.VerticesDrawn);
    }

    [Fact]
    public void Draw_ChunksWithWater_DrawsTranslucentAfterOpaqueAndNearestLast()
    {
        (ChunkStreamer streamer, FakeRenderer renderer) = FloodedStreamer();
        Frustum towardWorld = FrustumFrom(new Vector3(8, 40, 8), new Vector3(8, 0, 8), Vector3.UnitZ * -1);

        streamer.Draw(renderer, towardWorld, Camera);

        int firstTranslucent = renderer.AllDraws.FindIndex(draw => draw.Translucent);
        Assert.True(firstTranslucent > 0, "Expected opaque draws followed by translucent ones.");
        Assert.All(renderer.AllDraws.Skip(firstTranslucent), draw => Assert.True(draw.Translucent));
        MeshHandle nearestWater = renderer.Uploads[1]; // Chunk (0,0,0) uploads first: its opaque mesh, then its water.
        Assert.Equal((nearestWater, true), renderer.AllDraws[^1]);
    }

    [Fact]
    public void Draw_ChunkBehindSolidChunk_IsCulled()
    {
        (ChunkStreamer streamer, FakeRenderer renderer) = SliceStreamer(BlockTypes.Stone);

        streamer.Draw(renderer, LookingAlongX(), SliceCamera);

        Assert.Contains(new Vector3(Chunk.Size, 0, 0), renderer.DrawOrigins);
        Assert.DoesNotContain(new Vector3(2 * Chunk.Size, 0, 0), renderer.DrawOrigins);
    }

    [Fact]
    public void Draw_ChunkBehindEmptyChunk_IsDrawn()
    {
        (ChunkStreamer streamer, FakeRenderer renderer) = SliceStreamer(BlockTypes.Air);

        streamer.Draw(renderer, LookingAlongX(), SliceCamera);

        Assert.Contains(new Vector3(2 * Chunk.Size, 0, 0), renderer.DrawOrigins);
    }

    [Fact]
    public void Draw_CameraOutsideEveryChunk_FallsBackToFrustumOnly()
    {
        (ChunkStreamer streamer, FakeRenderer renderer) = SliceStreamer(BlockTypes.Stone);
        Vector3 aboveWorld = new(8, 100, 8);
        Frustum lookingDown = FrustumFrom(aboveWorld, new Vector3(8, 0, 8), Vector3.UnitZ * -1);

        streamer.Draw(renderer, lookingDown, aboveWorld);

        Assert.Contains(new Vector3(2 * Chunk.Size, 0, 0), renderer.DrawOrigins);
    }

    [Fact]
    public void Update_CameraMovesAway_ReleasesTranslucentMeshesToo()
    {
        (ChunkStreamer streamer, FakeRenderer renderer) = FloodedStreamer();

        streamer.Update(new Vector3(16_000f, 4f, 0f));
        streamer.UploadCompleted(renderer);

        Assert.Equal(2 * ChunksInWorld, renderer.Releases.Count);
    }

    private (ChunkStreamer Streamer, FakeRenderer Renderer) FloodedStreamer()
    {
        FakeRenderer renderer = new();
        ChunkStreamer streamer = new(new VoxelWorld(), new InlineChunkWorkQueue(new FloodedSlabGenerator(), new CulledChunkMesher()), _settings);
        for (int frame = 0; frame < 4; frame++)
        {
            streamer.Update(Camera);
            streamer.UploadCompleted(renderer);
        }

        return (streamer, renderer);
    }

    // A streamer over a world with a slice of the given block at chunk X = 1, which is a wall across it. The camera
    // sits in the open air of chunk (0,0,0); with radius 2 the only chunk beyond the slice is (2,0,0).
    private static (ChunkStreamer Streamer, FakeRenderer Renderer) SliceStreamer(BlockId sliceBlock)
    {
        GameSettings settings = new() { RenderDistance = 2, Bounds = new WorldBounds(0, Chunk.Size - 1), MaxUploadsPerFrame = 100 };
        FakeRenderer renderer = new();
        ChunkStreamer streamer = new(new VoxelWorld(), new InlineChunkWorkQueue(new XSliceGenerator(1, sliceBlock), new CulledChunkMesher()), settings);
        for (int frame = 0; frame < 6; frame++)
        {
            streamer.Update(SliceCamera);
            streamer.UploadCompleted(renderer);
        }

        return (streamer, renderer);
    }

    private static Frustum LookingAlongX() => FrustumFrom(SliceCamera, SliceCamera + new Vector3(100, 0, 0), Vector3.UnitY);

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
