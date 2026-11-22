// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.World;

/// <summary>A block coordinate in world space. Coordinates may be negative.</summary>
/// <param name="X">World X in blocks.</param>
/// <param name="Y">World Y in blocks; up is positive.</param>
/// <param name="Z">World Z in blocks.</param>
public readonly record struct BlockPosition(int X, int Y, int Z)
{
    /// <summary>The chunk that contains this block.</summary>
    // Arithmetic shift floors toward negative infinity, which is what chunk coordinates need.
    public ChunkPosition ChunkPosition => new(
        X >> Chunk.SizeShift,
        Y >> Chunk.SizeShift,
        Z >> Chunk.SizeShift);

    /// <summary>X within the containing chunk, in [0, <see cref="Chunk.Size"/>).</summary>
    public int LocalX => X & Chunk.Mask;

    /// <summary>Y within the containing chunk, in [0, <see cref="Chunk.Size"/>).</summary>
    public int LocalY => Y & Chunk.Mask;

    /// <summary>Z within the containing chunk, in [0, <see cref="Chunk.Size"/>).</summary>
    public int LocalZ => Z & Chunk.Mask;
}
