// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Entities;

/// <summary>
/// The player movement rules: walking, sneaking, sprinting, jumping, swimming, flying and noclip. One call to
/// <see cref="Tick"/> advances one player by one fixed step of <c>1 / TicksPerSecond</c> seconds. It is pure, so a
/// client (for prediction) and a server (as the authority) can run the very same code. Constants are in blocks per tick.
/// </summary>
public static class PlayerMovement
{
    /// <summary>How many times per second <see cref="Tick"/> must be called.</summary>
    public const int TicksPerSecond = 20;

    private const float GroundAcceleration = 0.1f;
    private const float AirAcceleration = 0.02f;
    private const float SprintFactor = 1.3f;
    private const float SneakFactor = 0.3f;
    private const float GroundDrag = 0.546f;
    private const float AirDrag = 0.91f;
    private const float Gravity = 0.08f;
    private const float VerticalDrag = 0.98f;
    private const float JumpVelocity = 0.42f;
    private const float SprintJumpBoost = 0.2f;
    private const float FlyAcceleration = 0.05f;
    private const float FlySprintFactor = 2f;
    private const float FlyVerticalAcceleration = 0.15f;
    private const float FlyVerticalDrag = 0.6f;
    private const float SwimAcceleration = 0.02f;
    private const float SwimDrag = 0.8f;
    private const float SwimSink = 0.02f;
    private const float SwimVertical = 0.04f;
    private const float ClimbOutVelocity = 0.3f;
    private const float ClimbOutHeight = 0.6f;
    private const float LedgeStep = 0.05f;
    private const float LedgeProbeDepth = 0.6f;

