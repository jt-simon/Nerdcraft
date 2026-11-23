// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Player;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Player;

public class LocalPlayerTests
{
    private const float Frame = 0.05f;

    private static readonly InputState Idle = new(Vector2.Zero, Vector2.Zero, false, false);
    private static readonly InputState Forward = Idle with { Move = new Vector2(0f, 1f) };

    private static LocalPlayer CreatePlayer()
    {
        PlayerState standing = PlayerState.Spawn(new Vector3(1.5f, 1f, 8.5f), yawDegrees: 90f) with { OnGround = true };
        return new LocalPlayer(standing, TestWorlds.Flat(), TestWorlds.OneRow, new PlayerAbilities(true, true));
    }

    [Fact]
    public void Update_DoubleTapForward_Sprints()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Forward, Frame);
        player.Update(Idle, Frame);
        player.Update(Forward, Frame);

        Assert.True(player.State.Sprinting);
    }

    [Fact]
    public void Update_SlowSecondTap_DoesNotSprint()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Forward, Frame);
        player.Update(Idle, 0.5f);
        player.Update(Forward, Frame);

        Assert.False(player.State.Sprinting);
    }

    [Fact]
    public void Update_TogglePressedOnFrameWithoutTick_AppliesOnNextTick()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Idle with { ToggleFly = true }, 0.01f);
        Assert.False(player.State.Flying);
        player.Update(Idle, Frame);

        Assert.True(player.State.Flying);
    }

    [Fact]
    public void Update_ToggleWhileFeetChunkUnloaded_AppliesOnceLoaded()
    {
        VoxelWorld world = new();
        PlayerState standing = PlayerState.Spawn(new Vector3(1.5f, 1f, 8.5f)) with { OnGround = true };
        LocalPlayer player = new(standing, world, TestWorlds.OneRow, new PlayerAbilities(true, true));

        player.Update(Idle with { ToggleFly = true }, Frame);
        world.LoadChunk(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);
        player.Update(Idle, Frame);

        Assert.True(player.State.Flying);
    }

    [Fact]
    public void Update_JumpTappedBetweenTicks_StillJumps()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Idle with { Jump = true }, 0.02f);
        player.Update(Idle, Frame);

        Assert.True(player.State.Position.Y > 1.3f);
    }

    [Fact]
    public void Update_LongFrame_RunsAtMostFiveTicks()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Forward, 10f);

        // Five ticks of walking from rest cover under 1.5 blocks; ten seconds would cover far more.
        Assert.InRange(player.State.Position.X - 1.5f, 0.01f, 1.5f);
    }

    [Fact]
    public void Update_LookBeyondLimits_ClampsPitch()
    {
        LocalPlayer player = CreatePlayer();

        player.Update(Idle with { LookDelta = new Vector2(0f, -100_000f) }, 0.016f);
        Assert.Equal(LocalPlayer.MaxPitchDegrees, player.PitchDegrees);

        player.Update(Idle with { LookDelta = new Vector2(0f, 200_000f) }, 0.016f);
        Assert.Equal(-LocalPlayer.MaxPitchDegrees, player.PitchDegrees);
    }
}
