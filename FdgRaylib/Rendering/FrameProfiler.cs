using System.Diagnostics;

namespace FdgRaylib.Rendering;

/// <summary>
/// Opt-in frame timing (set FDG_PROFILE=1): sections time themselves and every 3 seconds the average ms per
/// frame of each goes to the console. Off by default and free when off. Added 2026-11-06 to chase the
/// frame-rate drop Kai saw while previewing a move.
/// </summary>
internal static class FrameProfiler
{
    public static readonly bool On = Environment.GetEnvironmentVariable("FDG_PROFILE") == "1";
    private static readonly Dictionary<string, double> Totals = new();
    private static readonly Stopwatch Window = Stopwatch.StartNew();
    private static int _frames;

    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        public Scope(string? name) { _name = name; _start = name == null ? 0 : Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_name == null) return;
            double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            Totals[_name] = Totals.GetValueOrDefault(_name) + ms;
        }
    }

    public static Scope Time(string name) => new(On ? name : null);

    public static void EndFrame()
    {
        if (!On) return;
        _frames++;
        if (Window.Elapsed.TotalSeconds < 3) return;
        Console.WriteLine($"[profile] {_frames / Window.Elapsed.TotalSeconds:0} fps  " +
            string.Join("  ", Totals.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / _frames:0.0}ms")));
        Totals.Clear(); _frames = 0; Window.Restart();
    }
}
