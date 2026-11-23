// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;

using Raylib_cs;

using Color = Raylib_cs.Color;
using NerdcraftColor = Nerdcraft.Application.Rendering.Color;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Draws with raylib. Must be created and used on the thread that owns the window.</summary>
public sealed unsafe class RaylibRenderer : IRenderer, IDisposable
{
    private readonly Dictionary<int, Mesh> _meshes = [];
    private readonly Material _material = Raylib_cs.Raylib.LoadMaterialDefault();
    private int _nextId;

    /// <inheritdoc/>
    public float AspectRatio
        => Raylib_cs.Raylib.GetRenderWidth() / (float)Math.Max(1, Raylib_cs.Raylib.GetRenderHeight());

    /// <inheritdoc/>
    /// <remarks>
    /// Retains only the index data on the CPU after upload; the vertex data lives on the GPU. Normals are not uploaded:
    /// the default material's shader does not read them, so they would only cost video memory and upload time.
    /// </remarks>
    public MeshHandle UploadMesh(MeshData data)
    {
        Mesh mesh = default;
        mesh.VertexCount = data.VertexCount;
        mesh.TriangleCount = data.IndexCount / 3;
        mesh.Vertices = CopyToRaylibMemory(data.Positions);
        mesh.Colors = CopyToRaylibMemory(data.Colors);
        mesh.Indices = CopyToRaylibMemory(data.Indices);

        Raylib_cs.Raylib.UploadMesh(ref mesh, false);

        // The GPU now holds the data; drop the large CPU copies (matters on integrated graphics).
        // Indices must stay: DrawMesh draws non-indexed when mesh.Indices is null. UnloadMesh frees them.
        Raylib_cs.Raylib.MemFree(mesh.Vertices);
        Raylib_cs.Raylib.MemFree(mesh.Colors);
        mesh.Vertices = null;
        mesh.Colors = null;

        int id = ++_nextId;
        _meshes[id] = mesh;
        return new MeshHandle(id);
    }

    /// <inheritdoc/>
    public void ReleaseMesh(MeshHandle handle)
    {
        if (_meshes.Remove(handle.Id, out Mesh mesh))
        {
            Raylib_cs.Raylib.UnloadMesh(mesh);
        }
    }

    /// <inheritdoc/>
    public void BeginFrame(CameraView camera, NerdcraftColor clearColor)
    {
        Rlgl.SetClipPlanes(camera.NearPlane, camera.FarPlane);
        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(new Color(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        Raylib_cs.Raylib.BeginMode3D(new Camera3D(
            camera.Position, camera.Target, camera.Up, camera.FovYDegrees, CameraProjection.Perspective));
    }

    /// <inheritdoc/>
    // System.Numerics is row-major (translation in M41..M43); raylib expects column-major.
    public void DrawMesh(MeshHandle handle, Matrix4x4 transform)
        => Raylib_cs.Raylib.DrawMesh(_meshes[handle.Id], _material, Matrix4x4.Transpose(transform));

    /// <inheritdoc/>
    /// <remarks>Flushes the batch and turns depth writes off around the draw, so opaque meshes still occlude it.</remarks>
    public void DrawTranslucentMesh(MeshHandle handle, Matrix4x4 transform)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        DrawMesh(handle, transform);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    /// <inheritdoc/>
    /// <remarks>Draws the FPS counter and camera position when <paramref name="debugCameraPosition"/> is set.</remarks>
    public void EndFrame(Vector3? debugCameraPosition)
    {
        Raylib_cs.Raylib.EndMode3D();
        if (debugCameraPosition is Vector3 position)
        {
            Raylib_cs.Raylib.DrawFPS(10, 10);
            Raylib_cs.Raylib.DrawText(
                $"Pos: {position.X:F1} {position.Y:F1} {position.Z:F1}", 10, 34, 20, Color.White);
        }

        Raylib_cs.Raylib.EndDrawing();
    }

    /// <summary>Unloads every mesh that has not been released, and the shared material.</summary>
    public void Dispose()
    {
        foreach (Mesh mesh in _meshes.Values)
        {
            Raylib_cs.Raylib.UnloadMesh(mesh);
        }

        _meshes.Clear();
        Raylib_cs.Raylib.UnloadMaterial(_material);
    }

    // Raylib frees mesh memory with its own allocator, so buffers must come from it.
    private static T* CopyToRaylibMemory<T>(ReadOnlySpan<T> source)
        where T : unmanaged
    {
        T* destination = (T*)Raylib_cs.Raylib.MemAlloc((uint)(source.Length * sizeof(T)));
        source.CopyTo(new Span<T>(destination, source.Length));
        return destination;
    }
}
