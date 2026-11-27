namespace FdgRaylib.Audio;

/// <summary>A file that answers a sound slot: one of Kai's own (an absolute path on disk) or one shipped with the game
/// (a path under the Godot project without its extension, e.g. <c>sounds/cues/victory</c>).</summary>
public readonly record struct SoundFile(string Path, bool BuiltIn);

/// <summary>
/// Finds the files behind each <see cref="SoundSlot"/> (2026-11-27). Kai's own audio folder (<c>Assets/Audio</c>, beside
/// the books and pictures) is read first, so dropping a file in replaces or fills a slot with no code; then the game's
/// built-in files; then the slot's fallback. Pure file bookkeeping - the Godot build loads and plays what this returns.
/// </summary>
public static class SoundLibrary
{
    /// <summary>File types the Godot build can load from outside the project at run time.</summary>
    public static readonly string[] Extensions = { ".ogg", ".mp3", ".wav" };

    private static string? _folder;
    private static Dictionary<string, List<string>>? _user;
    private static readonly Dictionary<string, string> LastPick = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary><c>Assets/Audio</c>. Tests point it at a temporary folder (setting it rescans).</summary>
    public static string Folder
    {
        get => _folder ?? Path.Combine(AppAssets.AssetsFolder, "Audio");
        set { _folder = value; Rescan(); }
    }

    /// <summary>Bumped on every rescan, so the player knows to look again.</summary>
    public static int Version { get; private set; }

    /// <summary>Forget what was read; the next lookup reads the folder again.</summary>
    public static void Rescan()
    {
        lock (Gate) { _user = null; Version++; }
    }

    /// <summary>Makes the folder and (re)writes its README when the slot list changed. Never touches Kai's files.</summary>
    public static void EnsureFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string readme = Path.Combine(Folder, "README.txt");
            string text = SoundSlots.ReadmeText();
            if (!File.Exists(readme) || File.ReadAllText(readme) != text) File.WriteAllText(readme, text);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Kai's own files for one slot (no fallback): <c>&lt;id&gt;.ext</c> plus everything inside <c>&lt;id&gt;/</c>.</summary>
    public static IReadOnlyList<string> UserFiles(string id)
    {
        lock (Gate)
        {
            _user ??= Scan();
            return _user.TryGetValue(Normalise(id), out List<string>? f) ? f : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }

    /// <summary>The files a slot plays and the slot they came from: its own (Kai's first, else built-in), or the first
    /// slot down its fallback chain that has any. Empty when the whole chain is silent.</summary>
    public static (string Slot, IReadOnlyList<SoundFile> Files) Resolve(string id)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? at = id;
        while (at != null && seen.Add(at))
        {
            IReadOnlyList<string> mine = UserFiles(at);
            if (mine.Count > 0) return (at, mine.Select(p => new SoundFile(p, false)).ToList());
            SoundSlot? slot = SoundSlots.Find(at);
            if (slot is { BuiltIn.Count: > 0 }) return (at, slot.BuiltIn.Select(p => new SoundFile(p, true)).ToList());
            at = slot?.Fallback;
        }
        return (id, Array.Empty<SoundFile>());
    }

    /// <summary>True when the slot (or its fallback) has anything to play.</summary>
    public static bool HasSound(string id) => Resolve(id).Files.Count > 0;

    /// <summary>One file for a slot, picked at random but never the one picked last time when there is a choice.
    /// Null when the slot is silent.</summary>
    public static SoundFile? Pick(string id, Random? rng = null)
    {
        (string slot, IReadOnlyList<SoundFile> files) = Resolve(id);
        if (files.Count == 0) return null;
        rng ??= Random.Shared;
        lock (Gate)
        {
            LastPick.TryGetValue(slot, out string? last);
            var choices = files.Count > 1 ? files.Where(f => f.Path != last).ToList() : files.ToList();
            SoundFile f = choices[rng.Next(choices.Count)];
            LastPick[slot] = f.Path;
            return f;
        }
    }

    /// <summary>A shuffled playlist for a music slot (the first track never repeats the last one heard).</summary>
    public static List<SoundFile> Playlist(string id, Random? rng = null)
    {
        (string _, IReadOnlyList<SoundFile> files) = Resolve(id);
        rng ??= Random.Shared;
        return files.OrderBy(_ => rng.Next()).ToList();
    }

    /// <summary>What a slot will play, in words, for Settings -> Sound slots.</summary>
    public static string Status(SoundSlot slot)
    {
        int mine = UserFiles(slot.Id).Count;
        if (mine > 0) return mine == 1 ? "Your file" : $"Your files ({mine})";
        if (slot.BuiltIn.Count > 0) return "Built-in sound";
        (string from, IReadOnlyList<SoundFile> files) = Resolve(slot.Id);
        if (files.Count > 0) return $"Uses {from}";
        return slot.Channel == ESoundChannel.Menu ? "Game's own sound" : "Empty (silent)";
    }

    private static string Normalise(string id) => id.Replace('\\', '/').Trim('/').ToLowerInvariant();

    private static Dictionary<string, List<string>> Scan()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string root = Folder;
        if (!Directory.Exists(root)) return found;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            string noExt = rel[..^Path.GetExtension(rel).Length];
            // <id>.ogg answers <id>; <id>/<anything>.ogg answers <id> too (a folder of variants or a playlist).
            Add(found, Normalise(noExt), file);
            string? dir = Path.GetDirectoryName(rel)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir)) Add(found, Normalise(dir), file);
        }
        foreach (List<string> l in found.Values) l.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static void Add(Dictionary<string, List<string>> d, string key, string file)
    {
        if (!d.TryGetValue(key, out List<string>? l)) d[key] = l = new List<string>();
        if (!l.Contains(file)) l.Add(file);
    }
}
