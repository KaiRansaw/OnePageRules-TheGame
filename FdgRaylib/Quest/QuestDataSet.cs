using System.Text.Json;
using System.Text.Json.Serialization;
using FDG;
using FDG.Quest;

namespace FdgRaylib.Quest;

/// <summary>
/// #418 - one Quest system's player-side dataset: its hero roster and its rules (classes, skills,
/// conditions). There are two, and they are not interchangeable.
///
/// <para>
/// <b>Why this is an instance and not a static.</b> Until #418 this was
/// <see cref="StarQuestData"/>: a static class holding a single roster behind a <c>_loaded</c> flag,
/// with the asset folder and both file names inlined. That is fine for one Quest system and a dead end
/// for two - one process can only ever hold one dataset, and the first screen to touch it decides which.
/// The same shape has bitten this codebase repeatedly (<c>GameWideConstants</c>, <c>HordeRules</c>), so
/// the dataset is now an object you ask for by system, cached per system, and the old static is a thin
/// view onto the Star Quest one.
/// </para>
/// <para>
/// <b>The two datasets.</b> Both come from Army Forge, both are the player's HEROES, and each has an
/// enemy twin under a different game-system id whose profiles are completely different:
/// </para>
/// <list type="table">
/// <item><term>GF: Star Quest</term><description><c>Assets/StarQuest</c>, Army Forge id 9, 54 factions,
/// 205 heroes. Enemy twin: id 10 (the bundled <c>Books/GF Star Quest</c>).</description></item>
/// <item><term>AoF: Quest</term><description><c>Assets/AoFQuest</c>, Army Forge id 7, 46 factions,
/// 161 heroes. Enemy twin: id 8 (<c>Books/AoF Quest</c>, #417).</description></item>
/// </list>
/// Loaded once per system, on first use. If a file is missing or broken, <see cref="LoadError"/> says
/// why and the lists are empty rather than the screen throwing.
/// </summary>
public sealed class QuestDataSet
{
    private readonly object _gate = new();
    private readonly string _heroesFile;
    private readonly string _rulesFile;

    private bool _loaded;
    private List<SqFaction> _factions = new();
    private SqRules _rules = new();

    private QuestDataSet(EGameSystem system, string folderName, string heroesFile, string rulesFile)
    {
        System = system;
        FolderName = folderName;
        _heroesFile = heroesFile;
        _rulesFile = rulesFile;
    }

    /// <summary>Which Quest system this dataset belongs to.</summary>
    public EGameSystem System { get; }

    /// <summary>The folder under <c>Assets</c> this dataset ships in.</summary>
    public string FolderName { get; }

    /// <summary>Where the two files live for this run.</summary>
    public string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", FolderName);

    public string? LoadError { get; private set; }

    public IReadOnlyList<SqFaction> Factions { get { Load(); return _factions; } }

    public SqRules Rules { get { Load(); return _rules; } }

    /// <summary>The playable classes (not the "Default Skills" bucket).</summary>
    public IEnumerable<SqClass> Classes => Rules.Classes.Where(c => c.Name != "Default Skills");

    /// <summary>
    /// The groups the faction list is folded into, in the order they are shown - Army Forge's own.
    /// <para>
    /// Batch E (Kai, 2026-11-07: "Factions sorted EXACTLY the way the official Army Forge groups and orders
    /// them... No extra groupings of our own"). The group is each army book's <c>raceGroup</c>, which the
    /// hero import already stores on every faction (<see cref="SqFaction.Race"/>, from
    /// <c>/api/army-books?filters=official&amp;gameSystemSlug=...</c>). Army Forge's Quest hero page
    /// (<c>/quest/&lt;slug&gt;</c>) groups the books that have units by <c>raceGroup || "Unknown"</c> and
    /// shows the groups in lodash <c>orderBy</c> order of their names - a plain ascending string sort,
    /// which is ordinal here. Inside a group the books keep the API's own order, which is the order the
    /// import wrote them to the hero file, so the picker walks <see cref="Factions"/> as it is.
    /// </para>
    /// <para>This replaces two hand-made maps (Humanity / Chaos / Xenos, and an AoF set of eight) that
    /// were our own invention and that Kai dropped.</para>
    /// </summary>
    public IReadOnlyList<string> FactionGroups =>
        Factions.Select(GroupOf).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();

    /// <summary>The Army Forge race group a faction belongs to; "Unknown" (Army Forge's own word) when the
    /// book has none.</summary>
    public string GroupOf(SqFaction faction) =>
        string.IsNullOrWhiteSpace(faction.Race) ? UnknownGroup : faction.Race!;

    /// <summary>What Army Forge calls a book with no race group.</summary>
    public const string UnknownGroup = "Unknown";

    // ── The registry ──────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<EGameSystem, QuestDataSet> Sets = new();
    private static readonly object SetsGate = new();

