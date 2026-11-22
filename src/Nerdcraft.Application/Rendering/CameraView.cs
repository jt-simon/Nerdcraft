// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Application.Rendering;

/// <summary>Engine-neutral description of a perspective camera, in world space.</summary>
/// <param name="Position">Eye position.</param>
/// <param name="Target">Point the camera looks at.</param>
/// <param name="Up">Up direction.</param>
/// <param name="FovYDegrees">Vertical field of view, in degrees.</param>
/// <param name="NearPlane">Distance to the near clip plane.</param>
/// <param name="FarPlane">Distance to the far clip plane.</param>
public readonly record struct CameraView(
    Vector3 Position,
    Vector3 Target,
    Vector3 Up,
    float FovYDegrees,
    float NearPlane,
    float FarPlane)
{
    /// <summary>Combined view and projection matrix, in the row-vector <c>System.Numerics</c> convention.</summary>
    /// <param name="aspectRatio">Viewport width divided by height.</param>
    public Matrix4x4 CreateViewProjection(float aspectRatio)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(Position, Target, Up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            FovYDegrees * (MathF.PI / 180f), aspectRatio, NearPlane, FarPlane);
        return view * projection;
    }
}
