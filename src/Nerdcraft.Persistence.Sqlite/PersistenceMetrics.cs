// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nerdcraft.Persistence.Sqlite;

/// <summary>The <c>Nerdcraft.Persistence</c> meter: how far the writer is behind and how long its commits and column reads take.</summary>
internal static class PersistenceMetrics
{
    internal static readonly Meter Meter = new("Nerdcraft.Persistence");

    internal static readonly Histogram<int> QueueDepth = Meter.CreateHistogram<int>(
        "nerdcraft.persistence.queue.depth", "{chunk}", "Chunks queued for the writer when it starts a batch");
    internal static readonly Histogram<int> BatchSize = Meter.CreateHistogram<int>(
        "nerdcraft.persistence.batch.size", "{chunk}", "Chunks committed in one transaction");
    internal static readonly Histogram<double> CommitTime = Meter.CreateHistogram<double>(
        "nerdcraft.persistence.commit.duration", "ms", "Time to encode and commit one batch");
    internal static readonly Histogram<double> ReadTime = Meter.CreateHistogram<double>(
        "nerdcraft.persistence.read.duration", "ms", "Time to read one column");

    internal static long Start(Histogram<double> duration) => duration.Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void RecordElapsed(Histogram<double> duration, long started)
    {
        if (started != 0)
        {
            duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
