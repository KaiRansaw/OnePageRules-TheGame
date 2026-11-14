using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FdgRaylib.Models3D;

/// <summary>
/// The Age of Fantasy models' catalogue (Kai, 2026-11-13: "set it up like we set up the Grimdark Future and 40K mod"):
/// Stormvault, the Age of Sigmar Tabletop Simulator collection (github.com/AoS-TTS/Stormvault). One save file per AoS
/// faction; each model is a base with the figure as its child part - the same shape as the ForceOrg catalogue, so it is
/// written as a <see cref="ForceOrgCatalog"/> file (<see cref="FileName"/>) and everything that reads that one (the
/// downloader, Match models, Choose models) reads this one too.
///
/// <para><b>Built on the player's computer, never shipped.</b> The models are community uploads (personal use), so the
/// repo carries only which model names stand for which OPR unit (<c>Models3D/aof-unit-models.json</c>). The Asset Lab's
/// Download models window fetches Stormvault from GitHub and builds this file in the UnitModels folder.</para>
///
/// <para><b>Ids.</b> A model's id is worked out from its parts' mesh links (<see cref="StableId"/>), not counted, so it
/// stays the same when Stormvault adds models, and it can never meet a ForceOrg id (those count from 1; these start at
/// <see cref="IdBase"/>). The unit list's build script (<c>tools/unit-models/build_aof_map.py</c>) works out the same
/// numbers. Faction folders are numbered from <see cref="GroupBase"/> and the catalogue's group names are the AoS
/// faction names.</para>
/// </summary>
public static class StormvaultCatalog
{
    public const string FileName = "stormvault-models.json";
    public const string Source = "Stormvault (Age of Sigmar, Tabletop Simulator - github.com/AoS-TTS/Stormvault)";
    public const string ZipUrl = "https://github.com/AoS-TTS/Stormvault/archive/refs/heads/main.zip";
    public const int IdBase = 100_000_000;
    public const int GroupBase = 1001;

    public static string DefaultPath => Path.Combine(UnitModelPaths.Root, FileName);

    /// <summary>A model's id from its parts' mesh links, in part order.</summary>
    public static int StableId(IEnumerable<string> meshLinks)
    {
        byte[] h = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\n", meshLinks.Select(m => m.Trim()))));
        uint v = ((uint)h[0] << 24) | ((uint)h[1] << 16) | ((uint)h[2] << 8) | h[3];
        return IdBase + (int)(v % 900_000_000u);
    }

    /// <summary>The faction a save file holds: its file name ("Ogor Mawtribes.json" -> "Ogor Mawtribes").</summary>
    public static string FactionOf(string file) => Path.GetFileNameWithoutExtension(file);

