// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application.Rendering;

/// <summary>A rectangle in texture space, with both axes running from 0 to 1 and V growing downward like image rows.</summary>
/// <param name="U0">Left edge.</param>
/// <param name="V0">Top edge.</param>
/// <param name="U1">Right edge.</param>
/// <param name="V1">Bottom edge.</param>
public readonly record struct UvRect(float U0, float V0, float U1, float V1);

/// <summary>
/// The layout of a texture atlas: equal square tiles stacked in one vertical strip. Slot 0 is reserved for the
/// "missing" tile, which every unknown name maps to; the named textures follow in the order given. Renderers build the
/// atlas image from <see cref="Names"/> and the mesher takes its UVs from <see cref="GetUv"/>, so both agree on the layout.
/// </summary>
public sealed class TextureAtlas
{
    private readonly Dictionary<string, int> _slots = [];

    private TextureAtlas(IReadOnlyList<string> names)
    {
        Names = names;
        for (int i = 0; i < names.Count; i++)
        {
            _slots[names[i]] = i + 1;
        }
    }

    /// <summary>The texture names in slot order, starting at slot 1. Never contains duplicates.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Number of tiles, including the reserved missing tile.</summary>
    public int TileCount => Names.Count + 1;

    /// <summary>Lays out an atlas for <paramref name="names"/>.</summary>
    /// <param name="names">Unique texture names; slot 1 is the first. The order is part of the layout.</param>
    /// <exception cref="ArgumentException"><paramref name="names"/> contains a duplicate.</exception>
    public static TextureAtlas Layout(IReadOnlyList<string> names)
    {
        if (names.Distinct().Count() != names.Count)
        {
            throw new ArgumentException("Texture names must be unique.", nameof(names));
        }

        return new TextureAtlas(names);
    }

    /// <summary>The slot of <paramref name="name"/>, or 0 (the missing tile) when it is <see langword="null"/> or unknown.</summary>
    public int IndexOf(string? name) => name is not null && _slots.TryGetValue(name, out int slot) ? slot : 0;

    /// <summary>The tile of <paramref name="name"/> in texture space; the missing tile when it is <see langword="null"/> or unknown.</summary>
    public UvRect GetUv(string? name)
    {
        int slot = IndexOf(name);
        return new UvRect(0f, slot / (float)TileCount, 1f, (slot + 1) / (float)TileCount);
    }
}
