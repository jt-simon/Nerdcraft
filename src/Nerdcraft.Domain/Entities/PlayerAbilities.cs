// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Domain.Entities;

/// <summary>What a player is allowed to do. <see cref="PlayerMovement.Tick"/> enforces it, so an authority such as a server can deny a request a client makes.</summary>
/// <param name="CanFly">True when the player may toggle flight.</param>
/// <param name="CanNoclip">True when the player may toggle noclip (flying through blocks).</param>
public readonly record struct PlayerAbilities(bool CanFly, bool CanNoclip);