    /// <summary>
    /// Advances <paramref name="state"/> by one tick. While the chunk holding the player's feet is not loaded the
    /// player holds still (with no velocity), so a player cannot fall through terrain that is still streaming in.
    /// </summary>
    /// <param name="state">The player before the tick.</param>
    /// <param name="command">What the player asked for this tick.</param>
    /// <param name="world">The world to collide with. Read only.</param>
    /// <param name="bounds">The world's vertical extent; see <see cref="VoxelCollision"/>.</param>
    /// <param name="abilities">What the player is allowed to do; flight and noclip are switched off if not allowed.</param>
    /// <returns>The player after the tick.</returns>
    public static PlayerState Tick(PlayerState state, PlayerCommand command, VoxelWorld world, WorldBounds bounds, PlayerAbilities abilities)
    {
        // A command may come from outside this process; a non-finite value would poison the state or stall the ledge search.
        Vector2 move = float.IsFinite(command.Move.X) && float.IsFinite(command.Move.Y) ? command.Move : Vector2.Zero;
        float yawDegrees = float.IsFinite(command.YawDegrees) ? command.YawDegrees : state.YawDegrees;
        float pitchDegrees = float.IsFinite(command.PitchDegrees) ? command.PitchDegrees : state.PitchDegrees;

        state = state with { YawDegrees = yawDegrees, PitchDegrees = pitchDegrees };
        if (!IsReady(state, world, bounds))
        {
            return state with { Velocity = Vector3.Zero };
        }

        bool flying = state.Flying;
        bool noclip = state.Noclip;
        if (command.ToggleFly && abilities.CanFly)
        {
            flying = !flying;
            noclip &= flying;
        }

        if (command.ToggleNoclip && abilities.CanNoclip)
        {
            noclip = !noclip;
        }

        noclip &= abilities.CanNoclip;
        flying = noclip || (flying && abilities.CanFly);

        bool inLiquid = !flying && VoxelCollision.OverlapsLiquid(world, state.Bounds);
        bool forward = move.Y > 0f;
        bool sneaking = command.Sneak && !flying && !inLiquid;
        bool sprinting = forward && !sneaking && (command.Sprint || state.Sprinting);
        bool wasOnGround = state.OnGround;

        Vector2 input = move;
        if (input.LengthSquared() > 1f)
        {
            input = Vector2.Normalize(input);
        }

        if (sneaking)
        {
            input *= SneakFactor;
        }

        float yaw = yawDegrees * (MathF.PI / 180f);
        Vector3 forwardDirection = new(MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        Vector3 rightDirection = new(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
        Vector3 wish = (rightDirection * input.X) + (forwardDirection * input.Y);

        float acceleration;
        if (flying)
        {
            acceleration = FlyAcceleration * (sprinting ? FlySprintFactor : 1f);
        }
        else if (inLiquid)
        {
            acceleration = SwimAcceleration;
        }
        else
        {
            acceleration = (wasOnGround ? GroundAcceleration : AirAcceleration) * (sprinting ? SprintFactor : 1f);
        }

        Vector3 velocity = state.Velocity;
        velocity.X += wish.X * acceleration;
        velocity.Z += wish.Z * acceleration;

        float lift = (command.Jump ? 1f : 0f) - (command.Sneak ? 1f : 0f);
        if (flying)
        {
            velocity.Y += FlyVerticalAcceleration * lift;
        }
        else if (inLiquid)
        {
            velocity.Y += SwimVertical * lift;
        }
        else if (command.Jump && wasOnGround)
        {
            velocity.Y = JumpVelocity;
            if (sprinting)
            {
                velocity.X += forwardDirection.X * SprintJumpBoost;
                velocity.Z += forwardDirection.Z * SprintJumpBoost;
            }
        }

        if (sneaking && wasOnGround && !inLiquid)
        {
            (velocity.X, velocity.Z) = HoldOnLedge(state.Bounds, velocity.X, velocity.Z, world, bounds);
        }

        Vector3 allowed = noclip ? velocity : VoxelCollision.Move(world, bounds, state.Bounds, velocity);
        bool hitX = allowed.X != velocity.X;
        bool hitY = allowed.Y != velocity.Y;
        bool hitZ = allowed.Z != velocity.Z;
        bool onGround = velocity.Y < 0f && hitY;
        Vector3 wanted = velocity;
        Vector3 position = state.Position + allowed;
        Aabb moved = state.Bounds.Offset(allowed);

        if (hitX)
        {
            velocity.X = 0f;
        }

        if (hitY)
        {
            velocity.Y = 0f;
        }

        if (hitZ)
        {
            velocity.Z = 0f;
        }

        if (flying)
        {
            velocity.X *= AirDrag;
            velocity.Z *= AirDrag;
            velocity.Y *= FlyVerticalDrag;
        }
        else if (inLiquid)
        {
            velocity.X *= SwimDrag;
            velocity.Z *= SwimDrag;
            velocity.Y = (velocity.Y * SwimDrag) - SwimSink;
            bool pushingIntoBlock = hitX || hitZ;
            if (pushingIntoBlock && !VoxelCollision.Collides(world, bounds, moved.Offset(new Vector3(wanted.X, ClimbOutHeight, wanted.Z))))
            {
                velocity.Y = ClimbOutVelocity;
            }
        }
        else
        {
            float drag = wasOnGround ? GroundDrag : AirDrag;
            velocity.X *= drag;
            velocity.Z *= drag;
            velocity.Y = (velocity.Y - Gravity) * VerticalDrag;
        }

        if (flying && !noclip && onGround)
        {
            flying = false;
        }

        return new PlayerState(
            position,
            velocity,
            yawDegrees,
            pitchDegrees,
            onGround,
            flying,
            noclip,
            sneaking,
            sprinting && !hitX && !hitZ,
            VoxelCollision.OverlapsLiquid(world, moved));
    }

    /// <summary>
    /// True when <see cref="Tick"/> will simulate <paramref name="state"/>: the chunk holding the player's feet is loaded,
    /// or lies outside the world's vertical bounds. When false, <see cref="Tick"/> only holds the player still and
    /// ignores the command, so a caller should keep any one-tick request (toggle, sprint) for a later tick.
    /// </summary>
    /// <param name="state">The player.</param>
    /// <param name="world">The world to look the chunk up in.</param>
    /// <param name="bounds">The world's vertical extent.</param>
    public static bool IsReady(PlayerState state, VoxelWorld world, WorldBounds bounds)
    {
        Vector3 feet = state.Position;
        ChunkPosition chunk = new BlockPosition(
            (int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z)).ChunkPosition;
        bool insideWorld = chunk.Y >= bounds.MinChunkY && chunk.Y <= bounds.MaxChunkY;
        return !insideWorld || world.IsLoaded(chunk);
    }

    // Shrinks a horizontal step until the player would still have ground within a step's depth under them, as Minecraft does.
    private static (float X, float Z) HoldOnLedge(Aabb box, float dx, float dz, VoxelWorld world, WorldBounds bounds)
    {
        while (dx != 0f && !HasGround(box, dx, 0f))
        {
            dx = MathF.Abs(dx) < LedgeStep ? 0f : dx - MathF.CopySign(LedgeStep, dx);
        }

        while (dz != 0f && !HasGround(box, 0f, dz))
        {
            dz = MathF.Abs(dz) < LedgeStep ? 0f : dz - MathF.CopySign(LedgeStep, dz);
        }

        while (dx != 0f && dz != 0f && !HasGround(box, dx, dz))
        {
            dx = MathF.Abs(dx) < LedgeStep ? 0f : dx - MathF.CopySign(LedgeStep, dx);
            dz = MathF.Abs(dz) < LedgeStep ? 0f : dz - MathF.CopySign(LedgeStep, dz);
        }

        return (dx, dz);

        bool HasGround(Aabb start, float x, float z)
            => VoxelCollision.Collides(world, bounds, start.Offset(new Vector3(x, -LedgeProbeDepth, z)));
    }
}
