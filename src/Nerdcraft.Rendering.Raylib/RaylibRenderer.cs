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

    /// <summary>Whether <see cref="EndFrame"/> draws raylib's FPS counter. Off by default; the host decides.</summary>
    public bool ShowFps { get; init; }

    /// <inheritdoc/>
    public float AspectRatio
        => Raylib_cs.Raylib.GetRenderWidth() / (float)Math.Max(1, Raylib_cs.Raylib.GetRenderHeight());

    /// <inheritdoc/>
    /// <remarks>Retains only the index data on the CPU after upload; the vertex data lives on the GPU.</remarks>
    public MeshHandle UploadMesh(MeshData data)
    {
        Mesh mesh = default;
        mesh.VertexCount = data.VertexCount;
        mesh.TriangleCount = data.IndexCount / 3;
        mesh.Vertices = CopyToRaylibMemory(data.Positions);
        mesh.Normals = CopyToRaylibMemory(data.Normals);
        mesh.Colors = CopyToRaylibMemory(data.Colors);
        mesh.Indices = CopyToRaylibMemory(data.Indices);

        Raylib_cs.Raylib.UploadMesh(ref mesh, false);

        // The GPU now holds the data; drop the large CPU copies (matters on integrated graphics).
        // Indices must stay: DrawMesh draws non-indexed when mesh.Indices is null. UnloadMesh frees them.
        Raylib_cs.Raylib.MemFree(mesh.Vertices);
        Raylib_cs.Raylib.MemFree(mesh.Normals);
        Raylib_cs.Raylib.MemFree(mesh.Colors);
        mesh.Vertices = null;
        mesh.Normals = null;
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
    /// <remarks>Draws the FPS counter when <see cref="ShowFps"/> is set.</remarks>
    public void EndFrame()
    {
        Raylib_cs.Raylib.EndMode3D();
        if (ShowFps)
        {
            Raylib_cs.Raylib.DrawFPS(10, 10);
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
