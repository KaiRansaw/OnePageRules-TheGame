using System.Text.Json;
using System.Text.Json.Nodes;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering;

namespace FdgRaylib.Import;

/// <summary>
/// Real armies for the bots (Kai, 2026-11-15: "3" - bots should field real lists, not random ones). Army Forge's
/// <b>Ready-to-Play lists</b> - OPR's official lists first, then the best-voted community lists, per army book -
/// downloaded once, rebuilt against the bundled (current) books, checked, and written into the shipped
/// <c>armies/&lt;game&gt;/Ready-to-Play/&lt;book&gt;/</c> folders. From there the lobby's Random Army hands them to
/// bots (closest to the points limit first) and anyone can pick one with Choose an army.
///
/// <para><b>Only lists that are still right.</b> A list is kept only when every unit and every upgrade it picked
/// still exists in the current book, and our own list check finds nothing wrong (points limit, force-org caps).
/// Lists built on an older book version usually still pass - points are re-counted from the current book.</para>
///
/// <para>Run again whenever OPR updates (the headless <c>--harvest-ready-lists</c> flag): it rewrites the
/// Ready-to-Play folders from scratch, leaving every other army alone.</para>
/// </summary>
public static class ReadyListHarvester
{
    private const string BaseUrl = "https://army-forge.onepagerules.com";
    public const string FolderName = "Ready-to-Play";

    /// <summary>The battle games Army Forge has community lists for (Quest enemies are whole collections).</summary>
    public static readonly EGameSystem[] Systems =
        { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish };

