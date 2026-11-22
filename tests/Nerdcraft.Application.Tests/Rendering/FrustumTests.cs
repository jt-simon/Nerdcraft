// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Rendering;

namespace Nerdcraft.Application.Tests.Rendering;

public class FrustumTests
{
    // Camera at the origin looking down -Z.
    private static readonly Frustum Forward = Frustum.FromViewProjection(
        new CameraView(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 70f, 0.1f, 100f).CreateViewProjection(16f / 9f));

    [Fact]
    public void Intersects_BoxInFront_ReturnsTrue()
        => Assert.True(Forward.Intersects(new Vector3(-1, -1, -11), new Vector3(1, 1, -9)));

    [Fact]
    public void Intersects_BoxBehind_ReturnsFalse()
        => Assert.False(Forward.Intersects(new Vector3(-1, -1, 9), new Vector3(1, 1, 11)));

    [Fact]
    public void Intersects_BoxFarToTheSide_ReturnsFalse()
        => Assert.False(Forward.Intersects(new Vector3(200, -1, -11), new Vector3(202, 1, -9)));

    [Fact]
    public void Intersects_BoxBeyondFarPlane_ReturnsFalse()
        => Assert.False(Forward.Intersects(new Vector3(-1, -1, -301), new Vector3(1, 1, -299)));

    [Fact]
    public void Intersects_BoxContainingTheCamera_ReturnsTrue()
        => Assert.True(Forward.Intersects(new Vector3(-8, -8, -8), new Vector3(8, 8, 8)));
}
