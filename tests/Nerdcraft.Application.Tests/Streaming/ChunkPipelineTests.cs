// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Tests.Streaming;

public class ChunkPipelineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Generate_ManyRequests_ReturnsEveryChunkOnce()
    {
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 3, WorldBounds.Default);
        List<ChunkPosition> requested = [.. Enumerable.Range(0, 20).Select(i => new ChunkPosition(i, 1, -i))];
        foreach (ChunkPosition position in requested)
        {
            Assert.True(pipeline.TryRequestGenerateColumn(position.X, position.Z, position.Y, position.Y));
        }

        List<ChunkPosition> received = [];
        await WaitUntilAsync(() =>
        {
            while (pipeline.TryTakeGenerated(out GeneratedChunk chunk))
            {
                received.Add(chunk.Position);
                ArrayPool<BlockId>.Shared.Return(chunk.Blocks);
            }

            return received.Count == requested.Count;
        });

        Assert.Equal(requested.OrderBy(p => p.X), received.OrderBy(p => p.X));
    }

    [Fact]
    public async Task GenerateColumn_WithStore_UsesStoredRowsAndGeneratesAndSavesTheRest()
    {
        HeightmapTerrainGenerator generator = new(5, WorldBounds.Default);
        InMemoryChunkStore store = new();
        BlockId[] stored = new BlockId[Chunk.Volume];
        stored[Chunk.Index(2, 3, 4)] = BlockTypes.Gravel;
        store.Save(new ChunkPosition(3, 0, -2), stored);
        await using ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 2, WorldBounds.Default, store: store);
        Assert.True(pipeline.TryRequestGenerateColumn(3, -2, -1, 1));

        Dictionary<ChunkPosition, BlockId[]> received = [];
        await WaitUntilAsync(() =>
        {
            while (pipeline.TryTakeGenerated(out GeneratedChunk chunk))
            {
                received[chunk.Position] = chunk.Blocks.AsSpan(0, Chunk.Volume).ToArray();
                ArrayPool<BlockId>.Shared.Return(chunk.Blocks);
            }

            return received.Count == 3;
        });

        Assert.Equal(stored, received[new ChunkPosition(3, 0, -2)]);
        BlockId[] expected = new BlockId[Chunk.Volume];
        generator.Generate(new ChunkPosition(3, 1, -2), expected);
        Assert.Equal(expected, received[new ChunkPosition(3, 1, -2)]);
        Assert.Equal(expected, store.Get(new ChunkPosition(3, 1, -2)));
        Assert.NotNull(store.Get(new ChunkPosition(3, -1, -2)));
        Assert.Equal(0, store.GeneratedCount(new ChunkPosition(3, 0, -2)));
    }

    [Fact]
    public async Task GenerateColumn_StoreBusy_PublishesTheRowOnlyAfterTheSaveCompletes()
    {
        GatedStore store = new();
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default, store: store);
        Assert.True(pipeline.TryRequestGenerateColumn(0, 0, 0, 0));
        await store.Entered.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.False(pipeline.TryTakeGenerated(out _));

        store.Release();
        GeneratedChunk chunk = default;
        await WaitUntilAsync(() => pipeline.TryTakeGenerated(out chunk));
        ArrayPool<BlockId>.Shared.Return(chunk.Blocks);
    }

    [Fact]
    public async Task DisposeAsync_WorkerWaitingOnTheStore_Completes()
    {
        GatedStore store = new();
        ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default, store: store);
        Assert.True(pipeline.TryRequestGenerateColumn(0, 0, 0, 0));
        await store.Entered.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        // The store never releases, so this only returns if the worker's token reaches the store call.
        await pipeline.DisposeAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Mesh_Request_ReturnsMeshForTheSnapshotVersion()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);
        ChunkSnapshot snapshot = ChunkSnapshot.Create(world, new ChunkPosition(0, 0, 0));
        int version = snapshot.Version;

        Assert.True(pipeline.TryRequestMesh(snapshot));

        MeshedChunk result = default;
        await WaitUntilAsync(() => pipeline.TryTakeMeshed(out result));
        using ChunkMesh mesh = result.Mesh;
        Assert.Equal(version, result.Version);
        Assert.Equal(24, mesh.Opaque.VertexCount);
    }

    [Fact]
    public async Task GenerateColumn_ManyRows_ReturnsEachRowAtItsOwnPosition()
    {
        HeightmapTerrainGenerator generator = new(5, WorldBounds.Default);
        await using ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 2, WorldBounds.Default);
        Assert.True(pipeline.TryRequestGenerateColumn(3, -2, -1, 1));

        Dictionary<ChunkPosition, BlockId[]> received = [];
        await WaitUntilAsync(() =>
        {
            while (pipeline.TryTakeGenerated(out GeneratedChunk chunk))
            {
                received[chunk.Position] = chunk.Blocks.AsSpan(0, Chunk.Volume).ToArray();
                ArrayPool<BlockId>.Shared.Return(chunk.Blocks);
            }

            return received.Count == 3;
        });

        foreach ((ChunkPosition position, BlockId[] blocks) in received)
        {
            BlockId[] expected = new BlockId[Chunk.Volume];
            generator.Generate(position, expected);
            Assert.Equal(expected, blocks);
        }
    }

    [Fact]
    public async Task TryRequestGenerateColumn_MaxBelowMin_ThrowsOnTheCallingThread()
    {
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.TryRequestGenerateColumn(0, 0, 2, 1));
    }

    [Fact]
    public async Task Request_WhenQueueIsFull_ReturnsFalse()
    {
        using ManualResetEventSlim release = new();
        await using ChunkPipeline pipeline = new(
            new BlockingGenerator(release), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default, queueCapacity: 2);

        // The worker takes one job and blocks; two more fill the queue.
        int accepted = 0;
        while (pipeline.TryRequestGenerateColumn(accepted, 0, 0, 0) && accepted < 10)
        {
            accepted++;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        release.Set();
        Assert.InRange(accepted, 2, 4);
    }

    [Fact]
    public async Task DisposeAsync_WithPendingWork_StopsWorkersWithoutDrainingQueue()
    {
        using ManualResetEventSlim release = new();
        BlockingGenerator generator = new(release, blockFromCall: 2);
        ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = TestWorlds.WithEmptyChunks(position);
        using ChunkSnapshot snapshot = ChunkSnapshot.Create(world, position);
        try
        {
            Assert.True(pipeline.TryRequestGenerateColumn(0, 0, 0, 0));
            Assert.True(pipeline.TryRequestGenerateColumn(1, 0, 0, 0));
            await WaitUntilAsync(() => generator.Calls == 2);
            Assert.True(pipeline.TryRequestMesh(snapshot));
            Assert.True(pipeline.TryRequestGenerateColumn(2, 0, 0, 0));
            Task disposal = pipeline.DisposeAsync().AsTask();
            release.Set();
            await disposal.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            release.Set();
            await pipeline.DisposeAsync();
        }

        Assert.Equal(2, generator.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = snapshot.Blocks.Length);
        Assert.False(pipeline.TryTakeGenerated(out _));
    }

    [Fact]
    public async Task DisposeAsync_WithCompletedMesh_DisposesItsBuffers()
    {
        using ManualResetEventSlim release = new();
        BlockingGenerator generator = new(release);
        RecordingMesher mesher = new();
        ChunkPipeline pipeline = new(generator, mesher, workerCount: 1, WorldBounds.Default);
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = TestWorlds.WithEmptyChunks(position);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);
        using ChunkSnapshot snapshot = ChunkSnapshot.Create(world, position);
        try
        {
            Assert.True(pipeline.TryRequestMesh(snapshot));
            Assert.True(pipeline.TryRequestGenerateColumn(0, 0, 0, 0));
            await WaitUntilAsync(() => generator.Calls == 1);
        }
        finally
        {
            release.Set();
            await pipeline.DisposeAsync();
        }

        ChunkMesh mesh = Assert.IsType<ChunkMesh>(mesher.Result);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = mesh.Opaque.Positions.Length);
        Assert.False(pipeline.TryTakeMeshed(out _));
    }

    [Fact]
    public async Task DisposeAsync_AfterWorkerFault_DisposesQueuedSnapshots()
    {
        using ManualResetEventSlim release = new();
        BlockingGenerator generator = new(release, fault: new InvalidOperationException("boom"));
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = TestWorlds.WithEmptyChunks(position);
        using ChunkSnapshot snapshot = ChunkSnapshot.Create(world, position);
        await using ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);

        try
        {
            Assert.True(pipeline.TryRequestGenerateColumn(0, 0, 0, 0));
            await WaitUntilAsync(() => generator.Calls == 1);
            Assert.True(pipeline.TryRequestMesh(snapshot));
        }
        finally
        {
            release.Set();
        }

        await WaitForFaultAsync<InvalidOperationException>(pipeline);
        await pipeline.DisposeAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = snapshot.Blocks.Length);
    }

    [Fact]
    public async Task TryRequestMesh_WithOutstandingResultLimit_ResumesAfterResultIsTaken()
    {
        RecordingMesher mesher = new();
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = TestWorlds.WithEmptyChunks(position);
        using ChunkSnapshot first = ChunkSnapshot.Create(world, position);
        using ChunkSnapshot second = ChunkSnapshot.Create(world, position);
        using ChunkSnapshot third = ChunkSnapshot.Create(world, position);
        await using ChunkPipeline pipeline = new(
            new HalfSlabGenerator(), mesher, workerCount: 1, WorldBounds.Default, queueCapacity: 1);

        Assert.True(pipeline.TryRequestMesh(first));
        await WaitUntilAsync(() => mesher.Calls == 1);
        Assert.True(pipeline.TryRequestMesh(second));
        await WaitUntilAsync(() => mesher.Calls == 2);
        Assert.False(pipeline.TryRequestMesh(third));

        MeshedChunk result = default;
        await WaitUntilAsync(() => pipeline.TryTakeMeshed(out result));
        result.Mesh.Dispose();
        Assert.True(pipeline.TryRequestMesh(third));
    }

    [Fact]
    public async Task TryRequestGenerateColumn_WithOutstandingRowLimit_ResumesWhenRowsAreTaken()
    {
        using ManualResetEventSlim release = new(initialState: true);
        BlockingGenerator generator = new(release);
        await using ChunkPipeline pipeline = new(
            generator, new CulledChunkMesher(), workerCount: 1, WorldBounds.Default, queueCapacity: 1);
        WorldBounds bounds = WorldBounds.Default;
        int rows = bounds.MaxChunkY - bounds.MinChunkY + 1;
        int totalRows = 0;

        Assert.True(pipeline.TryRequestGenerateColumn(0, 0, bounds.MinChunkY, bounds.MaxChunkY));
        await WaitUntilAsync(() => generator.Calls == 1);
        Assert.True(pipeline.TryRequestGenerateColumn(1, 0, bounds.MinChunkY, bounds.MaxChunkY));
        await WaitUntilAsync(() => generator.Calls == 2);
        Assert.False(pipeline.TryRequestGenerateColumn(2, 0, bounds.MinChunkY, bounds.MaxChunkY));

        await WaitUntilAsync(() =>
        {
            while (totalRows < rows && pipeline.TryTakeGenerated(out GeneratedChunk result))
            {
                ArrayPool<BlockId>.Shared.Return(result.Blocks);
                totalRows++;
            }

            return totalRows == rows;
        });

        Assert.True(pipeline.TryRequestGenerateColumn(2, 0, bounds.MinChunkY, bounds.MaxChunkY));
    }

    [Fact]
    public async Task TryTakeGenerated_GeneratorThrows_RethrowsOnTheCallingThread()
    {
        await using ChunkPipeline pipeline = new(
            new ThrowingGenerator(new InvalidOperationException("boom")), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);
        pipeline.TryRequestGenerateColumn(0, 0, 0, 0);

        await WaitForFaultAsync<InvalidOperationException>(pipeline);
    }

    [Fact]
    public async Task TryTakeGenerated_GeneratorThrowsCancellationWithoutShutdown_RethrowsOnTheCallingThread()
    {
        await using ChunkPipeline pipeline = new(
            new ThrowingGenerator(new OperationCanceledException()), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);
        pipeline.TryRequestGenerateColumn(0, 0, 0, 0);

        await WaitForFaultAsync<OperationCanceledException>(pipeline);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1, WorldBounds.Default);

        await pipeline.DisposeAsync();
        await pipeline.DisposeAsync();
    }

    private static Task WaitForFaultAsync<TException>(ChunkPipeline pipeline)
        where TException : Exception
        => WaitUntilAsync(() =>
        {
            try
            {
                pipeline.TryTakeGenerated(out _);
                return false;
            }
            catch (TException)
            {
                return true;
            }
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        while (!condition())
        {
            await Task.Delay(5, cts.Token);
        }
    }

    // A store with nothing stored whose generated saves wait until released, like a writer that has fallen behind.
    private sealed class GatedStore : IChunkStore
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public void ReadColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
            => found.Clear();

        public void Save(ChunkPosition position, ReadOnlySpan<BlockId> blocks) => throw new NotSupportedException();

        public async ValueTask SaveGeneratedAsync(ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingGenerator(ManualResetEventSlim release, int blockFromCall = 1, Exception? fault = null) : ITerrainGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Generate(ChunkPosition position, Span<BlockId> blocks)
            => GenerateColumn(position.X, position.Z, position.Y, position.Y, blocks);

        public void GenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks)
        {
            if (Interlocked.Increment(ref _calls) >= blockFromCall)
            {
                release.Wait(Timeout);
            }

            if (fault is not null)
            {
                throw fault;
            }

            blocks.Clear();
        }
    }

    private sealed class RecordingMesher : IChunkMesher
    {
        private readonly CulledChunkMesher _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public ChunkMesh? Result { get; private set; }

        public ChunkMesh Mesh(ChunkSnapshot snapshot)
        {
            ChunkMesh mesh = _inner.Mesh(snapshot);
            Result = mesh;
            Interlocked.Increment(ref _calls);
            return mesh;
        }
    }

    private sealed class ThrowingGenerator(Exception exception) : ITerrainGenerator
    {
        public void Generate(ChunkPosition position, Span<BlockId> blocks) => throw exception;
    }
}