    /// <summary>
    /// Reads every faction save under <paramref name="folder"/> (an unzipped Stormvault) and writes the catalogue to
    /// <paramref name="output"/>. Returns how many models it holds.
    /// </summary>
    public static int Build(string folder, string output)
    {
        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.StartsWith('.')))
            .OrderBy(f => FactionOf(f), StringComparer.Ordinal).ToList();
        var groups = new Dictionary<int, string>();
        var models = new List<ForceOrgCatalog.Model>();
        var seen = new HashSet<int>();
        int g = GroupBase;
        foreach (string f in files)
        {
            JObject save;
            try { save = JObject.Parse(File.ReadAllText(f)); }
            catch (JsonException) { continue; }
            if (save["ObjectStates"] is not JArray states) continue;
            int group = g++;
            groups[group] = FactionOf(f);
            foreach (JToken o in states) Walk(o, group, models, seen);
        }
        models.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) is var c && c != 0 ? c : a.Id.CompareTo(b.Id));
        var file = new
        {
            source = Source,
            note = "Personal use only - community uploads. Built on this computer by the Asset Lab; do not commit.",
            groups,
            models,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonConvert.SerializeObject(file, Formatting.Indented));
        return models.Count;
    }

    // A model is a Custom_Model with its parts below it (ChildObjects); bags and saved states hold more models.
    private static void Walk(JToken o, int group, List<ForceOrgCatalog.Model> models, HashSet<int> seen)
    {
        if ((string?)o["Name"] == "Custom_Model")
        {
            var parts = new List<ForceOrgCatalog.Part>();
            Collect(o, parts, -1);
            if (parts.Count > 0 && parts.Any(p => p.Texture.Length > 0))
            {
                int id = StableId(parts.Select(p => p.Mesh));
                if (seen.Add(id)) models.Add(new ForceOrgCatalog.Model { Id = id, Name = NameOf(o), Group = group, Parts = parts });
            }
        }
        if (o["ContainedObjects"] is JArray contained) foreach (JToken c in contained) Walk(c, group, models, seen);
        if (o["States"] is JObject st) foreach (var kv in st) if (kv.Value != null) Walk(kv.Value, group, models, seen);
    }

    private static void Collect(JToken o, List<ForceOrgCatalog.Part> parts, int parent)
    {
        int idx = parent;
        string mesh = ((string?)o["CustomMesh"]?["MeshURL"] ?? "").Trim();
        if (mesh.Length > 0)
        {
            JToken? t = o["Transform"];
            float R(string k, float d) => MathF.Round((float?)t?[k] ?? d, 4);
            var c = o["ColorDiffuse"];
            var p = new ForceOrgCatalog.Part
            {
                Mesh = mesh,
                Texture = ((string?)o["CustomMesh"]?["DiffuseURL"] ?? "").Trim(),
                Color = new[] { MathF.Round((float?)c?["r"] ?? 1, 4), MathF.Round((float?)c?["g"] ?? 1, 4), MathF.Round((float?)c?["b"] ?? 1, 4) },
                // The model's own place on the TTS table means nothing here: the root part sits at the origin.
                Pos = parent < 0 ? new float[] { 0, 0, 0 } : new[] { R("posX", 0), R("posY", 0), R("posZ", 0) },
                Rot = parent < 0 ? new float[] { 0, 0, 0 } : new[] { R("rotX", 0), R("rotY", 0), R("rotZ", 0) },
                Scale = new[] { R("scaleX", 1), R("scaleY", 1), R("scaleZ", 1) },
                Parent = parent,
            };
            parts.Add(p);
            idx = parts.Count - 1;
        }
        if (o["ChildObjects"] is JArray kids) foreach (JToken k in kids) Collect(k, parts, idx);
    }

    private static string NameOf(JToken o)
    {
        string name = ((string?)o["Nickname"] ?? "").Trim();
        if (name.Length == 0 && o["ChildObjects"] is JArray kids)
            name = kids.Select(k => ((string?)k["Nickname"] ?? "").Trim()).FirstOrDefault(n => n.Length > 0) ?? "";
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return name.Length == 0 ? "Unnamed" : name;
    }

    /// <summary>
    /// Fetches Stormvault from GitHub and builds the catalogue in the UnitModels folder. Returns the model count, or
    /// throws with a plain reason. <paramref name="progress"/> gets short status lines.
    /// </summary>
    public static async Task<int> DownloadAndBuildAsync(Action<string>? progress, CancellationToken ct, string? output = null)
    {
        output ??= DefaultPath;
        string work = Path.Combine(Path.GetTempPath(), "fdg-stormvault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string zip = Path.Combine(work, "stormvault.zip");
            progress?.Invoke("Downloading Stormvault from GitHub (about 40 MB)...");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OnePageRules-TheGame/1.0 (Asset Lab)");
                using var resp = await http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode) throw new IOException($"GitHub said {(int)resp.StatusCode}.");
                await using var fs = File.Create(zip);
                await resp.Content.CopyToAsync(fs, ct);
            }
            progress?.Invoke("Reading the faction files...");
            int n = BuildFromZip(zip, output);
            progress?.Invoke($"Stormvault catalogue ready: {n:N0} models.");
            return n;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Builds the catalogue from a downloaded Stormvault zip. Only the faction saves are unpacked.</summary>
    public static int BuildFromZip(string zip, string output)
    {
        string unzipped = Path.Combine(Path.GetTempPath(), "fdg-stormvault-unzip-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (ZipArchive a = ZipFile.OpenRead(zip))
                foreach (ZipArchiveEntry e in a.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    string dest = Path.GetFullPath(Path.Combine(unzipped, e.FullName));
                    if (!dest.StartsWith(Path.GetFullPath(unzipped), StringComparison.Ordinal)) continue; // a bad path in the zip
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            return Build(unzipped, output);
        }
        finally
        {
            try { if (Directory.Exists(unzipped)) Directory.Delete(unzipped, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
