// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;

namespace Nerdcraft.Persistence.Sqlite.Tests.Support;

/// <summary>A unique empty directory under the system temp folder, deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Full path of the directory (created on first use by the code under test).</summary>
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nerdcraft-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Deletes the directory and everything in it.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
