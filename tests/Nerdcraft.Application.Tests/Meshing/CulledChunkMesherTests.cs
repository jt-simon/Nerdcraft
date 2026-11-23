// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Meshing;

public class CulledChunkMesherTests
{
    private static readonly ChunkPosition Origin = new(0, 0, 0);

    private static MeshData MeshChunk(VoxelWorld world, ChunkPosition position)
    {
        using ChunkSnapshot snapshot = ChunkSnapshot.Create(world, position);
        return new CulledChunkMesher().Mesh(snapshot);
    }

    // Index of the vertex at a chunk-local position with the given face normal.
    private static int FindVertex(MeshData mesh, Vector3 position, Vector3 normal)
    {
        ReadOnlySpan<float> p = mesh.Positions;
        ReadOnlySpan<float> n = mesh.Normals;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            if (new Vector3(p[v * 3], p[(v * 3) + 1], p[(v * 3) + 2]) == position
                && new Vector3(n[v * 3], n[(v * 3) + 1], n[(v * 3) + 2]) == normal)
            {
                return v;
            }
        }

        throw new InvalidOperationException($"No vertex at {position} facing {normal}.");
    }

    private static int Red(MeshData mesh, int vertex) => mesh.Colors[vertex * 4];

    private static bool SharesATriangle(MeshData mesh, int first, int second)
    {
        ReadOnlySpan<ushort> indices = mesh.Indices;
        for (int t = 0; t < indices.Length; t += 3)
        {
            bool hasFirst = indices[t] == first || indices[t + 1] == first || indices[t + 2] == first;
            bool hasSecond = indices[t] == second || indices[t + 1] == second || indices[t + 2] == second;
            if (hasFirst && hasSecond)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Mesh_SingleBlock_EmitsSixFaces()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin);
        world.SetBlock(new BlockPosition(5, 5, 5), BlockTypes.Stone);

        using MeshData mesh = MeshChunk(world, Origin);

        Assert.Equal(24, mesh.VertexCount);
        Assert.Equal(36, mesh.IndexCount);
    }

    [Fact]
    public void Mesh_AdjacentBlocks_CullsTheSharedFaces()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin);
        world.SetBlock(new BlockPosition(5, 5, 5), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(6, 5, 5), BlockTypes.Stone);

        using MeshData mesh = MeshChunk(world, Origin);

        Assert.Equal(10 * 4, mesh.VertexCount);
    }

    // One case per face. The two free axes use different values so a swapped-axis bug in the border copy can't hide.
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 1)]
    public void Mesh_SolidBlockInNeighborChunk_CullsTheBorderFace(int dx, int dy, int dz)
    {
        ChunkPosition neighbor = new(dx, dy, dz);
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin, neighbor);
        BlockPosition edge = new(
            dx != 0 ? EdgeCoordinate(dx) : 3,
            dy != 0 ? EdgeCoordinate(dy) : (dx != 0 ? 3 : 9),
            dz != 0 ? EdgeCoordinate(dz) : 9);
        world.SetBlock(edge, BlockTypes.Stone);
        world.SetBlock(new BlockPosition(edge.X + dx, edge.Y + dy, edge.Z + dz), BlockTypes.Stone);

        using MeshData mesh = MeshChunk(world, Origin);

        Assert.Equal(5 * 4, mesh.VertexCount);
    }

    private static int EdgeCoordinate(int direction) => direction > 0 ? Chunk.Mask : 0;

    [Fact]
    public void Mesh_EmptyChunk_EmitsNothing()
    {
        VoxelWorld world = new();
        world.LoadChunk(Origin, new BlockId[Chunk.Volume]);

        using MeshData mesh = MeshChunk(world, Origin);

        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.IndexCount);
    }

    [Fact]
    public void Mesh_Triangles_WindCounterClockwiseAroundTheirNormals()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin);
        world.SetBlock(new BlockPosition(3, 4, 5), BlockTypes.Grass);

        using MeshData mesh = MeshChunk(world, Origin);

        ReadOnlySpan<float> p = mesh.Positions;
        ReadOnlySpan<float> n = mesh.Normals;
        ReadOnlySpan<ushort> indices = mesh.Indices;
        for (int t = 0; t < indices.Length; t += 3)
        {
            Vector3 a = At(p, indices[t]);
            Vector3 b = At(p, indices[t + 1]);
            Vector3 c = At(p, indices[t + 2]);
            Vector3 faceNormal = Vector3.Cross(b - a, c - a);

            Assert.True(Vector3.Dot(faceNormal, At(n, indices[t])) > 0, $"Triangle {t / 3} faces inward.");
        }

        static Vector3 At(ReadOnlySpan<float> data, int vertex)
            => new(data[vertex * 3], data[(vertex * 3) + 1], data[(vertex * 3) + 2]);
    }

    [Fact]
    public void Mesh_BlockBesideWall_DarkensVerticesAgainstTheWall()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin);
        world.SetBlock(new BlockPosition(5, 5, 5), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(6, 6, 5), BlockTypes.Stone); // Rises beside the +X edge of the first block's top.

        using MeshData mesh = MeshChunk(world, Origin);

        int open = Red(mesh, FindVertex(mesh, new Vector3(5, 6, 5), Vector3.UnitY));
        int againstWall = Red(mesh, FindVertex(mesh, new Vector3(6, 6, 5), Vector3.UnitY));
        Assert.True(againstWall < open, $"Vertex against the wall ({againstWall}) should be darker than the open one ({open}).");
    }

    [Fact]
    public void Mesh_BlockOnlyDiagonalInNeighborChunk_StillDarkensTheCorner()
    {
        // The only occluder is across a chunk corner, so this fails unless snapshots copy edge and corner neighbors.
        ChunkPosition diagonal = new(1, 1, 1);
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin, diagonal);
        world.SetBlock(new BlockPosition(15, 15, 15), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(16, 16, 16), BlockTypes.Stone);

        using MeshData mesh = MeshChunk(world, Origin);

        int shadowed = Red(mesh, FindVertex(mesh, new Vector3(16, 16, 16), Vector3.UnitX));
        int open = Red(mesh, FindVertex(mesh, new Vector3(16, 15, 15), Vector3.UnitX));
        Assert.True(shadowed < open, $"Corner beside the diagonal chunk ({shadowed}) should be darker than the open one ({open}).");
    }

    [Fact]
    public void Mesh_QuadWithOneDarkCorner_SplitsAlongTheDiagonalThroughIt()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(Origin);
        world.SetBlock(new BlockPosition(5, 5, 5), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(6, 6, 6), BlockTypes.Stone); // Touches only the top face's (6, 6, 6) corner.

        using MeshData mesh = MeshChunk(world, Origin);

        int dark = FindVertex(mesh, new Vector3(6, 6, 6), Vector3.UnitY);
        int opposite = FindVertex(mesh, new Vector3(5, 6, 5), Vector3.UnitY);
        Assert.True(SharesATriangle(mesh, dark, opposite), "The shared edge of the two triangles should run through the dark corner.");
    }

    [Fact]
    public void Mesh_FarChunk_EmitsChunkLocalVertices()
    {
        ChunkPosition far = new(10, 3, -7);
        VoxelWorld world = TestWorlds.WithEmptyChunks(far);
        world.SetBlock(new BlockPosition(far.Origin.X + 2, far.Origin.Y + 2, far.Origin.Z + 2), BlockTypes.Stone);

        using MeshData mesh = MeshChunk(world, far);

        foreach (float coordinate in mesh.Positions)
        {
            Assert.InRange(coordinate, 0f, Chunk.Size);
        }
    }
}
