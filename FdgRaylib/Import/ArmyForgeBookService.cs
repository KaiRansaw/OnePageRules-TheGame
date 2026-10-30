using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FDG.ArmyBuilding;

namespace FdgRaylib.Import;

// #219 Slice 2 — refresh the bundled .fdgbook catalog's "unpriced upgrade" flags from OPR's army-book
// endpoint. The bundled snapshots record every option's cost as a plain number and carry costUnpriced=false
// everywhere (the flag was stamped by a later re-serialize, not a real import), so options OPR prices inside
// its own algorithm - it omits the `cost` key entirely on those - look identical to genuinely free ones.
// OPR never publishes the hidden numbers, but the /api/army-books/{uid} endpoint DOES still distinguish
// "cost present" from "cost absent", so we can recover the priced/unpriced DISTINCTION and set the flag.
//
// We do NOT rewrite the books wholesale (that would drop effect sets #239, embedded ruleDefinitions
// #153/#196/#197 and base retrofits #225). Instead we re-import to a throwaway BookFile and transfer ONLY
// the costUnpriced flags onto the existing book, matched by the OPR-stable option Id.
public static class ArmyForgeBookService
{
    private const string BaseUrl = "https://army-forge.onepagerules.com";

    // #378: the numeric ids OPR's endpoints take, per game-system slug (a book's BookFile.GameSystem;
    // absent means Grimdark Future). GDF=2 and AoF=4 verified against the live API.
    private static readonly Dictionary<string, int> GameSystemIds = new(StringComparer.OrdinalIgnoreCase)
    {
        [GameSystems.GrimdarkFuture] = 2,
        [GameSystems.AgeOfFantasy] = 4,
        // #412: the skirmish systems. GDF=2 and AoF=4 were verified against the live API; these two are
        // OPR's documented ordering and are NOT yet verified, because the site is refused at Claude's
        // network gateway. They are overridable with --book-system-id for exactly that reason, and the
        // importer prints the id it used so a wrong one is visible rather than silent. Verify by
        // importing one Firefight book and checking a unit Kai already knows differs: Alien Hives
        // Assault Grunts are 105 pts for 10 in Grimdark Future and 30 pts for 3 in Firefight.
        [GameSystems.GFFirefight] = 3,
        [GameSystems.AoFSkirmish] = 5,
    };

    /// <summary>#412 - override a game system's Army Forge id, for when a guessed one turns out wrong.</summary>
    public static void SetGameSystemId(string slug, int id) => GameSystemIds[GameSystems.Normalize(slug)] = id;

    /// <summary>The Army Forge id this run will use for a slug, so it can be printed before it is used.</summary>
    public static int IdFor(string? gameSystem) => GameSystemId(gameSystem);

    /// <summary>Whether a slug's id was verified against the live API, or is still OPR's documented
    /// ordering taken on trust (#412).</summary>
    public static bool IdIsVerified(string? gameSystem) =>
        GameSystems.Normalize(gameSystem) is GameSystems.GrimdarkFuture or GameSystems.AgeOfFantasy;

