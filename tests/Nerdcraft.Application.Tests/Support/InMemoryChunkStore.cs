// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Ports;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Support;

/// <summary>A thread-safe in-memory <see cref="IChunkStore"/> that counts saves per position.</summary>
public sealed class InMemoryChunkStore : IChunkStore
{
    private readonly object _gate = new();
    private readonly Dictionary<ChunkPosition, BlockId[]> _chunks = [];
    private readonly Dictionary<ChunkPosition, int> _saveCounts = [];
    private readonly Dictionary<ChunkPosition, int> _generatedCounts = [];

    public void ReadColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            for (int row = 0; row <= maxChunkY - minChunkY; row++)
            {
                if (_chunks.TryGetValue(new ChunkPosition(chunkX, minChunkY + row, chunkZ), out BlockId[]? stored))
                {
                    stored.CopyTo(blocks.Slice(row * Chunk.Volume, Chunk.Volume));
                    found[row] = true;
                }
                else
                {
                    found[row] = false;
                }
            }
        }
    }

    public void Save(ChunkPosition position, ReadOnlySpan<BlockId> blocks)
    {
        lock (_gate)
        {
            _chunks[position] = blocks.ToArray();
            _saveCounts[position] = _saveCounts.GetValueOrDefault(position) + 1;
        }
    }

    public ValueTask SaveGeneratedAsync(ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_chunks.ContainsKey(position))
            {
                _chunks[position] = blocks.ToArray();
                _generatedCounts[position] = _generatedCounts.GetValueOrDefault(position) + 1;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Returns the stored blocks at <paramref name="position"/>, or null when none were saved.</summary>
    public BlockId[]? Get(ChunkPosition position)
    {
        lock (_gate)
        {
            return _chunks.GetValueOrDefault(position);
        }
    }

    /// <summary>Returns how many times <paramref name="position"/> was saved.</summary>
    public int SaveCount(ChunkPosition position)
    {
        lock (_gate)
        {
            return _saveCounts.GetValueOrDefault(position);
        }
    }

    /// <summary>Returns how many times generated terrain was stored at <paramref name="position"/>.</summary>
    public int GeneratedCount(ChunkPosition position)
    {
        lock (_gate)
        {
            return _generatedCounts.GetValueOrDefault(position);
        }
    }

    /// <summary>Number of distinct positions stored.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _chunks.Count;
            }
        }
    }
}
