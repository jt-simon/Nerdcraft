// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Streaming;

/// <summary>
/// Runs terrain generation and meshing on background workers. Requests flow through a small bounded
/// channel so queued work stays fresh as the camera moves. Admitted requests reserve space for their
/// results until the main thread takes them, so workers can publish without waiting for the main thread.
/// </summary>
public sealed class ChunkPipeline : IChunkWorkQueue, IAsyncDisposable
{
    private static readonly Histogram<long> ResultDepth = StreamingMetrics.Meter.CreateHistogram<long>(
        "nerdcraft.streaming.result.depth", "{result}", "Completions waiting to be taken");
    private static readonly Histogram<long> ResultPayload = StreamingMetrics.Meter.CreateHistogram<long>(
        "nerdcraft.streaming.result.retained", "By", "Pooled block capacity or logical mesh payload waiting to be taken");
    private static readonly Histogram<double> ResultAge = StreamingMetrics.Meter.CreateHistogram<double>(
        "nerdcraft.streaming.result.age", "ms", "Time spent in the result queue");

    private readonly ITerrainGenerator _generator;
    private readonly IChunkMesher _mesher;
    private readonly Channel<ChunkJob> _requests;
    private readonly ResultQueue<GeneratedChunk> _generated = new("generation");
    private readonly ResultQueue<MeshedChunk> _meshed = new("mesh");
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task[] _workers;
    private readonly long _generatedCapacity;
    private readonly long _meshedCapacity;
    // Only the main thread reserves space and consumes results.
    private long _outstandingGeneratedRows;
    private long _outstandingMeshes;
    private ExceptionDispatchInfo? _fault;
    private Task? _disposal;

