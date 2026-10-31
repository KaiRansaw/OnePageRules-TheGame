using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FDG.ArmyBuilding;
using FdgRaylib.Quest;

namespace FdgRaylib.Import;

// #415 - rebuild Assets/StarQuest/StarQuestHeroes.json from Army Forge.
//
// Star Quest is its own Army Forge game system (id 9, slug "grimdark-future-star-quest"). Asking for a
// book under that id returns the SAME factions as Grimdark Future but with only their HERO units, costed
// for Star Quest: Alien Hives has 41 units under GF and 8 under Star Quest, and a Grunt Veteran is 40pts
// in GF and 55pts in Star Quest. That is the whole reason this importer exists separately from
// OprBookImporter - it writes the Hero Builder's roster, not a playable .fdgbook.
//
// WHICH COST FIELD. Each option's costs[] entry carries three numbers: exactCost (the algorithm's raw
// value for THIS game system), cost (exactCost rounded to the nearest 5) and questCost (roughly 3x
// exactCost, which is the number a GF-context client would show for a Star Quest purchase). Under
// gameSystem=9 the whole payload is already Star Quest-scaled, so `cost` is the one that belongs with the
// unit costs in the same response - questCost there would be a second quest-scaling on top of the first,
// pricing a 55pt hero's gun at 95pts. Verified against gameSystem=2 for the same options, where cost and
// exactCost both differ and questCost is ~3x: see WorkItems/412.
//
// PER-UNIT COSTS. One upgrade package can be shared by several heroes, and 53 options across the catalog
// cost different amounts depending on which hero buys them (Dark Prime Brothers' Combat Bike is 115 on one
// captain and 210 on another). The on-disk shape carries one cost per option, so when a shared package
// prices differently per hero it is cloned per hero under a derived uid ("B1@<unitId>") and that hero's
// upgrades[] points at the clone. Nothing downstream can tell the difference - packages are looked up by
// uid and never enumerated for their own sake.
public static class StarQuestHeroImporter
{
    private const string BaseUrl = "https://army-forge.onepagerules.com";
    private const string Slug = "grimdark-future-star-quest";
    private const int GameSystem = 9;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>How the written file is shaped: camelCase keys, no nulls, indented. Matches what
    /// <see cref="StarQuestData"/> reads back (it is case-insensitive, so Q -> "q" round-trips).</summary>
    public static readonly JsonSerializerOptions Write = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Not indented: the roster is ~200 factions of machine-generated data nobody hand-edits, and
        // indenting it costs ~2MB of shipped asset for whitespace.
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record IndexEntry(string Uid, string Name, string? RaceGroup, string? Version);

    // ── Fetching ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The official Star Quest book index. Books with no hero units (OPR's empty parent
    /// placeholders, e.g. "Wormhole Daemons") are returned too and dropped later by the build.</summary>
    public static async Task<IReadOnlyList<IndexEntry>> FetchIndexAsync(CancellationToken ct = default)
    {
        string json = await GetAsync($"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={Slug}",
            "the Star Quest book index", ct);
        var list = new List<IndexEntry>();
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string? uid = Str(e, "uid"), name = Str(e, "name");
            if (uid is null or "" || name is null or "") continue;
            list.Add(new IndexEntry(uid, name, Str(e, "raceGroup"), Str(e, "versionString")));
        }
        return list.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Task<string> FetchBookAsync(string uid, CancellationToken ct = default) =>
        GetAsync($"{BaseUrl}/api/army-books/{Uri.EscapeDataString(uid)}?gameSystem={GameSystem}",
            $"Star Quest book '{uid}'", ct);

