// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Streaming;

/// <summary>
/// Runs terrain generation and meshing on background workers. Requests flow through a small bounded
/// channel so queued work stays fresh as the camera moves; results come back on unbounded channels
/// (their size is limited by the number of in-flight requests).
/// </summary>
public sealed class ChunkPipeline : IChunkWorkQueue, IAsyncDisposable
{
    private readonly ITerrainGenerator _generator;
    private readonly IChunkMesher _mesher;
    private readonly Channel<ChunkJob> _requests;
    private readonly Channel<GeneratedChunk> _generated = Channel.CreateUnbounded<GeneratedChunk>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<MeshedChunk> _meshed = Channel.CreateUnbounded<MeshedChunk>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task[] _workers;
    private ExceptionDispatchInfo? _fault;
    private Task? _disposal;

    /// <summary>Starts the worker tasks immediately.</summary>
    /// <param name="generator">Produces blocks for generate requests; called from worker threads.</param>
    /// <param name="mesher">Meshes snapshots for mesh requests; called from worker threads.</param>
    /// <param name="workerCount">Number of background worker tasks.</param>
    /// <param name="queueCapacity">Maximum requests waiting for a worker; further requests are refused until space frees up.</param>
    public ChunkPipeline(ITerrainGenerator generator, IChunkMesher mesher, int workerCount, int queueCapacity = 32)
    {
        _generator = generator;
        _mesher = mesher;
        _requests = Channel.CreateBounded<ChunkJob>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
        });

        _workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(() => RunWorkerAsync(_cancellation.Token));
        }
    }

    /// <inheritdoc/>
    public bool TryRequestGenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkY, minChunkY);
        return _requests.Writer.TryWrite(new ChunkJob(new ChunkPosition(chunkX, minChunkY, chunkZ), null, maxChunkY));
    }

    /// <inheritdoc/>
    public bool TryRequestMesh(ChunkSnapshot snapshot)
        => _requests.Writer.TryWrite(new ChunkJob(snapshot.Position, snapshot, snapshot.Position.Y));

    /// <inheritdoc/>
    /// <exception cref="Exception">Rethrows the exception that stopped a worker, if one did.</exception>
    public bool TryTakeGenerated(out GeneratedChunk result)
    {
        ThrowIfFaulted();
        return _generated.Reader.TryRead(out result);
    }

    /// <inheritdoc/>
    /// <exception cref="Exception">Rethrows the exception that stopped a worker, if one did.</exception>
    public bool TryTakeMeshed(out MeshedChunk result)
    {
        ThrowIfFaulted();
        return _meshed.Reader.TryRead(out result);
    }

    /// <summary>Stops accepting work, cancels the workers, and waits for them to exit. Safe to call more than once.</summary>
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _requests.Writer.TryComplete();
        await _cancellation.CancelAsync();
        await Task.WhenAll(_workers);
        _cancellation.Dispose();
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ChunkJob job in _requests.Reader.ReadAllAsync(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    job.Snapshot?.Dispose();
                    return;
                }

                Process(job);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
        catch (Exception exception)
        {
            // Surface on the main thread rather than silently losing a worker.
            _fault ??= ExceptionDispatchInfo.Capture(exception);
        }
    }

    private void Process(ChunkJob job)
    {
        if (job.Snapshot is null)
        {
            GenerateColumn(job);
            return;
        }

        try
        {
            _meshed.Writer.TryWrite(new MeshedChunk(job.Position, job.Snapshot.Version, _mesher.Mesh(job.Snapshot)));
        }
        finally
        {
            job.Snapshot.Dispose();
        }
    }

    // Generates the whole column into one scratch buffer, then hands each row back in its own pooled array.
    private void GenerateColumn(ChunkJob job)
    {
        int rows = job.MaxChunkY - job.Position.Y + 1;
        int length = rows * Chunk.Volume;
        BlockId[] scratch = ArrayPool<BlockId>.Shared.Rent(length);
        try
        {
            _generator.GenerateColumn(job.Position.X, job.Position.Z, job.Position.Y, job.MaxChunkY, scratch.AsSpan(0, length));
            for (int row = 0; row < rows; row++)
            {
                BlockId[] blocks = ArrayPool<BlockId>.Shared.Rent(Chunk.Volume);
                scratch.AsSpan(row * Chunk.Volume, Chunk.Volume).CopyTo(blocks);
                _generated.Writer.TryWrite(new GeneratedChunk(job.Position with { Y = job.Position.Y + row }, blocks));
            }
        }
        finally
        {
            ArrayPool<BlockId>.Shared.Return(scratch);
        }
    }

    private void ThrowIfFaulted() => _fault?.Throw();

    // A generate job (Snapshot is null) covers the column from Position.Y up to MaxChunkY; a mesh job covers Position only.
    private readonly record struct ChunkJob(ChunkPosition Position, ChunkSnapshot? Snapshot, int MaxChunkY);
}
