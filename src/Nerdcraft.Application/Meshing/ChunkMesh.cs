// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Rendering;

namespace Nerdcraft.Application.Meshing;

/// <summary>The meshes of one chunk, split by how they are drawn. Owns both; dispose to return their pooled storage.</summary>
/// <param name="Opaque">Faces of opaque blocks. May be empty.</param>
/// <param name="Translucent">Faces of translucent blocks such as water, drawn after all opaque meshes. May be empty.</param>
public sealed record ChunkMesh(MeshData Opaque, MeshData Translucent) : IDisposable
{
    /// <summary>Disposes both meshes.</summary>
    public void Dispose()
    {
        Opaque.Dispose();
        Translucent.Dispose();
    }
}