    /// <summary>
    /// The dataset for a Quest system, cached. A non-Quest system gets Star Quest's, because every
    /// caller that reaches here has already decided it is drawing a party - see
    /// <see cref="GameSystemInfo.IsQuest"/>, which is what callers should gate on.
    /// </summary>
    public static QuestDataSet For(EGameSystem system)
    {
        EGameSystem key = system == EGameSystem.AoFQuest ? EGameSystem.AoFQuest : EGameSystem.GFStarQuest;
        lock (SetsGate)
        {
            if (Sets.TryGetValue(key, out QuestDataSet? set)) return set;
            set = key == EGameSystem.AoFQuest ? CreateAoFQuest() : CreateStarQuest();
            Sets[key] = set;
            return set;
        }
    }

    /// <summary>Every Quest dataset this build ships, for a fixture that wants to walk both.</summary>
    public static IEnumerable<QuestDataSet> All =>
        new[] { For(EGameSystem.GFStarQuest), For(EGameSystem.AoFQuest) };

    private static QuestDataSet CreateStarQuest() => new(
        EGameSystem.GFStarQuest, "StarQuest", "StarQuestHeroes.json", "StarQuestRules.json");

    private static QuestDataSet CreateAoFQuest() => new(
        EGameSystem.AoFQuest, "AoFQuest", "AoFQuestHeroes.json", "AoFQuestRules.json");

