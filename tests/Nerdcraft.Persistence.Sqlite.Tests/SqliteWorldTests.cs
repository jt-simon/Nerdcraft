// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Persistence.Sqlite.Tests.Support;

namespace Nerdcraft.Persistence.Sqlite.Tests;

public sealed class SqliteWorldTests : IDisposable
{
    private static readonly WorldBounds Bounds = new(-50, 200);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string DatabasePath => Path.Combine(_directory.Path, "world.db");

    [Fact]
    public async Task Open_NewWorld_UsesWalAndIncrementalAutoVacuum()
    {
        await using SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);

        Assert.Equal("wal", Scalar("PRAGMA journal_mode"));
        Assert.Equal("2", Scalar("PRAGMA auto_vacuum"));
    }

    [Fact]
    public async Task Open_ExistingWorld_ReturnsStoredSeedAndBounds()
    {
        SqliteWorld created = await OpenWorldAsync(_directory.Path, 42, new WorldBounds(-32, 100));
        await created.DisposeAsync();

        await using SqliteWorld reopened = await OpenWorldAsync(_directory.Path, 7, Bounds);

        Assert.Equal(42, reopened.Seed);
        Assert.Equal(new WorldBounds(-32, 100), reopened.Bounds);
    }

    [Fact]
    public async Task Open_WorldAlreadyOpen_ThrowsUntilTheFirstIsDisposed()
    {
        SqliteWorld first = await OpenWorldAsync(_directory.Path, 1, Bounds);

        await Assert.ThrowsAsync<IOException>(() => OpenWorldAsync(_directory.Path, 1, Bounds));

        await first.DisposeAsync();
        await using SqliteWorld second = await OpenWorldAsync(_directory.Path, 1, Bounds);
    }

    [Fact]
    public async Task Open_NewerSchemaVersion_Throws()
    {
        await (await OpenWorldAsync(_directory.Path, 1, Bounds)).DisposeAsync();
        Execute("PRAGMA user_version = 99");

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenWorldAsync(_directory.Path, 1, Bounds));
    }

    [Fact]
    public async Task Open_DatabaseThatIsNotAWorld_Throws()
    {
        Directory.CreateDirectory(_directory.Path);
        Execute("CREATE TABLE other (id INTEGER)");

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenWorldAsync(_directory.Path, 1, Bounds));
    }

    [Fact]
    public async Task Open_FileThatIsNotADatabase_ThrowsInvalidData()
    {
        Directory.CreateDirectory(_directory.Path);
        await File.WriteAllTextAsync(DatabasePath, new string('x', 4096), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenWorldAsync(_directory.Path, 1, Bounds));
    }

    [Fact]
    public async Task DisposeAsync_EmptiesWalAndMarksShutdownClean()
    {
        SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);
        world.Chunks(0).Save(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);

        await world.DisposeAsync();

        FileInfo wal = new(DatabasePath + "-wal");
        Assert.True(!wal.Exists || wal.Length == 0);
        Assert.Equal("1", Scalar("SELECT value FROM world_meta WHERE key = 'clean_shutdown'"));
    }

    [Fact]
    public async Task Open_AfterUncleanShutdown_OpensAnIntactWorld()
    {
        SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);
        world.Chunks(0).Save(new ChunkPosition(2, 0, 0), Enumerable.Repeat(BlockTypes.Stone, Chunk.Volume).ToArray());
        await world.DisposeAsync();
        Execute("UPDATE world_meta SET value = '0' WHERE key = 'clean_shutdown'");

        await using SqliteWorld reopened = await OpenWorldAsync(_directory.Path, 1, Bounds);

        BlockId[] blocks = new BlockId[Chunk.Volume];
        bool[] found = new bool[1];
        reopened.Chunks(0).ReadColumn(2, 0, 0, 0, blocks, found, TestContext.Current.CancellationToken);
        Assert.True(found[0]);
    }

    [Fact]
    public async Task Open_AfterUncleanShutdownWithDamage_Throws()
    {
        SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);
        Random random = new(3);
        for (int x = 0; x < 300; x++)
        {
            BlockId[] blocks = new BlockId[Chunk.Volume];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = new BlockId((ushort)random.Next(2000));
            }

            world.Chunks(0).Save(new ChunkPosition(x, 0, 0), blocks);
        }

        await world.DisposeAsync();
        Execute("UPDATE world_meta SET value = '0' WHERE key = 'clean_shutdown'");
        using (FileStream file = new(DatabasePath, FileMode.Open, FileAccess.Write))
        {
            file.Seek(4 * 4096, SeekOrigin.Begin);
            file.Write(Enumerable.Repeat((byte)0xFF, 4096).ToArray());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenWorldAsync(_directory.Path, 1, Bounds));
    }

    [Fact]
    public async Task ReadColumn_DamagedPage_ThrowsInvalidDataNamingTheColumn()
    {
        await using SqliteWorld world = await OpenDamagedWorldAsync();

        (int column, InvalidDataException exception) = FirstReadFailure(world);

        Assert.Contains($"({column}, 0)", exception.Message);
    }

    [Fact]
    public async Task DisposeAsync_AfterAFailedRead_LeavesShutdownUnclean()
    {
        SqliteWorld world = await OpenDamagedWorldAsync();
        FirstReadFailure(world);

        await world.DisposeAsync();

        // The next open then runs the integrity check instead of trusting the file.
        Assert.Equal("0", Scalar("SELECT value FROM world_meta WHERE key = 'clean_shutdown'"));
    }

    [Fact]
    public async Task Open_BackupDue_WritesBackupAndKeepsNewestThree()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds, clock);
        world.Chunks(0).Save(new ChunkPosition(0, 0, 0), new BlockId[Chunk.Volume]);
        await world.DisposeAsync();

        for (int day = 0; day < 4; day++)
        {
            clock.Advance(TimeSpan.FromHours(25));
            await (await OpenWorldAsync(_directory.Path, 1, Bounds, clock)).DisposeAsync();
        }

        string[] backups = Directory.GetFiles(Path.Combine(_directory.Path, "backups"), "*.db").Order().Select(Path.GetFileName).ToArray()!;
        Assert.Equal(["world-20260103T140000Z.db", "world-20260104T150000Z.db", "world-20260105T160000Z.db"], backups);
        Assert.Equal("1", Scalar("SELECT count(*) FROM chunks", Path.Combine(_directory.Path, "backups", backups[^1])));
    }

    [Fact]
    public async Task Open_BackupNotDue_WritesNoBackup()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await (await OpenWorldAsync(_directory.Path, 1, Bounds, clock)).DisposeAsync();
        clock.Advance(TimeSpan.FromHours(1));

        await (await OpenWorldAsync(_directory.Path, 1, Bounds, clock)).DisposeAsync();

        Assert.False(Directory.Exists(Path.Combine(_directory.Path, "backups")));
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_ThrowsAndLeavesTheWorldClosed()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SqliteWorld.OpenAsync(_directory.Path, 1, Bounds, cancellationToken: cancelled.Token));

        await using SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);
    }

    [Fact]
    public async Task Open_BackupDue_DeletesLeftoverTemporaryBackups()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await (await OpenWorldAsync(_directory.Path, 1, Bounds, clock)).DisposeAsync();
        string backups = Path.Combine(_directory.Path, "backups");
        Directory.CreateDirectory(backups);
        string leftover = Path.Combine(backups, "world-20251231T000000Z.db.tmp");
        await File.WriteAllTextAsync(leftover, "half a backup", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(25));

        await (await OpenWorldAsync(_directory.Path, 1, Bounds, clock)).DisposeAsync();

        Assert.False(File.Exists(leftover));
        Assert.Single(Directory.GetFiles(backups, "*.db"));
    }

    [Fact]
    public async Task BackupAsync_CancelledToken_WritesNoBackup()
    {
        await (await OpenWorldAsync(_directory.Path, 1, Bounds)).DisposeAsync();
        SqliteMaintenance maintenance = new(TimeProvider.System);
        string backups = Path.Combine(_directory.Path, "backups");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => maintenance.BackupAsync(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString(), backups, cancelled.Token));

        Assert.False(Directory.Exists(backups));
    }

    // A world that was closed cleanly, so it opens without the integrity check, with one page of its chunk data overwritten.
    private async Task<SqliteWorld> OpenDamagedWorldAsync()
    {
        SqliteWorld world = await OpenWorldAsync(_directory.Path, 1, Bounds);
        Random random = new(3);
        for (int x = 0; x < 300; x++)
        {
            BlockId[] blocks = new BlockId[Chunk.Volume];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = new BlockId((ushort)random.Next(2000));
            }

            world.Chunks(0).Save(new ChunkPosition(x, 0, 0), blocks);
        }

        await world.DisposeAsync();
        using (FileStream file = new(DatabasePath, FileMode.Open, FileAccess.Write))
        {
            file.Seek(4 * 4096, SeekOrigin.Begin);
            file.Write(Enumerable.Repeat((byte)0xFF, 4096).ToArray());
        }

        return await OpenWorldAsync(_directory.Path, 1, Bounds);
    }

    private static (int Column, InvalidDataException Exception) FirstReadFailure(SqliteWorld world)
    {
        BlockId[] blocks = new BlockId[Chunk.Volume];
        bool[] found = new bool[1];
        for (int x = 0; x < 300; x++)
        {
            try
            {
                world.Chunks(0).ReadColumn(x, 0, 0, 0, blocks, found, TestContext.Current.CancellationToken);
            }
            catch (InvalidDataException exception)
            {
                return (x, exception);
            }
        }

        throw new InvalidOperationException("No read failed, so the damage was not on any chunk's path.");
    }

    private Task<SqliteWorld> OpenWorldAsync(string directory, int seed, WorldBounds bounds, TimeProvider? clock = null)
        => SqliteWorld.OpenAsync(directory, seed, bounds, clock, TestContext.Current.CancellationToken);

    private string Scalar(string sql, string? path = null)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path ?? DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void Execute(string sql)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
