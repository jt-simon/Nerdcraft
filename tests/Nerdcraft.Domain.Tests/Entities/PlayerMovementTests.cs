// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Tests.Support;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Tests.Entities;

public class PlayerMovementTests
{
    private static readonly PlayerAbilities All = new(CanFly: true, CanNoclip: true);

    // Yaw 90 faces +X; feet at Y = 1 stand on the flat terrain's stone layer.
    private const float FaceEast = 90f;

    private static PlayerState Standing(float x = 1.5f, float y = 1f, float z = 8.5f)
        => PlayerState.Spawn(new Vector3(x, y, z), FaceEast) with { OnGround = true };

    private static PlayerCommand Command(bool forward = false, float yaw = FaceEast, bool jump = false, bool sneak = false, bool sprint = false)
        => new(new Vector2(0f, forward ? 1f : 0f), yaw, 0f, jump, sneak, sprint);

    private static PlayerState Run(PlayerState state, PlayerCommand command, VoxelWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            state = PlayerMovement.Tick(state, command, world, TestTerrain.Bounds, All);
        }

        return state;
    }

    [Fact]
    public void Tick_JumpOnFlatGround_RisesMoreThanOneBlockButLessThanTwo()
    {
        VoxelWorld world = TestTerrain.Flat();
        PlayerState state = Standing();
        float highest = state.Position.Y;

        for (int i = 0; i < 30; i++)
        {
            state = PlayerMovement.Tick(state, Command(jump: true), world, TestTerrain.Bounds, All);
            highest = MathF.Max(highest, state.Position.Y);
        }

        Assert.InRange(highest - 1f, 1.01f, 1.99f);
    }

    [Fact]
    public void Tick_SneakingAtLedge_DoesNotFallOff()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Air, (3, 0, 0), (15, 0, 15));

        PlayerState state = Run(Standing(), Command(forward: true, sneak: true), world, 60);

        Assert.True(state.OnGround);
        Assert.Equal(1f, state.Position.Y, 3);
        Assert.InRange(state.Position.X, 2.5f, 3.31f);
    }

    [Fact]
    public void Tick_Flying_HasNoGravity()
    {
        VoxelWorld world = TestTerrain.Flat();
        PlayerState state = PlayerState.Spawn(new Vector3(1.5f, 10f, 8.5f)) with { Flying = true };

        state = Run(state, Command(), world, 20);

        Assert.Equal(10f, state.Position.Y, 3);
    }

    [Fact]
    public void Tick_FlyingOntoGround_EndsFlight()
    {
        VoxelWorld world = TestTerrain.Flat();
        PlayerState state = PlayerState.Spawn(new Vector3(1.5f, 5f, 8.5f)) with { Flying = true };

        state = Run(state, Command(sneak: true), world, 60);

        Assert.True(state.OnGround);
        Assert.False(state.Flying);
    }

    [Fact]
    public void Tick_Noclip_PassesThroughBlocks()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Stone, (4, 1, 6), (4, 3, 10));
        PlayerState state = Standing() with { Flying = true, Noclip = true, OnGround = false };

        state = Run(state, Command(forward: true), world, 40);

        Assert.True(state.Position.X > 7f);
    }

    [Fact]
    public void Tick_ToggleFlyWithoutAbility_StaysWalking()
    {
        VoxelWorld world = TestTerrain.Flat();
        PlayerCommand toggle = Command() with { ToggleFly = true };

        PlayerState state = PlayerMovement.Tick(Standing(), toggle, world, TestTerrain.Bounds, new PlayerAbilities(false, false));

        Assert.False(state.Flying);
    }

    [Fact]
    public void Tick_HoldJumpInWater_Rises()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Water, (0, 1, 7), (3, 4, 10));

        PlayerState state = Run(Standing(), Command(jump: true), world, 30);

        Assert.True(state.InLiquid);
        Assert.True(state.Position.Y > 2f);
    }

    [Fact]
    public void Tick_SprintIntoWall_StopsSprinting()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Stone, (4, 1, 6), (4, 2, 10));

        PlayerState started = PlayerMovement.Tick(Standing(), Command(forward: true, sprint: true), world, TestTerrain.Bounds, All);
        PlayerState state = Run(started, Command(forward: true), world, 30);

        Assert.True(started.Sprinting);
        Assert.False(state.Sprinting);
    }

    [Fact]
    public void Tick_FeetChunkUnloaded_HoldsStill()
    {
        PlayerState falling = PlayerState.Spawn(new Vector3(1.5f, 5f, 8.5f)) with { Velocity = new Vector3(0f, -1f, 0f) };

        PlayerState state = PlayerMovement.Tick(falling, Command(), new VoxelWorld(), TestTerrain.Bounds, All);

        Assert.Equal(falling.Position, state.Position);
        Assert.Equal(Vector3.Zero, state.Velocity);
    }

    [Fact]
    public void Tick_SneakInWater_DoesNotSlowSwimming()
    {
        VoxelWorld world = TestTerrain.Flat();
        TestTerrain.Fill(world, BlockTypes.Water, (0, 1, 0), (15, 4, 15));

        PlayerState swimming = Run(Standing(), Command(forward: true), world, 15);
        PlayerState sneakSwimming = Run(Standing(), Command(forward: true, sneak: true), world, 15);

        Assert.Equal(swimming.Position.X, sneakSwimming.Position.X, 3);
        Assert.False(sneakSwimming.Sneaking);
    }

    [Fact]
    public void Tick_NonFiniteMoveWhileSneaking_IgnoresTheMove()
    {
        VoxelWorld world = TestTerrain.Flat();
        PlayerCommand bad = Command(sneak: true) with { Move = new Vector2(float.NaN, 1f) };

        PlayerState state = Run(Standing(), bad, world, 5);

        Assert.Equal(1.5f, state.Position.X, 3);
    }
}
