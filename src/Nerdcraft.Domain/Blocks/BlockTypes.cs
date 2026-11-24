// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.Blocks;

/// <summary>
/// The built-in block types. The numeric ids are part of the saved world format: never renumber or reuse one,
/// only add new ids.
/// </summary>
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

    /// <summary>Indestructible floor that fills the bottom layer of the world.</summary>
    public static readonly BlockId Bedrock = new(4);

    /// <summary>Loose grains found on beaches and on shallow seabeds.</summary>
    public static readonly BlockId Sand = new(5);

    /// <summary>Coarse stones that cover the deep seabed.</summary>
    public static readonly BlockId Gravel = new(6);

    /// <summary>Settled snow that caps the highest peaks.</summary>
    public static readonly BlockId Snow = new(7);

    /// <summary>Water that fills the terrain below sea level; the only translucent block.</summary>
    public static readonly BlockId Water = new(8);

    /// <summary>True when entities cannot pass through <paramref name="block"/>: every block except air and <see cref="Water"/>.</summary>
    public static bool IsSolid(BlockId block) => !block.IsAir && block != Water;

    /// <summary>True when <paramref name="block"/> is a liquid an entity can swim in; currently only <see cref="Water"/>.</summary>
    public static bool IsLiquid(BlockId block) => block == Water;

    /// <summary>True when a player can break <paramref name="block"/>: every solid block except <see cref="Bedrock"/>.</summary>
    public static bool IsBreakable(BlockId block) => IsSolid(block) && block != Bedrock;

    /// <summary>True when a placed block may take the place of <paramref name="block"/>: air and liquids.</summary>
    public static bool IsReplaceable(BlockId block) => block.IsAir || IsLiquid(block);
}
