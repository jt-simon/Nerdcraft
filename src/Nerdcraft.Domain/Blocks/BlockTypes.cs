// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.Blocks;

/// <summary>The built-in block types.</summary>
public static class BlockTypes
{
    /// <summary>Empty space; same as <see cref="BlockId.Air"/>.</summary>
    public static readonly BlockId Air = BlockId.Air;

    /// <summary>Solid rock that fills the ground below the dirt layer.</summary>
    public static readonly BlockId Stone = new(1);

    /// <summary>Soil found just beneath the grass surface.</summary>
    public static readonly BlockId Dirt = new(2);

    /// <summary>The top block of the terrain surface.</summary>
    public static readonly BlockId Grass = new(3);
}
