using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FDG;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Game;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// Battle replays (homebrew, Kai 2026-11-27). A replay is the game's own activation-start save points (the ones
/// Take Back keeps - <see cref="FDG.TakeBackTools.TakeBackHistory.Captured"/>), each one the WHOLE game as it stood,
/// plus the battle report's lines up to that moment. Watching one shows those save points in turn: nothing is played
/// again, no dice are rolled again, so it is always exactly the game that happened.
/// <para>
/// On disk a replay is a zip (<c>.fdgreplay</c>) in the <see cref="DirectoryName"/> folder beside the saves: a
/// <c>replay.json</c> manifest and one <c>frames/NNNN.fdgsave</c> per save point (zip-compressed - a save point is
/// mostly repeated JSON and is gzipped, so a whole game is a few MB). The newest <see cref="KeepNewest"/> are kept.
/// </para>
/// </summary>
public static class BattleReplay
{
    public const string Extension = ".fdgreplay";
    public const string DirectoryName = "Replays";
    public const int KeepNewest = 50;

    /// <summary>One plain-text battle report line, as the replay keeps it.</summary>
    public sealed record ReportLine(string Kind, string Text, string Body);

    /// <summary>One save point: the round, whose activation it starts, and the report lines new since the last one.</summary>
    public sealed record FrameInfo(int Round, string Player, List<ReportLine> Report);

    /// <summary>The replay's manifest: what game it was and how it ended, and every frame's details.</summary>
    public sealed class Manifest
    {
        public string Title { get; set; } = "";
        public string Game { get; set; } = "";
        public List<string> Players { get; set; } = new();
        public string Result { get; set; } = "";
        public DateTime PlayedUtc { get; set; }
        public string Build { get; set; } = AppVersion.BuildNumber;
        public List<FrameInfo> Frames { get; set; } = new();
        /// <summary>The report lines after the last frame (the end of the game).</summary>
        public List<ReportLine> Ending { get; set; } = new();
    }

