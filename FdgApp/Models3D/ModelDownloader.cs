using System.Security.Cryptography;
using System.Text;

namespace FdgRaylib.Models3D;

/// <summary>
/// Fetches the parts of catalogue models - meshes and skins - into <c>UnitModels/_cache</c>, one file per link
/// (named by a hash of it). Many models share parts (bases above all), so each link is fetched once, ever, and a
/// stopped or failed run picks up where it left off. No drawing here: the Godot build puts the parts together.
/// </summary>
public sealed class ModelDownloader : IDisposable
{
    public const string CacheFolderName = "_cache";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(4);

    public string CacheFolder { get; }

    public ModelDownloader(string? cacheFolder = null)
    {
        CacheFolder = cacheFolder ?? Path.Combine(UnitModelPaths.Root, CacheFolderName);
        Directory.CreateDirectory(CacheFolder);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OnePageRules-TheGame/1.0 (Asset Lab)");
    }

    /// <summary>The cache file for a link (it may not exist yet).</summary>
    /// <summary>
    /// The address to actually fetch a link from. Steam retired its old Workshop file server: links saved as
    /// <c>http://cloud-3.steamusercontent.com/ugc/...</c> (every Stormvault model, and older mods generally) now answer
    /// 403, while the same files are served at <c>https://steamusercontent-a.akamaihd.net/ugc/...</c> - the address the
    /// ForceOrg catalogue already uses (Kai, 2026-11-14: every Eternal Wardens download failed "Mesh: server said 403").
    /// Only the fetch is redirected; the catalogue keeps its links, so model ids and the download cache don't change.
    /// </summary>
    public static string FetchAddress(string url)
    {
        string u = url.Trim();
        foreach (string old in OldSteamHosts)
            if (u.StartsWith(old, StringComparison.OrdinalIgnoreCase))
                return "https://steamusercontent-a.akamaihd.net/ugc/" + u[old.Length..];
        return u;
    }

    private static readonly string[] OldSteamHosts =
    {
        "http://cloud-3.steamusercontent.com/ugc/", "https://cloud-3.steamusercontent.com/ugc/",
        "http://cloud-2.steamusercontent.com/ugc/", "https://cloud-2.steamusercontent.com/ugc/",
        "http://cloud-1.steamusercontent.com/ugc/", "https://cloud-1.steamusercontent.com/ugc/",
        "http://steamusercontent-a.akamaihd.net/ugc/",
    };

    public string CachePathFor(string url)
    {
        byte[] h = SHA1.HashData(Encoding.UTF8.GetBytes(url.Trim()));
        return Path.Combine(CacheFolder, Convert.ToHexString(h).ToLowerInvariant());
    }

    /// <summary>
    /// The local file for a link, fetching it first if it isn't cached. <c>file://</c> links and plain paths are copied
    /// (tests use them). Two tries; null when both fail, with the reason in <paramref name="error"/>.
    /// </summary>
    public async Task<(string? Path, string? Error)> FetchAsync(string url, CancellationToken ct)
    {
        string dest = CachePathFor(url);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0) return (dest, null);
        await _gate.WaitAsync(ct);
        try
        {
            string? last = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    string tmp = dest + ".part";
                    if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || File.Exists(url))
                    {
                        string src = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;
                        File.Copy(src, tmp, true);
                    }
                    else
                    {
                        using var resp = await _http.GetAsync(FetchAddress(url), HttpCompletionOption.ResponseHeadersRead, ct);
                        if (!resp.IsSuccessStatusCode) { last = $"server said {(int)resp.StatusCode}"; continue; }
                        await using (var fs = File.Create(tmp))
                            await resp.Content.CopyToAsync(fs, ct);
                    }
                    if (new FileInfo(tmp).Length == 0) { File.Delete(tmp); last = "empty file"; continue; }
                    File.Move(tmp, dest, true);
                    return (dest, null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { last = e.Message; }
            }
            return (null, last);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Every mesh and skin of one model, fetched side by side. Returns link -> local file for those that arrived,
    /// and the links that didn't with why.
    /// </summary>
    public async Task<(Dictionary<string, string> Files, List<string> Failed)> FetchModelAsync(ForceOrgCatalog.Model model, CancellationToken ct)
    {
        var links = model.Parts.SelectMany(p => new[] { p.Mesh, p.Texture }).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
        var tasks = links.Select(async u => (u, r: await FetchAsync(u, ct))).ToList();
        var files = new Dictionary<string, string>();
        var failed = new List<string>();
        foreach (var (u, r) in await Task.WhenAll(tasks))
        {
            if (r.Path != null) files[u] = r.Path;
            else failed.Add($"{(model.Parts.Any(p => p.Mesh == u) ? "mesh" : "skin")}: {r.Error}");
        }
        return (files, failed);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
