// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Meshing;

/// <summary>
/// Emits one quad per block face that can be seen, into an opaque mesh or, for translucent blocks such as water,
/// a translucent mesh. An opaque block shows a face toward air or a translucent neighbor; a translucent block
/// shows a face only toward air, so there are no faces inside a body of water or between water and rock.
/// Vertices are chunk-local (0..16), so the renderer positions the chunk with a translation and large world
/// coordinates stay precise. Ambient occlusion is baked into the vertex colors: each vertex is darkened by how
/// many opaque blocks crowd its corner, which makes individual blocks and creases visible. Translucent blocks
/// do not occlude.
/// </summary>
public sealed class CulledChunkMesher : IChunkMesher
{
    // 16-bit indices cap a mesh at 65,536 vertices. A worst-case checkerboard chunk needs ~49,000.
    private const int MaxVertices = 65536;

    // Brightness by ambient occlusion level: 0 is a corner boxed in by two sides, 3 is fully open.
    private static readonly float[] AoBrightness = [0.55f, 0.7f, 0.85f, 1f];

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
    /// <remarks>Stops adding faces, returning partial meshes, if the 16-bit index limit of either mesh would be exceeded.</remarks>
    public ChunkMesh Mesh(ChunkSnapshot snapshot)
    {
        ReadOnlySpan<BlockId> blocks = snapshot.Blocks;
        using MeshBuilder opaque = new();
        using MeshBuilder translucent = new();
        Span<int> ao = stackalloc int[4];

        for (int y = 0; y < Chunk.Size; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    int index = ChunkSnapshot.Index(x, y, z);
                    BlockId block = blocks[index];
                    if (block.IsAir)
                    {
                        continue;
                    }

                    Color color = BlockPalette.GetColor(block);
                    bool isTranslucent = BlockPalette.IsTranslucent(block);
                    MeshBuilder builder = isTranslucent ? translucent : opaque;
                    foreach (Face face in Faces)
                    {
                        BlockId neighbor = blocks[index + face.NeighborOffset];
                        if (!neighbor.IsAir && (isTranslucent || !BlockPalette.IsTranslucent(neighbor)))
                        {
                            continue;
                        }

                        for (int corner = 0; corner < 4; corner++)
                        {
                            ao[corner] = OcclusionLevel(blocks, index, face, corner);
                        }

                        if (!builder.TryAddQuad(x, y, z, face, color, ao))
                        {
                            return new ChunkMesh(opaque.Build(), translucent.Build());
                        }
                    }
                }
            }
        }

        return new ChunkMesh(opaque.Build(), translucent.Build());
    }

    // Ambient occlusion for one corner of a face. In the layer just outside the face, the two blocks beside the
    // corner (along the face's two tangent axes) and the block diagonal to it decide how crowded it is.
    // Returns 0 (darkest) to 3 (open).
    private static int OcclusionLevel(ReadOnlySpan<BlockId> blocks, int index, Face face, int corner)
    {
        int offsets = corner * 3;
        bool sideU = Occludes(blocks[index + face.OcclusionOffsets[offsets]]);
        bool sideV = Occludes(blocks[index + face.OcclusionOffsets[offsets + 1]]);
        bool diagonal = Occludes(blocks[index + face.OcclusionOffsets[offsets + 2]]);

        if (sideU && sideV)
        {
            return 0; // The diagonal is hidden behind the two sides; fully boxed in either way.
        }

        return 3 - ((sideU ? 1 : 0) + (sideV ? 1 : 0) + (diagonal ? 1 : 0));
    }

    private static bool Occludes(BlockId block) => !block.IsAir && !BlockPalette.IsTranslucent(block);

    private static (int X, int Y, int Z) AxisStep(int axis, int sign) => axis switch
    {
        0 => (sign, 0, 0),
        1 => (0, sign, 0),
        _ => (0, 0, sign),
    };

    private sealed record Face(int Dx, int Dy, int Dz, float Shade, int[] Corners)
    {
        // The two axes (0 = X, 1 = Y, 2 = Z) lying in the face's plane, used to find a corner's neighbors.
        public int UAxis => Dx != 0 ? 1 : 0;

        public int VAxis => Dz != 0 ? 1 : 2;

        // Steps in a snapshot's block array: from a block to its neighbor across this face.
        public int NeighborOffset { get; } = Step(Dx, Dy, Dz);

        // For each corner, the steps from a block to the three blocks that crowd it: beside it along U, beside it
        // along V, and the diagonal. Three entries per corner, in that order.
        public int[] OcclusionOffsets { get; } = BuildOcclusionOffsets(Dx, Dy, Dz, Corners);

        private static int Step(int dx, int dy, int dz) => ChunkSnapshot.Index(dx, dy, dz) - ChunkSnapshot.Index(0, 0, 0);

        private static int[] BuildOcclusionOffsets(int dx, int dy, int dz, int[] corners)
        {
            int uAxis = dx != 0 ? 1 : 0;
            int vAxis = dz != 0 ? 1 : 2;
            int[] offsets = new int[12];
            for (int corner = 0; corner < 4; corner++)
            {
                // +1 when this corner lies on the positive side of the face along each tangent axis, otherwise -1.
                (int ux, int uy, int uz) = AxisStep(uAxis, (corners[(corner * 3) + uAxis] * 2) - 1);
                (int vx, int vy, int vz) = AxisStep(vAxis, (corners[(corner * 3) + vAxis] * 2) - 1);
                offsets[corner * 3] = Step(dx + ux, dy + uy, dz + uz);
                offsets[(corner * 3) + 1] = Step(dx + vx, dy + vy, dz + vz);
                offsets[(corner * 3) + 2] = Step(dx + ux + vx, dy + uy + vy, dz + uz + vz);
            }

            return offsets;
        }
    }

    private sealed class MeshBuilder : IDisposable
    {
        private readonly float[] _positions = ArrayPool<float>.Shared.Rent(MaxVertices * 3);
        private readonly float[] _normals = ArrayPool<float>.Shared.Rent(MaxVertices * 3);
        private readonly byte[] _colors = ArrayPool<byte>.Shared.Rent(MaxVertices * 4);
        private readonly ushort[] _indices = ArrayPool<ushort>.Shared.Rent(MaxVertices / 4 * 6);
        private int _vertexCount;
        private int _indexCount;

        public bool TryAddQuad(int x, int y, int z, Face face, Color color, ReadOnlySpan<int> ao)
        {
            if (_vertexCount + 4 > MaxVertices)
            {
                return false;
            }

            for (int corner = 0; corner < 4; corner++)
            {
                float shade = face.Shade * AoBrightness[ao[corner]];
                byte r = (byte)(color.R * shade);
                byte g = (byte)(color.G * shade);
                byte b = (byte)(color.B * shade);

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

            // Split the quad along the diagonal whose corners are darker in total. Splitting along the other one
            // makes a single dark corner bleed across a lopsided triangle.
            bool flip = ao[0] + ao[2] > ao[1] + ao[3];
            ushort first = (ushort)_vertexCount;
            ushort i0 = flip ? (ushort)(first + 1) : first;
            ushort i1 = flip ? (ushort)(first + 2) : (ushort)(first + 1);
            ushort i2 = flip ? (ushort)(first + 3) : (ushort)(first + 2);
            ushort i3 = flip ? first : (ushort)(first + 3);
            _indices[_indexCount++] = i0;
            _indices[_indexCount++] = i1;
            _indices[_indexCount++] = i2;
            _indices[_indexCount++] = i0;
            _indices[_indexCount++] = i2;
            _indices[_indexCount++] = i3;
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