    public sealed class Report
    {
        public int Books, Fetched, Kept;
        public readonly List<string> Written = new();
        public readonly Dictionary<string, int> SkippedBecause = new();
        public void Skip(string why) => SkippedBecause[why] = SkippedBecause.GetValueOrDefault(why) + 1;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <param name="armiesRoot">The shipped armies folder (the repo's <c>armies/</c>).</param>
    /// <param name="perBook">Most lists kept per army book: official ones first, then by votes, preferring
    /// different sizes so a bot finds one near any points limit.</param>
    public static async Task<Report> RunAsync(string armiesRoot, IEnumerable<EGameSystem>? systems = null, int perBook = 4,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var report = new Report();
        List<BookFile> bundled = BookLibrary.Load();
        foreach (EGameSystem system in systems ?? Systems)
        {
            string slug = GameSystemInfo.Slug(system);
            int systemId = ArmyForgeBookService.IdFor(slug);
            string folder = Path.Combine(armiesRoot, ArmyPaths.FolderNameFor(system), FolderName);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

            JsonArray? index = JsonNode.Parse(await Get($"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={slug}", ct)) as JsonArray;
            foreach (JsonNode? afBook in index ?? new JsonArray())
            {
                string? uid = afBook?["uid"]?.GetValue<string>(), name = afBook?["name"]?.GetValue<string>();
                if (uid == null || name == null) continue;
                BookFile? book = bundled.FirstOrDefault(b => GameSystems.SameSystem(b.GameSystem, slug)
                                                              && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
                if (book == null) { report.Skip("no bundled book of that name"); continue; }
                report.Books++;

                // The ten best-voted, plus the thirty newest: the best-voted are mostly built on older books whose
                // upgrades are gone, the newest on the current one.
                var candidates = new List<JsonNode>();
                foreach (string url in new[]
                         {
                             $"{BaseUrl}/api/community-lists/top10?gameSystem={systemId}&armyId={uid}",
                             $"{BaseUrl}/api/community-lists?gameSystem={systemId}&armyId={uid}&searchText=&page=1&sortBy=newest&forceOrg=false",
                         })
                {
                    try
                    {
                        if (JsonNode.Parse(await Get(url, ct)) is JsonArray page)
                            foreach (JsonNode? n in page)
                                if (n?["id"]?.GetValue<string>() is { } nid && candidates.All(c => c["id"]?.GetValue<string>() != nid))
                                    candidates.Add(n!);
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        report.Skip("Army Forge did not answer");
                    }
                }
                var kept = new List<(BuiltArmyFile Army, int Points, bool Official, int Votes)>();
                foreach (JsonNode entry in candidates)
                {
                    string? id = entry["id"]?.GetValue<string>();
                    if (id == null) continue;
                    // Built on a book older than one this game still reads: its picks are gone - don't fetch it.
                    string? version = (entry["details"]?["armyVersions"] as JsonArray)?.FirstOrDefault()?["version"]?.GetValue<string>();
                    if (version != null && !OprListImporter.SupportedVersionPrefixes.Any(v => version.StartsWith(v, StringComparison.Ordinal)))
                    {
                        report.Skip("built on a book older than 3.5");
                        continue;
                    }
                    report.Fetched++;
                    try
                    {
                        JsonNode? full = JsonNode.Parse(await Get($"{BaseUrl}/api/community-lists/{Uri.EscapeDataString(id)}", ct));
                        if (Rebuild(full, book, out string? why) is not { } army) { report.Skip(why ?? "unknown"); continue; }
                        bool official = entry["official"]?.GetValue<bool>() ?? false;
                        int votes = entry["votes"]?.GetValue<int>() ?? entry["popularity"]?.GetValue<int>() ?? 0;
                        kept.Add((army, army.Units.Sum(u => u.PointCost) + army.UnattributedPoints, official, votes));
                    }
                    catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                    {
                        report.Skip("unreadable list");
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        report.Skip("Army Forge did not answer");
                    }
                }

                // Official first, then votes; then spread the sizes so most points limits have one near them.
                var chosen = new List<(BuiltArmyFile Army, int Points, bool Official, int Votes)>();
                foreach (var k in kept.OrderByDescending(k => k.Official).ThenByDescending(k => k.Votes))
                {
                    if (chosen.Count >= perBook) break;
                    if (chosen.Any(c => c.Army.Name == k.Army.Name)) continue;
                    // A second list of nearly the same size is kept only once every size has one.
                    if (chosen.Any(c => Math.Abs(c.Points - k.Points) < 150) && kept.Any(o => !chosen.Contains(o) && chosen.All(c => Math.Abs(c.Points - o.Points) >= 150)))
                        continue;
                    chosen.Add(k);
                }
                foreach (var c in chosen)
                {
                    string dir = Path.Combine(folder, ImportedArmySaver.SafeFileName(book.Name));
                    Directory.CreateDirectory(dir);
                    // Many list names already say their size ("... (1000)"): add the points only when they don't.
                    string fileName = c.Army.Name.Contains(c.Points.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                        ? c.Army.Name : $"{c.Army.Name} ({c.Points})";
                    string path = ImportedArmySaver.FreePath(dir, ImportedArmySaver.SafeFileName(fileName));
                    File.WriteAllText(path, JsonSerializer.Serialize(c.Army, c.Army.GetType(), RuleJson.Options));
                    report.Written.Add(path);
                    report.Kept++;
                }
                log?.Invoke($"{GameSystems.DisplayName(slug)} - {book.Name}: {chosen.Count} kept of {candidates.Count}");
            }
        }
        ArmyCatalogCache.Invalidate();
        return report;
    }

    /// <summary>
    /// One community list (its /api/community-lists/{id} reply) rebuilt against <paramref name="book"/>, or null with
    /// the reason when anything in it no longer fits the current book or the list breaks the rules.
    /// </summary>
    public static BuiltArmyFile? Rebuild(JsonNode? full, BookFile book, out string? why)
    {
        why = null;
        if (full?["list"] is not JsonObject list) { why = "no list in the reply"; return null; }
        // Army Forge saves a pick as {upgradeId, optionId}; the share format the importer reads nests them.
        var shaped = (JsonObject)list.DeepClone();
        foreach (JsonNode? unit in shaped["units"] as JsonArray ?? new JsonArray())
        {
            if (unit?["selectedUpgrades"] is not JsonArray picks) continue;
            var nested = new JsonArray();
            foreach (JsonNode? p in picks)
                nested.Add(new JsonObject
                {
                    ["upgrade"] = new JsonObject { ["uid"] = p?["upgradeId"]?.DeepClone() },
                    ["option"] = new JsonObject { ["uid"] = p?["optionId"]?.DeepClone() },
                });
            unit["selectedUpgrades"] = nested;
        }
        if (full["details"]?["listPoints"] is { } listPoints) shaped["listPoints"] = listPoints.DeepClone();

        OprForgeSessionResult session = OprListImporter.ReconstructSelections(shaped.ToJsonString(), book);
        if (session.ExcludedUnits.Count > 0) { why = "a unit no longer in the current book"; return null; }
        if (session.Warnings.Any(w => w.Contains("could not be matched", StringComparison.Ordinal))) { why = "an upgrade no longer in the current book"; return null; }
        if (session.Selections.Units.Count == 0) { why = "empty"; return null; }

        bool forceOrg = list["forceOrg"]?.GetValue<bool>() ?? true;
        BuiltArmyFile compiled = ListCompiler.Compile(book, session.Selections);
        var problems = ListValidator.Validate(book, session.Selections, compiled, forceOrg)
            .Where(i => i.Severity == ListIssueSeverity.Error && !i.Message.Contains("no published points cost", StringComparison.Ordinal))
            .ToList();
        if (problems.Count > 0) { why = "breaks the list rules now"; return null; }

        string name = ImportedArmySaver.SafeFileName(session.Selections.Name).Trim();
        BuiltArmyFile army = EditableSession.Attach(compiled, session.Selections, book);
        army.Name = OprBookImporter.AsciiFold(name);
        army.Book = book;
        army.GameSystem = book.GameSystem;   // the lobby offers an army only to its own game
        return army;
    }

    /// <summary>Army Forge, with three tries (a slow answer is common; one is no reason to stop the whole run).</summary>
    private static async Task<string> Get(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using HttpResponseMessage r = await Http.GetAsync(url, ct);
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadAsStringAsync(ct);
            }
            catch (Exception e) when (attempt < 3 && !ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }
}
