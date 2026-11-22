// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.World;

/// <summary>A chunk coordinate: world position divided by <see cref="Chunk.Size"/>, rounded down.</summary>
/// <param name="X">Chunk X.</param>
/// <param name="Y">Chunk Y.</param>
/// <param name="Z">Chunk Z.</param>
public readonly record struct ChunkPosition(int X, int Y, int Z)
{
    /// <summary>The world position of this chunk's lowest-coordinate corner block.</summary>
    public BlockPosition Origin => new(
        X << Chunk.SizeShift,
        Y << Chunk.SizeShift,
        Z << Chunk.SizeShift);

    /// <summary>The chunk displaced by the given number of chunks along each axis.</summary>
    public ChunkPosition Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);
}