    private static int GameSystemId(string? gameSystem)
    {
        string slug = GameSystems.Normalize(gameSystem);
        if (!GameSystemIds.TryGetValue(slug, out int id))
            throw new InvalidOperationException($"Unknown OPR game system '{slug}' - no Army Forge id for it.");
        return id;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>What one book's refresh changed. <see cref="Priced"/> options were flagged unpriced and now
    /// carry a real cost recovered from OPR's per-unit costs[] (the core #219 fix); <see cref="Repriced"/>
    /// already-priced options whose number moved; <see cref="Unmatched"/> options had a (unit, option) key
    /// absent from the live book (OPR renamed/removed it) and were left untouched. <see cref="Samples"/> shows
    /// a few concrete before/after lines.</summary>
    public sealed record BookCostRefreshReport(
        string BookName, int Priced, int Repriced, int Unmatched, IReadOnlyList<string> Samples);

    /// <summary>OPR official-books listing for a game system (#378: null = Grimdark Future), as a
    /// case-insensitive book-name -> uid map.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> FetchBookIndexAsync(
        string? gameSystem = null, CancellationToken ct = default)
    {
        string slug = GameSystems.Normalize(gameSystem);
        string json = await GetAsync(
            $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Uri.EscapeDataString(slug)}",
            $"the {slug} book index", ct);
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("name", out JsonElement name) && e.TryGetProperty("uid", out JsonElement uid)
                && name.GetString() is { Length: > 0 } n && uid.GetString() is { Length: > 0 } u)
                index[n] = u;
        }
        return index;
    }

    /// <summary>Fetch the full OPR army-book JSON for a uid (the endpoint that still carries per-option
    /// costs). <paramref name="gameSystem"/> is the book's slug (#378: null = Grimdark Future).</summary>
    public static Task<string> FetchBookJsonAsync(string uid, string? gameSystem = null, CancellationToken ct = default) =>
        GetAsync($"{BaseUrl}/api/army-books/{Uri.EscapeDataString(uid)}?gameSystem={GameSystemId(gameSystem)}",
            $"army book '{uid}'", ct);

    /// <summary>Pure, network-free core: re-import <paramref name="rawBookJson"/> (now cost-aware) and copy the
    /// resolved per-unit Cost + costUnpriced onto <paramref name="bundled"/>, matched by (unit Id, option Id) -
    /// the SAME option Id costs differently on different units, so a flat option-Id match would smear one price
    /// across all of them. Mutates <paramref name="bundled"/> in place and returns what changed.</summary>
    public static BookCostRefreshReport RefreshCosts(BookFile bundled, string rawBookJson)
    {
        BookFile fresh = OprBookImporter.Import(rawBookJson, bundled.Source, bundled.License);

        var freshByKey = new Dictionary<(string Unit, string Option), UpgradeOption>();
        foreach (RosterUnit u in fresh.Units)
            foreach (UpgradeOption o in u.Sections.SelectMany(s => s.Options))
                freshByKey[(u.Id, o.Id)] = o;

        int priced = 0, repriced = 0, unmatched = 0;
        var samples = new List<string>();
        foreach (RosterUnit u in bundled.Units)
            foreach (UpgradeOption o in u.Sections.SelectMany(s => s.Options))
            {
                if (!freshByKey.TryGetValue((u.Id, o.Id), out UpgradeOption? f)) { unmatched++; continue; }

                bool wasUnpriced = o.CostUnpriced;
                int oldCost = o.Cost;
                o.Cost = f.Cost;
                o.CostUnpriced = f.CostUnpriced;

                if (wasUnpriced && !f.CostUnpriced)
                {
                    priced++;
                    if (samples.Count < 8) samples.Add($"{u.Name} / {o.Label}: was unpriced -> {f.Cost} pts");
                }
                else if (!wasUnpriced && oldCost != f.Cost)
                {
                    repriced++;
                    if (samples.Count < 8) samples.Add($"{u.Name} / {o.Label}: {oldCost} -> {f.Cost} pts");
                }
            }
        return new BookCostRefreshReport(bundled.Name, priced, repriced, unmatched, samples);
    }

    /// <summary>What one book's #383 per-model section refresh changed: <see cref="Stamped"/> sections
    /// were re-classified (Affects -> Any, PerModelBudget set); <see cref="Unmatched"/> bundled sections
    /// had no (unit, section) key in the live book and were left untouched.</summary>
    public sealed record SectionShapeRefreshReport(
        string BookName, int Stamped, int Unmatched, IReadOnlyList<string> Samples);

    /// <summary>Pure, network-free core (#383): re-import <paramref name="rawBookJson"/> with the
    /// per-model-aware importer and copy each per-model section's classified shape (Affects +
    /// PerModelBudget) onto <paramref name="bundled"/>, matched by (unit Id, section LABEL) - the
    /// classification needs the raw `select`/`model` fields the snapshot never stored, and section
    /// `id`s regenerate on every OPR publish (unit and option ids are stable, section ids are not),
    /// so the label is the only cross-publish section key. A (unit, label) that appears twice in the
    /// live book stamps only when every duplicate classifies identically. Only sections the live book
    /// classifies per-model are touched; all other curation is preserved (the <see cref="RefreshCosts"/>
    /// discipline). Mutates <paramref name="bundled"/> in place.</summary>
    public static SectionShapeRefreshReport RefreshPerModelSections(BookFile bundled, string rawBookJson)
    {
        BookFile fresh = OprBookImporter.Import(rawBookJson, bundled.Source, bundled.License);

        var freshByKey = new Dictionary<(string Unit, string Label), List<UpgradeSection>>();
        foreach (RosterUnit u in fresh.Units)
            foreach (UpgradeSection s in u.Sections)
            {
                if (!freshByKey.TryGetValue((u.Id, s.Label), out List<UpgradeSection>? group))
                    freshByKey[(u.Id, s.Label)] = group = new List<UpgradeSection>();
                group.Add(s);
            }

        int stamped = 0, unmatched = 0;
        var samples = new List<string>();
        foreach (RosterUnit u in bundled.Units)
            foreach (UpgradeSection s in u.Sections)
            {
                if (!freshByKey.TryGetValue((u.Id, s.Label), out List<UpgradeSection>? group))
                {
                    unmatched++;
                    continue;
                }

                UpgradeSection f = group[0];
                if (group.Any(g => g.PerModelBudget != f.PerModelBudget || g.Affects != f.Affects)) continue;
                if (!f.PerModelBudget || (s.PerModelBudget && s.Affects == f.Affects)) continue;

                s.Affects = f.Affects;
                s.PerModelBudget = true;
                stamped++;
                if (samples.Count < 12) samples.Add($"{u.Name} / {s.Label}");
            }
        return new SectionShapeRefreshReport(bundled.Name, stamped, unmatched, samples);
    }

    /// <summary>
    /// #411 - a folder of saved Army Forge responses to read instead of (or before) the network.
    /// <para>
    /// Every importer mode goes through <see cref="GetAsync"/>, so setting this once makes all of them
    /// work with no network at all. That matters because Claude's cloud workspace is refused at the
    /// network gateway for onepagerules.com, while Kai's own machine and browser reach it normally: the
    /// browser saves the responses into this folder, and the importers - which are already written and
    /// tested - do the rest. A response fetched over the network is also written here, so the folder
    /// doubles as a download cache and a second run never re-fetches.
    /// </para>
    /// </summary>
    public static string? CacheFolder { get; set; }

    /// <summary>
    /// #411 - with a <see cref="CacheFolder"/>, never touch the network: a response that isn't already
    /// saved is an error that names the URL and the file it must be saved as.
    /// </summary>
    public static bool CacheOnly { get; set; }

    /// <summary>
    /// The file name a URL's response is cached under: predictable, printable and safe on Windows, so a
    /// person saving it by hand from a browser can get it right. Host and scheme are dropped; everything
    /// that isn't a letter, digit, dot or dash becomes a dash.
    /// </summary>
    public static string CacheFileName(string url)
    {
        int cut = url.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        string tail = cut >= 0 ? url[(cut + 5)..] : url;
        var sb = new System.Text.StringBuilder(tail.Length + 8);
        foreach (char c in tail)
            sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '-');
        string name = sb.ToString().Trim('-');
        while (name.Contains("--", StringComparison.Ordinal)) name = name.Replace("--", "-");
        return (name.Length == 0 ? "response" : name) + ".json";
    }

    /// <summary>The two book-index URLs - the starting point for a cache run, since every book URL is
    /// derived from what they list.</summary>
    public static IReadOnlyList<string> IndexUrls() => new[]
    {
        $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Uri.EscapeDataString(GameSystems.GrimdarkFuture)}",
        $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Uri.EscapeDataString(GameSystems.AgeOfFantasy)}",
    };

    /// <summary>The URL one book's full JSON comes from, for a uid out of an index.</summary>
    public static string BookUrl(string uid, string? gameSystem) =>
        $"{BaseUrl}/api/army-books/{Uri.EscapeDataString(uid)}?gameSystem={GameSystemId(gameSystem)}";

    /// <summary>The book index read straight from the cache folder, with no network. Null if not saved yet.</summary>
    public static IReadOnlyDictionary<string, string>? IndexFromCache(string? gameSystem)
    {
        if (CacheFolder == null) return null;
        string slug = GameSystems.Normalize(gameSystem);
        string url = $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Uri.EscapeDataString(slug)}";
        string path = Path.Combine(CacheFolder, CacheFileName(url));
        if (!File.Exists(path)) return null;
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("name", out JsonElement name) && e.TryGetProperty("uid", out JsonElement uid)
                && name.GetString() is { Length: > 0 } n && uid.GetString() is { Length: > 0 } u)
                index[n] = u;
        return index;
    }

    private static async Task<string> GetAsync(string url, string what, CancellationToken ct)
    {
        string? cached = CacheFolder == null ? null : Path.Combine(CacheFolder, CacheFileName(url));
        if (cached != null && File.Exists(cached)) return await File.ReadAllTextAsync(cached, ct);
        if (cached != null && CacheOnly)
            throw new InvalidOperationException(
                $"Not in the cache: {what}." + Environment.NewLine +
                $"  Save this URL's response as: {Path.GetFileName(cached)}" + Environment.NewLine +
                $"  {url}");

        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach Army Forge: {ex.Message}", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Army Forge did not respond (timed out).");
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Army Forge returned HTTP {(int)response.StatusCode} fetching {what}.");
        string body = await response.Content.ReadAsStringAsync(ct);
        // A fetched response is saved, so the folder doubles as a download cache and a second run - or a
        // run on a machine with no network - never has to go out again.
        if (cached != null)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder!);
                await File.WriteAllTextAsync(cached, body, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Caching is a convenience; a failure to save must not fail the import.
            }
        }
        return body;
    }
}
