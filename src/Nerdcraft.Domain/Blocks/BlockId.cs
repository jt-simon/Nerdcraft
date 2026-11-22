// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.Blocks;

/// <summary>Identifies a block type. The default value is air; named types live in <see cref="BlockTypes"/>.</summary>
/// <param name="Value">Numeric type id; 0 is air.</param>
public readonly record struct BlockId(ushort Value)
{
    /// <summary>The empty block, equal to <c>default</c>.</summary>
    public static BlockId Air => default;

    /// <summary>True when this is <see cref="Air"/>.</summary>
    public bool IsAir => Value == 0;
}
