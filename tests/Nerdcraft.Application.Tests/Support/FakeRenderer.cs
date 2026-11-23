// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;

namespace Nerdcraft.Application.Tests.Support;

public sealed class FakeRenderer : IRenderer
{
    private int _nextHandle;

    public float AspectRatio => 16f / 9f;

    public List<MeshHandle> Uploads { get; } = [];

    public List<MeshHandle> Releases { get; } = [];

    public List<MeshHandle> Draws { get; } = [];

    /// <summary>Camera position passed to each <see cref="EndFrame"/>; null when the debug overlay was hidden.</summary>
    public List<Vector3?> DebugOverlays { get; } = [];

    public List<MeshHandle> TranslucentDraws { get; } = [];

    /// <summary>Every draw call in submission order; true marks a translucent draw.</summary>
    public List<(MeshHandle Handle, bool Translucent)> AllDraws { get; } = [];

    public MeshHandle UploadMesh(MeshData mesh)
    {
        MeshHandle handle = new(++_nextHandle);
        Uploads.Add(handle);
        return handle;
    }

    public void ReleaseMesh(MeshHandle handle) => Releases.Add(handle);

    public void BeginFrame(CameraView camera, Color clearColor)
    {
    }

    public void DrawMesh(MeshHandle handle, Matrix4x4 transform)
    {
        Draws.Add(handle);
        AllDraws.Add((handle, false));
    }

    public void DrawTranslucentMesh(MeshHandle handle, Matrix4x4 transform)
    {
        TranslucentDraws.Add(handle);
        AllDraws.Add((handle, true));
    }

    public void EndFrame(Vector3? debugCameraPosition) => DebugOverlays.Add(debugCameraPosition);
}
