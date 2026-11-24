// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

using Microsoft.Data.Sqlite;

using Nerdcraft.Application.Ports;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>
/// A world saved in a directory: <c>world.db</c> (a SQLite database in WAL mode that holds the world's settings and its compressed
/// chunks), <c>backups/</c> (daily copies, newest three kept) and <c>session.lock</c> (held while the world is open, so only one
/// process writes it). Upkeep runs automatically: an integrity check after an unclean shutdown, a daily backup, and trimming of the
/// write-ahead log and free pages.
/// </summary>
public sealed class SqliteWorld : IAsyncDisposable
{
    private const int ApplicationId = 0x4E524346; // 'NRCF'
    private const int SchemaVersion = 1;
    private const string GeneratorName = "heightmap";
    private const int GeneratorVersion = 1;
    private const string DatabaseFile = "world.db";
    private const string LockFile = "session.lock";
    private const string BackupDirectory = "backups";

    // On quit a backup still running is given this long, then abandoned; it is retried on the next launch.
    private static readonly TimeSpan BackupGrace = TimeSpan.FromSeconds(2);

    private readonly FileStream _sessionLock;
    private readonly SqliteConnection _writer;
    private readonly SqliteChunkStore _store;
    private readonly Task _backup;
    private readonly CancellationTokenSource _backupCancellation;
    private readonly TimeProvider _time;
    private Task? _disposal;
    private bool _backupAbandoned;

    private SqliteWorld(
        int seed,
        WorldBounds bounds,
        FileStream sessionLock,
        SqliteConnection writer,
        SqliteChunkStore store,
        Task backup,
        CancellationTokenSource backupCancellation,
        TimeProvider time)
    {
        Seed = seed;
        Bounds = bounds;
        _sessionLock = sessionLock;
        _writer = writer;
        _store = store;
        _backup = backup;
        _backupCancellation = backupCancellation;
        _time = time;
    }

    /// <summary>The terrain seed of this world. For an existing world it is the seed it was created with, not the one passed to <see cref="OpenAsync(string, int, WorldBounds, TimeProvider?, CancellationToken)"/>.</summary>
    public int Seed { get; }

    /// <summary>The vertical extent of this world. For an existing world it is the one it was created with.</summary>
    public WorldBounds Bounds { get; }

    /// <summary>
    /// Opens the world in <paramref name="directory"/>, creating it from <paramref name="seed"/> and <paramref name="bounds"/> when
    /// there is none. Runs the integrity check when the last session did not end cleanly, which can take a moment on a large world.
    /// The blocking work runs on a pool thread. Cancellation is checked between steps, not inside one.
    /// </summary>
    /// <param name="directory">The world's directory; created if missing.</param>
    /// <param name="seed">The seed of a new world; ignored for an existing one.</param>
    /// <param name="bounds">The vertical extent of a new world; ignored for an existing one.</param>
    /// <param name="timeProvider">The clock for backups and upkeep; the system clock when null.</param>
    /// <param name="cancellationToken">Abandons the open; the world is left closed and unlocked.</param>
    /// <exception cref="IOException">Another process has the world open.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory or its files cannot be accessed.</exception>
    /// <exception cref="InvalidDataException">The directory holds a database that is not a Nerdcraft world or cannot be read, is from a newer version, or is corrupt.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<SqliteWorld> OpenAsync(
        string directory, int seed, WorldBounds bounds, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        => OpenAsync(directory, seed, bounds, timeProvider, SqliteChunkStore.DefaultMaxPendingGenerated, cancellationToken);

