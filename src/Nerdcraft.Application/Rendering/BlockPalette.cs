// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Application.Rendering;

/// <summary>Maps block types to display colors, keeping color choices out of the domain.</summary>
public static class BlockPalette
{
    private static readonly Color Missing = new(255, 0, 255);

    /// <summary>The color for <paramref name="block"/>; unknown types (including air) get magenta so gaps are obvious.</summary>
    public static Color GetColor(BlockId block)
    {
        if (block == BlockTypes.Stone)
        {
            return new Color(128, 128, 132);
        }

        if (block == BlockTypes.Dirt)
        {
            return new Color(134, 96, 67);
        }

        if (block == BlockTypes.Grass)
        {
            return new Color(88, 160, 62);
        }

        return Missing;
    }
}
