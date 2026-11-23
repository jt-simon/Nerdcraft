// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Physics;

namespace Nerdcraft.Domain.Entities;

/// <summary>The complete simulated state of one player after a movement tick. Y is up; one block is one unit.</summary>
/// <param name="Position">Center of the player's feet in world space.</param>
/// <param name="Velocity">Movement per tick, in blocks.</param>
/// <param name="YawDegrees">Yaw; 0 looks toward -Z and positive turns right.</param>
/// <param name="PitchDegrees">Look elevation; positive looks up.</param>
/// <param name="OnGround">True when the last tick ended with the player resting on a block.</param>
/// <param name="Flying">True while flight is on.</param>
/// <param name="Noclip">True while the player passes through blocks. Implies <paramref name="Flying"/>.</param>
/// <param name="Sneaking">True while sneaking; eyes are lower.</param>
/// <param name="Sprinting">True while sprinting.</param>
/// <param name="InLiquid">True when the player's body overlaps a liquid.</param>
public readonly record struct PlayerState(
    Vector3 Position,
    Vector3 Velocity,
    float YawDegrees,
    float PitchDegrees,
    bool OnGround,
    bool Flying,
    bool Noclip,
    bool Sneaking,
    bool Sprinting,
    bool InLiquid)
{
    /// <summary>Width and depth of the player's collision box, in blocks.</summary>
    public const float Width = 0.6f;

    /// <summary>Height of the player's collision box, in blocks.</summary>
    public const float Height = 1.8f;

    /// <summary>Eye height above the feet while standing.</summary>
    public const float StandingEyeHeight = 1.62f;

    /// <summary>Eye height above the feet while sneaking.</summary>
    public const float SneakingEyeHeight = 1.27f;

    /// <summary>Eye height above the feet for the current pose.</summary>
    public float EyeHeight => Sneaking ? SneakingEyeHeight : StandingEyeHeight;

    /// <summary>Eye position in world space.</summary>
    public Vector3 EyePosition => Position + new Vector3(0f, EyeHeight, 0f);

    /// <summary>The collision box in world space.</summary>
    public Aabb Bounds
    {
        get
        {
            Vector3 half = new(Width / 2f, 0f, Width / 2f);
            return new Aabb(Position - half, Position + half + new Vector3(0f, Height, 0f));
        }
    }

    /// <summary>A standing, motionless player in the air at <paramref name="feet"/>.</summary>
    /// <param name="feet">Center of the feet.</param>
    /// <param name="yawDegrees">Starting yaw.</param>
    /// <param name="pitchDegrees">Starting pitch.</param>
    public static PlayerState Spawn(Vector3 feet, float yawDegrees = 0f, float pitchDegrees = 0f)
        => new(feet, Vector3.Zero, yawDegrees, pitchDegrees, false, false, false, false, false, false);
}
