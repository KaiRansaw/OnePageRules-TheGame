using Newtonsoft.Json.Linq;

namespace FdgRaylib.Models3D;

/// <summary>
/// The official OPR unit pictures from Army Forge (Kai, 2026-11-14: "start fresh and download the official OPR model
/// pictures" - he didn't like portraits made from the 3D models). The same fetch as tools/download-unit-images.ps1,
/// built into the game so the Asset Lab can run it:
/// <list type="bullet">
/// <item><c>UnitImages/&lt;army&gt;/Portraits/&lt;unit&gt;.png</c> - the round token portrait (Army Forge's avatar picture)</item>
/// <item><c>UnitImages/&lt;army&gt;/&lt;unit&gt;.png</c> - the standee picture (the unit's first picture), optional</item>
/// </list>
/// A picture already there is never replaced (delete it to fetch it again); every downloaded file is listed in a
/// .gitignore beside it - OPR's artwork stays on the player's computer. Pictures are shrunk on the way in (the front end
/// supplies the shrinking: this layer has no image library).
/// </summary>
public static class OfficialPictures
{
    public const string Api = "https://army-forge.onepagerules.com/api";
    public const string Cdn = "https://army-forge.opr-cdn.com/";
    public const int PortraitMax = 256;
    public const int StandeeMax = 768;

    /// <summary>A game in the menu: one or more Army Forge systems (id, the slug its book index is keyed by). The Quest
    /// games are two ids each - heroes and enemies have separate books (CLAUDE.md).</summary>
    public sealed record GameChoice(string Name, (int Id, string Slug)[] Systems);

    public static readonly IReadOnlyList<GameChoice> Games = new GameChoice[]
    {
        new("Grimdark Future", new[] { (2, "grimdark-future") }),
        new("GF: Firefight", new[] { (3, "grimdark-future-firefight") }),
        new("Age of Fantasy", new[] { (4, "age-of-fantasy") }),
        new("AoF: Skirmish", new[] { (5, "age-of-fantasy-skirmish") }),
        new("GF: Star Quest", new[] { (9, "grimdark-future-star-quest"), (10, "grimdark-future-star-quest") }),
        new("AoF: Quest", new[] { (7, "age-of-fantasy-quest"), (8, "age-of-fantasy-quest") }),
    };

