// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Meshing;
using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Application.Rendering;

/// <summary>Maps block types to texture names, tints and display names, keeping presentation choices out of the domain.</summary>
public static class BlockPalette
{
    private static readonly Color Missing = new(255, 0, 255);

    /// <summary>The texture names the default pack provides, in atlas order. Every name returned by <see cref="GetTexture"/> is listed here.</summary>
    public static readonly IReadOnlyList<string> TextureNames =
        ["stone", "dirt", "grass_top", "grass_side", "bedrock", "sand", "gravel", "snow", "water_still"];

    /// <summary>The atlas layout shared by the mesher (which writes UVs) and the renderer (which builds the image).</summary>
    public static readonly TextureAtlas Atlas = TextureAtlas.Layout(TextureNames);

    // Block ids below this get their six face tiles precomputed, so meshing does no name lookups for them.
    private const int CachedBlockIds = 256;

    private static readonly UvRect[] FaceUvs = BuildFaceUvs();

    /// <summary>
    /// The tint multiplied into the texture of <paramref name="block"/>: white (so the texture shows unchanged) for opaque blocks,
    /// white with an alpha below 255 for translucent blocks; unknown types (including air) get magenta so gaps are obvious.
    /// </summary>
    public static Color GetColor(BlockId block)
    {
        if (block == BlockTypes.Water)
        {
            return new Color(255, 255, 255, 150);
        }

        return GetTexture(block, ChunkFace.PositiveY) is null ? Missing : new Color(255, 255, 255);
    }

    /// <summary>
    /// The atlas tile of <see cref="GetTexture"/> for one face of <paramref name="block"/>, the missing tile when it has no texture.
    /// Precomputed for the first 256 block ids, so it is cheap enough to call for every face the mesher emits; safe from any thread.
    /// </summary>
    public static UvRect GetUv(BlockId block, ChunkFace face)
        => block.Value < CachedBlockIds
            ? FaceUvs[(block.Value * 6) + (int)face]
            : Atlas.GetUv(GetTexture(block, face));

    /// <summary>
    /// The texture name for one face of <paramref name="block"/>, or <see langword="null"/> for air and unknown types.
    /// Grass shows <c>grass_top</c> on top, <c>dirt</c> below and <c>grass_side</c> around; every other block uses one texture on all faces.
    /// </summary>
    public static string? GetTexture(BlockId block, ChunkFace face)
    {
        if (block == BlockTypes.Grass)
        {
            return face switch
            {
                ChunkFace.PositiveY => "grass_top",
                ChunkFace.NegativeY => "dirt",
                _ => "grass_side",
            };
        }

        if (block == BlockTypes.Stone)
        {
            return "stone";
        }

        if (block == BlockTypes.Dirt)
        {
            return "dirt";
        }

        if (block == BlockTypes.Bedrock)
        {
            return "bedrock";
        }

        if (block == BlockTypes.Sand)
        {
            return "sand";
        }

        if (block == BlockTypes.Gravel)
        {
            return "gravel";
        }

        if (block == BlockTypes.Snow)
        {
            return "snow";
        }

        if (block == BlockTypes.Water)
        {
            return "water_still";
        }

        return null;
    }

    private static UvRect[] BuildFaceUvs()
    {
        UvRect[] uvs = new UvRect[CachedBlockIds * 6];
        for (int id = 0; id < CachedBlockIds; id++)
        {
            for (int face = 0; face < 6; face++)
            {
                uvs[(id * 6) + face] = Atlas.GetUv(GetTexture(new BlockId((ushort)id), (ChunkFace)face));
            }
        }

        return uvs;
    }

    /// <summary>True when <paramref name="block"/> is drawn see-through (water), so it goes in the translucent mesh and does not hide faces behind it.</summary>
    public static bool IsTranslucent(BlockId block) => block == BlockTypes.Water;

    /// <summary>
    /// The display name for <paramref name="block"/>, or <see langword="null"/> for air and unknown types.
    /// Used for hotbar hover text.
    /// </summary>
    public static string? GetDisplayName(BlockId block)
    {
        if (block == BlockTypes.Stone)
        {
            return "Stone";
        }

        if (block == BlockTypes.Dirt)
        {
            return "Dirt";
        }

        if (block == BlockTypes.Grass)
        {
            return "Grass";
        }

        if (block == BlockTypes.Bedrock)
        {
            return "Bedrock";
        }

        if (block == BlockTypes.Sand)
        {
            return "Sand";
        }

        if (block == BlockTypes.Gravel)
        {
            return "Gravel";
        }

        if (block == BlockTypes.Snow)
        {
            return "Snow";
        }

        if (block == BlockTypes.Water)
        {
            return "Water";
        }

        return null;
    }
}