    // The pending limit is a seam for tests; the public overload uses the default.
    internal static Task<SqliteWorld> OpenAsync(
        string directory, int seed, WorldBounds bounds, TimeProvider? timeProvider, int maxPendingGenerated, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            try
            {
                return OpenCore(directory, seed, bounds, timeProvider ?? TimeProvider.System, maxPendingGenerated, cancellationToken);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException($"The world database in '{directory}' could not be opened: {exception.Message}", exception);
            }
        }, cancellationToken);

    private static SqliteWorld OpenCore(
        string directory, int seed, WorldBounds bounds, TimeProvider time, int maxPendingGenerated, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        FileStream sessionLock = AcquireLock(directory);
        SqliteConnection? writer = null;
        CancellationTokenSource backupCancellation = new();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, DatabaseFile);
            string backupDirectory = Path.Combine(directory, BackupDirectory);
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 5,
            };
            string writeConnectionString = builder.ToString();
            builder.Mode = SqliteOpenMode.ReadOnly;
            builder.Pooling = true;
            string readConnectionString = builder.ToString();

            writer = new SqliteConnection(writeConnectionString);
            writer.Open();
            Execute(writer, "PRAGMA synchronous = NORMAL");
            Execute(writer, "PRAGMA journal_size_limit = 67108864");

            SqliteMaintenance maintenance = new(time);
            Task backup = Task.CompletedTask;
            DateTimeOffset now = time.GetUtcNow();
            if (IsNew(writer))
            {
                CreateSchema(writer, seed, bounds, now);
            }
            else
            {
                CheckVersion(writer);
                cancellationToken.ThrowIfCancellationRequested();
                if (WorldMeta.Get(writer, WorldMeta.CleanShutdown) != "1")
                {
                    SqliteMaintenance.VerifyIntegrity(writer, backupDirectory);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                seed = int.Parse(Required(writer, WorldMeta.Seed), CultureInfo.InvariantCulture);
                bounds = new WorldBounds(
                    int.Parse(Required(writer, WorldMeta.MinY), CultureInfo.InvariantCulture),
                    int.Parse(Required(writer, WorldMeta.MaxY), CultureInfo.InvariantCulture));
                if (maintenance.IsBackupDue(WorldMeta.Get(writer, WorldMeta.LastBackupUtc)))
                {
                    backup = maintenance.BackupAsync(writeConnectionString, backupDirectory, backupCancellation.Token);
                }
            }

            // Marked clean again only by a proper dispose, so a crash is noticed on the next open.
            WorldMeta.Set(writer, WorldMeta.CleanShutdown, "0");
            SqliteChunkStore store = new(writer, readConnectionString, maintenance, time, maxPendingGenerated);
            return new SqliteWorld(seed, bounds, sessionLock, writer, store, backup, backupCancellation, time);
        }
        catch
        {
            backupCancellation.Cancel();
            writer?.Dispose();
            SqliteConnection.ClearAllPools();
            sessionLock.Dispose();
            throw;
        }
    }

    /// <summary>The most generated chunks that were ever waiting for the writer at once.</summary>
    internal int PeakPendingGenerated => _store.PeakPendingGenerated;

    /// <summary>The chunk store of one dimension. Only dimension 0 is used so far. Valid until this world is disposed.</summary>
    public IChunkStore Chunks(byte dimension) => _store.ForDimension(dimension);

    /// <summary>
    /// Commits every queued save, stops the writer, gives a running backup up to two seconds to finish (then abandons it), empties the write-ahead log, marks the shutdown clean (unless a read failed, so the next open checks the file) and
    /// releases the world. Dispose the game first so nothing saves afterwards. If the writer failed, rethrows its exception and leaves
    /// the shutdown marked unclean so the next open checks the database.
    /// </summary>
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _store.DisposeAsync();
            _backupAbandoned = await FinishBackupAsync();
            SqliteConnection.ClearAllPools(); // Reader connections would keep the write-ahead log from being emptied.
            if (!_store.ReadFailed)
            {
                // After a failed read the file may be damaged, so it stays marked unclean and the next open checks it.
                WorldMeta.Set(_writer, WorldMeta.CleanShutdown, "1");
            }

            SqliteMaintenance.Close(_writer, emptyLog: !_backupAbandoned);
        }
        finally
        {
            _writer.Dispose();
            SqliteConnection.ClearAllPools();
            _sessionLock.Dispose();
            if (!_backupAbandoned)
            {
                _backupCancellation.Dispose(); // An abandoned backup may still be checking its token.
            }
        }
    }

    // A backup that is nearly done is allowed to finish; a long one is abandoned so quitting stays quick. Its temporary file is
    // renamed only when complete and its time is recorded only on success, so the next launch simply tries again. Returns true
    // when the backup was abandoned and may still be reading the database.
    private async Task<bool> FinishBackupAsync()
    {
        try
        {
            await _backup.WaitAsync(BackupGrace, _time);
        }
        catch (TimeoutException)
        {
            await _backupCancellation.CancelAsync();
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SqliteException or UnauthorizedAccessException)
        {
            // A failed backup is retried on the next launch; it must not stop the save.
        }

        return false;
    }

    private static FileStream AcquireLock(string directory)
    {
        try
        {
            return new FileStream(Path.Combine(directory, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException($"The world in '{directory}' is already open in another process.", exception);
        }
    }

    // A database with no application id and no tables is one we are about to create.
    private static bool IsNew(SqliteConnection connection)
    {
        long applicationId = Scalar(connection, "PRAGMA application_id");
        if (applicationId == ApplicationId)
        {
            return false;
        }

        if (applicationId != 0 || Scalar(connection, "SELECT count(*) FROM sqlite_master") != 0)
        {
            throw new InvalidDataException("The world database is not a Nerdcraft world.");
        }

        return true;
    }

    private static void CheckVersion(SqliteConnection connection)
    {
        long version = Scalar(connection, "PRAGMA user_version");
        if (version > SchemaVersion)
        {
            throw new InvalidDataException($"The world was saved by a newer version of Nerdcraft (format {version}; this version reads up to {SchemaVersion}).");
        }
    }

    private static void CreateSchema(SqliteConnection connection, int seed, WorldBounds bounds, DateTimeOffset now)
    {
        // Both settings must be chosen before the first table exists.
        Execute(connection, "PRAGMA auto_vacuum = INCREMENTAL");
        Execute(connection, "PRAGMA journal_mode = WAL");
        using SqliteTransaction transaction = connection.BeginTransaction();
        Execute(connection, $"PRAGMA application_id = {ApplicationId}");
        Execute(connection, "CREATE TABLE world_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID");

        // Chunk blobs are often larger than a WITHOUT ROWID table likes, so this is a rowid table whose integer key is the packed position.
        Execute(connection, "CREATE TABLE chunks (key INTEGER PRIMARY KEY, data BLOB NOT NULL)");
        WorldMeta.Set(connection, WorldMeta.Seed, seed.ToString(CultureInfo.InvariantCulture));
        WorldMeta.Set(connection, WorldMeta.MinY, bounds.MinY.ToString(CultureInfo.InvariantCulture));
        WorldMeta.Set(connection, WorldMeta.MaxY, bounds.MaxY.ToString(CultureInfo.InvariantCulture));
        WorldMeta.Set(connection, WorldMeta.Generator, GeneratorName);
        WorldMeta.Set(connection, WorldMeta.GeneratorVersion, GeneratorVersion.ToString(CultureInfo.InvariantCulture));
        WorldMeta.Set(connection, WorldMeta.CreatedUtc, now.ToString("O", CultureInfo.InvariantCulture));
        WorldMeta.Set(connection, WorldMeta.LastBackupUtc, now.ToString("O", CultureInfo.InvariantCulture)); // Nothing to back up yet.
        Execute(connection, $"PRAGMA user_version = {SchemaVersion}");
        transaction.Commit();
    }

    private static string Required(SqliteConnection connection, string key)
        => WorldMeta.Get(connection, key) ?? throw new InvalidDataException($"The world database has no '{key}' setting.");

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