    /// <summary>The UnitImages folder the game reads (next to the repo or the game, like UnitModels).</summary>
    public static string Root
    {
        get
        {
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(AppAssets.Root, "..", "UnitImages")),
                Path.GetFullPath(Path.Combine(AppAssets.Root, "UnitImages")),
            };
            foreach (string c in candidates)
                if (Directory.Exists(c)) return c;
            string make = Directory.Exists(Path.Combine(AppAssets.Root, "..", "UnitModels")) ? candidates[0] : candidates[1];
            Directory.CreateDirectory(make);
            return make;
        }
    }

    public sealed class Tally
    {
        public int Saved, Skipped, Missing, Failed;
        public readonly List<string> Failures = new();
        public override string ToString() =>
            $"{Saved} new pictures, {Skipped} already there, {Missing} units have no picture on Army Forge" + (Failed > 0 ? $", {Failed} failed" : "");
    }

    /// <summary>
    /// Fetches every picture of the chosen games' official books. <paramref name="shrink"/> takes the downloaded bytes and
    /// a largest side and returns PNG bytes (null when it can't read the picture). <paramref name="armyFilter"/> keeps only
    /// armies whose name contains it.
    /// </summary>
    public static async Task<Tally> DownloadAsync(IEnumerable<GameChoice> games, string armyFilter, bool standees,
        Func<byte[], int, byte[]?> shrink, Action<string>? progress, CancellationToken ct, string? root = null)
    {
        root ??= Root;
        var tally = new Tally();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OnePageRules-TheGame/1.0 (Asset Lab)");

        foreach (GameChoice game in games)
            foreach (var (id, slug) in game.Systems)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke($"== {game.Name} (Army Forge system {id})");
                JArray index;
                try { index = JArray.Parse(await GetString(http, $"{Api}/army-books?filters=official&gameSystemSlug={slug}", ct)); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    progress?.Invoke($"  Could not reach Army Forge: {e.Message}");
                    tally.Failed++;
                    continue;
                }
                var books = index.OfType<JObject>()
                    .Where(b => armyFilter.Length == 0 || ((string?)b["name"] ?? "").Contains(armyFilter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => (string?)b["name"]).ToList();
                if (books.Count == 0) progress?.Invoke("  No army matches that name here.");

                foreach (JObject b in books)
                {
                    ct.ThrowIfCancellationRequested();
                    JObject book;
                    try { book = JObject.Parse(await GetString(http, $"{Api}/army-books/{b["uid"]}?gameSystem={id}", ct)); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        tally.Failed++;
                        tally.Failures.Add($"{b["name"]}: the book did not load");
                        continue;
                    }
                    string bookName = (string?)book["name"] ?? (string?)b["name"] ?? "Army";
                    string army = ArmyFolder(root, bookName);
                    string portraits = Path.Combine(army, "Portraits");
                    Directory.CreateDirectory(portraits);
                    int got = 0;
                    foreach (JObject u in (book["units"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        ct.ThrowIfCancellationRequested();
                        string unit = (string?)u["name"] ?? "";
                        string file = SafeName(unit) + ".png";
                        if (file == ".png") continue;
                        JToken? product = u["product"];
                        string avatar = (string?)product?["avatarImageUrl"] ?? "";
                        if (avatar.Length == 0) tally.Missing++;
                        else if (HasPicture(portraits, unit)) tally.Skipped++;
                        else if (await Save(http, FullUrl(avatar), Path.Combine(portraits, file), PortraitMax, shrink, tally, $"{bookName} / {unit} portrait", ct)) got++;

                        if (!standees || product?["imageUrls"] is not JArray images || images.Count == 0) continue;
                        if (HasPicture(army, unit)) tally.Skipped++;
                        else if (await Save(http, FullUrl((string?)images[0] ?? ""), Path.Combine(army, file), StandeeMax, shrink, tally, $"{bookName} / {unit} standee", ct)) got++;
                    }
                    progress?.Invoke($"  {bookName}: {got} new");
                }
            }
        return tally;
    }

    private static async Task<bool> Save(HttpClient http, string url, string target, int maxSide, Func<byte[], int, byte[]?> shrink,
        Tally tally, string what, CancellationToken ct)
    {
        try
        {
            byte[] raw = await http.GetByteArrayAsync(url, ct);
            byte[] png = shrink(raw, maxSide) ?? throw new IOException("the picture could not be read");
            await File.WriteAllBytesAsync(target, png, ct);
            AddGitIgnore(Path.GetDirectoryName(target)!, Path.GetFileName(target));
            tally.Saved++;
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            tally.Failed++;
            tally.Failures.Add($"{what}: {e.Message}");
            return false;
        }
    }

    private static async Task<string> GetString(HttpClient http, string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return await http.GetStringAsync(url, ct); }
            catch (HttpRequestException) when (attempt < 3) { await Task.Delay(2000 * attempt, ct); }
        }
    }

    /// <summary>Army Forge gives a picture either as a path on its CDN or, for newer units, as a full web address (their
    /// S3 bucket). Only the first kind gets the CDN in front - gluing it onto a full address made a link that 404s (the
    /// "failed ... 404 (Not Found)" lines a friend hit on 2026-11-17).</summary>
    public static string FullUrl(string url)
    {
        url = url.Trim();
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith("//")) return "https:" + url;
        return Cdn + url.TrimStart('/');
    }

    public static string Normalise(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static string SafeName(string name)
    {
        // Windows' list on every computer, so a folder made here still opens there.
        const string bad = "<>:\"/\\|?*";
        return new string(name.Trim().Where(c => c >= ' ' && !bad.Contains(c)).ToArray()).Trim().TrimEnd('.').Trim();
    }

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

    /// <summary>A picture of this unit already in the folder, under any spelling or format.</summary>
    public static bool HasPicture(string folder, string unit)
    {
        if (!Directory.Exists(folder)) return false;
        string want = Normalise(unit);
        return Directory.EnumerateFiles(folder).Any(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())
            && Normalise(Path.GetFileNameWithoutExtension(f)) == want);
    }

    /// <summary>The army's folder the way the game finds it (an existing folder whose name normalises the same), else a new one.</summary>
    public static string ArmyFolder(string root, string bookName)
    {
        Directory.CreateDirectory(root);
        string want = Normalise(bookName);
        string? existing = Directory.EnumerateDirectories(root).FirstOrDefault(d => Normalise(Path.GetFileName(d)) == want);
        if (existing != null) return existing;
        string path = Path.Combine(root, SafeName(bookName));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Lists a downloaded file in its folder's .gitignore, so OPR's artwork is never committed.</summary>
    public static void AddGitIgnore(string folder, string fileName)
    {
        string gi = Path.Combine(folder, ".gitignore");
        string line = "/" + System.Text.RegularExpressions.Regex.Replace(fileName, @"([\[\]\*\?\\!#])", @"\$1");
        if (File.Exists(gi))
        {
            if (File.ReadAllLines(gi).Contains(line)) return;
        }
        else File.WriteAllText(gi, "# Pictures downloaded from Army Forge - OPR artwork, kept on this PC only.\n");
        File.AppendAllText(gi, line + "\n");
    }

    /// <summary>
    /// Kai's fresh start: deletes the portraits made from the 3D models (UnitModels/_Portraits), so only the official
    /// pictures are used. Returns how many files went.
    /// </summary>
    public static int DeleteModelPortraits(string? folder = null)
    {
        folder ??= Path.Combine(UnitModelPaths.Root, UnitPortraits.FolderName);
        if (!Directory.Exists(folder)) return 0;
        int n = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(folder, true);
        UnitPortraits.Forget();
        return n;
    }
}
