// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nerdcraft.Application.Streaming;

internal static class StreamingMetrics
{
    internal static readonly Meter Meter = new("Nerdcraft.Streaming");

    internal static long Start(Histogram<double> duration) => duration.Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void RecordElapsed(Histogram<double> duration, long started)
    {
        if (started != 0)
        {
            duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
