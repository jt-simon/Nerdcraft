// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Meshing;
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
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 3);
        List<ChunkPosition> requested = [.. Enumerable.Range(0, 20).Select(i => new ChunkPosition(i, 1, -i))];
        foreach (ChunkPosition position in requested)
        {
            Assert.True(pipeline.TryRequestGenerate(position));
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
    public async Task Mesh_Request_ReturnsMeshForTheSnapshotVersion()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(new ChunkPosition(0, 0, 0));
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);
        await using ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1);
        ChunkSnapshot snapshot = ChunkSnapshot.Create(world, new ChunkPosition(0, 0, 0));
        int version = snapshot.Version;

        Assert.True(pipeline.TryRequestMesh(snapshot));

        MeshedChunk result = default;
        await WaitUntilAsync(() => pipeline.TryTakeMeshed(out result));
        using MeshData mesh = result.Mesh;
        Assert.Equal(version, result.Version);
        Assert.Equal(24, mesh.VertexCount);
    }

    [Fact]
    public async Task Request_WhenQueueIsFull_ReturnsFalse()
    {
        using ManualResetEventSlim release = new();
        await using ChunkPipeline pipeline = new(
            new BlockingGenerator(release), new CulledChunkMesher(), workerCount: 1, queueCapacity: 2);

        // The worker takes one job and blocks; two more fill the queue.
        int accepted = 0;
        while (pipeline.TryRequestGenerate(new ChunkPosition(accepted, 0, 0)) && accepted < 10)
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
        SlowGenerator generator = new(TimeSpan.FromMilliseconds(50));
        ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 1, queueCapacity: 32);
        for (int i = 0; i < 30; i++)
        {
            pipeline.TryRequestGenerate(new ChunkPosition(i, 0, 0));
        }

        await WaitUntilAsync(() => generator.Calls >= 1); // Dispose only once the worker has really started.
        await pipeline.DisposeAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.InRange(generator.Calls, 1, 29);
    }

    [Fact]
    public async Task TryTakeGenerated_GeneratorThrows_RethrowsOnTheCallingThread()
    {
        await using ChunkPipeline pipeline = new(
            new ThrowingGenerator(new InvalidOperationException("boom")), new CulledChunkMesher(), workerCount: 1);
        pipeline.TryRequestGenerate(new ChunkPosition(0, 0, 0));

        await WaitForFaultAsync<InvalidOperationException>(pipeline);
    }

    [Fact]
    public async Task TryTakeGenerated_GeneratorThrowsCancellationWithoutShutdown_RethrowsOnTheCallingThread()
    {
        await using ChunkPipeline pipeline = new(
            new ThrowingGenerator(new OperationCanceledException()), new CulledChunkMesher(), workerCount: 1);
        pipeline.TryRequestGenerate(new ChunkPosition(0, 0, 0));

        await WaitForFaultAsync<OperationCanceledException>(pipeline);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        ChunkPipeline pipeline = new(new HeightmapTerrainGenerator(5, WorldBounds.Default), new CulledChunkMesher(), workerCount: 1);

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

    private sealed class BlockingGenerator(ManualResetEventSlim release) : ITerrainGenerator
    {
        public void Generate(ChunkPosition position, Span<BlockId> blocks) => release.Wait(Timeout);
    }

    private sealed class SlowGenerator(TimeSpan delay) : ITerrainGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Generate(ChunkPosition position, Span<BlockId> blocks)
        {
            Interlocked.Increment(ref _calls);
            Thread.Sleep(delay);
        }
    }

    private sealed class ThrowingGenerator(Exception exception) : ITerrainGenerator
    {
        public void Generate(ChunkPosition position, Span<BlockId> blocks) => throw exception;
    }
}
