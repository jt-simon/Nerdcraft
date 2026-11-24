// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

using ZstdSharp;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>
/// Converts the blocks of one chunk to and from the stored blob. Format 1: a format byte, a compression byte (0 raw, 1 zstd),
/// then the payload: a little-endian <c>ushort</c> palette size, the palette's block ids, and, when the palette holds more than one
/// id, a bit-packed palette index for every block in <see cref="Chunk.Index"/> order. A chunk of one block type stores no indices
/// and is never compressed. <see cref="Encode"/> is for one thread at a time; <see cref="Decode"/> is thread-safe.
/// </summary>
internal sealed class ChunkCodec : IDisposable
{
    private const byte FormatVersion = 1;
    private const byte Raw = 0;
    private const byte Zstandard = 1;
    private const int CompressionLevel = 3;
    private const int MaxBitsPerIndex = 12; // A palette holds at most Chunk.Volume (4096) ids.

    // The largest payload: the palette size, a full palette, and the widest index for every block.
    private const int MaxPayload = sizeof(ushort) + (Chunk.Volume * sizeof(ushort)) + (Chunk.Volume * MaxBitsPerIndex / 8);

    private readonly Compressor _compressor = new(CompressionLevel);
    private readonly ConcurrentBag<Decompressor> _decompressors = [];

