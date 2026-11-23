// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Domain.Entities;

/// <summary>
/// Everything a player asks for during one movement tick. It holds no device or clock state, so a client can
/// send it to a server, and both can feed it to <see cref="PlayerMovement.Tick"/> for the same result.
/// </summary>
/// <param name="Move">X = strafe right, Y = forward; each in [-1, 1]. A longer vector is normalized.</param>
/// <param name="YawDegrees">Absolute yaw for this tick; 0 looks toward -Z and positive turns right.</param>
/// <param name="PitchDegrees">Absolute look elevation; positive looks up.</param>
/// <param name="Jump">True while jump is held: jumps from the ground, swims up, or flies up.</param>
/// <param name="Sneak">True while sneak is held: slows walking, keeps the player on ledges, swims down, or flies down.</param>
/// <param name="Sprint">True on the tick sprinting is requested; sprinting then lasts until forward is released, the player sneaks, or they hit a wall.</param>
/// <param name="ToggleFly">True on the tick flight is toggled. Ignored without <see cref="PlayerAbilities.CanFly"/>.</param>
/// <param name="ToggleNoclip">True on the tick noclip is toggled. Ignored without <see cref="PlayerAbilities.CanNoclip"/>.</param>
public readonly record struct PlayerCommand(
    Vector2 Move,
    float YawDegrees,
    float PitchDegrees,
    bool Jump = false,
    bool Sneak = false,
    bool Sprint = false,
    bool ToggleFly = false,
    bool ToggleNoclip = false);
