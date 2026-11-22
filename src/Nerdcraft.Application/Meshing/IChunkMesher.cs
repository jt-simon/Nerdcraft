// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Rendering;

namespace Nerdcraft.Application.Meshing;

/// <summary>Pure and thread-safe. The caller owns the returned mesh and must dispose it.</summary>
public interface IChunkMesher
{
    /// <summary>Builds a mesh for the chunk inside <paramref name="snapshot"/>, in chunk-local coordinates.</summary>
    MeshData Mesh(ChunkSnapshot snapshot);
}
