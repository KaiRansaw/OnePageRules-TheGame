using FDG;
using FDG.Data;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;

namespace FdgRaylib.Game;

/// <summary>
/// Watching a battle replay (homebrew, Kai 2026-11-27): one table view whose store is switched from save point to
/// save point (<see cref="SwitchingDataStore"/>), and the battle report rebuilt to match. No engine runs and no dice
/// are rolled - every frame is the game exactly as it stood when that activation started.
/// </summary>
public sealed class ReplayPlayer : IDisposable
{
    private readonly BattleReplay.Opened _replay;
    private readonly Dictionary<int, GameDataStore> _cache = new();

    public SwitchingDataStore Store { get; }
    public BattleReplay.Manifest Manifest => _replay.Manifest;
    public int Count => _replay.Count;
    public string Path => _replay.Path;

    /// <summary>The frame on the table, from 0.</summary>
    public int Index { get; private set; }

    /// <summary>The last frame is shown and the game's ending lines are in the report.</summary>
    public bool AtEnd => Index >= Count - 1;

    /// <summary>The report the screen shows; set by the session.</summary>
    public BattleReport? Report { get; set; }

    /// <summary>Raised after every <see cref="Show"/>.</summary>
    public event Action? Changed;

    private ReplayPlayer(BattleReplay.Opened replay, GameDataStore first)
    {
        _replay = replay;
        _cache[0] = first;
        Store = new SwitchingDataStore(first);
    }

    /// <summary>Opens a replay file on its first frame. Null with a sentence for the player when it can't be watched.</summary>
    public static ReplayPlayer? Open(string path, out string error)
    {
        BattleReplay.Opened? replay = BattleReplay.Open(path, out error);
        if (replay == null) return null;
        try
        {
            GameDataStore first = replay.Frame(0);
            if (GameProgressUtilities.SettingsOf(first) is { } settings) GameWideConstants.ConfigureTable(settings);
            return new ReplayPlayer(replay, first);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Replay] Could not open {path}: {e.GetBaseException().Message}");
            replay.Dispose();
            error = replay.Manifest.Build != AppVersion.BuildNumber
                ? $"That replay was recorded on build {replay.Manifest.Build} and this build can't read it."
                : "That replay couldn't be read.";
            return null;
        }
    }

    /// <summary>The first frame's store - what the lobby the screen reads its settings from is built around.</summary>
    public GameDataStore FirstFrame => _cache[0];

    /// <summary>Shows frame <paramref name="index"/> (clamped). False when that frame can't be read.</summary>
    public bool Show(int index)
    {
        index = Math.Clamp(index, 0, Count - 1);
        GameDataStore store;
        try { store = Load(index); }
        catch (Exception e)
        {
            Console.WriteLine($"[Replay] Frame {index}: {e.GetBaseException().Message}");
            return false;
        }
        Index = index;
        Store.Show(store);
        Report?.LoadReplay(_replay.ReportUpTo(index, includeEnding: AtEnd).Select(l => (l.Kind, l.Text, l.Body)));
        // Keep the frames either side warm, so stepping is quick; drop the rest.
        foreach (int k in _cache.Keys.Where(k => k != 0 && Math.Abs(k - index) > 2).ToList()) _cache.Remove(k);
        Changed?.Invoke();
        return true;
    }

    private GameDataStore Load(int index)
    {
        if (_cache.TryGetValue(index, out GameDataStore? hit)) return hit;
        GameDataStore store = _replay.Frame(index);
        _cache[index] = store;
        return store;
    }

    public bool Next() => !AtEnd && Show(Index + 1);
    public bool Previous() => Index > 0 && Show(Index - 1);

    /// <summary>The first frame of the next / previous round.</summary>
    public bool NextRound()
    {
        int round = Manifest.Frames[Index].Round;
        int i = Manifest.Frames.FindIndex(Index, f => f.Round > round);
        return i >= 0 ? Show(i) : Show(Count - 1);
    }

    public bool PreviousRound()
    {
        int round = Manifest.Frames[Index].Round;
        int start = _replay.FirstOfRound(round);
        if (start >= 0 && start < Index) return Show(start);
        int prev = _replay.FirstOfRound(round - 1);
        return prev >= 0 ? Show(prev) : Show(0);
    }

    /// <summary>"Round 2 - activation 7 of 46 - Kai".</summary>
    public string Where()
    {
        BattleReplay.FrameInfo f = Manifest.Frames[Index];
        return $"Round {f.Round} - activation {Index + 1} of {Count}" + (f.Player.Length > 0 ? $" - {f.Player}" : "");
    }

    public void Dispose()
    {
        _cache.Clear();
        _replay.Dispose();
    }
}
