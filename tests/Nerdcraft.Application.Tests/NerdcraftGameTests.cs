// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests;

public class NerdcraftGameTests
{
    // Each of these would otherwise start a game that runs but never shows any terrain.
    public static TheoryData<string> InvalidSettingNames =>
    [
        nameof(GameSettings.RenderDistance),
        nameof(GameSettings.Bounds),
        nameof(GameSettings.WorkerCount),
        nameof(GameSettings.MaxUploadsPerFrame),
        nameof(GameSettings.FovYDegrees),
    ];

    [Theory]
    [MemberData(nameof(InvalidSettingNames))]
    public void Create_SettingOutOfRange_Throws(string settingName)
        => Assert.Throws<ArgumentOutOfRangeException>(() => NerdcraftGame.Create(Invalid(settingName)));

    [Theory]
    [MemberData(nameof(InvalidSettingNames))]
    public void Constructor_SettingOutOfRange_Throws(string settingName)
    {
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());

        Assert.Throws<ArgumentOutOfRangeException>(() => new NerdcraftGame(Invalid(settingName), queue, new VoxelWorld()));
    }

    [Fact]
    public async Task Create_DefaultSettings_Succeeds()
    {
        await using NerdcraftGame game = NerdcraftGame.Create(new GameSettings());

        Assert.NotNull(game);
    }

    [Fact]
    public void Update_ToggleDebugPressed_ShowsOverlayWithCameraPosition()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();

        game.Update(new InputState(Vector3.Zero, Vector2.Zero, false, ToggleDebug: true), 0.016f);
        game.Render(renderer);

        Assert.Equal(new Vector3(8f, 100f, 8f), renderer.DebugOverlays[^1]);
    }

    [Fact]
    public void Update_ToggleDebugPressedAgain_HidesOverlay()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();

        game.Update(new InputState(Vector3.Zero, Vector2.Zero, false, ToggleDebug: true), 0.016f);
        game.Update(new InputState(Vector3.Zero, Vector2.Zero, false, ToggleDebug: true), 0.016f);
        game.Render(renderer);

        Assert.Null(renderer.DebugOverlays[^1]);
    }

    private static (NerdcraftGame Game, FakeRenderer Renderer) CreateDebugTestGame()
    {
        GameSettings settings = new()
        {
            RenderDistance = 1,
            Bounds = new WorldBounds(0, Chunk.Size - 1),
            MaxUploadsPerFrame = 100,
        };
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        return (new NerdcraftGame(settings, queue, new VoxelWorld(), spawnHeight: 100f), new FakeRenderer());
    }

    private static GameSettings Invalid(string settingName) => settingName switch
    {
        nameof(GameSettings.RenderDistance) => new GameSettings { RenderDistance = -1 },
        nameof(GameSettings.Bounds) => new GameSettings { Bounds = new WorldBounds(10, 5) },
        nameof(GameSettings.WorkerCount) => new GameSettings { WorkerCount = 0 },
        nameof(GameSettings.MaxUploadsPerFrame) => new GameSettings { MaxUploadsPerFrame = 0 },
        nameof(GameSettings.FovYDegrees) => new GameSettings { FovYDegrees = 180f },
        _ => throw new ArgumentOutOfRangeException(nameof(settingName)),
    };
}