    /// <summary>Returns the stored form of <paramref name="blocks"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="blocks"/> does not hold exactly <see cref="Chunk.Volume"/> blocks.</exception>
    public byte[] Encode(ReadOnlySpan<BlockId> blocks)
    {
        if (blocks.Length != Chunk.Volume)
        {
            throw new ArgumentException($"A chunk requires exactly {Chunk.Volume} blocks.", nameof(blocks));
        }

        // The palette is every id present, ascending.
        Span<ulong> present = stackalloc ulong[ushort.MaxValue / 64 + 1];
        present.Clear();
        foreach (BlockId block in blocks)
        {
            present[block.Value >> 6] |= 1UL << (block.Value & 63);
        }

        Span<ushort> palette = stackalloc ushort[Chunk.Volume];
        int count = 0;
        for (int word = 0; word < present.Length; word++)
        {
            for (ulong bits = present[word]; bits != 0; bits &= bits - 1)
            {
                palette[count++] = (ushort)((word << 6) + BitOperations.TrailingZeroCount(bits));
            }
        }

        byte[] payload = ArrayPool<byte>.Shared.Rent(MaxPayload);
        try
        {
            int length = WritePayload(blocks, palette[..count], payload);
            return count == 1 ? Frame(Raw, payload.AsSpan(0, length)) : Compress(payload.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>Writes the blocks stored in <paramref name="data"/> to <paramref name="blocks"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="blocks"/> does not hold exactly <see cref="Chunk.Volume"/> blocks.</exception>
    /// <exception cref="InvalidDataException"><paramref name="data"/> is not a valid chunk blob.</exception>
    public void Decode(ReadOnlySpan<byte> data, Span<BlockId> blocks)
    {
        if (blocks.Length != Chunk.Volume)
        {
            throw new ArgumentException($"A chunk requires exactly {Chunk.Volume} blocks.", nameof(blocks));
        }

        if (data.Length < 2 || data[0] != FormatVersion)
        {
            throw new InvalidDataException("The chunk blob has an unknown format.");
        }

        ReadOnlySpan<byte> payload = data[2..];
        if (data[1] == Raw)
        {
            ReadPayload(payload, blocks);
            return;
        }

        if (data[1] != Zstandard)
        {
            throw new InvalidDataException("The chunk blob uses an unknown compression.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxPayload);
        try
        {
            if (!_decompressors.TryTake(out Decompressor? decompressor))
            {
                decompressor = new Decompressor();
            }

            bool reusable = false;
            try
            {
                int length = decompressor.Unwrap(payload, buffer);
                reusable = true; // The zstd frame was valid; a bad payload inside it does not spoil the decompressor.
                ReadPayload(buffer.AsSpan(0, length), blocks);
            }
            catch (ZstdException exception)
            {
                throw new InvalidDataException("The chunk blob is corrupt.", exception);
            }
            finally
            {
                if (reusable)
                {
                    _decompressors.Add(decompressor);
                }
                else
                {
                    decompressor.Dispose();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Releases the compressor and decompressors.</summary>
    public void Dispose()
    {
        _compressor.Dispose();
        while (_decompressors.TryTake(out Decompressor? decompressor))
        {
            decompressor.Dispose();
        }
    }

    private static int WritePayload(ReadOnlySpan<BlockId> blocks, ReadOnlySpan<ushort> palette, Span<byte> payload)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)palette.Length);
        int length = sizeof(ushort);
        foreach (ushort id in palette)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload[length..], id);
            length += sizeof(ushort);
        }

        if (palette.Length == 1)
        {
            return length;
        }

        int bits = BitsPerIndex(palette.Length);
        Span<ulong> words = stackalloc ulong[Chunk.Volume * MaxBitsPerIndex / 64];
        words = words[..(Chunk.Volume * bits / 64)];
        words.Clear();
        int position = 0;
        foreach (BlockId block in blocks)
        {
            ulong index = (ulong)IndexOf(palette, block.Value);
            int word = position >> 6;
            int shift = position & 63;
            words[word] |= index << shift;
            if (shift + bits > 64)
            {
                words[word + 1] |= index >> (64 - shift);
            }

            position += bits;
        }

        foreach (ulong word in words)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(payload[length..], word);
            length += sizeof(ulong);
        }

        return length;
    }

    private static void ReadPayload(ReadOnlySpan<byte> payload, Span<BlockId> blocks)
    {
        if (payload.Length < sizeof(ushort))
        {
            throw new InvalidDataException("The chunk blob is truncated.");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (count is 0 or > Chunk.Volume)
        {
            throw new InvalidDataException("The chunk blob has an invalid palette size.");
        }

        int bits = count == 1 ? 0 : BitsPerIndex(count);
        int indexBytes = Chunk.Volume * bits / 8;
        if (payload.Length != sizeof(ushort) + (count * sizeof(ushort)) + indexBytes)
        {
            throw new InvalidDataException("The chunk blob has the wrong length.");
        }

        Span<ushort> palette = stackalloc ushort[count];
        for (int i = 0; i < count; i++)
        {
            palette[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload[(sizeof(ushort) * (i + 1))..]);
        }

        if (count == 1)
        {
            blocks.Fill(new BlockId(palette[0]));
            return;
        }

        ReadOnlySpan<byte> packed = payload[(sizeof(ushort) * (count + 1))..];
        ulong mask = (1UL << bits) - 1;
        int position = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            int word = position >> 6;
            int shift = position & 63;
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(packed[(word * sizeof(ulong))..]) >> shift;
            if (shift + bits > 64)
            {
                value |= BinaryPrimitives.ReadUInt64LittleEndian(packed[((word + 1) * sizeof(ulong))..]) << (64 - shift);
            }

            int index = (int)(value & mask);
            if (index >= count)
            {
                throw new InvalidDataException("The chunk blob has a palette index out of range.");
            }

            blocks[i] = new BlockId(palette[index]);
            position += bits;
        }
    }

    // The palette is ascending and holds every id in the chunk, so a plain binary search always finds it (and never boxes).
    private static int IndexOf(ReadOnlySpan<ushort> palette, ushort id)
    {
        int low = 0;
        int high = palette.Length - 1;
        while (low < high)
        {
            int middle = (low + high) >> 1;
            if (palette[middle] < id)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // Bits needed to number a palette of the given size (at least 2 entries).
    private static int BitsPerIndex(int paletteSize) => 32 - BitOperations.LeadingZeroCount((uint)(paletteSize - 1));

    // Compresses the payload, keeping the raw form when that is not smaller.
    private byte[] Compress(ReadOnlySpan<byte> payload)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Compressor.GetCompressBound(payload.Length));
        try
        {
            int written = _compressor.Wrap(payload, buffer);
            return written < payload.Length ? Frame(Zstandard, buffer.AsSpan(0, written)) : Frame(Raw, payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static byte[] Frame(byte compression, ReadOnlySpan<byte> payload)
    {
        byte[] result = new byte[2 + payload.Length];
        result[0] = FormatVersion;
        result[1] = compression;
        payload.CopyTo(result.AsSpan(2));
        return result;
    }
}
