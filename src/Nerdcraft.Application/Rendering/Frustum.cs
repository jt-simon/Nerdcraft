// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nerdcraft.Application.Rendering;

/// <summary>The six clip planes of a camera, used to skip drawing geometry that is off screen.</summary>
public readonly struct Frustum
{
    private readonly PlaneSet _planes;

    private Frustum(PlaneSet planes) => _planes = planes;

    /// <summary>Extracts planes from a row-vector view-projection matrix (System.Numerics convention).</summary>
    public static Frustum FromViewProjection(Matrix4x4 m)
    {
        Vector4 c0 = new(m.M11, m.M21, m.M31, m.M41);
        Vector4 c1 = new(m.M12, m.M22, m.M32, m.M42);
        Vector4 c2 = new(m.M13, m.M23, m.M33, m.M43);
        Vector4 c3 = new(m.M14, m.M24, m.M34, m.M44);

        // System.Numerics projections map depth to [0, 1], so the near plane is c2 alone.
        PlaneSet planes = default;
        planes[0] = Normalize(c3 + c0); // left
        planes[1] = Normalize(c3 - c0); // right
        planes[2] = Normalize(c3 + c1); // bottom
        planes[3] = Normalize(c3 - c1); // top
        planes[4] = Normalize(c2);      // near
        planes[5] = Normalize(c3 - c2); // far
        return new Frustum(planes);
    }

    /// <summary>
    /// False only when the axis-aligned box lies entirely outside one plane; conservative, so it may keep a few
    /// boxes that are not visible. A <c>default</c> frustum has no planes and accepts every box.
    /// </summary>
    /// <param name="min">Box corner with the smallest coordinates, in world space.</param>
    /// <param name="max">Box corner with the largest coordinates, in world space.</param>
    public bool Intersects(Vector3 min, Vector3 max)
    {
        ReadOnlySpan<Vector4> planes = _planes;
        foreach (Vector4 plane in planes)
        {
            Vector3 positive = new(
                plane.X >= 0 ? max.X : min.X,
                plane.Y >= 0 ? max.Y : min.Y,
                plane.Z >= 0 ? max.Z : min.Z);

            if ((plane.X * positive.X) + (plane.Y * positive.Y) + (plane.Z * positive.Z) + plane.W < 0)
            {
                return false;
            }
        }

        return true;
    }

    [InlineArray(6)]
    private struct PlaneSet
    {
        private Vector4 _plane;
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        float length = MathF.Sqrt((plane.X * plane.X) + (plane.Y * plane.Y) + (plane.Z * plane.Z));
        return plane / length;
    }
}
