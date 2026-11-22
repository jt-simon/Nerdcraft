// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Camera;
using Nerdcraft.Application.Input;

namespace Nerdcraft.Application.Tests.Camera;

public class FlyCameraTests
{
    [Fact]
    public void Apply_LookBeyondLimits_ClampsPitch()
    {
        FlyCamera camera = new(Vector3.Zero);

        camera.Apply(new InputState(Vector3.Zero, new Vector2(0, -100_000), false), 0.016f);
        Assert.Equal(FlyCamera.MaxPitchDegrees, camera.PitchDegrees);

        camera.Apply(new InputState(Vector3.Zero, new Vector2(0, 200_000), false), 0.016f);
        Assert.Equal(-FlyCamera.MaxPitchDegrees, camera.PitchDegrees);
    }

    [Fact]
    public void Apply_ForwardAtZeroYaw_MovesTowardNegativeZ()
    {
        FlyCamera camera = new(Vector3.Zero);

        camera.Apply(new InputState(Vector3.UnitZ, Vector2.Zero, false), 1f);

        Assert.True(camera.Position.Z < 0);
        Assert.Equal(0f, camera.Position.X, 3);
    }

    [Fact]
    public void Apply_DiagonalInput_IsNoFasterThanStraight()
    {
        FlyCamera straight = new(Vector3.Zero);
        FlyCamera diagonal = new(Vector3.Zero);

        straight.Apply(new InputState(Vector3.UnitZ, Vector2.Zero, false), 1f);
        diagonal.Apply(new InputState(new Vector3(1, 0, 1), Vector2.Zero, false), 1f);

        Assert.Equal(straight.Position.Length(), diagonal.Position.Length(), 3);
    }

    [Fact]
    public void Apply_Sprint_MovesFasterThanWalking()
    {
        FlyCamera walking = new(Vector3.Zero);
        FlyCamera sprinting = new(Vector3.Zero);

        walking.Apply(new InputState(Vector3.UnitZ, Vector2.Zero, false), 1f);
        sprinting.Apply(new InputState(Vector3.UnitZ, Vector2.Zero, true), 1f);

        Assert.True(sprinting.Position.Length() > walking.Position.Length());
    }
}
