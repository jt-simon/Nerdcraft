// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application.Meshing;

/// <summary>One of the six faces of a chunk. Opposite faces differ only in the lowest bit, so <c>face ^ 1</c> is the opposite face.</summary>
public enum ChunkFace
{
    /// <summary>The face toward +X.</summary>
    PositiveX,

    /// <summary>The face toward -X.</summary>
    NegativeX,

    /// <summary>The face toward +Y (up).</summary>
    PositiveY,

    /// <summary>The face toward -Y (down).</summary>
    NegativeY,

    /// <summary>The face toward +Z.</summary>
    PositiveZ,

    /// <summary>The face toward -Z.</summary>
    NegativeZ,
}
