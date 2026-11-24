// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>
/// The automatic upkeep of a world database: an integrity check after an unclean shutdown, a daily backup, and keeping the
/// write-ahead log and free pages in check. <see cref="RunIdle"/> and <see cref="Close"/> must run on the connection's owner.
/// </summary>
internal sealed class SqliteMaintenance(TimeProvider time)
{
    /// <summary>How long after the last backup the next one is due.</summary>
    public static readonly TimeSpan BackupInterval = TimeSpan.FromHours(24);

    /// <summary>How many backup files are kept; older ones are deleted after a new one is written.</summary>
    public const int BackupsToKeep = 3;

    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(5);

    // Free pages are only worth returning to the file when there are many and they are a real share of it.
    private const int MinFreePages = 64;
    private const int IdleVacuumPages = 256;
    private const int CloseVacuumPages = 4096;

    private DateTimeOffset _lastCheckpoint = time.GetUtcNow();

    /// <summary>Checks the database for corruption.</summary>
    /// <exception cref="InvalidDataException">The check found a problem; the message names the newest backup, if any.</exception>
    public static void VerifyIntegrity(SqliteConnection connection, string backupDirectory)
    {
        string result;
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            result = command.ExecuteScalar() as string ?? "no result";
        }
        catch (SqliteException exception)
        {
            result = exception.Message; // Damage can also surface as an error rather than a report.
        }

        if (result != "ok")
        {
            string? newest = Directory.Exists(backupDirectory) ? Backups(backupDirectory).LastOrDefault() : null;
            throw new InvalidDataException(
                $"The world database failed its integrity check after an unclean shutdown ({result}). "
                + (newest is null ? "There is no backup." : $"The newest backup is {newest}."));
        }
    }

    /// <summary>True when no backup was made yet, or the last one is at least <see cref="BackupInterval"/> old.</summary>
    public bool IsBackupDue(string? lastBackupUtc)
        => !DateTimeOffset.TryParse(lastBackupUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset last)
            || time.GetUtcNow() - last >= BackupInterval;

    /// <summary>
    /// Copies the database to a new file in <paramref name="backupDirectory"/> on a background thread, using its own connection so the game
    /// keeps writing, then deletes all but the newest <see cref="BackupsToKeep"/> backups and records the time. The copy is written to a
    /// temporary name and renamed only when complete, so an abandoned backup leaves no partial file (the next one deletes the leftover).
    /// A failure or cancellation is returned in the task and leaves the time unchanged, so the next launch tries again.
    /// </summary>
    /// <param name="connectionString">A read-write connection string for the database.</param>
    /// <param name="backupDirectory">Where backups are kept; created if missing.</param>
    /// <param name="cancellationToken">Checked before the copy starts and again when it ends; it cannot interrupt the copy itself.</param>
    public Task BackupAsync(string connectionString, string backupDirectory, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(backupDirectory);
            foreach (string leftover in Directory.GetFiles(backupDirectory, "world-*.db.tmp"))
            {
                File.Delete(leftover);
            }

            string target = Path.Combine(backupDirectory, $"world-{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.db");
            string temporary = target + ".tmp";

            using SqliteConnection connection = new(connectionString);
            connection.Open();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", temporary);
                command.ExecuteNonQuery();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                File.Delete(temporary);
                cancellationToken.ThrowIfCancellationRequested();
            }

            File.Move(temporary, target, overwrite: true);
            foreach (string old in Backups(backupDirectory).SkipLast(BackupsToKeep))
            {
                File.Delete(old);
            }

            WorldMeta.Set(connection, WorldMeta.LastBackupUtc, now.ToString("O", CultureInfo.InvariantCulture));
        }, cancellationToken);
    }

    /// <summary>
    /// Called when the writer has had nothing to do for a while. At most every few minutes it moves the log into the database
    /// without blocking and returns a bounded number of free pages to the file system.
    /// </summary>
    public void RunIdle(SqliteConnection connection)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (now - _lastCheckpoint < CheckpointInterval)
        {
            return;
        }

        _lastCheckpoint = now;
        Run(connection, "PRAGMA wal_checkpoint(PASSIVE)");
        ReclaimFreePages(connection, IdleVacuumPages);
    }

    /// <summary>
    /// Called once the writer has stopped: returns free pages and, unless <paramref name="emptyLog"/> is false, empties the
    /// write-ahead log file. Emptying waits for readers to finish, so it is skipped while an abandoned backup still reads.
    /// </summary>
    public static void Close(SqliteConnection connection, bool emptyLog)
    {
        ReclaimFreePages(connection, CloseVacuumPages);
        if (emptyLog)
        {
            Run(connection, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
    }

    private static void ReclaimFreePages(SqliteConnection connection, int pages)
    {
        long free = Scalar(connection, "PRAGMA freelist_count");
        if (free >= MinFreePages && free * 10 > Scalar(connection, "PRAGMA page_count"))
        {
            Run(connection, $"PRAGMA incremental_vacuum({pages})");
        }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // Steps the statement to the end: incremental_vacuum frees one page per step.
    private static void Run(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    // Backup files, oldest first (the names carry a sortable UTC time).
    private static IEnumerable<string> Backups(string directory)
        => Directory.GetFiles(directory, "world-*.db").Order(StringComparer.Ordinal);
}
