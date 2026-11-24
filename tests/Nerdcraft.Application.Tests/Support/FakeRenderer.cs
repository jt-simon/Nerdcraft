// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Physics;

namespace Nerdcraft.Application.Tests.Support;

public sealed class FakeRenderer : IRenderer
{
    private int _nextHandle;

    public float AspectRatio => 16f / 9f;

    public List<MeshHandle> Uploads { get; } = [];

    /// <summary>Vertex count of each uploaded mesh, by handle.</summary>
    public Dictionary<MeshHandle, int> VertexCounts { get; } = [];

    public List<MeshHandle> Releases { get; } = [];

    public List<MeshHandle> Draws { get; } = [];

    /// <summary>World position of each opaque draw's chunk, in submission order.</summary>
    public List<Vector3> DrawOrigins { get; } = [];

    /// <summary>Overlay passed to each <see cref="EndFrame"/>; null when the debug overlay was hidden.</summary>
    public List<DebugOverlay?> DebugOverlays { get; } = [];

    /// <summary>Pause menu passed to each <see cref="EndFrame"/>; null when the menu was hidden.</summary>
    public List<PauseMenuView?> Menus { get; } = [];

    public List<MeshHandle> TranslucentDraws { get; } = [];

    /// <summary>Every draw call in submission order; true marks a translucent draw.</summary>
    public List<(MeshHandle Handle, bool Translucent)> AllDraws { get; } = [];

    public MeshHandle UploadMesh(MeshData mesh)
    {
        MeshHandle handle = new(++_nextHandle);
        Uploads.Add(handle);
        VertexCounts[handle] = mesh.VertexCount;
        return handle;
    }

    public void ReleaseMesh(MeshHandle handle) => Releases.Add(handle);

    /// <summary>Direction the camera looked in at each <see cref="BeginFrame"/>.</summary>
    public List<Vector3> CameraForwards { get; } = [];

    public void BeginFrame(CameraView camera, Color clearColor) => CameraForwards.Add(Vector3.Normalize(camera.Target - camera.Position));

    public void DrawMesh(MeshHandle handle, Matrix4x4 transform)
    {
        Draws.Add(handle);
        DrawOrigins.Add(transform.Translation);
        AllDraws.Add((handle, false));
    }

    public void DrawTranslucentMesh(MeshHandle handle, Matrix4x4 transform)
    {
        TranslucentDraws.Add(handle);
        AllDraws.Add((handle, true));
    }

    /// <summary>Box passed to each <see cref="DrawBoxOutline"/>, in submission order.</summary>
    public List<Aabb> Outlines { get; } = [];

    /// <summary>Crosshair flag passed to each <see cref="EndFrame"/>.</summary>
    public List<bool> Crosshairs { get; } = [];

    public void DrawBoxOutline(Aabb box, Color color) => Outlines.Add(box);

    public void EndFrame(bool crosshair, DebugOverlay? debug, PauseMenuView? menu)
    {
        Crosshairs.Add(crosshair);
        DebugOverlays.Add(debug);
        Menus.Add(menu);
    }
}
