using System.Diagnostics;

/// <summary>
/// Performance sweep (2026-11-15): named stopwatches for the battle screen's per-frame work, read by ScriptedRun's
/// "perf:N" command. Does nothing (no clock reads) unless a perf run has switched it on.
/// </summary>
public static class PerfProbe
{
    private static readonly Dictionary<string, long> Ticks = new();

    /// <summary>On only while ScriptedRun's "perf:N" is measuring.</summary>
    public static bool Enabled;

    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Stop(string name, long started)
    {
        if (!Enabled) return;
        long t = Stopwatch.GetTimestamp() - started;
        lock (Ticks) Ticks[name] = Ticks.GetValueOrDefault(name) + t;
    }

    /// <summary>Milliseconds per section since the last call, divided by <paramref name="frames"/>; resets.</summary>
    public static string TakeReport(double frames)
    {
        lock (Ticks)
        {
            string s = string.Join(" ", Ticks.OrderByDescending(k => k.Value)
                .Select(k => $"{k.Key}={k.Value * 1000.0 / Stopwatch.Frequency / frames:0.000}"));
            Ticks.Clear();
            return s;
        }
    }
}
