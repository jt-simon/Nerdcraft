// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using Microsoft.Data.Sqlite;

using Nerdcraft.Application.Ports;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>
/// The chunk table of a world. Saves are copied and queued, and one writer task commits them in batches on the writer
/// connection. Until a <see cref="IChunkStore.Save"/> is committed it also sits in an in-memory overlay that reads check first,
/// so a read always sees the latest save. Reads use their own pooled read-only connections, which WAL mode lets run beside the writer.
/// Generated terrain is not kept in the overlay and its queue is bounded, so memory stays bounded when generation outruns the disk.
/// </summary>
internal sealed class SqliteChunkStore : IAsyncDisposable
{
    /// <summary>How many generated chunks may wait for the writer by default (about 8 MB).</summary>
    internal const int DefaultMaxPendingGenerated = 1024;

    private const int MaxBatch = 512;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    private readonly SqliteConnection _writer;
    private readonly string _readConnectionString;
    private readonly SqliteMaintenance _maintenance;
    private readonly TimeProvider _time;
    private readonly ChunkCodec _codec = new();
    private readonly Channel<WriteItem> _queue = Channel.CreateUnbounded<WriteItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _gate = new();
    private readonly Dictionary<long, Pending> _pending = [];
    private readonly SemaphoreSlim _generatedSlots;
    private readonly int _maxPendingGenerated;
    private readonly Task _writerTask;
    private long _sequence;
    private int _queued;
    private int _pendingGenerated;
    private int _peakPendingGenerated;
    private int _readFailed;
    private ExceptionDispatchInfo? _fault;
    private Task? _disposal;

