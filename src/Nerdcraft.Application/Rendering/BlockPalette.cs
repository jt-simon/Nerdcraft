// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Application.Rendering;

/// <summary>Maps block types to display colors, keeping color choices out of the domain.</summary>
public static class BlockPalette
{
    private static readonly Color Missing = new(255, 0, 255);

    /// <summary>The color for <paramref name="block"/>, with an alpha below 255 for translucent blocks; unknown types (including air) get magenta so gaps are obvious.</summary>
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

        if (block == BlockTypes.Bedrock)
        {
            return new Color(48, 48, 52);
        }

        if (block == BlockTypes.Sand)
        {
            return new Color(219, 204, 150);
        }

        if (block == BlockTypes.Gravel)
        {
            return new Color(136, 126, 118);
        }

        if (block == BlockTypes.Snow)
        {
            return new Color(240, 244, 248);
        }

        if (block == BlockTypes.Water)
        {
            return new Color(52, 110, 200, 150);
        }

        return Missing;
    }

    /// <summary>True when <paramref name="block"/> is drawn see-through (water), so it goes in the translucent mesh and does not hide faces behind it.</summary>
    public static bool IsTranslucent(BlockId block) => block == BlockTypes.Water;
}