    /// <summary>The replays folder (created if missing), beside the saved games.</summary>
    public static string FolderPath
    {
        get
        {
            string path = Path.Combine(Path.GetDirectoryName(SavePaths.FolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? AppAssets.Root, DirectoryName);
            try { Directory.CreateDirectory(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return path;
        }
    }

    /// <summary>Every replay file, newest first.</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            return Directory.EnumerateFiles(FolderPath, "*" + Extension)
                .OrderByDescending(SavePaths.SafeWriteTime).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    // ================================================================== recording

    /// <summary>
    /// Records one game: subscribes to the save points as the game makes them, keeps them compressed in memory, and
    /// writes the replay file when <see cref="Finish"/> is called (game over, or paused). Host only - only the host
    /// has the game's save points.
    /// </summary>
    public sealed class Recorder : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<(byte[] Frame, FrameInfo Info)> _frames = new();
        private readonly BattleReport _report;
        private readonly Func<PlayerID, string> _playerName;
        private int _reportCursor;
        private bool _done;

        // Only one game records at a time: a new game's recorder retires the last one (a game left unfinished
        // never wrote its file, and must not pick up the next game's save points).
        private static Recorder? _live;

        public Recorder(BattleReport report, Func<PlayerID, string> playerName)
        {
            _report = report;
            _playerName = playerName;
            Interlocked.Exchange(ref _live, this)?.Dispose();
            FDG.TakeBackTools.TakeBackHistory.Shared.Captured += OnCaptured;
        }

        public int FrameCount { get { lock (_gate) return _frames.Count; } }

        private void OnCaptured(FDG.TakeBackTools.TakeBackHistory.Entry entry)
        {
            byte[] packed = Pack(entry.Json);
            lock (_gate)
            {
                if (_done) return;
                _frames.Add((packed, new FrameInfo(entry.Round, _playerName(entry.Player), NewReportLines())));
            }
        }

        // Caller holds _gate.
        private List<ReportLine> NewReportLines()
        {
            var lines = new List<ReportLine>();
            foreach (BattleReport.Line line in _report.Snapshot().Where(l => l.Id > _reportCursor))
            {
                lines.Add(new ReportLine(line.Kind.ToString(), string.Concat(line.Title.Select(p => p.Text)), line.Body));
                _reportCursor = Math.Max(_reportCursor, line.Id);
            }
            return lines;
        }

        /// <summary>Writes the replay file and stops recording. Returns its path, or null (nothing recorded / write failed).</summary>
        public string? Finish(string game, IEnumerable<string> players, string result)
        {
            List<(byte[] Frame, FrameInfo Info)> frames;
            List<ReportLine> ending;
            lock (_gate)
            {
                if (_done) return null;
                _done = true;
                frames = _frames.ToList();
                ending = NewReportLines();
            }
            FDG.TakeBackTools.TakeBackHistory.Shared.Captured -= OnCaptured;
            if (frames.Count == 0) return null;

            var playerList = players.ToList();
            var manifest = new Manifest
            {
                Game = game,
                Players = playerList,
                Result = result,
                PlayedUtc = DateTime.UtcNow,
                Title = $"{game} - {string.Join(" vs ", playerList)}",
                Frames = frames.Select(f => f.Info).ToList(),
                Ending = ending,
            };
            try
            {
                string safe = string.Concat($"{DateTime.Now:yyyy-MM-dd HH-mm} {manifest.Title}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
                string path = Path.Combine(FolderPath, safe + Extension);
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(FolderPath, $"{safe} ({n}){Extension}");
                Write(path, manifest, frames.Select(f => f.Frame).ToList());
                Prune();
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[Replay] Could not write the replay: {e.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            lock (_gate) _done = true;
            FDG.TakeBackTools.TakeBackHistory.Shared.Captured -= OnCaptured;
        }
    }

    private static void Prune()
    {
        foreach (string old in List().Skip(KeepNewest))
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ================================================================== file

    /// <summary>A save point, gzipped - kept that way in memory while the game runs and written as-is.</summary>
    private static byte[] Pack(string json)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest)) gz.Write(Encoding.UTF8.GetBytes(json));
        return ms.ToArray();
    }

    public static void Write(string path, Manifest manifest, IReadOnlyList<byte[]> frames)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var m = zip.CreateEntry("replay.json", CompressionLevel.Optimal);
        using (var w = new StreamWriter(m.Open())) w.Write(JsonSerializer.Serialize(manifest));
        for (int i = 0; i < frames.Count; i++)
        {
            // Already gzipped (Pack), so stored as it is.
            var e = zip.CreateEntry($"frames/{i:D4}{GameSaveFile.EXTENSION_WITH_PERIOD}.gz", CompressionLevel.NoCompression);
            using Stream s = e.Open();
            s.Write(frames[i]);
        }
    }

    /// <summary>An open replay: its manifest, and its frames read on demand.</summary>
    public sealed class Opened : IDisposable
    {
        private readonly ZipArchive _zip;
        public Manifest Manifest { get; }
        public string Path { get; }
        public int Count => Manifest.Frames.Count;

        internal Opened(string path, ZipArchive zip, Manifest manifest) { Path = path; _zip = zip; Manifest = manifest; }

        /// <summary>Frame <paramref name="index"/> as a save string.</summary>
        public string FrameJson(int index)
        {
            var e = _zip.GetEntry($"frames/{index:D4}{GameSaveFile.EXTENSION_WITH_PERIOD}.gz")
                    ?? throw new InvalidDataException($"The replay has no frame {index}.");
            lock (_zip)
            {
                using var gz = new GZipStream(e.Open(), CompressionMode.Decompress);
                using var r = new StreamReader(gz, Encoding.UTF8);
                return r.ReadToEnd();
            }
        }

        /// <summary>Frame <paramref name="index"/> loaded as a game store.</summary>
        public GameDataStore Frame(int index) => GameSaveSerializer.Load(FrameJson(index));

        /// <summary>Every report line up to and including frame <paramref name="index"/> (and the ending, for the last).</summary>
        public IEnumerable<ReportLine> ReportUpTo(int index, bool includeEnding)
        {
            for (int i = 0; i <= index && i < Count; i++)
                foreach (ReportLine l in Manifest.Frames[i].Report) yield return l;
            if (includeEnding) foreach (ReportLine l in Manifest.Ending) yield return l;
        }

        /// <summary>The first frame of round <paramref name="round"/>, or -1.</summary>
        public int FirstOfRound(int round) => Manifest.Frames.FindIndex(f => f.Round == round);

        public void Dispose() => _zip.Dispose();
    }

    /// <summary>Opens a replay file. Null with a sentence for the player when it can't be read.</summary>
    public static Opened? Open(string path, out string error)
    {
        error = "";
        try
        {
            var zip = ZipFile.OpenRead(path);
            var m = zip.GetEntry("replay.json");
            if (m == null) { zip.Dispose(); error = "That isn't a replay file."; return null; }
            Manifest? manifest;
            using (var r = new StreamReader(m.Open())) manifest = JsonSerializer.Deserialize<Manifest>(r.ReadToEnd());
            if (manifest == null || manifest.Frames.Count == 0) { zip.Dispose(); error = "That replay is empty."; return null; }
            return new Opened(path, zip, manifest);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            error = "That replay couldn't be read.";
            return null;
        }
    }

    /// <summary>Just the manifest (for the Load Game list), or null.</summary>
    public static Manifest? Peek(string path)
    {
        using Opened? o = Open(path, out _);
        return o?.Manifest;
    }
}
