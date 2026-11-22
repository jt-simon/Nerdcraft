// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Meshing;

/// <summary>
/// A private copy of a chunk plus a one-block border taken from its six face neighbors, so a worker
/// thread can mesh it without touching live world data. Dispose to return the pooled buffer.
/// </summary>
public sealed class ChunkSnapshot : IDisposable
{
    /// <summary>Edge length of the padded block volume: the chunk plus one border block on each side.</summary>
    public const int PaddedSize = Chunk.Size + 2;

    private static readonly (int Dx, int Dy, int Dz)[] FaceDirections =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    private BlockId[]? _blocks;

    private ChunkSnapshot(ChunkPosition position, int version, BlockId[] blocks)
    {
        Position = position;
        Version = version;
        _blocks = blocks;
    }

    /// <summary>The chunk this snapshot was taken from.</summary>
    public ChunkPosition Position { get; }

    /// <summary>The <see cref="Chunk.Version"/> at the time of the snapshot; lets a stale mesh be recognized.</summary>
    public int Version { get; }

    /// <summary>Padded blocks indexed by <see cref="Index"/>.</summary>
    public ReadOnlySpan<BlockId> Blocks => _blocks.AsSpan(0, PaddedSize * PaddedSize * PaddedSize);

    /// <summary>Index for coordinates in [-1, Chunk.Size], where -1 and Chunk.Size are the border.</summary>
    public static int Index(int x, int y, int z) => ((((y + 1) * PaddedSize) + (z + 1)) * PaddedSize) + (x + 1);

    /// <summary>Copies a loaded chunk and its face-neighbor borders into a pooled buffer. Unloaded neighbors read as air.</summary>
    /// <exception cref="InvalidOperationException">The chunk at <paramref name="position"/> is not loaded.</exception>
    public static ChunkSnapshot Create(VoxelWorld world, ChunkPosition position)
    {
        if (!world.TryGetChunk(position, out Chunk chunk))
        {
            throw new InvalidOperationException($"Chunk {position} is not loaded.");
        }

        BlockId[] blocks = ArrayPool<BlockId>.Shared.Rent(PaddedSize * PaddedSize * PaddedSize);
        ReadOnlySpan<BlockId> source = chunk.Blocks;

        for (int y = 0; y < Chunk.Size; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                source.Slice(Chunk.Index(0, y, z), Chunk.Size).CopyTo(blocks.AsSpan(Index(0, y, z), Chunk.Size));
            }
        }

        // Edges and corners of the padding are never read: face culling only looks at face neighbors.
        foreach ((int dx, int dy, int dz) in FaceDirections)
        {
            world.TryGetChunk(position.Offset(dx, dy, dz), out Chunk? neighbor);
            CopyBorder(blocks, neighbor, dx, dy, dz);
        }

        return new ChunkSnapshot(position, chunk.Version, blocks);
    }

    /// <summary>Returns the pooled buffer. Safe to call more than once; <see cref="Blocks"/> must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_blocks is not null)
        {
            ArrayPool<BlockId>.Shared.Return(_blocks);
            _blocks = null;
        }
    }

    private static void CopyBorder(BlockId[] blocks, Chunk? neighbor, int dx, int dy, int dz)
    {
        for (int u = 0; u < Chunk.Size; u++)
        {
            for (int v = 0; v < Chunk.Size; v++)
            {
                // (x, y, z) is the edge cell inside this chunk; the border cell sits one step further out.
                int x = dx != 0 ? (dx < 0 ? 0 : Chunk.Mask) : u;
                int y = dy != 0 ? (dy < 0 ? 0 : Chunk.Mask) : (dx != 0 ? u : v);
                int z = dz != 0 ? (dz < 0 ? 0 : Chunk.Mask) : v;

                BlockId block = BlockId.Air;
                if (neighbor is not null)
                {
                    block = neighbor.Blocks[Chunk.Index(x - (dx * Chunk.Mask), y - (dy * Chunk.Mask), z - (dz * Chunk.Mask))];
                }

                blocks[Index(x + dx, y + dy, z + dz)] = block;
            }
        }
    }
}
