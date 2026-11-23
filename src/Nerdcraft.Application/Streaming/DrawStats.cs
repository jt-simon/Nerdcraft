// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application.Streaming;

/// <summary>What one <see cref="ChunkStreamer.Draw"/> call submitted, for the debug overlay.</summary>
/// <param name="ChunksDrawn">Chunks submitted with at least one mesh: those inside the frustum and reachable from the camera's chunk through open space, so chunks hidden behind solid terrain are not counted.</param>
/// <param name="ChunksMeshed">Chunks that hold at least one uploaded mesh, whether or not they were in view.</param>
/// <param name="VerticesDrawn">Vertices in the opaque and translucent meshes of the chunks drawn.</param>
public readonly record struct DrawStats(int ChunksDrawn, int ChunksMeshed, long VerticesDrawn);
