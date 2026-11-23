// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.World;

/// <summary>
/// The vertical extent of the world. Sea level is Y = 0. <c>MaxY</c> must be greater than <c>MinY</c>; the
/// struct does not enforce this, so consumers validate it (<c>GameSettings</c> and the terrain generator do).
/// An inverted range loads no chunks.
/// </summary>
/// <param name="MinY">Lowest block Y in the world, inclusive. This layer is filled with bedrock.</param>
/// <param name="MaxY">Highest block Y in the world, inclusive.</param>
public readonly record struct WorldBounds(int MinY, int MaxY)
{
    /// <summary>The default world: Y = -50 to Y = 200.</summary>
    public static WorldBounds Default => new(-50, 200);

    /// <summary>Chunk Y of the lowest chunk row, rounded down.</summary>
    public int MinChunkY => MinY >> Chunk.SizeShift;

    /// <summary>Chunk Y of the highest chunk row, rounded down.</summary>
    public int MaxChunkY => MaxY >> Chunk.SizeShift;
}