    /// <summary>Starts the writer task, which is the only user of <paramref name="writer"/> until <see cref="DisposeAsync"/> completes.</summary>
    public SqliteChunkStore(
        SqliteConnection writer, string readConnectionString, SqliteMaintenance maintenance, TimeProvider time, int maxPendingGenerated = DefaultMaxPendingGenerated)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPendingGenerated, 1);
        _writer = writer;
        _readConnectionString = readConnectionString;
        _maintenance = maintenance;
        _time = time;
        _maxPendingGenerated = maxPendingGenerated;
        _generatedSlots = new SemaphoreSlim(maxPendingGenerated);
        _writerTask = Task.Run(RunWriterAsync);
    }

    /// <summary>True once any column read failed because the stored data could not be read, so the file should be checked at the next open.</summary>
    internal bool ReadFailed => Volatile.Read(ref _readFailed) != 0;

    /// <summary>The most generated chunks that were ever waiting for the writer at once.</summary>
    internal int PeakPendingGenerated => Volatile.Read(ref _peakPendingGenerated);

    /// <summary>A store for one dimension; all dimensions share this store's writer.</summary>
    public IChunkStore ForDimension(byte dimension) => new DimensionStore(this, dimension);

    /// <summary>Stops accepting saves, commits everything queued, and stops the writer. Rethrows the exception that stopped the writer, if any.</summary>
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _queue.Writer.TryComplete();
        await _writerTask;
        _codec.Dispose();
        ThrowIfFaulted();
    }

    private void ReadColumn(
        byte dimension, int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFaulted();
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkY, minChunkY);
        long first = ChunkKey.Pack(dimension, chunkX, minChunkY, chunkZ);
        long last = ChunkKey.Pack(dimension, chunkX, maxChunkY, chunkZ);
        int rows = maxChunkY - minChunkY + 1;
        if (blocks.Length != rows * Chunk.Volume || found.Length != rows)
        {
            throw new ArgumentException("The destination does not match the number of rows.");
        }

        long started = PersistenceMetrics.Start(PersistenceMetrics.ReadTime);
        BlockId[]?[] saved = ArrayPool<BlockId[]?>.Shared.Rent(rows);
        try
        {
            // Saves not yet committed come first: they are newer than anything in the table. They are never modified once
            // published, so only the lookup is under the lock and the copying is not.
            int remaining = rows;
            lock (_gate)
            {
                for (int row = 0; row < rows; row++)
                {
                    saved[row] = _pending.TryGetValue(first + row, out Pending pending) ? pending.Blocks : null;
                }
            }

            for (int row = 0; row < rows; row++)
            {
                if (saved[row] is { } overlay)
                {
                    overlay.CopyTo(blocks.Slice(row * Chunk.Volume, Chunk.Volume));
                    found[row] = true;
                    remaining--;
                }
                else
                {
                    found[row] = false;
                }
            }

            if (remaining > 0)
            {
                try
                {
                    ReadStored(chunkX, chunkZ, minChunkY, first, last, blocks, found, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    Volatile.Write(ref _readFailed, 1);
                    throw;
                }
            }
        }
        finally
        {
            ArrayPool<BlockId[]?>.Shared.Return(saved, clearArray: true);
            PersistenceMetrics.RecordElapsed(PersistenceMetrics.ReadTime, started);
        }
    }

    // Corruption is reported as InvalidDataException and any other database failure as IOException, each naming the column.
    private void ReadStored(int chunkX, int chunkZ, int minChunkY, long first, long last, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
    {
        try
        {
            using SqliteConnection connection = new(_readConnectionString);
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT key, data FROM chunks WHERE key BETWEEN $first AND $last";
            command.Parameters.AddWithValue("$first", first);
            command.Parameters.AddWithValue("$last", last);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                int row = (int)(reader.GetInt64(0) - first);
                if (!found[row])
                {
                    try
                    {
                        _codec.Decode(reader.GetFieldValue<byte[]>(1), blocks.Slice(row * Chunk.Volume, Chunk.Volume));
                    }
                    catch (InvalidDataException exception)
                    {
                        // Never regenerated in its place: that would silently discard what the player built there.
                        throw new InvalidDataException($"The saved chunk ({chunkX}, {minChunkY + row}, {chunkZ}) is unreadable: {exception.Message}", exception);
                    }

                    found[row] = true;
                }
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            throw new InvalidDataException($"The saved column ({chunkX}, {chunkZ}) is damaged: {exception.Message}", exception);
        }
        catch (SqliteException exception)
        {
            throw new IOException($"The saved column ({chunkX}, {chunkZ}) could not be read: {exception.Message}", exception);
        }
    }

    private void Save(byte dimension, ChunkPosition position, ReadOnlySpan<BlockId> blocks)
    {
        ThrowIfFaulted();
        if (blocks.Length != Chunk.Volume)
        {
            throw new ArgumentException($"A chunk requires exactly {Chunk.Volume} blocks.", nameof(blocks));
        }

        long key = ChunkKey.Pack(dimension, position.X, position.Y, position.Z);

        // A fresh array that nothing modifies afterwards: the overlay's readers and the writer share it without copying.
        BlockId[] copy = blocks.ToArray();
        bool queued;
        lock (_gate)
        {
            long sequence = ++_sequence;
            queued = TryEnqueue(new WriteItem(key, copy, sequence, OnlyIfAbsent: false));
            if (queued)
            {
                _pending[key] = new Pending(copy, sequence);
            }
        }

        if (!queued)
        {
            ThrowIfFaulted();
            throw new ObjectDisposedException(nameof(SqliteChunkStore));
        }
    }

    private async ValueTask SaveGeneratedAsync(byte dimension, ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        if (blocks.Length != Chunk.Volume)
        {
            throw new ArgumentException($"A chunk requires exactly {Chunk.Volume} blocks.", nameof(blocks));
        }

        long key = ChunkKey.Pack(dimension, position.X, position.Y, position.Z);

        // Waits here, off the main thread, while the writer is too far behind.
        await _generatedSlots.WaitAsync(cancellationToken);
        BlockId[]? copy = null;
        bool queued = false;
        try
        {
            ThrowIfFaulted(); // A writer failure releases the waiters so they can see it.
            copy = ArrayPool<BlockId>.Shared.Rent(Chunk.Volume);
            blocks.Span.CopyTo(copy);
            lock (_gate)
            {
                if (_pending.ContainsKey(key))
                {
                    return; // An edit is waiting to be written and wins.
                }

                int pending = Interlocked.Increment(ref _pendingGenerated);
                queued = TryEnqueue(new WriteItem(key, copy, 0, OnlyIfAbsent: true));
                if (queued)
                {
                    RecordPeak(pending);
                }
                else
                {
                    Interlocked.Decrement(ref _pendingGenerated);
                }
            }

            if (!queued)
            {
                throw new ObjectDisposedException(nameof(SqliteChunkStore));
            }
        }
        finally
        {
            if (!queued)
            {
                _generatedSlots.Release();
                if (copy is not null)
                {
                    ArrayPool<BlockId>.Shared.Return(copy);
                }
            }
        }
    }

    private bool TryEnqueue(WriteItem item)
    {
        Interlocked.Increment(ref _queued);
        if (_queue.Writer.TryWrite(item))
        {
            return true;
        }

        Interlocked.Decrement(ref _queued);
        return false;
    }

    private void RecordPeak(int value)
    {
        int peak;
        while ((peak = Volatile.Read(ref _peakPendingGenerated)) < value
            && Interlocked.CompareExchange(ref _peakPendingGenerated, value, peak) != peak)
        {
        }
    }

    private async Task RunWriterAsync()
    {
        ChannelReader<WriteItem> reader = _queue.Reader;
        List<WriteItem> batch = new(MaxBatch);
        try
        {
            while (true)
            {
                bool available;
                using (CancellationTokenSource idle = new(IdleDelay, _time))
                {
                    try
                    {
                        available = await reader.WaitToReadAsync(idle.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _maintenance.RunIdle(_writer);
                        available = await reader.WaitToReadAsync();
                    }
                }

                if (!available)
                {
                    return;
                }

                PersistenceMetrics.QueueDepth.Record(Volatile.Read(ref _queued));
                while (batch.Count < MaxBatch && reader.TryRead(out WriteItem item))
                {
                    batch.Add(item);
                }

                WriteBatch(batch);
                batch.Clear();
            }
        }
        catch (Exception exception)
        {
            // Surfaced on the next save, read or dispose rather than lost with the task.
            Interlocked.CompareExchange(ref _fault, ExceptionDispatchInfo.Capture(exception), null);
            _queue.Writer.TryComplete();
            while (reader.TryRead(out WriteItem item))
            {
                if (item.OnlyIfAbsent)
                {
                    ArrayPool<BlockId>.Shared.Return(item.Blocks);
                }
            }

            // Wake workers waiting for room; they find the fault and stop.
            _generatedSlots.Release(_maxPendingGenerated);
        }
    }

    private void WriteBatch(List<WriteItem> batch)
    {
        long started = PersistenceMetrics.Start(PersistenceMetrics.CommitTime);
        using (SqliteTransaction transaction = _writer.BeginTransaction())
        {
            using SqliteCommand upsert = CreateInsert(transaction, "ON CONFLICT(key) DO UPDATE SET data = excluded.data");
            using SqliteCommand insertNew = CreateInsert(transaction, "ON CONFLICT(key) DO NOTHING");
            foreach (WriteItem item in batch)
            {
                if (item.Sequence != 0)
                {
                    lock (_gate)
                    {
                        // A newer save of this chunk is queued behind this one, so writing this one would be wasted work.
                        if (!_pending.TryGetValue(item.Key, out Pending pending) || pending.Sequence != item.Sequence)
                        {
                            continue;
                        }
                    }
                }

                SqliteCommand command = item.OnlyIfAbsent ? insertNew : upsert;
                command.Parameters["$key"].Value = item.Key;
                command.Parameters["$data"].Value = _codec.Encode(item.Blocks.AsSpan(0, Chunk.Volume));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // Committed: reads can find these in the table now. A save that was replaced meanwhile keeps its newer overlay entry.
        int generated = 0;
        lock (_gate)
        {
            foreach (WriteItem item in batch)
            {
                if (item.Sequence != 0 && _pending.TryGetValue(item.Key, out Pending pending) && pending.Sequence == item.Sequence)
                {
                    _pending.Remove(item.Key);
                }
            }
        }

        foreach (WriteItem item in batch)
        {
            if (item.OnlyIfAbsent)
            {
                ArrayPool<BlockId>.Shared.Return(item.Blocks);
                generated++;
            }
        }

        Interlocked.Add(ref _queued, -batch.Count);
        if (generated > 0)
        {
            Interlocked.Add(ref _pendingGenerated, -generated);
            _generatedSlots.Release(generated);
        }

        PersistenceMetrics.BatchSize.Record(batch.Count);
        PersistenceMetrics.RecordElapsed(PersistenceMetrics.CommitTime, started);
    }

    private SqliteCommand CreateInsert(SqliteTransaction transaction, string conflictClause)
    {
        SqliteCommand command = _writer.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO chunks(key, data) VALUES($key, $data) " + conflictClause;
        command.Parameters.Add("$key", SqliteType.Integer);
        command.Parameters.Add("$data", SqliteType.Blob);
        return command;
    }

    private void ThrowIfFaulted() => Volatile.Read(ref _fault)?.Throw();

    // Sequence 0 marks generated terrain, which is not kept in the overlay. A generated item's array is pooled and owned by the
    // queue until it is written; an edit's array is a plain one shared with the overlay and never returned.
    private readonly record struct WriteItem(long Key, BlockId[] Blocks, long Sequence, bool OnlyIfAbsent);

    private readonly record struct Pending(BlockId[] Blocks, long Sequence);

    private sealed class DimensionStore(SqliteChunkStore core, byte dimension) : IChunkStore
    {
        public void ReadColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
            => core.ReadColumn(dimension, chunkX, chunkZ, minChunkY, maxChunkY, blocks, found, cancellationToken);

        public void Save(ChunkPosition position, ReadOnlySpan<BlockId> blocks) => core.Save(dimension, position, blocks);

        public ValueTask SaveGeneratedAsync(ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken)
            => core.SaveGeneratedAsync(dimension, position, blocks, cancellationToken);
    }
}
