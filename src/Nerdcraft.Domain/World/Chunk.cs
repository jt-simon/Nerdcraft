// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World;

/// <summary>
/// A fixed-size cube of blocks. Not thread-safe: only the owning (main) thread may touch it.
/// Background workers operate on copies.
/// </summary>
public sealed class Chunk
{
    /// <summary>Base-2 logarithm of <see cref="Size"/>, used for shifts between block and chunk coordinates.</summary>
    public const int SizeShift = 4;

    /// <summary>Edge length in blocks; always <c>1 &lt;&lt; <see cref="SizeShift"/></c>.</summary>
    public const int Size = 1 << SizeShift;

    /// <summary>Bit mask that reduces a world coordinate to a local one in [0, <see cref="Size"/>).</summary>
    public const int Mask = Size - 1;

    /// <summary>Total number of blocks in a chunk.</summary>
    public const int Volume = Size * Size * Size;

    private readonly BlockId[] _blocks = new BlockId[Volume];

    /// <summary>Creates an empty chunk at version 0.</summary>
    public Chunk()
    {
    }

    /// <summary>Creates an empty chunk whose <see cref="Version"/> starts at <paramref name="initialVersion"/>.</summary>
    internal Chunk(int initialVersion) => Version = initialVersion;

    /// <summary>
    /// Incremented on every change, including changes to neighboring chunks that affect meshing.
    /// <see cref="VoxelWorld"/> keeps it increasing across an unload and reload of the same position.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Position of a local coordinate in <see cref="Blocks"/>, laid out Y-major, then Z, then X. Does not bounds-check.</summary>
    public static int Index(int x, int y, int z) => (y << (SizeShift * 2)) | (z << SizeShift) | x;

    /// <summary>Returns the block at a local coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any coordinate is outside [0, <see cref="Size"/>).</exception>
    public BlockId Get(int x, int y, int z)
    {
        ThrowIfOutOfBounds(x, y, z);
        return _blocks[Index(x, y, z)];
    }

    /// <summary>Sets the block at a local coordinate. <see cref="Version"/> advances only if the block actually changes.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any coordinate is outside [0, <see cref="Size"/>).</exception>
    public void Set(int x, int y, int z, BlockId block)
    {
        ThrowIfOutOfBounds(x, y, z);
        int index = Index(x, y, z);
        if (_blocks[index] == block)
        {
            return;
        }

        _blocks[index] = block;
        Version++;
    }

    /// <summary>All <see cref="Volume"/> blocks, indexed by <see cref="Index"/>.</summary>
    public ReadOnlySpan<BlockId> Blocks => _blocks;

    /// <summary>Replaces every block from <paramref name="source"/> and always advances <see cref="Version"/>.</summary>
    internal void Load(ReadOnlySpan<BlockId> source)
    {
        source.CopyTo(_blocks);
        Version++;
    }

    /// <summary>Advances <see cref="Version"/> without changing blocks, to invalidate meshes after a neighbor changed.</summary>
    internal void Touch() => Version++;

    private static void ThrowIfOutOfBounds(int x, int y, int z)
    {
        if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
        {
            throw new ArgumentOutOfRangeException(null, $"Local coordinate ({x}, {y}, {z}) is outside the chunk.");
        }
    }
}
