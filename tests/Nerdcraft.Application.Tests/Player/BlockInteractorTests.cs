// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Player;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Items;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests.Player;

public class BlockInteractorTests
{
    private static readonly InputState LookDown = new(Vector2.Zero, new Vector2(0f, 2000f), false, false);

    // The player hovers above a two-block stone column at X = 8, Z = 8 and looks straight down at it.
    private static (VoxelWorld World, LocalPlayer Player, BlockInteractor Interactor) Create()
    {
        VoxelWorld world = TestWorlds.Flat();
        world.SetBlock(new BlockPosition(8, 1, 8), BlockTypes.Stone);
        world.SetBlock(new BlockPosition(8, 2, 8), BlockTypes.Stone);
        LocalPlayer player = new(
            PlayerState.Spawn(new Vector3(8.5f, 5f, 8.5f)), world, TestWorlds.OneRow, new PlayerAbilities(false, false));
        player.Update(LookDown, 0f);
        return (world, player, new BlockInteractor(world));
    }

    private static void Step(BlockInteractor interactor, LocalPlayer player, InputState input, float seconds)
    {
        player.Update(input with { LookDelta = Vector2.Zero }, 0f);
        interactor.Update(input, seconds, player);
    }

    [Fact]
    public void Update_BreakHeld_RepeatsOnlyAfterInterval()
    {
        (VoxelWorld world, LocalPlayer player, BlockInteractor interactor) = Create();
        InputState hold = new(Vector2.Zero, Vector2.Zero, false, false, Break: true);

        Step(interactor, player, hold, 0f);
        Step(interactor, player, hold, 0.1f);

        Assert.Equal(BlockId.Air, world.GetBlock(new BlockPosition(8, 2, 8)));
        Assert.Equal(BlockTypes.Stone, world.GetBlock(new BlockPosition(8, 1, 8)));

        Step(interactor, player, hold, 0.2f);

        Assert.Equal(BlockId.Air, world.GetBlock(new BlockPosition(8, 1, 8)));
        Assert.Equal(2, interactor.Inventory.GetSlot(0).Count);
    }

    [Fact]
    public void Update_PlaceOnTargetedTopFace_PutsSelectedBlockAboveHit()
    {
        (VoxelWorld world, LocalPlayer player, BlockInteractor interactor) = Create();
        interactor.Inventory.TryAdd(BlockTypes.Sand);

        Step(interactor, player, new InputState(Vector2.Zero, Vector2.Zero, false, false, Place: true), 0f);

        Assert.Equal(BlockTypes.Sand, world.GetBlock(new BlockPosition(8, 3, 8)));
        Assert.True(interactor.Inventory.GetSlot(0).IsEmpty);
    }

    [Fact]
    public void Update_ButtonHeldAcrossClearPendingInput_DoesNothingUntilReleased()
    {
        (VoxelWorld world, LocalPlayer player, BlockInteractor interactor) = Create();
        InputState hold = new(Vector2.Zero, Vector2.Zero, false, false, Break: true);
        interactor.ClearPendingInput();

        Step(interactor, player, hold, 1f);
        Assert.Equal(BlockTypes.Stone, world.GetBlock(new BlockPosition(8, 2, 8)));

        Step(interactor, player, hold with { Break = false }, 0f);
        Step(interactor, player, hold, 0f);
        Assert.Equal(BlockId.Air, world.GetBlock(new BlockPosition(8, 2, 8)));
    }

    [Fact]
    public void Update_NumberKeyAndScroll_ChangeSelectedSlot()
    {
        (_, LocalPlayer player, BlockInteractor interactor) = Create();

        Step(interactor, player, new InputState(Vector2.Zero, Vector2.Zero, false, false, SelectSlot: 4), 0f);
        Step(interactor, player, new InputState(Vector2.Zero, Vector2.Zero, false, false, HotbarScroll: 1), 0f);

        Assert.Equal(5, interactor.Inventory.SelectedSlot);
    }
}
