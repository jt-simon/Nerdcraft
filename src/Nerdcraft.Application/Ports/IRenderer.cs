// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Physics;

namespace Nerdcraft.Application.Ports;

/// <summary>
/// The seam between the game and a graphics engine. Implementations are called only from the thread
/// that owns the graphics context, never from background workers.
/// </summary>
public interface IRenderer
{
    /// <summary>Viewport width divided by height.</summary>
    float AspectRatio { get; }

    /// <summary>Copies the mesh to the GPU. The caller disposes <paramref name="mesh"/> afterwards.</summary>
    MeshHandle UploadMesh(MeshData mesh);

    /// <summary>Frees the GPU resources behind <paramref name="handle"/>. The handle must not be used afterwards.</summary>
    void ReleaseMesh(MeshHandle handle);

    /// <summary>Starts a frame: clears the target and sets up the camera. Call once before any <see cref="DrawMesh"/>.</summary>
    void BeginFrame(CameraView camera, Color clearColor);

    /// <summary>Draws an uploaded mesh. Valid only between <see cref="BeginFrame"/> and <see cref="EndFrame"/>.</summary>
    /// <param name="handle">A handle from <see cref="UploadMesh"/>.</param>
    /// <param name="transform">Places the mesh's local coordinates in world space.</param>
    void DrawMesh(MeshHandle handle, Matrix4x4 transform);

    /// <summary>
    /// Draws an uploaded mesh with alpha blending and without writing depth, so what lies behind it shows through.
    /// Valid only between <see cref="BeginFrame"/> and <see cref="EndFrame"/>, and only after every <see cref="DrawMesh"/>
    /// call of the frame. The caller submits translucent meshes from farthest to nearest.
    /// </summary>
    /// <param name="handle">A handle from <see cref="UploadMesh"/>; vertex alpha sets the opacity.</param>
    /// <param name="transform">Places the mesh's local coordinates in world space.</param>
    void DrawTranslucentMesh(MeshHandle handle, Matrix4x4 transform);

    /// <summary>
    /// Draws the twelve edges of a box in world space. Depth-tested, so terrain in front of the box hides it. Valid only
    /// between <see cref="BeginFrame"/> and <see cref="EndFrame"/>, and only after every <see cref="DrawMesh"/> and
    /// <see cref="DrawTranslucentMesh"/> call of the frame, so water does not cover it.
    /// </summary>
    /// <param name="box">The box to outline, in world coordinates.</param>
    /// <param name="color">Line color; alpha sets the opacity.</param>
    void DrawBoxOutline(Aabb box, Color color);

    /// <summary>Finishes the frame and presents it.</summary>
    /// <param name="crosshair">
    /// When true, draws a crosshair at the center of the screen, over the scene and under the overlay and the menu.
    /// </param>
    /// <param name="debug">
    /// When set, draws the debug overlay (FPS, the camera position and the draw statistics); when
    /// <see langword="null"/>, draws no overlay.
    /// </param>
    /// <param name="menu">
    /// When set, draws the pause menu last, over the scene and the overlay: the scene dimmed and each button at its
    /// pixel bounds. When <see langword="null"/>, draws no menu.
    /// </param>
    void EndFrame(bool crosshair, DebugOverlay? debug, PauseMenuView? menu);
}
