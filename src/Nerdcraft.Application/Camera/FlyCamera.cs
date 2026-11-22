// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Rendering;

namespace Nerdcraft.Application.Camera;

/// <summary>A free-flying first-person camera driven by <see cref="InputState"/>. Y is up; pitch is clamped to avoid flipping.</summary>
/// <param name="position">Starting position in world space.</param>
/// <param name="yawDegrees">Starting yaw; see <see cref="YawDegrees"/>.</param>
/// <param name="pitchDegrees">Starting pitch, clamped to ±<see cref="MaxPitchDegrees"/>.</param>
public sealed class FlyCamera(Vector3 position, float yawDegrees = 0f, float pitchDegrees = 0f)
{
    /// <summary>Largest allowed pitch in either direction, just short of straight up or down.</summary>
    public const float MaxPitchDegrees = 89f;

    private const float DegreesPerPixel = 0.1f;
    private const float Speed = 14f;
    private const float SprintMultiplier = 4f;

    /// <summary>Current position in world space.</summary>
    public Vector3 Position { get; private set; } = position;

    /// <summary>Yaw 0 looks toward -Z; positive yaw turns right.</summary>
    public float YawDegrees { get; private set; } = yawDegrees;

    /// <summary>Look elevation in degrees; positive looks up, within ±<see cref="MaxPitchDegrees"/>.</summary>
    public float PitchDegrees { get; private set; } = Math.Clamp(pitchDegrees, -MaxPitchDegrees, MaxPitchDegrees);

    /// <summary>Unit vector in the direction the camera is looking.</summary>
    public Vector3 Forward
    {
        get
        {
            float yaw = YawDegrees * (MathF.PI / 180f);
            float pitch = PitchDegrees * (MathF.PI / 180f);
            float horizontal = MathF.Cos(pitch);
            return new Vector3(MathF.Sin(yaw) * horizontal, MathF.Sin(pitch), -MathF.Cos(yaw) * horizontal);
        }
    }

    /// <summary>Applies look and movement input for one frame. Movement is relative to the view, and diagonal input is normalized so it is no faster than straight movement.</summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    public void Apply(InputState input, float deltaSeconds)
    {
        YawDegrees = (YawDegrees + (input.LookDelta.X * DegreesPerPixel)) % 360f;
        PitchDegrees = Math.Clamp(
            PitchDegrees - (input.LookDelta.Y * DegreesPerPixel), -MaxPitchDegrees, MaxPitchDegrees);

        Vector3 forward = Forward;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 move = (right * input.Move.X) + (Vector3.UnitY * input.Move.Y) + (forward * input.Move.Z);
        if (move.LengthSquared() > 1f)
        {
            move = Vector3.Normalize(move);
        }

        float speed = input.Sprint ? Speed * SprintMultiplier : Speed;
        Position += move * (speed * deltaSeconds);
    }

    /// <summary>Builds the view description a renderer needs for the current pose.</summary>
    public CameraView ToView(float fovYDegrees, float nearPlane, float farPlane)
        => new(Position, Position + Forward, Vector3.UnitY, fovYDegrees, nearPlane, farPlane);
}
