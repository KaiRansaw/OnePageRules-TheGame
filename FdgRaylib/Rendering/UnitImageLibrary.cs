using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Pictures for units, from a "UnitImages" folder: a file named after the unit ("Heavy Gunners.png") is that
/// unit's picture. Matching ignores case, spaces and punctuation, so "heavy-gunners.png" works too. PNG
/// (best, with a see-through background) or JPG.
///
/// <para>Each faction gets its own sub-folder ("UnitImages/Battle Brothers/Warriors.png"), made
/// automatically the first time an army of that faction is on the table. A unit looks in its faction's
/// folder first, then for a loose picture directly in UnitImages (shared by every faction). A unit whose
/// army has no faction name looks in every folder.</para>
///
/// <para>The folder is looked for next to where the game is started from (the FutureOfDarkGrimness folder
/// when run with "dotnet run") and next to the game's own files. It is checked again every couple of
/// seconds, so a picture dropped in (or replaced) shows up without restarting.</para>
///
/// <para>Must be used on the render thread (it loads GPU textures).</para>
/// </summary>
internal sealed class UnitImageLibrary
{
    public const string FolderName = "UnitImages";
    /// <summary>The sub-folder (in UnitImages and in each faction folder) holding the 2D token portraits.</summary>
    public const string PortraitFolderName = "Portraits";

    /// <summary>Pictures for the deployment cards: UnitImages/&lt;faction&gt;/Deployment/&lt;unit&gt;.png (falls back to
    /// the portrait, then the full picture).</summary>
    public const string DeploymentFolderName = "Deployment";

    // null: the 3D standee pictures (files in UnitImages and its faction folders). "Portraits": the 2D
    // token pictures (files in UnitImages/Portraits and each faction's Portraits folder).
    private readonly string? _subfolder;

    public UnitImageLibrary(string? subfolder = null) => _subfolder = subfolder;
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
    private const double RescanSeconds = 2.0;

    // (normalised folder name - "" for loose files, normalised unit name) -> (file, last write time).
    private Dictionary<(string Folder, string Unit), (string Path, DateTime Stamp)> _files = new();
    // Loaded pictures by file path (Texture null = the file could not be loaded).
    private readonly Dictionary<string, (DateTime Stamp, Texture2D? Texture)> _loaded = new();
    private static readonly HashSet<string> MadeFolders = new(StringComparer.OrdinalIgnoreCase);
    private double _lastScan = double.NegativeInfinity;
    private static bool _folderMade;

    /// <summary>The picture for this unit of this faction, or null when there is none.</summary>
    public Texture2D? Get(string unitName, string faction)
    {
        EnsureFactionFolder(faction);
        if (_folderMade) { _folderMade = false; _lastScan = double.NegativeInfinity; }
        RescanIfDue();
        string unit = Normalise(unitName);
        if (unit.Length == 0) return null;
        string folder = Normalise(faction);

        (string Path, DateTime Stamp)? file = null;
        if (folder.Length > 0 && _files.TryGetValue((folder, unit), out var own)) file = own;
        else if (_files.TryGetValue(("", unit), out var loose)) file = loose;
        else if (folder.Length == 0)
            foreach (var entry in _files)
                if (entry.Key.Unit == unit) { file = entry.Value; break; }
        if (file is not { } f) return null;

        if (_loaded.TryGetValue(f.Path, out var have) && have.Stamp == f.Stamp) return have.Texture;
        if (have.Texture is Texture2D stale) Raylib.UnloadTexture(stale);
        var (path, stamp) = f;
        Texture2D? texture = Load(path);
        _loaded[path] = (stamp, texture);
        return texture;
    }

    private static Texture2D? Load(string path)
    {
        Texture2D? texture = null;
        try
        {
            Image image = Raylib.LoadImage(path);
            unsafe
            {
                if (image.Data != null && image.Width > 0 && image.Height > 0)
                {
                    // Big VTT art (1000px+) is far more than a 2" standee needs; shrink it to save memory.
                    const int maxSide = 768;
                    if (image.Width > maxSide || image.Height > maxSide)
                    {
                        float s = maxSide / (float)Math.Max(image.Width, image.Height);
                        Raylib.ImageResize(ref image, Math.Max(1, (int)(image.Width * s)), Math.Max(1, (int)(image.Height * s)));
                    }
                    Texture2D t = Raylib.LoadTextureFromImage(image);
                    Raylib.GenTextureMipmaps(ref t);
                    Raylib.SetTextureFilter(t, TextureFilter.Trilinear);
                    texture = t;
                }
            }
            Raylib.UnloadImage(image);
        }
        catch
        {
            texture = null;
        }
        return texture;
    }

    /// <summary>Makes UnitImages/&lt;faction&gt; (once per faction) so there is an obvious place to drop
    /// that faction's pictures.</summary>
    public static void EnsureFactionFolder(string faction)
    {
        if (string.IsNullOrWhiteSpace(faction) || !MadeFolders.Add(faction)) return;
        try
        {
            string safe = string.Concat(faction.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string root = Folders().First();
            string? existing = Directory.Exists(root)
                ? Directory.EnumerateDirectories(root).FirstOrDefault(d => Normalise(Path.GetFileName(d)) == Normalise(faction))
                : null;
            string folder = existing ?? Path.Combine(root, safe);
            Directory.CreateDirectory(Path.Combine(folder, PortraitFolderName));   // makes the faction folder too
            Directory.CreateDirectory(Path.Combine(root, PortraitFolderName));
            Directory.CreateDirectory(Path.Combine(folder, DeploymentFolderName));
            Directory.CreateDirectory(Path.Combine(root, DeploymentFolderName));
            _folderMade = true;
        }
        catch
        {
            // Read-only location: the folder can still be made by hand.
        }
    }

    private void RescanIfDue()
    {
        double now = Raylib.GetTime();
        if (now - _lastScan < RescanSeconds) return;
        _lastScan = now;

        var found = new Dictionary<(string, string), (string, DateTime)>();
        foreach (string root in Folders())
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                AddFiles(found, Within(root), "");
                foreach (string sub in Directory.EnumerateDirectories(root))
                {
                    if (string.Equals(Path.GetFileName(sub), PortraitFolderName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddFiles(found, Within(sub), Normalise(Path.GetFileName(sub)));
                }
            }
            catch
            {
                // Unreadable folder: just no pictures from it.
            }
        }
        _files = found;
    }

    private string Within(string folder) => _subfolder == null ? folder : Path.Combine(folder, _subfolder);

    private static void AddFiles(Dictionary<(string, string), (string, DateTime)> found, string folder, string folderKey)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                string unit = Normalise(Path.GetFileNameWithoutExtension(path));
                if (unit.Length == 0 || found.ContainsKey((folderKey, unit))) continue;   // first folder wins
                found[(folderKey, unit)] = (path, File.GetLastWriteTimeUtc(path));
            }
        }
        catch
        {
            // Unreadable folder: skip it.
        }
    }

    private static IEnumerable<string> Folders()
    {
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), FolderName);
        string app = Path.Combine(AppContext.BaseDirectory, FolderName);
        yield return cwd;
        if (!string.Equals(Path.GetFullPath(cwd), Path.GetFullPath(app), StringComparison.OrdinalIgnoreCase))
            yield return app;
    }

    private static string Normalise(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public void Unload()
    {
        foreach (var entry in _loaded.Values)
            if (entry.Texture is Texture2D t) Raylib.UnloadTexture(t);
        _loaded.Clear();
    }
}
