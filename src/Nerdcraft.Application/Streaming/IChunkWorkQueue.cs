// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Streaming;

/// <summary>Blocks produced by a worker, in a pooled array of at least <see cref="Chunk.Volume"/> elements.</summary>
/// <param name="Position">The generated chunk.</param>
/// <param name="Blocks">Block data, indexed by <see cref="Chunk.Index"/>; may be longer than <see cref="Chunk.Volume"/>.</param>
public readonly record struct GeneratedChunk(ChunkPosition Position, BlockId[] Blocks);

/// <summary>The finished meshes for the chunk contents at <see cref="Version"/>.</summary>
/// <param name="Position">The meshed chunk.</param>
/// <param name="Version">The <see cref="Chunk.Version"/> the mesh was built from.</param>
/// <param name="Mesh">The opaque and translucent meshes, in chunk-local coordinates; the caller disposes it.</param>
public readonly record struct MeshedChunk(ChunkPosition Position, int Version, ChunkMesh Mesh);

/// <summary>
/// Hands CPU-heavy chunk work to background workers. Every member is non-blocking and is called from the
/// main thread only. The caller owns taken results: it must return <see cref="GeneratedChunk.Blocks"/>
/// to <c>ArrayPool&lt;BlockId&gt;.Shared</c> and dispose <see cref="MeshedChunk.Mesh"/>.
/// </summary>
public interface IChunkWorkQueue
{
    /// <summary>
    /// Requests terrain for every chunk of one column, <paramref name="minChunkY"/> through <paramref name="maxChunkY"/>
    /// inclusive. Each chunk comes back as its own <see cref="GeneratedChunk"/>, in any order. Returns false when the
    /// queue is full; the caller retries on a later frame.
    /// </summary>
    /// <param name="chunkX">Chunk X of the column.</param>
    /// <param name="chunkZ">Chunk Z of the column.</param>
    /// <param name="minChunkY">Chunk Y of the lowest row.</param>
    /// <param name="maxChunkY">Chunk Y of the highest row. Must not be below <paramref name="minChunkY"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxChunkY"/> is below <paramref name="minChunkY"/>.</exception>
    bool TryRequestGenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY);

    /// <summary>Takes ownership of <paramref name="snapshot"/> only when it returns true.</summary>
    bool TryRequestMesh(ChunkSnapshot snapshot);

    /// <summary>Takes one finished generation result if any is ready; returns false otherwise.</summary>
    bool TryTakeGenerated(out GeneratedChunk result);

    /// <summary>Takes one finished mesh if any is ready; returns false otherwise.</summary>
    bool TryTakeMeshed(out MeshedChunk result);
}
