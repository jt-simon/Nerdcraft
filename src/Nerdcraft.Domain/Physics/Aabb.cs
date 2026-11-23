// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Domain.Physics;

/// <summary>An axis-aligned box in world space. Boxes that only touch do not intersect.</summary>
/// <param name="Min">Corner with the smallest X, Y and Z.</param>
/// <param name="Max">Corner with the largest X, Y and Z.</param>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    /// <summary>Returns this box moved by <paramref name="delta"/>.</summary>
    public Aabb Offset(Vector3 delta) => new(Min + delta, Max + delta);

    /// <summary>True when the interiors of the two boxes overlap.</summary>
    public bool Intersects(Aabb other)
        => Min.X < other.Max.X && Max.X > other.Min.X
        && Min.Y < other.Max.Y && Max.Y > other.Min.Y
        && Min.Z < other.Max.Z && Max.Z > other.Min.Z;
}
