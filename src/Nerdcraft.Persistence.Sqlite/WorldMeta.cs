// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>Reads and writes the <c>world_meta</c> key/value table.</summary>
internal static class WorldMeta
{
    public const string Seed = "seed";
    public const string MinY = "min_y";
    public const string MaxY = "max_y";
    public const string Generator = "generator";
    public const string GeneratorVersion = "generator_version";
    public const string CreatedUtc = "created_utc";
    public const string CleanShutdown = "clean_shutdown";
    public const string LastBackupUtc = "last_backup_utc";

    /// <summary>Returns the value stored under <paramref name="key"/>, or null when there is none.</summary>
    public static string? Get(SqliteConnection connection, string key)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM world_meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing any earlier value.</summary>
    public static void Set(SqliteConnection connection, string key, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO world_meta(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
