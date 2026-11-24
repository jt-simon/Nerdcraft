// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Streaming;
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
        nameof(GameSettings.AutosaveSeconds),
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
    public async Task DisposeAsync_ModifiedChunks_AreSaved()
    {
        InMemoryChunkStore store = new();
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = TestWorlds.WithEmptyChunks(position);
        NerdcraftGame game = new(new GameSettings { RenderDistance = 0, Bounds = TestWorlds.OneRow }, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher(), store), world, store: store);
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);

        await game.DisposeAsync();

        Assert.Equal(BlockTypes.Stone, store.Get(position)?[Chunk.Index(1, 1, 1)]);
    }

    [Fact]
    public async Task DisposeAsync_StoreFails_StillDisposesTheWorkQueue()
    {
        VoxelWorld world = TestWorlds.WithEmptyChunks(new ChunkPosition(0, 0, 0));
        DisposableQueue queue = new();
        NerdcraftGame game = new(new GameSettings { RenderDistance = 0, Bounds = TestWorlds.OneRow }, queue, world, store: new ThrowingStore());
        world.SetBlock(new BlockPosition(1, 1, 1), BlockTypes.Stone);

        await Assert.ThrowsAsync<IOException>(async () => await game.DisposeAsync());

        Assert.True(queue.Disposed);
    }

    [Fact]
    public async Task Update_AutosaveIntervalElapsed_SavesModifiedChunksOnce()
    {
        InMemoryChunkStore store = new();
        ChunkPosition position = new(0, 0, 0);
        VoxelWorld world = new();
        GameSettings settings = new() { RenderDistance = 0, Bounds = TestWorlds.OneRow, AutosaveSeconds = 5f };
        NerdcraftGame game = new(settings, new InlineChunkWorkQueue(new HalfSlabGenerator(), new CulledChunkMesher(), store), world, store: store);
        game.Update(default, 0.1f, 0.1f); // Requests the terrain; the next update installs it.
        game.Update(default, 0.1f, 0.1f);
        Assert.True(world.SetBlock(new BlockPosition(1, Chunk.Size - 1, 1), BlockTypes.Stone));

        game.Update(default, 0.1f, 4f);
        Assert.Equal(0, store.SaveCount(position));
        game.Update(default, 0.1f, 1.5f);
        game.Update(default, 0.1f, 6f);

        Assert.Equal(1, store.SaveCount(position));
        await game.DisposeAsync();
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
    public void Update_PausedHoverText_FadesAndExpiresByUiTime()
    {
        (NerdcraftGame game, FakeRenderer renderer) = CreateTargetingGame();
        RenderFrames(game, renderer, LookDown);
        game.Update(new InputState(Vector2.Zero, Vector2.Zero, false, false, Break: true), 0f);
        game.Update(new InputState(Vector2.Zero, Vector2.Zero, false, false, SelectSlot: 1, ScreenSize: Screen), 0f);
        game.Update(new InputState(Vector2.Zero, Vector2.Zero, false, false, SelectSlot: 0, ScreenSize: Screen), 0f);
        game.Update(TogglePause, 0f);
        Assert.True(game.IsPaused);

        // The simulation step is clamped to 0.1 s, but the HUD has really been up for 2 s: the end of full opacity.
        game.Update(default, 0.1f, 2f);
        game.Render(renderer);
        HudView hud = renderer.Huds[^1]!.Value;
        Assert.Equal("Stone", hud.HoverText);
        Assert.Equal(1f, hud.HoverOpacity, 0.001f);

        // 0.6 s later only 0.4 s of the 1 s fade remains.
        game.Update(default, 0.1f, 0.6f);
        game.Render(renderer);
        hud = renderer.Huds[^1]!.Value;
        Assert.Equal("Stone", hud.HoverText);
        Assert.Equal(0.4f, hud.HoverOpacity, 0.001f);

        // The simulation stands still; real time alone expires the text.
        game.Update(default, 0f, 0.5f);
        game.Render(renderer);
        Assert.Null(renderer.Huds[^1]!.Value.HoverText);
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
        nameof(GameSettings.AutosaveSeconds) => new GameSettings { AutosaveSeconds = 0f },
        _ => throw new ArgumentOutOfRangeException(nameof(settingName)),
    };

    private sealed class ThrowingStore : IChunkStore
    {
        public void ReadColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY, Span<BlockId> blocks, Span<bool> found, CancellationToken cancellationToken)
            => throw new IOException("The disk is full.");

        public void Save(ChunkPosition position, ReadOnlySpan<BlockId> blocks) => throw new IOException("The disk is full.");

        public ValueTask SaveGeneratedAsync(ChunkPosition position, ReadOnlyMemory<BlockId> blocks, CancellationToken cancellationToken)
            => throw new IOException("The disk is full.");
    }

    private sealed class DisposableQueue : IChunkWorkQueue, IAsyncDisposable
    {
        private readonly InlineChunkWorkQueue _inner = new(new HalfSlabGenerator(), new CulledChunkMesher());

        public bool Disposed { get; private set; }

        public bool TryRequestGenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY)
            => _inner.TryRequestGenerateColumn(chunkX, chunkZ, minChunkY, maxChunkY);

        public bool TryRequestMesh(ChunkSnapshot snapshot) => _inner.TryRequestMesh(snapshot);

        public bool TryTakeGenerated(out GeneratedChunk result) => _inner.TryTakeGenerated(out result);

        public bool TryTakeMeshed(out MeshedChunk result) => _inner.TryTakeMeshed(out result);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
