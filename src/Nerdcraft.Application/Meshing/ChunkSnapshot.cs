// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Meshing;

/// <summary>
/// A private copy of a chunk plus a one-block border taken from its 26 neighbors (faces, edges and corners),
/// so a worker thread can mesh it, including ambient occlusion, without touching live world data. Dispose to
/// return the pooled buffer.
/// </summary>
public sealed class ChunkSnapshot : IDisposable
{
    /// <summary>Edge length of the padded block volume: the chunk plus one border block on each side.</summary>
    public const int PaddedSize = Chunk.Size + 2;

    // Every offset in {-1, 0, 1}^3 except (0, 0, 0): 6 face, 12 edge and 8 corner neighbors.
    private static readonly (int Dx, int Dy, int Dz)[] NeighborDirections = BuildNeighborDirections();

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

    /// <summary>Copies a loaded chunk and its borders from all 26 neighbors into a pooled buffer. Unloaded neighbors read as air.</summary>
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

        // Face culling reads only face neighbors, but ambient occlusion also reads the padding's edges and corners.
        foreach ((int dx, int dy, int dz) in NeighborDirections)
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

    // Fills the padded cells that belong to the neighbor at offset (dx, dy, dz): a whole face (16 x 16 cells), an
    // edge (16 cells) or a corner (1 cell). Every cell is written, so stale pooled data never leaks through.
    private static void CopyBorder(BlockId[] blocks, Chunk? neighbor, int dx, int dy, int dz)
    {
        (int x0, int x1) = AxisRange(dx);
        (int y0, int y1) = AxisRange(dy);
        (int z0, int z1) = AxisRange(dz);

        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    // The padded cell sits one step outside this chunk; the same cell in the neighbor is Size blocks back.
                    BlockId block = neighbor is null
                        ? BlockId.Air
                        : neighbor.Blocks[Chunk.Index(x - (dx * Chunk.Size), y - (dy * Chunk.Size), z - (dz * Chunk.Size))];
                    blocks[Index(x, y, z)] = block;
                }
            }
        }
    }

    // The padded coordinates a neighbor at this offset along one axis covers: the border cell on its side, or the whole chunk.
    private static (int First, int Last) AxisRange(int offset) => offset switch
    {
        < 0 => (-1, -1),
        > 0 => (Chunk.Size, Chunk.Size),
        _ => (0, Chunk.Size - 1),
    };

    private static (int Dx, int Dy, int Dz)[] BuildNeighborDirections()
    {
        List<(int Dx, int Dy, int Dz)> directions = [];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx | dy | dz) != 0)
                    {
                        directions.Add((dx, dy, dz));
                    }
                }
            }
        }

        return [.. directions];
    }
}
