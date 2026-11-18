using Newtonsoft.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// Kai's model settings (facing, tilt, up axis, height, base) for every downloaded model, shipped inside the game so a
/// fresh download on another computer stands every model the way Kai set it (Kai, 2026-11-17: "the model rotation and
/// other things that were supposed to be saved as config and pushed to the github wasnt").
///
/// <para>Each model's own settings live beside it as <c>&lt;model&gt;.model.json</c> in UnitModels - which is never
/// pushed (the models are too big and not ours to share). This file, <see cref="RelativePath"/> in Assets, holds a copy
/// of every one of them keyed by the model's path inside UnitModels (<c>Alien Hives/Assault Grunts/Hormagaunt
/// (45).glb</c>), which is the same on every computer because the downloader names files the same way.</para>
///
/// <para><b>Which copy wins.</b> Running from the repo on Kai's PC (<see cref="IsAuthority"/>), the
/// files beside the models are the truth: every save is copied into this file at once and <see cref="Harvest"/> copies
/// all of them at start-up, so the file in the repo is always current and only needs committing. Anywhere else (a
/// friend's download, Kai's laptop), this file wins over the files beside the models - the downloader writes those
/// with a default facing - unless the model was changed by hand on that computer
/// (<see cref="UnitModelSettings.ChangedHere"/>).</para>
/// </summary>
public static class SharedModelSettings
{
    public const string RelativePath = "Models3D/model-settings.json";

    public sealed class FileData
    {
        [JsonProperty("_comment")]
        public string Comment { get; set; } =
            "Kai's model settings from the Asset Lab, one per downloaded model (path inside UnitModels). Written by the game when it runs from the repo - do not edit by hand.";

        /// <summary>The computer these settings come from (Kai's PC). A copy of the repo on another computer (his laptop)
        /// reads them like a download instead of overwriting them with its own fresh, unturned downloads.</summary>
        [JsonProperty("owner")] public string Owner { get; set; } = "";

        /// <summary>The facing every new download gets (<see cref="ModelDownloadDefaults"/>), when Kai set one.</summary>
        [JsonProperty("downloadYawDegrees")] public float? DownloadYawDegrees { get; set; }

        [JsonProperty("models")]
        public SortedDictionary<string, UnitModelSettings> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object Gate = new();
    private static FileData? _data;
    private static string? _loadedFrom;

    public static string ShippedPath => Path.Combine(AppAssets.AssetsFolder, RelativePath);

    /// <summary>
    /// True when the game is running from the repo (Assets is the repo's own FdgRaylib/Assets, with .git beside it): the
    /// settings made here are the ones to ship. False in an unzipped download.
    /// </summary>
    public static bool IsSourceTree
    {
        get
        {
            if (_sourceTreeOverride is bool b) return b;
            if (_sourceTreeFor == AppAssets.Root && _sourceTree is bool known) return known;
            _sourceTreeFor = AppAssets.Root;
            try { return (_sourceTree = Directory.Exists(Path.Combine(AppAssets.Root, "..", ".git")) || File.Exists(Path.Combine(AppAssets.Root, "..", ".git"))).Value; }
            catch (Exception) { return (_sourceTree = false).Value; }
        }
    }

