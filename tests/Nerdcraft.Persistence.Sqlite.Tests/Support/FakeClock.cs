// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Persistence.Sqlite.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> whose UTC time only moves when a test says so.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
