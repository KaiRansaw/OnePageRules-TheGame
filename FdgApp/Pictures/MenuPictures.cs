using System.Text.Json;
using FDG;

namespace FdgRaylib.Pictures;

/// <summary>
/// Kai's own pictures for the menus (2026-11-16, "Menu Art &amp; Themes Mockup"): the two main-menu backdrops, the six
/// game tiles, and one picture per army - shared by that army's three games (Orcs is the same picture in Age of
/// Fantasy, Skirmish and Quest). Set in the Asset Lab's picture manager: the game keeps its own copy of each picture in
/// <c>Assets/Pictures/</c> with a focus point and zoom in <c>Assets/Pictures/pictures.json</c>, so moving or deleting the
/// original changes nothing. <b>The folder is personal and stays out of git</b> (Kai, 2026-11-17: his pictures are
/// for his own use, so <c>.gitignore</c> keeps every <c>Assets/Pictures/</c> folder out of the push; friends see the
/// fallbacks - unit pictures on the army colour - unless he hands them the pictures himself).
/// </summary>
public static class MenuPictures
{
    public enum EFamily { GrimdarkFuture, AgeOfFantasy }

    public enum EKind { Backdrop, GameTile, Army }

    /// <summary>One place a picture can go. <see cref="Key"/> is what pictures.json stores.</summary>
    public sealed record Slot(string Key, EKind Kind, EFamily Family, string Label, string? Army = null, EGameSystem? Game = null);

    /// <summary>A set picture: the copied file, and the point the crops centre on (0-1 across / down), and the zoom (1 = just covers).</summary>
    public sealed class Entry
    {
        public string File { get; set; } = "";
        public float FocusX { get; set; } = 0.5f;
        public float FocusY { get; set; } = 0.5f;
        public float Zoom { get; set; } = 1f;
    }

    private static string? _folder;
    /// <summary>Assets/Pictures. Tests point it at a temporary folder (setting it forgets what was read).</summary>
    public static string Folder
    {
        get => _folder ?? Path.Combine(AppAssets.AssetsFolder, "Pictures");
        set { _folder = value; Reload(); }
    }
    private static string IndexPath => Path.Combine(Folder, "pictures.json");

    private static Dictionary<string, Entry>? _entries;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Bumped on every change, so screens holding textures know to reload.</summary>
    public static int Version { get; private set; }

    public static EFamily FamilyOf(EGameSystem system) =>
        system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest ? EFamily.AgeOfFantasy : EFamily.GrimdarkFuture;

    public static string FamilyName(EFamily f) => f == EFamily.AgeOfFantasy ? "Age of Fantasy" : "Grimdark Future";

    private static string FamilyKey(EFamily f) => f == EFamily.AgeOfFantasy ? "aof" : "gf";

    public static Slot Backdrop(EFamily f) => new($"backdrop/{FamilyKey(f)}", EKind.Backdrop, f, $"Main menu - {FamilyName(f)}");

    public static Slot GameTile(EGameSystem game) =>
        new($"game/{GameSystemInfo.Slug(game)}", EKind.GameTile, FamilyOf(game), GameSystemInfo.DisplayName(game), Game: game);

    /// <summary>An army's picture - one per army name per family, used in all three of that family's games.</summary>
    public static Slot Army(EFamily f, string army) => new($"army/{FamilyKey(f)}/{army}", EKind.Army, f, army, Army: army);

    public static Slot Army(EGameSystem system, string army) => Army(FamilyOf(system), army);

    private static Dictionary<string, Entry> Entries()
    {
        lock (Gate)
        {
            if (_entries != null) return _entries;
            try
            {
                _entries = File.Exists(IndexPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(IndexPath)) ?? new()
                    : new();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _entries = new(); }
            return _entries;
        }
    }

    /// <summary>The picture set for a slot, with its file present; null when none (the screen falls back).</summary>
    public static Entry? Get(Slot slot)
    {
        lock (Gate)
            return Entries().TryGetValue(slot.Key, out Entry? e) && File.Exists(PathOf(e)) ? e : null;
    }

    public static string PathOf(Entry e) => Path.Combine(Folder, e.File);

    /// <summary>Copies a picture file in for a slot (replacing any earlier one), focus centred.</summary>
    public static Entry SetFromFile(Slot slot, string sourcePath)
    {
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new InvalidOperationException("Pick a PNG, JPG or WEBP picture.");
        return Store(slot, ext, dest => File.Copy(sourcePath, dest, true));
    }

    /// <summary>Stores picture bytes for a slot (a pasted picture, saved as PNG by the front end).</summary>
    public static Entry SetFromBytes(Slot slot, byte[] png) => Store(slot, ".png", dest => File.WriteAllBytes(dest, png));

    private static Entry Store(Slot slot, string ext, Action<string> write)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            var all = Entries();
            if (all.TryGetValue(slot.Key, out Entry? old)) TryDelete(PathOf(old));
            // A new name each time, so a texture cached from the old file is never mistaken for the new one.
            string file = $"{SafeName(slot.Key)}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            write(Path.Combine(Folder, file));
            var e = new Entry { File = file };
            all[slot.Key] = e;
            Save();
            return e;
        }
    }

    public static void SetFraming(Slot slot, float focusX, float focusY, float zoom)
    {
        lock (Gate)
        {
            if (!Entries().TryGetValue(slot.Key, out Entry? e)) return;
            e.FocusX = Math.Clamp(focusX, 0, 1);
            e.FocusY = Math.Clamp(focusY, 0, 1);
            e.Zoom = Math.Clamp(zoom, 1, 4);
            Save();
        }
    }

    public static void Remove(Slot slot)
    {
        lock (Gate)
        {
            if (!Entries().Remove(slot.Key, out Entry? e)) return;
            TryDelete(PathOf(e));
            Save();
        }
    }

    /// <summary>How many of these slots have a picture.</summary>
    public static int CountSet(IEnumerable<Slot> slots) => slots.Count(s => Get(s) != null);

    private static void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(Entries(), Json));
        Version++;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>"army/aof/Orcs" -> "army-aof-orcs" (letters, digits and dashes only).</summary>
    public static string SafeName(string key)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in key.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    /// <summary>For tests: forget what was read so the next call reads the file again.</summary>
    public static void Reload() { lock (Gate) { _entries = null; Version++; } }
}
