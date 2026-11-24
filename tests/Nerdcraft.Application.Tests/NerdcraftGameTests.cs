// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Tests.Support;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Tests;

public class NerdcraftGameTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly InputState ToggleDebug = new(Vector2.Zero, Vector2.Zero, false, false, ToggleDebug: true);

    private static readonly InputState LookDown = new(Vector2.Zero, new Vector2(0f, 600f), false, false);

    private static readonly Vector2 Screen = new(800f, 600f);
    private static readonly InputState TogglePause = new(Vector2.Zero, Vector2.Zero, false, false, TogglePause: true, ScreenSize: Screen);

    // Each of these would otherwise start a game that runs but never shows any terrain.
    public static TheoryData<string> InvalidSettingNames =>
    [
        nameof(GameSettings.RenderDistance),
        nameof(GameSettings.Bounds),
        nameof(GameSettings.WorkerCount),
        nameof(GameSettings.MaxUploadsPerFrame),
        nameof(GameSettings.MaxChunkInstallsPerFrame),
        nameof(GameSettings.MaxMeshRequestsPerFrame),
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
    public async Task Create_WithFactoryWiring_UploadsTerrainAndDisposes()
    {
        FakeRenderer renderer = new();
        NerdcraftGame game = NerdcraftGame.Create(new GameSettings { RenderDistance = 0, WorkerCount = 1 });

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(Timeout);
            while (renderer.Uploads.Count == 0)
            {
                game.Update(default, 0f);
                game.Render(renderer);
                await Task.Delay(5, cts.Token);
            }

            Assert.Contains(renderer.VertexCounts.Values, count => count > 0);
        }
        finally
        {
            await game.DisposeAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Update_ToggleDebugPressed_ShowsOverlayWithEyePosition()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();

        game.Update(ToggleDebug, 0.016f);
        game.Render(renderer);

        // Spawn is centered on block (8, 8) with the feet at 100 and the eyes 1.62 above them.
        DebugOverlay? overlay = renderer.DebugOverlays[^1];
        Assert.NotNull(overlay);
        Vector3 eye = overlay.Value.CameraPosition;
        Assert.True(Vector3.Distance(new Vector3(8.5f, 101.62f, 8.5f), eye) < 1e-3f);
    }

    [Fact]
    public void Update_ToggleDebugPressedAgain_HidesOverlay()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();

        game.Update(ToggleDebug, 0.016f);
        game.Update(ToggleDebug, 0.016f);
        game.Render(renderer);

        Assert.Null(renderer.DebugOverlays[^1]);
    }

    [Fact]
    public void Update_TogglePausePressed_ShowsMenuAndFreezesPlayer()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();
        InputState walk = new(new Vector2(0f, 1f), Vector2.Zero, false, false, ToggleDebug: true, ScreenSize: Screen);

        game.Update(TogglePause, 0.016f);
        game.Update(walk, 1f);
        game.Render(renderer);

        Assert.True(game.IsPaused);
        Assert.NotNull(renderer.Menus[^1]);
        // Debug toggles still work while paused; the eye stays at the spawn point.
        Vector3 eye = renderer.DebugOverlays[^1]!.Value.CameraPosition;
        Assert.True(Vector3.Distance(new Vector3(8.5f, 101.62f, 8.5f), eye) < 1e-3f);
    }

    [Fact]
    public void Update_ClickOnQuitWhilePaused_RequestsExit()
    {
        (NerdcraftGame game, _) = CreateDebugTestGame();
        game.Update(TogglePause, 0.016f);

        game.Update(ClickOn(PauseMenu.QuitIndex), 0.016f);

        Assert.True(game.ExitRequested);
    }

    [Fact]
    public void Update_ClickOnResumeWhilePaused_ClosesMenu()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();
        game.Update(TogglePause, 0.016f);

        game.Update(ClickOn(PauseMenu.ResumeIndex), 0.016f);
        game.Render(renderer);

        Assert.False(game.IsPaused);
        Assert.False(game.ExitRequested);
        Assert.Null(renderer.Menus[^1]);
    }

    [Fact]
    public void Update_AfterResume_IgnoresLookDeltaWhileCursorRecaptures()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateDebugTestGame();
        game.Update(TogglePause, 0.016f);
        game.Update(TogglePause, 0.016f);
        InputState spin = new(Vector2.Zero, new Vector2(10_000f, 0f), false, false, ScreenSize: Screen);
        game.Update(spin, 0.016f);
        game.Update(spin, 0.016f);
        game.Render(renderer);
        Vector3 ignored = renderer.CameraForwards[^1];

        game.Update(spin, 0.016f);
        game.Render(renderer);

        // The resume frame and the two after it drop look; the next one applies it.
        Assert.Equal(new Vector3(0f, 0f, -1f), ignored);
        Assert.NotEqual(ignored, renderer.CameraForwards[^1]);
    }

    [Fact]
    public void Update_ClickOnQuitWhileNotPaused_DoesNotRequestExit()
    {
        (NerdcraftGame game, _) = CreateDebugTestGame();

        game.Update(ClickOn(PauseMenu.QuitIndex), 0.016f);

        Assert.False(game.ExitRequested);
    }

    [Fact]
    public void Render_PlayerLookingDownAtGround_OutlinesTopStoneBlockAndShowsCrosshair()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateTargetingGame();

        RenderFrames(game, renderer, LookDown);

        Assert.True(renderer.Crosshairs[^1]);
        Aabb outline = renderer.Outlines[^1];
        Assert.Equal(7f, outline.Min.Y, 0.01f);
        Assert.Equal(8f, outline.Max.Y, 0.01f);
    }

    [Fact]
    public void Render_Paused_HidesOutlineAndCrosshair()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateTargetingGame();
        RenderFrames(game, renderer, LookDown);
        Assert.NotEmpty(renderer.Outlines);
        renderer.Outlines.Clear();

        game.Update(TogglePause, 0f);
        game.Render(renderer);

        Assert.Empty(renderer.Outlines);
        Assert.False(renderer.Crosshairs[^1]);
    }

    [Fact]
    public void Update_ResumeClickStillHeld_DoesNotBreakBlock()
    {
        VoxelWorld world = new();
        GameSettings settings = new() { RenderDistance = 1, Bounds = new WorldBounds(0, Chunk.Size - 1), MaxUploadsPerFrame = 100 };
        NerdcraftGame game = new(settings, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher()), world, spawnHeight: 8f);
        FakeRenderer renderer = new();
        RenderFrames(game, renderer, LookDown);
        Vector3 min = renderer.Outlines[^1].Min;
        BlockPosition target = new((int)MathF.Round(min.X), (int)MathF.Round(min.Y), (int)MathF.Round(min.Z));
        Assert.Equal(BlockTypes.Stone, world.GetBlock(target));

        game.Update(TogglePause, 0.016f);
        game.Update(ClickOn(PauseMenu.ResumeIndex) with { Break = true }, 0.016f);
        game.Update(new InputState(Vector2.Zero, Vector2.Zero, false, false, Break: true, ScreenSize: Screen), 0.016f);

        Assert.False(game.IsPaused);
        Assert.Equal(BlockTypes.Stone, world.GetBlock(target));
    }

    private static void RenderFrames(NerdcraftGame game, FakeRenderer renderer, InputState firstFrame)
    {
        game.Update(firstFrame, 0f);
        for (int i = 0; i < 3; i++)
        {
            game.Render(renderer);
            game.Update(default, 0f);
        }

        game.Render(renderer);
    }

    // The slab's stone top is at Y = 8; the eye is above it and looks about 60 degrees down, well inside reach.
    private static (NerdcraftGame Game, FakeRenderer Renderer) CreateTargetingGame()
    {
        GameSettings settings = new()
        {
            RenderDistance = 1,
            Bounds = new WorldBounds(0, Chunk.Size - 1),
            MaxUploadsPerFrame = 100,
        };
        InlineChunkWorkQueue queue = new(new HalfSlabGenerator(), new CulledChunkMesher());
        return (new NerdcraftGame(settings, queue, new VoxelWorld(), spawnHeight: 8f), new FakeRenderer());
    }

    private static InputState ClickOn(int buttonIndex)
    {
        UiRect bounds = PauseMenu.Layout(Screen, Vector2.Zero).Buttons[buttonIndex].Bounds;
        Vector2 center = new(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        return new InputState(Vector2.Zero, Vector2.Zero, false, false, Pointer: center, Click: true, ScreenSize: Screen);
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
        nameof(GameSettings.MaxChunkInstallsPerFrame) => new GameSettings { MaxChunkInstallsPerFrame = 0 },
        nameof(GameSettings.MaxMeshRequestsPerFrame) => new GameSettings { MaxMeshRequestsPerFrame = 0 },
        nameof(GameSettings.FovYDegrees) => new GameSettings { FovYDegrees = 180f },
        _ => throw new ArgumentOutOfRangeException(nameof(settingName)),
    };
}