    /// <summary>Starts the worker tasks immediately.</summary>
    /// <param name="generator">Produces blocks for generate requests; called from worker threads.</param>
    /// <param name="mesher">Meshes snapshots for mesh requests; called from worker threads.</param>
    /// <param name="workerCount">Number of background worker tasks.</param>
    /// <param name="queueCapacity">
    /// Maximum requests waiting for a worker. Together with the worker count it sets independent limits for outstanding
    /// meshes and generated rows. Further requests are refused until space frees up.
    /// </param>
    public ChunkPipeline(ITerrainGenerator generator, IChunkMesher mesher, int workerCount, int queueCapacity = 32)
    {
        _generator = generator;
        _mesher = mesher;
        _meshedCapacity = (long)queueCapacity + workerCount;
        WorldBounds defaults = WorldBounds.Default;
        _generatedCapacity = _meshedCapacity * (defaults.MaxChunkY - defaults.MinChunkY + 1);
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
    /// <exception cref="ArgumentOutOfRangeException">The requested column cannot fit in a block array.</exception>
    public bool TryRequestGenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkY, minChunkY);
        long rows = (long)maxChunkY - minChunkY + 1;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, Array.MaxLength / Chunk.Volume, nameof(maxChunkY));
        // A taller custom world can still generate one column when no other generation is outstanding.
        if ((_outstandingGeneratedRows != 0 && rows > _generatedCapacity - _outstandingGeneratedRows)
            || !_requests.Writer.TryWrite(new ChunkJob(new ChunkPosition(chunkX, minChunkY, chunkZ), null, maxChunkY)))
        {
            return false;
        }

        _outstandingGeneratedRows += rows;
        return true;
    }

    /// <inheritdoc/>
    public bool TryRequestMesh(ChunkSnapshot snapshot)
    {
        if (_outstandingMeshes >= _meshedCapacity
            || !_requests.Writer.TryWrite(new ChunkJob(snapshot.Position, snapshot, snapshot.Position.Y)))
        {
            return false;
        }

        _outstandingMeshes++;
        return true;
    }

    /// <inheritdoc/>
    /// <exception cref="Exception">Rethrows the exception that stopped a worker, if one did.</exception>
    public bool TryTakeGenerated(out GeneratedChunk result)
    {
        ThrowIfFaulted();
        if (!_generated.TryTake(out result))
        {
            return false;
        }

        _outstandingGeneratedRows--;
        return true;
    }

    /// <inheritdoc/>
    /// <exception cref="Exception">Rethrows the exception that stopped a worker, if one did.</exception>
    public bool TryTakeMeshed(out MeshedChunk result)
    {
        ThrowIfFaulted();
        if (!_meshed.TryTake(out result))
        {
            return false;
        }

        _outstandingMeshes--;
        return true;
    }

    /// <summary>
    /// Stops accepting work, cancels and joins the workers, and returns the pooled storage of unread jobs and
    /// results. Call from the main thread after it stops taking results. Safe to call more than once.
    /// </summary>
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        Task cancellation = _cancellation.CancelAsync();
        _requests.Writer.TryComplete();
        try
        {
            await Task.WhenAll(cancellation, Task.WhenAll(_workers));
        }
        finally
        {
            while (_requests.Reader.TryRead(out ChunkJob job))
            {
                job.Snapshot?.Dispose();
            }

            _generated.CompleteAndDrain(static result => ArrayPool<BlockId>.Shared.Return(result.Blocks));
            _meshed.CompleteAndDrain(static result => result.Mesh.Dispose());
            _cancellation.Dispose();
        }
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

                Process(job, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
        catch (Exception exception)
        {
            // Surface on the main thread rather than silently losing a worker.
            Interlocked.CompareExchange(ref _fault, ExceptionDispatchInfo.Capture(exception), null);
        }
    }

    private void Process(ChunkJob job, CancellationToken cancellationToken)
    {
        if (job.Snapshot is null)
        {
            GenerateColumn(job, cancellationToken);
            return;
        }

        try
        {
            ChunkMesh mesh = _mesher.Mesh(job.Snapshot);
            _meshed.Add(new MeshedChunk(job.Position, job.Snapshot.Version, mesh),
                MeshPayloadBytes(mesh.Opaque) + MeshPayloadBytes(mesh.Translucent));
        }
        finally
        {
            job.Snapshot.Dispose();
        }
    }

    // Generates the whole column into one scratch buffer, then hands each row back in its own pooled array.
    private void GenerateColumn(ChunkJob job, CancellationToken cancellationToken)
    {
        int rows = job.MaxChunkY - job.Position.Y + 1;
        int length = rows * Chunk.Volume;
        BlockId[] scratch = ArrayPool<BlockId>.Shared.Rent(length);
        try
        {
            _generator.GenerateColumn(job.Position.X, job.Position.Z, job.Position.Y, job.MaxChunkY, scratch.AsSpan(0, length));
            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BlockId[] blocks = ArrayPool<BlockId>.Shared.Rent(Chunk.Volume);
                scratch.AsSpan(row * Chunk.Volume, Chunk.Volume).CopyTo(blocks);
                _generated.Add(new GeneratedChunk(job.Position with { Y = job.Position.Y + row }, blocks),
                    blocks.Length * sizeof(ushort));
            }
        }
        finally
        {
            ArrayPool<BlockId>.Shared.Return(scratch);
        }
    }

    private void ThrowIfFaulted() => Volatile.Read(ref _fault)?.Throw();

    private static int MeshPayloadBytes(MeshData mesh)
        => (mesh.VertexCount * ((6 * sizeof(float)) + 4)) + (mesh.IndexCount * sizeof(ushort));

    // A generate job (Snapshot is null) covers the column from Position.Y up to MaxChunkY; a mesh job covers Position only.
    private readonly record struct ChunkJob(ChunkPosition Position, ChunkSnapshot? Snapshot, int MaxChunkY);

    private sealed class ResultQueue<TResult>(string kind)
        where TResult : struct
    {
        private readonly Channel<Completion> _channel = Channel.CreateUnbounded<Completion>(
            new UnboundedChannelOptions { SingleReader = true });
        private long _depth;
        private long _payload;

        public void Add(TResult result, int bytes)
        {
            RecordChange(1, bytes);
            // The writer is completed only after all of its workers have exited.
            _channel.Writer.TryWrite(new Completion(result, bytes, StreamingMetrics.Start(ResultAge)));
        }

        public bool TryTake(out TResult result)
        {
            if (!_channel.Reader.TryRead(out Completion completion))
            {
                result = default;
                return false;
            }

            RecordRemoval(completion, "taken");
            result = completion.Result;
            return true;
        }

        public void CompleteAndDrain(Action<TResult> release)
        {
            _channel.Writer.TryComplete();
            while (_channel.Reader.TryRead(out Completion completion))
            {
                release(completion.Result);
                RecordRemoval(completion, "discarded");
            }
        }

        private void RecordRemoval(Completion completion, string disposition)
        {
            RecordChange(-1, -completion.Bytes);
            if (completion.Timestamp != 0 && ResultAge.Enabled)
            {
                ResultAge.Record(Stopwatch.GetElapsedTime(completion.Timestamp).TotalMilliseconds,
                    new KeyValuePair<string, object?>("kind", kind),
                    new KeyValuePair<string, object?>("disposition", disposition));
            }
        }

        private void RecordChange(int depth, int payload)
        {
            KeyValuePair<string, object?> tag = new("kind", kind);
            ResultDepth.Record(Interlocked.Add(ref _depth, depth), tag);
            ResultPayload.Record(Interlocked.Add(ref _payload, payload), tag);
        }

        private readonly record struct Completion(TResult Result, int Bytes, long Timestamp);
    }
}
