// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Meshing;

/// <summary>
/// Emits one quad per solid-block face whose neighbor is air. Vertices are chunk-local (0..16), so the
/// renderer positions the chunk with a translation and large world coordinates stay precise.
/// </summary>
public sealed class CulledChunkMesher : IChunkMesher
{
    // 16-bit indices cap a mesh at 65,536 vertices. A worst-case checkerboard chunk needs ~49,000.
    private const int MaxVertices = 65536;

    // Corners are counter-clockwise when seen from outside the cube.
    private static readonly Face[] Faces =
    [
        new(1, 0, 0, 0.8f, [1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1]),
        new(-1, 0, 0, 0.8f, [0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0]),
        new(0, 1, 0, 1.0f, [0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0]),
        new(0, -1, 0, 0.5f, [0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 1]),
        new(0, 0, 1, 0.7f, [1, 0, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1]),
        new(0, 0, -1, 0.7f, [0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0]),
    ];

    /// <inheritdoc/>
    /// <remarks>Stops adding faces, returning a partial mesh, if the 16-bit index limit would be exceeded.</remarks>
    public MeshData Mesh(ChunkSnapshot snapshot)
    {
        ReadOnlySpan<BlockId> blocks = snapshot.Blocks;
        using MeshBuilder builder = new();

        for (int y = 0; y < Chunk.Size; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    BlockId block = blocks[ChunkSnapshot.Index(x, y, z)];
                    if (block.IsAir)
                    {
                        continue;
                    }

                    Color color = BlockPalette.GetColor(block);
                    foreach (Face face in Faces)
                    {
                        if (blocks[ChunkSnapshot.Index(x + face.Dx, y + face.Dy, z + face.Dz)].IsAir
                            && !builder.TryAddQuad(x, y, z, face, color))
                        {
                            return builder.Build();
                        }
                    }
                }
            }
        }

        return builder.Build();
    }

    private sealed record Face(int Dx, int Dy, int Dz, float Shade, int[] Corners);

    private sealed class MeshBuilder : IDisposable
    {
        private readonly float[] _positions = ArrayPool<float>.Shared.Rent(MaxVertices * 3);
        private readonly float[] _normals = ArrayPool<float>.Shared.Rent(MaxVertices * 3);
        private readonly byte[] _colors = ArrayPool<byte>.Shared.Rent(MaxVertices * 4);
        private readonly ushort[] _indices = ArrayPool<ushort>.Shared.Rent(MaxVertices / 4 * 6);
        private int _vertexCount;
        private int _indexCount;

        public bool TryAddQuad(int x, int y, int z, Face face, Color color)
        {
            if (_vertexCount + 4 > MaxVertices)
            {
                return false;
            }

            byte r = (byte)(color.R * face.Shade);
            byte g = (byte)(color.G * face.Shade);
            byte b = (byte)(color.B * face.Shade);

            for (int corner = 0; corner < 4; corner++)
            {
                int p = (_vertexCount + corner) * 3;
                _positions[p] = x + face.Corners[corner * 3];
                _positions[p + 1] = y + face.Corners[(corner * 3) + 1];
                _positions[p + 2] = z + face.Corners[(corner * 3) + 2];
                _normals[p] = face.Dx;
                _normals[p + 1] = face.Dy;
                _normals[p + 2] = face.Dz;

                int c = (_vertexCount + corner) * 4;
                _colors[c] = r;
                _colors[c + 1] = g;
                _colors[c + 2] = b;
                _colors[c + 3] = color.A;
            }

            ushort first = (ushort)_vertexCount;
            _indices[_indexCount++] = first;
            _indices[_indexCount++] = (ushort)(first + 1);
            _indices[_indexCount++] = (ushort)(first + 2);
            _indices[_indexCount++] = first;
            _indices[_indexCount++] = (ushort)(first + 2);
            _indices[_indexCount++] = (ushort)(first + 3);
            _vertexCount += 4;
            return true;
        }

        public MeshData Build() => MeshData.CopyFrom(
            _positions.AsSpan(0, _vertexCount * 3),
            _normals.AsSpan(0, _vertexCount * 3),
            _colors.AsSpan(0, _vertexCount * 4),
            _indices.AsSpan(0, _indexCount));

        public void Dispose()
        {
            ArrayPool<float>.Shared.Return(_positions);
            ArrayPool<float>.Shared.Return(_normals);
            ArrayPool<byte>.Shared.Return(_colors);
            ArrayPool<ushort>.Shared.Return(_indices);
        }
    }
}
