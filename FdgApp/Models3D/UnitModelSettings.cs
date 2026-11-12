using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// How one 3D model file sits on the table, saved beside it as <c>&lt;file&gt;.model.json</c>.
///
/// <para>
/// This is the "normalization pass" from <c>claude/3d-model-pipeline.md</c> recorded as data instead of baked into
/// the file: models from the Tabletop Simulator mod are scaled correctly against each other but do not share an
/// up axis, a facing or an origin. The front end reads these numbers and does the rest the same way every time:
/// stand the model up (<see cref="UpAxis"/>), turn it (<see cref="YawDegrees"/>), put its feet on the table and its
/// middle over the base centre, then scale it so it is <see cref="HeightMm"/> tall. Nothing per-model lives in
/// drawing code, so the numbers survive any front-end change.
/// </para>
///
/// <para>Written by the Asset Lab (Godot), read by the table when real models are wired in (port step 6).</para>
/// </summary>
public sealed class UnitModelSettings
{
    /// <summary>The file this settings sidecar belongs to is <c>&lt;model&gt;</c> + this.</summary>
    public const string Suffix = ".model.json";

    /// <summary>Millimetres per inch - the tabletop's own conversion (bases are sold in mm, the rules measure inches).</summary>
    public const float MmPerInch = 25.4f;

    /// <summary>Tall the finished model stands, in millimetres, foot to top. 0 = keep the file's own size (1 file unit = 1 inch).</summary>
    public float HeightMm { get; set; }

    /// <summary>Base it is shown on, in millimetres across (round). 0 = no base.</summary>
    public float BaseMm { get; set; } = 25;

    /// <summary>Which way the file thinks is "up". Many TTS / CAD exports are Z-up.</summary>
    public EUpAxis UpAxis { get; set; } = EUpAxis.YUp;

    /// <summary>Turn about the vertical, applied after standing it up, so it faces the same way as every other model.</summary>
    public float YawDegrees { get; set; }

    /// <summary>
    /// Centre the model over its base (true) or keep the file's own origin (false - for a model whose origin was
    /// deliberately placed, e.g. a rider whose mount overhangs the base).
    /// </summary>
    public bool CentreOnBase { get; set; } = true;

    /// <summary>Free text: where the model came from, which unit it is meant for.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Settings sidecar path for a model file.</summary>
    public static string PathFor(string modelFile) => modelFile + Suffix;

    /// <summary>The saved settings for <paramref name="modelFile"/>, or null when none were saved (or the file is unreadable).</summary>
    public static UnitModelSettings? Load(string modelFile)
    {
        string path = PathFor(modelFile);
        if (!File.Exists(path)) return null;
        try { return JsonConvert.DeserializeObject<UnitModelSettings>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public void Save(string modelFile) => File.WriteAllText(PathFor(modelFile), JsonConvert.SerializeObject(this, Formatting.Indented));

    public UnitModelSettings Copy() => (UnitModelSettings)MemberwiseClone();

    /// <summary>The usual round bases, in mm (OPR's own data uses these; 28.5 and 75+ show up on a few big models).</summary>
    public static readonly float[] RoundBasesMm = { 25, 28.5f, 32, 40, 50, 60, 75, 90, 100, 130, 160 };

    /// <summary>A sensible starting height for a model on a base of this size (heroic 28mm scale), before anyone tunes it.</summary>
    public static float GuessHeightMm(float baseMm) => baseMm switch
    {
        <= 0 => 32,
        <= 28.5f => 32,   // infantry: a 28mm-scale person is ~32mm to the top of the head
        <= 32 => 36,
        <= 40 => 45,      // bigger infantry, small monsters
        <= 50 => 55,
        <= 60 => 70,
        _ => baseMm * 1.1f,
    };
}

public enum EUpAxis
{
    /// <summary>Already standing (glTF / Godot / most game exports).</summary>
    YUp,
    /// <summary>Z is up (Blender default for some exporters, many CAD / TTS files): tipped back upright.</summary>
    ZUp,
    /// <summary>-Z is up: the model is face-down.</summary>
    ZDown,
    /// <summary>Upside down.</summary>
    YDown,
    /// <summary>X is up: lying on its side.</summary>
    XUp,
    /// <summary>-X is up: lying on its other side.</summary>
    XDown,
}

/// <summary>Where 3D unit models live: <c>UnitModels/&lt;Faction&gt;/&lt;Unit Name&gt;.glb</c>, mirroring <c>UnitImages</c>.</summary>
public static class UnitModelPaths
{
    /// <summary>Files the Asset Lab can open: 3D models and pictures.</summary>
    public static readonly string[] ModelExtensions = { ".glb", ".gltf", ".obj" };
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    public const string FolderName = "UnitModels";

    /// <summary>
    /// The UnitModels folder, found next to the repo / the game the same way UnitImages is (the parent of
    /// <see cref="AppAssets.Root"/> first - the repo root in a dev build - then the root itself). Created beside
    /// the repo root when there is none yet.
    /// </summary>
    public static string Root
    {
        get
        {
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(AppAssets.Root, "..", FolderName)),
                Path.GetFullPath(Path.Combine(AppAssets.Root, FolderName)),
            };
            foreach (string c in candidates)
                if (Directory.Exists(c)) return c;
            // A dev build (Root = repo/FdgRaylib) makes it at the repo root, beside UnitImages; a shipped build
            // (Root = the game folder) makes it beside the executable.
            string make = Directory.Exists(Path.Combine(AppAssets.Root, "..", "UnitImages")) ? candidates[0] : candidates[1];
            Directory.CreateDirectory(make);
            return make;
        }
    }

    public static bool IsModel(string file) => ModelExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());
    public static bool IsImage(string file) => ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    /// <summary>Every model and picture under the folder, sorted by relative path (pictures that are an OBJ's texture are skipped).</summary>
    public static List<string> Browse(string root)
    {
        if (!Directory.Exists(root)) return new();
        // The downloader's cache (raw parts, no file names) is not something to open.
        var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).StartsWith(ModelDownloader.CacheFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // A picture with the same name as a model beside it (Grunt.obj + Grunt.png) is that model's skin, not an asset of its own.
        var modelStems = all.Where(IsModel).Select(f => Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(f => IsModel(f) || (IsImage(f)
                && !modelStems.Contains(Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f)))
                && !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("textures", StringComparison.OrdinalIgnoreCase))))
            .OrderBy(f => Path.GetRelativePath(root, f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