    /// <summary>
    /// Running from the repo on the computer the settings come from (the file's owner, or any computer while the file has
    /// no owner yet): this computer's settings are the ones to ship. False in a download and in a copy of the repo on
    /// another computer.
    /// </summary>
    public static bool IsAuthority
    {
        get
        {
            if (_sourceTreeOverride is bool b) return b;
            if (!IsSourceTree) return false;
            string owner;
            lock (Gate) owner = Data().Owner;
            return owner.Length == 0 || owner.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool? _sourceTreeOverride, _sourceTree;
    private static string? _sourceTreeFor;
    /// <summary>Tests only.</summary>
    public static void OverrideSourceTree(bool? value) => _sourceTreeOverride = value;

    /// <summary>The model's key: its path inside UnitModels with forward slashes, or null for a file outside it.</summary>
    public static string? KeyFor(string modelFile, string? root = null)
    {
        try
        {
            string r = Path.GetFullPath(root ?? UnitModelPaths.Root);
            string f = Path.GetFullPath(modelFile);
            string rel = Path.GetRelativePath(r, f);
            if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
            return rel.Replace('\\', '/');
        }
        catch (Exception) { return null; }
    }

    private static FileData Data(string? path = null)
    {
        path ??= ShippedPath;
        if (_data != null && _loadedFrom == path) return _data;
        _loadedFrom = path;
        _data = new FileData();
        try
        {
            if (File.Exists(path))
            {
                var d = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path));
                if (d != null)
                {
                    _data.DownloadYawDegrees = d.DownloadYawDegrees;
                    foreach (var (k, v) in d.Models) _data.Models[k] = v;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return _data;
    }

    private static void Write(string? path = null)
    {
        path ??= ShippedPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (Data(path).Owner.Length == 0) Data(path).Owner = Environment.MachineName;
            string json = JsonConvert.SerializeObject(Data(path), Formatting.Indented);
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            File.WriteAllText(path, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static Timer? _timer;
    private static bool _hooked;

    /// <summary>A facing review or a download saves hundreds of models in a row: the shipped file is written once, a
    /// moment after the last of them (and when the game closes), not once per model.</summary>
    private static void WriteSoon()
    {
        if (!_hooked)
        {
            _hooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }
        _timer ??= new Timer(_ => Flush());
        _timer.Change(1500, Timeout.Infinite);
    }

    /// <summary>Writes any pending change to the shipped file now.</summary>
    public static void Flush()
    {
        lock (Gate) if (_data != null && _loadedFrom == ShippedPath) Write();
    }

    /// <summary>The shipped settings for a model, or null when the file has none for it.</summary>
    public static UnitModelSettings? For(string modelFile)
    {
        string? key = KeyFor(modelFile);
        if (key == null) return null;
        lock (Gate) return Data().Models.TryGetValue(key, out var s) ? s.Copy() : null;
    }

    /// <summary>The shipped facing for new downloads, if Kai set one.</summary>
    public static float? DownloadYaw()
    {
        lock (Gate) return Data().DownloadYawDegrees;
    }

    /// <summary>Copies one model's settings into the shipped file - only when running from the repo.</summary>
    public static void Record(string modelFile, UnitModelSettings settings)
    {
        if (!IsAuthority) return;
        string? key = KeyFor(modelFile);
        if (key == null) return;
        lock (Gate)
        {
            var copy = settings.Copy();
            copy.ChangedHere = false;
            Data().Models[key] = copy;
            WriteSoon();
        }
    }

    /// <summary>Copies the facing for new downloads into the shipped file - only when running from the repo.</summary>
    public static void RecordDownloadYaw(float yaw)
    {
        if (!IsAuthority) return;
        lock (Gate)
        {
            Data().DownloadYawDegrees = yaw;
            WriteSoon();
        }
    }

    /// <summary>
    /// Copies every model's settings file in UnitModels (and the download facing) into the shipped file, and drops
    /// entries for models that are no longer there. Only when running from the repo; returns how many models it holds.
    /// </summary>
    public static int Harvest(string? root = null, string? shippedPath = null)
    {
        if (!IsAuthority) return -1;
        root ??= UnitModelPaths.Root;
        if (!Directory.Exists(root)) return -1;
        var found = new SortedDictionary<string, UnitModelSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (string side in Directory.EnumerateFiles(root, "*" + UnitModelSettings.Suffix, SearchOption.AllDirectories))
        {
            string model = side[..^UnitModelSettings.Suffix.Length];
            string? key = KeyFor(model, root);
            if (key == null || key.StartsWith(ModelDownloader.CacheFolderName + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(model)) continue;
            UnitModelSettings? s = UnitModelSettings.LoadLocal(model);
            if (s == null) continue;
            s.ChangedHere = false;
            found[key] = s;
        }
        lock (Gate)
        {
            var data = Data(shippedPath);
            data.Models = found;
            float? yaw = File.Exists(ModelDownloadDefaults.PathIn(root)) ? ModelDownloadDefaults.LoadLocal(root)?.YawDegrees : null;
            if (yaw != null) data.DownloadYawDegrees = yaw;
            Write(shippedPath);
            return found.Count;
        }
    }

    /// <summary>Forget what was read (tests).</summary>
    public static void Forget()
    {
        lock (Gate) { _data = null; _loadedFrom = null; }
    }
}
