// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

namespace Nerdcraft.Application.Rendering;

/// <summary>
/// Engine-neutral triangle mesh backed by pooled arrays. Dispose to return the buffers once uploaded.
/// Indices are 16-bit, so a mesh holds at most 65,536 vertices.
/// </summary>
public sealed class MeshData : IDisposable
{
    /// <summary>A shared mesh with no geometry. Disposing it does nothing.</summary>
    public static readonly MeshData Empty = new(0, 0, [], [], [], [], []);

    private float[]? _positions;
    private float[]? _normals;
    private float[]? _texCoords;
    private byte[]? _colors;
    private ushort[]? _indices;

    private MeshData(int vertexCount, int indexCount, float[] positions, float[] normals, float[] texCoords, byte[] colors, ushort[] indices)
    {
        VertexCount = vertexCount;
        IndexCount = indexCount;
        _positions = positions;
        _normals = normals;
        _texCoords = texCoords;
        _colors = colors;
        _indices = indices;
    }

    /// <summary>Number of vertices.</summary>
    public int VertexCount { get; }

    /// <summary>Number of indices; three per triangle.</summary>
    public int IndexCount { get; }

    /// <summary>XYZ per vertex.</summary>
    public ReadOnlySpan<float> Positions => _positions.AsSpan(0, VertexCount * 3);

    /// <summary>XYZ per vertex.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>UV per vertex, in the texture atlas of <see cref="BlockPalette.Atlas"/>.</summary>
    public ReadOnlySpan<float> TexCoords => _texCoords.AsSpan(0, VertexCount * 2);

    /// <summary>RGBA per vertex.</summary>
    public ReadOnlySpan<byte> Colors => _colors.AsSpan(0, VertexCount * 4);

    /// <summary>Triangle list indices into the vertex arrays.</summary>
    public ReadOnlySpan<ushort> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Copies the data into pooled arrays and returns a mesh that owns them; returns <see cref="Empty"/> when <paramref name="indices"/> is empty.</summary>
    /// <param name="positions">XYZ per vertex.</param>
    /// <param name="normals">XYZ per vertex.</param>
    /// <param name="texCoords">UV per vertex.</param>
    /// <param name="colors">RGBA per vertex.</param>
    /// <param name="indices">Triangle list indices.</param>
    public static MeshData CopyFrom(
        ReadOnlySpan<float> positions,
        ReadOnlySpan<float> normals,
        ReadOnlySpan<float> texCoords,
        ReadOnlySpan<byte> colors,
        ReadOnlySpan<ushort> indices)
    {
        if (indices.IsEmpty)
        {
            return Empty;
        }

        float[] p = ArrayPool<float>.Shared.Rent(positions.Length);
        float[] n = ArrayPool<float>.Shared.Rent(normals.Length);
        float[] t = ArrayPool<float>.Shared.Rent(texCoords.Length);
        byte[] c = ArrayPool<byte>.Shared.Rent(colors.Length);
        ushort[] i = ArrayPool<ushort>.Shared.Rent(indices.Length);
        positions.CopyTo(p);
        normals.CopyTo(n);
        texCoords.CopyTo(t);
        colors.CopyTo(c);
        indices.CopyTo(i);
        return new MeshData(positions.Length / 3, indices.Length, p, n, t, c, i);
    }

    /// <summary>Returns the pooled arrays. Safe to call more than once; the data must not be read afterwards.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(this, Empty) || _positions is null)
        {
            return;
        }

        ArrayPool<float>.Shared.Return(_positions);
        ArrayPool<float>.Shared.Return(_normals!);
        ArrayPool<float>.Shared.Return(_texCoords!);
        ArrayPool<byte>.Shared.Return(_colors!);
        ArrayPool<ushort>.Shared.Return(_indices!);
        _positions = null;
        _normals = null;
        _texCoords = null;
        _colors = null;
        _indices = null;
    }
}