    // ── Loading ───────────────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private void Load()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var heroes = JsonSerializer.Deserialize<SqHeroFile>(
                    File.ReadAllText(Path.Combine(Folder, _heroesFile)), Json);
                _factions = heroes?.Factions?.Where(f => f.Heroes.Count > 0)
                    .OrderBy(f => f.Faction, StringComparer.OrdinalIgnoreCase).ToList() ?? new();
                _rules = JsonSerializer.Deserialize<SqRules>(
                    File.ReadAllText(Path.Combine(Folder, _rulesFile)), Json) ?? new();
            }
            catch (Exception ex)
            {
                LoadError = $"Couldn't read the {GameSystemInfo.DisplayName(System)} data in "
                    + $"{Folder}: {ex.Message}";
                _factions = new();
                _rules = new();
            }
        }
    }

    // ── Classes and skills ────────────────────────────────────────────────────────────────────────

    public SqClass? ClassById(string id) => Rules.Classes.FirstOrDefault(c => c.Id == id);

    /// <summary>The class's feature: its one "no test" skill at tier 0.</summary>
    public SqSkill? ClassFeat(SqClass cls) =>
        Rules.Skills.FirstOrDefault(s => s.ClassId == cls.Id && s.Type == 3);

    /// <summary>The class's skills of one tier (feature excluded).</summary>
    public IEnumerable<SqSkill> ClassSkills(SqClass cls, int tier) =>
        Rules.Skills.Where(s => s.ClassId == cls.Id && s.Type != 3 && s.Tier == tier);

    /// <summary>Skills every class may learn, of one tier.</summary>
    public IEnumerable<SqSkill> DefaultSkills(int tier) =>
        Rules.Skills.Where(s => (s.ClassId == null || s.ClassId == "core") && s.Tier == tier);

    public SqSkill? SkillByName(string name) => Rules.Skills.FirstOrDefault(s => s.Name == name);

    /// <summary>"Str", "Dex", "Wil", or "" for a class feature.</summary>
    public static string SkillStat(SqSkill s) =>
        s.Type switch { 0 => "Str", 1 => "Dex", 2 => "Wil", _ => "" };

    // ── Building a hero ───────────────────────────────────────────────────────────────────────────

    /// <summary>A fresh level-1 hero: the army-book hero with this system's base stats, no class yet.
    /// The hero records which Quest system built it, so a party picker can tell an AoF: Quest hero from
    /// a Star Quest one even when both sit in the same folder.</summary>
    public QuestHero NewHero(SqFaction faction, SqHero hero)
    {
        var h = new QuestHero
        {
            Faction = faction.Faction,
            FactionUid = faction.Uid,
            BaseHeroId = hero.Id,
            BaseHeroName = hero.Name,
            BaseCost = hero.Cost,
            Quality = Rules.BaseHero.Qua,
            Defense = Rules.BaseHero.Def,
            Power = Rules.BaseHero.Pow,
            Strength = Rules.BaseHero.Str,
            Dexterity = Rules.BaseHero.Dex,
            Willpower = Rules.BaseHero.Wil,
            Credits = Rules.BaseHero.Credits,
            Tough = Math.Max(1, hero.T),
            Rules = hero.Rules.Where(r => r != "Hero").ToList(),
            Gear = hero.Gear.ToList(),
            Weapons = hero.Weapons.Select(w => w.ToQuest()).ToList(),
            GameType = QuestGameTypes.For(System),
        };
        return h;
    }

    /// <summary>Gives the hero a class: the base stats, then the class bonuses, its feature, and no
    /// skills picked yet. Keeps the weapons and upgrades already chosen.</summary>
    public void ApplyClass(QuestHero h, SqClass cls)
    {
        h.Class = cls.Name;
        h.ClassId = cls.Id;
        h.Quality = Math.Max(2, Rules.BaseHero.Qua - cls.Qua);
        h.Power = Rules.BaseHero.Pow + cls.Pow;
        h.Strength = Math.Max(2, Rules.BaseHero.Str - cls.Str);
        h.Dexterity = Math.Max(2, Rules.BaseHero.Dex - cls.Dex);
        h.Willpower = Math.Max(2, Rules.BaseHero.Wil - cls.Wil);
        h.Caster = cls.Caster;
        h.SkillSlots = cls.Skills;
        h.ClassFeat = ClassFeat(cls)?.Name ?? "";
        h.Skills.Clear();
        h.Rules.RemoveAll(r => r.StartsWith("Caster(", StringComparison.OrdinalIgnoreCase));
        if (h.Caster > 0) h.Rules.Add($"Caster({h.Caster})");
    }

    /// <summary>
    /// Rebuilds the hero's weapons and rules from its base hero plus the chosen upgrade options (by
    /// label), in order. "Replace X" swaps the named weapon out; "Upgrade with..." adds; "Take ...
    /// attachment" adds.
    /// </summary>
    public void ApplyUpgrades(QuestHero h, SqFaction faction, SqHero baseHero,
        IReadOnlyList<(SqSection Section, SqOption Option)> picks)
    {
        var (weapons, rules, cost) = Loadout(baseHero, picks);
        h.Weapons = weapons;
        h.Rules = rules.Where(r => !IsGear(r)).ToList();
        h.Gear = rules.Where(IsGear).ToList();
        if (h.Caster > 0) h.Rules.Add($"Caster({h.Caster})");
        h.Upgrades = picks.Select(p => p.Option.Label).ToList();
        h.UpgradeCost = cost;
    }

    // ── Loadout maths (system-independent) ────────────────────────────────────────────────────────

    /// <summary>The weapons, rules/gear and upgrade points the base hero ends up with after these
    /// picks.</summary>
    public static (List<QuestWeapon> Weapons, List<string> Rules, int Cost) Loadout(SqHero baseHero,
        IReadOnlyList<(SqSection Section, SqOption Option)> picks)
    {
        var weapons = baseHero.Weapons.Select(w => w.ToQuest()).ToList();
        var rules = baseHero.Rules.Where(r => r != "Hero").Concat(baseHero.Gear).ToList();
        int cost = 0;
        foreach (var (section, option) in picks)
        {
            cost += option.Cost ?? 0;
            if (section.Variant == "replace")
                foreach (string target in section.Targets ?? new List<string>())
                {
                    int i = weapons.FindIndex(w => SameName(w.Name, target));
                    if (i >= 0) { weapons.RemoveAt(i); continue; }
                    int r = rules.FindIndex(x => SameName(RuleName(x), target));
                    if (r >= 0) rules.RemoveAt(r);
                }
            foreach (SqGain g in option.Gains)
            {
                if (g.W != null) weapons.Add(g.W.ToQuest());
                else if (!string.IsNullOrEmpty(g.Rule)) rules.Add(g.Rule);
            }
        }
        return (weapons, rules, cost);
    }

    /// <summary>A "Replace X" or "Take one X attachment" section only applies once the hero has every X
    /// (a weapon or piece of gear it started with or took in an earlier upgrade).</summary>
    public static bool HasTargets(SqSection section, List<QuestWeapon> weapons, List<string> rules)
    {
        if (section.Variant != "replace" && section.Variant != "attachment") return true;
        var ws = weapons.Select(w => w.Name).ToList();
        var rs = rules.Select(RuleName).ToList();
        foreach (string target in section.Targets ?? new List<string>())
        {
            int i = ws.FindIndex(w => SameName(w, target));
            if (i >= 0) { ws.RemoveAt(i); continue; }
            int r = rs.FindIndex(x => SameName(x, target));
            if (r >= 0) { rs.RemoveAt(r); continue; }
            return false;
        }
        return true;
    }

    /// <summary>"Combat Shield (Shielded)" (a name, a space, then its rules) is gear; "Tough(9)" is a
    /// rule.</summary>
    public static bool IsGear(string s) => s.Contains(" (", StringComparison.Ordinal);

    // "Combat Shield (Shielded)" -> "Combat Shield".
    private static string RuleName(string rule)
    {
        int p = rule.IndexOf(" (", StringComparison.Ordinal);
        return p > 0 ? rule[..p] : rule;
    }

    // "Razor Claw" (an upgrade target) matches the weapon "Razor Claws"; "2x Pistols" too.
    private static bool SameName(string have, string target)
    {
        static string N(string s) => s.Trim().TrimEnd('s').ToLowerInvariant();
        string t = target.Length > 3 && char.IsDigit(target[0]) && target[1] == 'x' && target[2] == ' '
            ? target[3..] : target;
        return N(have) == N(t);
    }

    /// <summary>True when a section lets you take several of its options ("Upgrade with any").</summary>
    public static bool IsMultiPick(SqSection s) =>
        s.Label.Contains(" any", StringComparison.OrdinalIgnoreCase);

}
