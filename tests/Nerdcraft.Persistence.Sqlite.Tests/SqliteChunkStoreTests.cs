// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;

using Nerdcraft.Application.Ports;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Persistence.Sqlite.Tests.Support;

namespace Nerdcraft.Persistence.Sqlite.Tests;

public sealed class SqliteChunkStoreTests : IDisposable
{
    private static readonly WorldBounds Bounds = new(-50, 200);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task ReadColumn_AfterReopen_ReturnsSavedChunksAndReportsMissingRows()
    {
        BlockId[][] saved = [Patterned(1), Patterned(2), Patterned(3)];
        SqliteWorld world = await OpenAsync();
        world.Chunks(0).Save(new ChunkPosition(-3, -1, 5), saved[0]);
        world.Chunks(0).Save(new ChunkPosition(-3, 0, 5), saved[1]);
        world.Chunks(0).Save(new ChunkPosition(-3, 2, 5), saved[2]);
        await world.DisposeAsync();

        await using SqliteWorld reopened = await OpenAsync();
        (BlockId[] blocks, bool[] found) = Read(reopened.Chunks(0), -3, 5, -1, 2);

        Assert.Equal([true, true, false, true], found);
        Assert.Equal(saved[0], blocks[..Chunk.Volume]);
        Assert.Equal(saved[1], blocks[Chunk.Volume..(2 * Chunk.Volume)]);
        Assert.Equal(saved[2], blocks[(3 * Chunk.Volume)..]);
    }

    [Fact]
    public async Task ReadColumn_RightAfterSave_ReturnsTheLatestSave()
    {
        await using SqliteWorld world = await OpenAsync();
        IChunkStore store = world.Chunks(0);
        ChunkPosition position = new(0, 0, 0);

        store.Save(position, Patterned(1));
        Assert.Equal(Patterned(1), Read(store, 0, 0, 0, 0).Blocks);

        store.Save(position, Patterned(2));
        Assert.Equal(Patterned(2), Read(store, 0, 0, 0, 0).Blocks);
    }

    [Fact]
    public async Task SaveGeneratedAsync_AfterSave_KeepsTheSavedBlocks()
    {
        ChunkPosition position = new(1, 1, 1);
        SqliteWorld world = await OpenAsync();
        world.Chunks(0).Save(position, Patterned(1));
        await world.Chunks(0).SaveGeneratedAsync(position, Patterned(2), TestContext.Current.CancellationToken);
        await world.DisposeAsync();

        await using SqliteWorld reopened = await OpenAsync();

        Assert.Equal(Patterned(1), Read(reopened.Chunks(0), 1, 1, 1, 1).Blocks);
    }

    [Fact]
    public async Task SaveGeneratedAsync_AfterEarlierSaveWasCommitted_KeepsTheSavedBlocks()
    {
        ChunkPosition position = new(1, 1, 1);
        SqliteWorld world = await OpenAsync();
        world.Chunks(0).Save(position, Patterned(1));
        await world.DisposeAsync();
        world = await OpenAsync();
        await world.Chunks(0).SaveGeneratedAsync(position, Patterned(2), TestContext.Current.CancellationToken);
        await world.DisposeAsync();

        await using SqliteWorld reopened = await OpenAsync();

        Assert.Equal(Patterned(1), Read(reopened.Chunks(0), 1, 1, 1, 1).Blocks);
    }

    [Fact]
    public async Task Save_MoreThanOneBatch_PersistsEveryChunk()
    {
        const int Count = 1500;
        SqliteWorld world = await OpenAsync();
        for (int x = 0; x < Count; x++)
        {
            world.Chunks(0).Save(new ChunkPosition(x, 0, 0), Filled((ushort)(x % 9)));
        }

        await world.DisposeAsync();

        await using SqliteWorld reopened = await OpenAsync();
        for (int x = 0; x < Count; x++)
        {
            Assert.Equal(Filled((ushort)(x % 9)), Read(reopened.Chunks(0), x, 0, 0, 0).Blocks);
        }
    }

    [Fact]
    public async Task SaveGeneratedAsync_SlowWriterAndSmallLimit_NeverQueuesMoreThanTheLimitAndPersistsEverything()
    {
        const int Limit = 4;
        const int Workers = 4;
        const int PerWorker = 400;
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        SqliteWorld world = await SqliteWorld.OpenAsync(_directory.Path, 1, Bounds, null, Limit, cancellation);
        IChunkStore store = world.Chunks(0);

        await Task.WhenAll(Enumerable.Range(0, Workers).Select(async worker =>
        {
            for (int i = 0; i < PerWorker; i++)
            {
                await store.SaveGeneratedAsync(new ChunkPosition(worker, 0, i), Filled((ushort)(i % 9)), cancellation);
            }
        }));
        int peak = world.PeakPendingGenerated;
        await world.DisposeAsync();

        Assert.InRange(peak, 1, Limit);
        await using SqliteWorld reopened = await OpenAsync();
        for (int worker = 0; worker < Workers; worker++)
        {
            for (int i = 0; i < PerWorker; i++)
            {
                Assert.Equal(Filled((ushort)(i % 9)), Read(reopened.Chunks(0), worker, i, 0, 0).Blocks);
            }
        }
    }

    [Fact]
    public async Task SaveGeneratedAsync_CancelledToken_Throws()
    {
        await using SqliteWorld world = await OpenAsync();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await world.Chunks(0).SaveGeneratedAsync(new ChunkPosition(0, 0, 0), Filled(1), cancelled.Token));
    }

    [Fact]
    public async Task ReadColumn_UnreadableBlob_ThrowsNamingTheChunk()
    {
        SqliteWorld world = await OpenAsync();
        world.Chunks(0).Save(new ChunkPosition(3, 1, -7), Patterned(1));
        await world.DisposeAsync();
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory.Path, "world.db"), Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE chunks SET data = X'0102'";
            command.ExecuteNonQuery();
        }

        await using SqliteWorld reopened = await OpenAsync();
        BlockId[] blocks = new BlockId[Chunk.Volume];
        bool[] found = new bool[1];
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => reopened.Chunks(0).ReadColumn(3, -7, 1, 1, blocks, found, TestContext.Current.CancellationToken));

        Assert.Contains("(3, 1, -7)", exception.Message);
    }

    [Fact]
    public async Task ReadColumn_CancelledToken_Throws()
    {
        await using SqliteWorld world = await OpenAsync();
        IChunkStore store = world.Chunks(0);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        BlockId[] blocks = new BlockId[Chunk.Volume];
        bool[] found = new bool[1];

        Assert.ThrowsAny<OperationCanceledException>(() => store.ReadColumn(0, 0, 0, 0, blocks, found, cancelled.Token));
    }

    private Task<SqliteWorld> OpenAsync() => SqliteWorld.OpenAsync(_directory.Path, seed: 1, Bounds, null, TestContext.Current.CancellationToken);

    private static (BlockId[] Blocks, bool[] Found) Read(IChunkStore store, int x, int z, int minY, int maxY)
    {
        int rows = maxY - minY + 1;
        BlockId[] blocks = new BlockId[rows * Chunk.Volume];
        bool[] found = new bool[rows];
        store.ReadColumn(x, z, minY, maxY, blocks, found, TestContext.Current.CancellationToken);
        return (blocks, found);
    }

    private static BlockId[] Filled(ushort id) => Enumerable.Repeat(new BlockId(id), Chunk.Volume).ToArray();

    private static BlockId[] Patterned(int seed)
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new BlockId((ushort)(((i * 31) + seed) % 5));
        }

        return blocks;
    }
}
