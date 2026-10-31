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
        // #412: the skirmish systems. All four of these are now VERIFIED against the live API
        // (2026-10-30) rather than taken from OPR's documented ordering - the check that settles it is
        // a unit whose profile differs between systems: Alien Hives Assault Grunts are 105pts for 10
        // models in Grimdark Future and 30pts for 3 in Firefight.
        [GameSystems.GFFirefight] = 3,
        [GameSystems.AoFSkirmish] = 5,
        // #412: Star Quest's ARMY BOOKS are id 10, not 9. Both ids answer to the same index slug and
        // hold the same faction names, which is exactly what makes this dangerous:
        //   id  9  grimdark-future-star-quest      the player's HEROES   (8 Alien Hives units, Grunt
        //                                                                 Veteran 55pts Q6 D6)
        //   id 10  grimdark-future-star-quest-ai   the ENEMY army books  (41 units, Grunt Veteran
        //                                                                 100pts Q5 D5)
        // A Star Quest game's opposition is built from the army books, so this slug maps to 10. The
        // hero roster is not a GameSystems book at all - it is its own dataset under
        // FdgRaylib/Assets/StarQuest/, built in the Hero Builder. Pulling the hero roster from id 10
        // would give every hero the enemy's profile, and pulling the enemy books from id 9 would leave
        // 33 of 41 enemy units missing.
        [GameSystems.GFStarQuest] = 10,
    };

    /// <summary>#412 - override a game system's Army Forge id, for when a guessed one turns out wrong.</summary>
    public static void SetGameSystemId(string slug, int id) => GameSystemIds[GameSystems.Normalize(slug)] = id;

    /// <summary>The Army Forge id this run will use for a slug, so it can be printed before it is used.</summary>
    public static int IdFor(string? gameSystem) => GameSystemId(gameSystem);

    /// <summary>Whether a slug's id was verified against the live API, or is still OPR's documented
    /// ordering taken on trust (#412).</summary>
    public static bool IdIsVerified(string? gameSystem) =>
        GameSystems.Normalize(gameSystem) is GameSystems.GrimdarkFuture or GameSystems.AgeOfFantasy
            or GameSystems.GFFirefight or GameSystems.AoFSkirmish or GameSystems.GFStarQuest;

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
    public static IReadOnlyList<string> IndexUrls() =>
        AllSystems.Select(IndexUrl).ToList();

    /// <summary>Every game system the game carries books for (#412: all four, not just the two that
    /// happened to be downloaded first).</summary>
    // Ordered to match the lobby's own system list (GameSystemInfo.DisplayOrder): the Grimdark Future
    // family, then the Age of Fantasy family. The Army Forge screen's system filter is built straight
    // from this (#412), so the two screens list the systems in the same order rather than each in its
    // own - and nothing else here depends on the order.
    public static IReadOnlyList<string> AllSystems { get; } = new[]
    {
        GameSystems.GrimdarkFuture, GameSystems.GFFirefight, GameSystems.GFStarQuest,
        GameSystems.AgeOfFantasy, GameSystems.AoFSkirmish,
    };

    /// <summary>The book-index URL for one game system.</summary>
    public static string IndexUrl(string? gameSystem) =>
        $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Uri.EscapeDataString(GameSystems.Normalize(gameSystem))}";

    /// <summary>The URL one book's full JSON comes from, for a uid out of an index.</summary>
    public static string BookUrl(string uid, string? gameSystem) =>
        $"{BaseUrl}/api/army-books/{Uri.EscapeDataString(uid)}?gameSystem={GameSystemId(gameSystem)}";

    /// <summary>The book index read straight from the cache folder, with no network. Null if not saved yet.</summary>
    public static IReadOnlyDictionary<string, string>? IndexFromCache(string? gameSystem)
    {
        if (CacheFolder == null) return null;
        string path = Path.Combine(CacheFolder, CacheFileName(IndexUrl(gameSystem)));
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
