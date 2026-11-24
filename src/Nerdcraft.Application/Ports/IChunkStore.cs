// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Ports;

/// <summary>
/// Durable storage for chunk blocks within one dimension. An implementation may limit the chunk coordinates it accepts, and then
/// throws <see cref="ArgumentOutOfRangeException"/> for any other position. The SQLite store accepts X and Z from -2^21 to 2^21 - 1
/// and Y from -2^11 to 2^11 - 1.
/// </summary>
public interface IChunkStore
{
    /// <summary>
    /// Reads the stored chunks of one column. Blocks the calling thread while it reads, so call it from a worker, never the main thread.
    /// Safe to call from several worker threads at once. Sees every earlier <see cref="Save"/> from any thread, even one not yet
    /// written to disk. Terrain from <see cref="SaveGeneratedAsync"/> may be missing until it is written; a caller then generates the
    /// same terrain again.
    /// </summary>
    /// <param name="chunkX">Chunk X of the column.</param>
    /// <param name="chunkZ">Chunk Z of the column.</param>
    /// <param name="minChunkY">Chunk Y of the lowest row, inclusive.</param>
    /// <param name="maxChunkY">Chunk Y of the highest row, inclusive.</param>
    /// <param name="blocks">
    /// Destination of length <c>(maxChunkY - minChunkY + 1) * <see cref="Chunk.Volume"/></c>, rows stacked from
    /// <paramref name="minChunkY"/> upward. Only rows that were found are written.
    /// </param>
    /// <param name="found">Destination with one element per row; each is set to whether that row was found in storage.</param>
    /// <param name="cancellationToken">Checked before the read and between rows; it cannot interrupt a statement that is already running.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    void ReadColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the blocks of one chunk, replacing any earlier save of the same position. Safe to call from any thread.
    /// Copies <paramref name="blocks"/> and returns without waiting for the disk, so the caller may reuse its buffer.
    /// When one position is saved twice, the later call wins.
    /// </summary>
    /// <param name="position">The chunk to store.</param>
    /// <param name="blocks">Exactly <see cref="Chunk.Volume"/> blocks, indexed by <see cref="Chunk.Index"/>.</param>
    void Save(ChunkPosition position, ReadOnlySpan<BlockId> blocks);

    /// <summary>
    /// Stores freshly generated terrain for <paramref name="position"/> unless a chunk is already stored or waiting to be
    /// stored there, in which case it does nothing. A worker that generated a chunk before another thread saved an edit to it
    /// therefore cannot overwrite the edit. Safe to call from any thread.
    /// </summary>
    /// <remarks>
    /// Completes without waiting for the disk, except that it waits for room while too much generated terrain is already queued
    /// to be written. That keeps memory bounded and makes generation keep pace with the disk. The blocks are copied before the
    /// returned task completes, so the caller must leave <paramref name="blocks"/> unchanged until then.
    /// </remarks>
    /// <param name="position">The chunk to store.</param>
    /// <param name="blocks">Exactly <see cref="Chunk.Volume"/> blocks, indexed by <see cref="Chunk.Index"/>.</param>
    /// <param name="cancellationToken">Cancels the wait for room.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting.</exception>
    ValueTask SaveGeneratedAsync(ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken);
}
