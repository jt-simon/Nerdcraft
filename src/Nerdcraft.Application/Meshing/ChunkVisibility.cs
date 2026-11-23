// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Meshing;

/// <summary>
/// Which pairs of a chunk's six faces are joined by open space (air or translucent blocks) inside the chunk. A line of
/// sight can enter the chunk through one face and leave through another only if they are connected, so a chunk that
/// is solid rock between two faces hides whatever lies beyond it. Used to skip chunks behind solid terrain.
/// </summary>
public readonly record struct ChunkVisibility
{
    private const int FaceCount = 6;
    private const int Mask = Chunk.Size - 1;

    private const ulong AllPairs = (1UL << (FaceCount * FaceCount)) - 1;

    // Stored inverted, so that default(ChunkVisibility) is All and an unset value never hides anything.
    private readonly ulong _blocked;

    private ChunkVisibility(ulong blocked) => _blocked = blocked;

    /// <summary>No face connects to any other, as in a chunk that is solid all the way through.</summary>
    public static ChunkVisibility None { get; } = new(AllPairs);

    /// <summary>Every face connects to every other. The safe value for a chunk whose contents are not known yet, and the value of <c>default</c>.</summary>
    public static ChunkVisibility All => default;

    /// <summary>True when open space inside the chunk joins <paramref name="from"/> and <paramref name="to"/>.</summary>
    /// <param name="from">The face a line of sight enters through.</param>
    /// <param name="to">The face it leaves through.</param>
    public bool Connects(ChunkFace from, ChunkFace to) => ((_blocked >> (((int)from * FaceCount) + (int)to)) & 1UL) == 0;

    /// <summary>
    /// Works out the connectivity of the chunk inside <paramref name="snapshot"/> by flood-filling its open cells; the
    /// snapshot's border is ignored. Pure and thread-safe; allocates nothing on the heap.
    /// </summary>
    /// <param name="snapshot">The chunk to analyse.</param>
    public static ChunkVisibility Compute(ChunkSnapshot snapshot)
    {
        ReadOnlySpan<BlockId> blocks = snapshot.Blocks;
        Span<bool> visited = stackalloc bool[Chunk.Volume];
        Span<ushort> queue = stackalloc ushort[Chunk.Volume];
        ulong pairs = 0;

        for (int start = 0; start < Chunk.Volume; start++)
        {
            if (visited[start] || !IsOpen(blocks, start))
            {
                continue;
            }

            int touched = Fill(blocks, visited, queue, start);
            for (int a = 0; a < FaceCount; a++)
            {
                for (int b = 0; b < FaceCount; b++)
                {
                    if ((touched & (1 << a)) != 0 && (touched & (1 << b)) != 0)
                    {
                        pairs |= 1UL << ((a * FaceCount) + b);
                    }
                }
            }
        }

        return new ChunkVisibility(~pairs & AllPairs);
    }

    // Marks every open cell connected to start as visited and returns the set of chunk faces they touch, one bit per ChunkFace.
    private static int Fill(ReadOnlySpan<BlockId> blocks, Span<bool> visited, Span<ushort> queue, int start)
    {
        int head = 0;
        int tail = 0;
        int touched = 0;
        queue[tail++] = (ushort)start;
        visited[start] = true;

        while (head < tail)
        {
            int cell = queue[head++];
            int x = cell & Mask;
            int z = (cell >> Chunk.SizeShift) & Mask;
            int y = cell >> (Chunk.SizeShift * 2);

            touched |= x == Mask ? 1 << (int)ChunkFace.PositiveX : 0;
            touched |= x == 0 ? 1 << (int)ChunkFace.NegativeX : 0;
            touched |= y == Mask ? 1 << (int)ChunkFace.PositiveY : 0;
            touched |= y == 0 ? 1 << (int)ChunkFace.NegativeY : 0;
            touched |= z == Mask ? 1 << (int)ChunkFace.PositiveZ : 0;
            touched |= z == 0 ? 1 << (int)ChunkFace.NegativeZ : 0;

            tail = Enqueue(blocks, visited, queue, tail, x + 1, y, z);
            tail = Enqueue(blocks, visited, queue, tail, x - 1, y, z);
            tail = Enqueue(blocks, visited, queue, tail, x, y + 1, z);
            tail = Enqueue(blocks, visited, queue, tail, x, y - 1, z);
            tail = Enqueue(blocks, visited, queue, tail, x, y, z + 1);
            tail = Enqueue(blocks, visited, queue, tail, x, y, z - 1);
        }

        return touched;
    }

    // Queues the cell if it lies inside the chunk, is open and is new; returns the new queue length.
    private static int Enqueue(ReadOnlySpan<BlockId> blocks, Span<bool> visited, Span<ushort> queue, int tail, int x, int y, int z)
    {
        if ((uint)x > Mask || (uint)y > Mask || (uint)z > Mask)
        {
            return tail;
        }

        int cell = Chunk.Index(x, y, z);
        if (visited[cell] || !IsOpen(blocks, cell))
        {
            return tail;
        }

        visited[cell] = true;
        queue[tail] = (ushort)cell;
        return tail + 1;
    }

    // Looks up a chunk-local cell index in the snapshot's padded block array.
    private static bool IsOpen(ReadOnlySpan<BlockId> blocks, int cell)
    {
        BlockId block = blocks[ChunkSnapshot.Index(cell & Mask, cell >> (Chunk.SizeShift * 2), (cell >> Chunk.SizeShift) & Mask)];
        return block.IsAir || BlockPalette.IsTranslucent(block);
    }
}
