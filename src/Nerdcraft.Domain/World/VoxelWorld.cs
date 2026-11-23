// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World;

/// <summary>
/// The set of loaded chunks, addressed by block or chunk position. Not thread-safe: only the owning (main)
/// thread may use it. Background workers operate on snapshots.
/// </summary>
public sealed class VoxelWorld
{
    private readonly Dictionary<ChunkPosition, Chunk> _chunks = [];

    // Starts every new chunk above the version any unloaded chunk reached, so a version number identifies one
    // load of a chunk and results from an earlier load can never be mistaken for the current one.
    private int _versionFloor;

    /// <summary>Positions of every chunk currently loaded. A live view: do not load or unload chunks while enumerating.</summary>
    public IReadOnlyCollection<ChunkPosition> LoadedChunks => _chunks.Keys;

    /// <summary>Gets the chunk at <paramref name="position"/>; returns false if it is not loaded.</summary>
    public bool TryGetChunk(ChunkPosition position, out Chunk chunk) => _chunks.TryGetValue(position, out chunk!);

    /// <summary>True when a chunk exists at <paramref name="position"/>.</summary>
    public bool IsLoaded(ChunkPosition position) => _chunks.ContainsKey(position);

    /// <summary>Returns the block at <paramref name="position"/>, or air when its chunk is not loaded.</summary>
    public BlockId GetBlock(BlockPosition position)
    {
        return _chunks.TryGetValue(position.ChunkPosition, out Chunk? chunk)
            ? chunk.Get(position.LocalX, position.LocalY, position.LocalZ)
            : BlockId.Air;
    }

    /// <summary>
    /// Sets a block in an already loaded chunk. When the block changes, neighboring chunks that share a face,
    /// edge or corner with it are touched so their meshes are rebuilt. Chunks are created only by <see cref="LoadChunk"/>,
    /// so terrain generation can never overwrite an edit.
    /// </summary>
    /// <returns>False, changing nothing, when the chunk containing <paramref name="position"/> is not loaded.</returns>
    public bool SetBlock(BlockPosition position, BlockId block)
    {
        ChunkPosition chunkPosition = position.ChunkPosition;
        if (!_chunks.TryGetValue(chunkPosition, out Chunk? chunk))
        {
            return false;
        }

        int x = position.LocalX;
        int y = position.LocalY;
        int z = position.LocalZ;
        int before = chunk.Version;
        chunk.Set(x, y, z, block);
        if (chunk.Version == before)
        {
            return true;
        }

        // A block on a chunk border changes the padded view of every neighbor it touches: the face neighbor on each
        // side it lies against, plus the edge and corner neighbors when it lies against more than one.
        TouchNeighbors(
            chunkPosition,
            x == 0 ? -1 : 0,
            x == Chunk.Mask ? 1 : 0,
            y == 0 ? -1 : 0,
            y == Chunk.Mask ? 1 : 0,
            z == 0 ? -1 : 0,
            z == Chunk.Mask ? 1 : 0);
        return true;
    }

    /// <summary>Installs generated blocks, creating the chunk if needed, and invalidates the meshes of all 26 neighbors.</summary>
    public void LoadChunk(ChunkPosition position, ReadOnlySpan<BlockId> blocks)
    {
        if (!_chunks.TryGetValue(position, out Chunk? chunk))
        {
            chunk = new Chunk(_versionFloor);
            _chunks[position] = chunk;
        }

        chunk.Load(blocks);
        TouchNeighbors(position, -1, 1, -1, 1, -1, 1);
    }

    /// <summary>Removes a chunk; returns false if it was not loaded. Neighbors are not touched.</summary>
    public bool UnloadChunk(ChunkPosition position)
    {
        if (!_chunks.Remove(position, out Chunk? chunk))
        {
            return false;
        }

        _versionFloor = Math.Max(_versionFloor, chunk.Version + 1);
        return true;
    }

    // Touches every loaded neighbor whose offset lies in the given inclusive ranges, except the chunk itself.
    private void TouchNeighbors(ChunkPosition origin, int minDx, int maxDx, int minDy, int maxDy, int minDz, int maxDz)
    {
        for (int dy = minDy; dy <= maxDy; dy++)
        {
            for (int dz = minDz; dz <= maxDz; dz++)
            {
                for (int dx = minDx; dx <= maxDx; dx++)
                {
                    if ((dx | dy | dz) != 0 && _chunks.TryGetValue(origin.Offset(dx, dy, dz), out Chunk? neighbor))
                    {
                        neighbor.Touch();
                    }
                }
            }
        }
    }
}
