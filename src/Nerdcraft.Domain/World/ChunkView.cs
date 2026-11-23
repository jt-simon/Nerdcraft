// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World;

/// <summary>
/// Read-only access to a live chunk on the world's owning thread. Obtain an initialized view
/// from a successful <see cref="VoxelWorld.TryGetChunk"/> call.
/// </summary>
public readonly struct ChunkView
{
    private readonly Chunk _chunk;

    internal ChunkView(Chunk chunk) => _chunk = chunk;

    /// <inheritdoc cref="Chunk.Version"/>
    public int Version => _chunk.Version;

    /// <inheritdoc cref="Chunk.IsEmpty"/>
    public bool IsEmpty => _chunk.IsEmpty;

    /// <inheritdoc cref="Chunk.Blocks"/>
    public ReadOnlySpan<BlockId> Blocks => _chunk.Blocks;

    /// <inheritdoc cref="Chunk.Get"/>
    public BlockId Get(int x, int y, int z) => _chunk.Get(x, y, z);
}