    /// <summary>The 39 rules every Star Quest book assumes but none of them define (Tough, Strider, Scout,
    /// Fearless...), in Star Quest's own wording - its Tough reads "once a model has taken as many wounds
    /// as its Tough value, it is killed", which is the skirmish reading, not Grimdark Future's.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> FetchCommonRulesAsync(
        CancellationToken ct = default)
    {
        string json = OprBookImporter.AsciiFoldJsonValues(
            await GetAsync($"{BaseUrl}/api/rules/common/{GameSystem}", "the Star Quest common rules", ct));
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rules", out JsonElement rules)) return map;
        foreach (JsonElement r in rules.EnumerateArray())
        {
            string? name = Str(r, "name"), desc = Str(r, "description");
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(desc)) map[name] = desc;
        }
        return map;
    }

    private static async Task<string> GetAsync(string url, string what, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Army Forge returned {(int)res.StatusCode} for {what}.");
            return await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Couldn't reach Army Forge for {what}: {ex.Message}", ex);
        }
    }

    // ── Building ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Turns one raw Army Forge book response into a faction entry, or null when the book has no
    /// hero units (OPR keeps a few empty parent books). <paramref name="warn"/> gets anything skipped.</summary>
    public static SqFaction? BuildFaction(string rawJson, Action<string>? warn = null) =>
        BuildFaction(rawJson, null, warn);

    public static SqFaction? BuildFaction(string rawJson,
        IReadOnlyDictionary<string, string>? commonRules, Action<string>? warn = null)
    {
        string folded = OprBookImporter.AsciiFoldJsonValues(rawJson, warn);
        JsonNode root = JsonNode.Parse(folded) ?? throw new InvalidOperationException("Book JSON is empty.");

        string name = (string?)root["name"] ?? "";
        var units = root["units"]?.AsArray() ?? new JsonArray();
        if (units.Count == 0) return null;

        var faction = new SqFaction
        {
            Uid = (string?)root["uid"] ?? "",
            Faction = name,
            Race = (string?)root["raceGroup"],
            Version = (string?)root["versionString"],
        };

        // Package uid -> its raw node, so a hero's upgrades[] can be resolved and (where costs differ per
        // hero) cloned.
        var rawPackages = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (JsonNode? p in root["upgradePackages"]?.AsArray() ?? new JsonArray())
        {
            if (p is null) continue;
            string? uid = (string?)p["uid"];
            if (!string.IsNullOrEmpty(uid)) rawPackages[uid] = p;
        }

        var emitted = new Dictionary<string, SqPackage>(StringComparer.Ordinal);
        var usedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonNode? u in units)
        {
            if (u is null) continue;
            SqHero hero = BuildHero(u, usedRules);
            var upgradeUids = new List<string>();

            foreach (JsonNode? up in u["upgrades"]?.AsArray() ?? new JsonArray())
            {
                string? uid = (string?)up;
                if (string.IsNullOrEmpty(uid)) continue;
                if (!rawPackages.TryGetValue(uid, out JsonNode? pkg))
                {
                    warn?.Invoke($"{name}/{hero.Name}: upgrade package '{uid}' is missing from the book.");
                    continue;
                }

                // Shared package, per-hero prices -> clone it under "<uid>@<heroId>".
                bool perUnit = PricesVaryByUnit(pkg);
                string emitUid = perUnit ? $"{uid}@{hero.Id}" : uid;
                if (!emitted.ContainsKey(emitUid))
                    emitted[emitUid] = BuildPackage(emitUid, pkg, perUnit ? hero.Id : null, usedRules, warn);
                upgradeUids.Add(emitUid);
            }

            hero.Upgrades = upgradeUids;
            faction.Heroes.Add(hero);
        }

        faction.Heroes = faction.Heroes.OrderBy(h => h.Cost).ThenBy(h => h.Name, StringComparer.Ordinal).ToList();
        faction.Packages = emitted.Values.OrderBy(p => p.Uid, StringComparer.Ordinal).ToList();

        // Only the rules these heroes can actually end up with, with OPR's own wording. Keeps the file to
        // the ~16 rules a faction uses rather than the ~45 its whole GF book defines.
        foreach (JsonNode? r in root["specialRules"]?.AsArray() ?? new JsonArray())
        {
            string? rn = (string?)r?["name"];
            if (string.IsNullOrEmpty(rn) || !usedRules.Contains(rn)) continue;
            if (faction.Rules.Any(x => string.Equals(x.N, rn, StringComparison.OrdinalIgnoreCase))) continue;
            faction.Rules.Add(new SqNamedText { N = rn, D = (string?)r!["description"] });
        }

        // The book defines its own rules but not the shared ones its units lean on (Tough, Strider,
        // Scout...). Fold those in so every rule a hero carries has wording to hover.
        if (commonRules is not null)
            foreach (string rn in usedRules)
            {
                if (!commonRules.TryGetValue(rn, out string? desc)) continue;
                if (faction.Rules.Any(x => string.Equals(x.N, rn, StringComparison.OrdinalIgnoreCase))) continue;
                faction.Rules.Add(new SqNamedText { N = rn, D = desc });
            }
        faction.Rules = faction.Rules.OrderBy(r => r.N, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (JsonNode? s in root["spells"]?.AsArray() ?? new JsonArray())
        {
            string? sn = (string?)s?["name"];
            if (string.IsNullOrEmpty(sn)) continue;
            faction.Spells.Add(new SqSpell { N = sn, T = (int?)s!["threshold"], D = (string?)s["effect"] });
        }

        return faction;
    }

    private static SqHero BuildHero(JsonNode u, HashSet<string> usedRules)
    {
        var hero = new SqHero
        {
            Id = (string?)u["id"] ?? "",
            Name = (string?)u["name"] ?? "Hero",
            Cost = (int?)u["cost"] ?? 0,
            Q = (int?)u["quality"] ?? 6,
            D = (int?)u["defense"] ?? 6,
        };

        foreach (JsonNode? r in u["rules"]?.AsArray() ?? new JsonArray())
        {
            if (r is null) continue;
            hero.Rules.Add(RuleLabel(r, usedRules));
        }

        // Tough comes off the Tough(X) rule; OPR also mirrors it in a `tough` field. A hero with neither
        // is a one-wound model.
        hero.T = (int?)u["tough"] ?? ToughFrom(u["rules"]) ?? 1;

        foreach (JsonNode? w in u["weapons"]?.AsArray() ?? new JsonArray())
            if (w is not null) hero.Weapons.Add(BuildWeapon(w, usedRules));

        // items[] is the hero's named gear ("For the Hive! (Furious Aura)") - wargear that grants rules,
        // kept apart from the bare rules so the Hero Builder can show it as equipment. A mount or vehicle
        // item also carries weapons inside it; those have to land in the weapon list or the hero can never
        // fire them (and the "Replace <that weapon>" section it unlocks becomes unreachable).
        foreach (JsonNode? it in u["items"]?.AsArray() ?? new JsonArray())
        {
            if (it is null) continue;
            hero.Gear.Add(ItemLabel(it, usedRules));
            foreach (SqWeapon w in ItemWeapons(it, usedRules)) hero.Weapons.Add(w);
        }

        return hero;
    }

    private static SqWeapon BuildWeapon(JsonNode w, HashSet<string> usedRules)
    {
        var weapon = new SqWeapon
        {
            N = (string?)w["name"] ?? "Weapon",
            R = (int?)w["range"] ?? 0,
            A = (int?)w["attacks"] ?? 1,
            C = Math.Max(1, (int?)w["count"] ?? 1),
        };
        foreach (JsonNode? r in w["specialRules"]?.AsArray() ?? new JsonArray())
            if (r is not null) weapon.S.Add(RuleLabel(r, usedRules));
        return weapon;
    }

    private static SqPackage BuildPackage(string emitUid, JsonNode pkg, string? forUnitId,
        HashSet<string> usedRules, Action<string>? warn)
    {
        var package = new SqPackage { Uid = emitUid };
        foreach (JsonNode? s in pkg["sections"]?.AsArray() ?? new JsonArray())
        {
            if (s is null) continue;
            var section = new SqSection
            {
                Label = (string?)s["label"] ?? "",
                Variant = (string?)s["variant"],
            };
            if (s["targets"] is JsonArray targets && targets.Count > 0)
                section.Targets = targets.Select(t => (string?)t ?? "").Where(t => t.Length > 0).ToList();

            foreach (JsonNode? o in s["options"]?.AsArray() ?? new JsonArray())
            {
                if (o is null) continue;
                var option = new SqOption
                {
                    Label = (string?)o["label"] ?? "",
                    Cost = CostOf(o, forUnitId),
                };
                foreach (JsonNode? g in o["gains"]?.AsArray() ?? new JsonArray())
                {
                    if (g is null) continue;
                    string type = (string?)g["type"] ?? "";
                    if (type == "ArmyBookWeapon")
                        option.Gains.Add(new SqGain { W = BuildWeapon(g, usedRules) });
                    else if (type == "ArmyBookItem")
                    {
                        option.Gains.Add(new SqGain { Rule = ItemLabel(g, usedRules) });
                        foreach (SqWeapon w in ItemWeapons(g, usedRules))
                            option.Gains.Add(new SqGain { W = w });
                    }
                    else if (type == "ArmyBookRule")
                        option.Gains.Add(new SqGain { Rule = RuleLabel(g, usedRules) });
                    else
                        warn?.Invoke($"Package {emitUid}: ignoring gain of unknown type '{type}'.");
                }
                section.Opts.Add(option);
            }
            package.Sections.Add(section);
        }
        return package;
    }

    // ── Small readers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>"Tough" + rating 9 -> "Tough(9)". Also records the bare name as used, so the faction's
    /// rule glossary picks up its description.</summary>
    private static string RuleLabel(JsonNode r, HashSet<string> usedRules)
    {
        string name = (string?)r["name"] ?? "";
        if (name.Length > 0) usedRules.Add(name);
        string? label = (string?)r["label"];
        if (!string.IsNullOrEmpty(label)) return label;
        string? rating = r["rating"]?.ToString();
        return string.IsNullOrEmpty(rating) ? name : $"{name}({rating})";
    }

    /// <summary>An item is wargear with a name and the rules it grants: "Combat Shield (Shielded)". The
    /// " (" is what <see cref="StarQuestData.IsGear"/> keys on to tell gear from a bare rule.</summary>
    private static string ItemLabel(JsonNode item, HashSet<string> usedRules)
    {
        string name = (string?)item["name"] ?? "Gear";
        var granted = new List<string>();
        foreach (JsonNode? c in item["content"]?.AsArray() ?? new JsonArray())
            if (c is not null) granted.Add(RuleLabel(c, usedRules));
        string? label = (string?)item["label"];
        if (!string.IsNullOrEmpty(label)) return label;
        return granted.Count == 0 ? name : $"{name} ({string.Join(", ", granted)})";
    }

    /// <summary>The weapons an item carries inside it - a Combat Bike's Heavy Bike-Rifle, a mount's bite.
    /// They are real weapons on the hero, not part of the gear's name.</summary>
    private static List<SqWeapon> ItemWeapons(JsonNode item, HashSet<string> usedRules)
    {
        var weapons = new List<SqWeapon>();
        foreach (JsonNode? c in item["content"]?.AsArray() ?? new JsonArray())
            if (c is not null && (string?)c["type"] == "ArmyBookWeapon")
                weapons.Add(BuildWeapon(c, usedRules));
        return weapons;
    }

    private static int? ToughFrom(JsonNode? rules)
    {
        foreach (JsonNode? r in rules?.AsArray() ?? new JsonArray())
        {
            if (!string.Equals((string?)r?["name"], "Tough", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(r!["rating"]?.ToString(), out int t)) return t;
        }
        return null;
    }

    /// <summary>The option's price. <paramref name="forUnitId"/> picks the right row when a shared package
    /// prices differently per hero; otherwise the single row. Null means OPR published no price.</summary>
    private static int? CostOf(JsonNode option, string? forUnitId)
    {
        JsonArray costs = option["costs"]?.AsArray() ?? new JsonArray();
        JsonNode? row = null;
        if (forUnitId is not null)
            row = costs.FirstOrDefault(c => (string?)c?["unitId"] == forUnitId);
        row ??= costs.FirstOrDefault();
        return row is null ? null : (int?)row["cost"];
    }

    private static bool PricesVaryByUnit(JsonNode pkg)
    {
        foreach (JsonNode? s in pkg["sections"]?.AsArray() ?? new JsonArray())
            foreach (JsonNode? o in s?["options"]?.AsArray() ?? new JsonArray())
            {
                var seen = new HashSet<int>();
                foreach (JsonNode? c in o?["costs"]?.AsArray() ?? new JsonArray())
                    if ((int?)c?["cost"] is int v) seen.Add(v);
                if (seen.Count > 1) return true;
            }
        return false;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ── The whole run ─────────────────────────────────────────────────────────────────────────────

    public sealed record RunReport(int Books, int Factions, int Heroes, int Packages, int Options,
        IReadOnlyList<string> Skipped);

    /// <summary>Fetches every official Star Quest book (or reads them from <paramref name="cacheDir"/>,
    /// one "&lt;uid&gt;.json" per book) and writes the Hero Builder's data file.</summary>
    public static async Task<RunReport> RunAsync(string outPath, string? cacheDir, Action<string> log,
        CancellationToken ct = default)
    {
        IReadOnlyList<IndexEntry> index = await FetchIndexAsync(ct);
        IReadOnlyDictionary<string, string> common = await FetchCommonRulesAsync(ct);
        log($"{index.Count} official Star Quest book(s) in the index, {common.Count} common rule(s).");

        var file = new SqHeroFile
        {
            Source = $"Army Forge, Grimdark Future: Star Quest (gameSystem {GameSystem}), " +
                     $"fetched {DateTime.UtcNow:yyyy-MM-dd}",
        };
        var skipped = new List<string>();

        foreach (IndexEntry entry in index)
        {
            ct.ThrowIfCancellationRequested();
            string raw;
            string? cachePath = cacheDir is null ? null : Path.Combine(cacheDir, entry.Uid + ".json");
            if (cachePath is not null && File.Exists(cachePath))
                raw = await File.ReadAllTextAsync(cachePath, ct);
            else
            {
                raw = await FetchBookAsync(entry.Uid, ct);
                if (cachePath is not null)
                {
                    Directory.CreateDirectory(cacheDir!);
                    await File.WriteAllTextAsync(cachePath, raw, ct);
                }
            }

            SqFaction? faction = BuildFaction(raw, common, m => log($"  {entry.Name}: {m}"));
            if (faction is null || faction.Heroes.Count == 0)
            {
                skipped.Add(entry.Name);
                log($"  {entry.Name}: no hero units - skipped.");
                continue;
            }
            file.Factions.Add(faction);
            log($"  {faction.Faction}: {faction.Heroes.Count} hero(es), {faction.Packages.Count} package(s), " +
                $"v{faction.Version}");
        }

        file.Factions = file.Factions.OrderBy(f => f.Faction, StringComparer.OrdinalIgnoreCase).ToList();

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(file, Write), ct);

        return new RunReport(
            index.Count,
            file.Factions.Count,
            file.Factions.Sum(f => f.Heroes.Count),
            file.Factions.Sum(f => f.Packages.Count),
            file.Factions.Sum(f => f.Packages.Sum(p => p.Sections.Sum(s => s.Opts.Count))),
            skipped);
    }

    /// <summary>Reads an existing StarQuestHeroes.json, for the before/after comparison.</summary>
    public static SqHeroFile? ReadFile(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<SqHeroFile>(File.ReadAllText(path), Read) : null;
}
