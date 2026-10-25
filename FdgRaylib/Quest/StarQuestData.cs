using System.Text.Json;
using System.Text.Json.Serialization;
using FDG.Quest;

namespace FdgRaylib.Quest;

/// <summary>
/// The Star Quest data pulled from Army Forge (game system 9), shipped in Assets/StarQuest:
/// <list type="bullet">
/// <item><c>StarQuestHeroes.json</c> - every official faction's heroes, their weapons and upgrade options.</item>
/// <item><c>StarQuestRules.json</c> - the classes (stat bonuses), skills, shop items, conditions, injuries.</item>
/// </list>
/// Loaded once, on first use. If a file is missing or broken, <see cref="LoadError"/> says why and the lists
/// are empty.
/// </summary>
public static class StarQuestData
{
    private static bool _loaded;
    private static List<SqFaction> _factions = new();
    private static SqRules _rules = new();

    public static string? LoadError { get; private set; }

    public static IReadOnlyList<SqFaction> Factions { get { Load(); return _factions; } }
    public static SqRules Rules { get { Load(); return _rules; } }

    /// <summary>The 12 playable classes (not the "Default Skills" bucket).</summary>
    public static IEnumerable<SqClass> Classes => Rules.Classes.Where(c => c.Name != "Default Skills");

    /// <summary>The three groups the faction list is folded into, in the order they are shown.</summary>
    public static readonly string[] FactionGroups = { "Humanity", "Chaos", "Xenos", "Other" };

    // Which group each of the 53 Star Quest factions belongs to. Anything not listed (a new faction, a
    // renamed one) falls into "Other" rather than disappearing from the picker.
    private static readonly Dictionary<string, string> GroupByFaction = new(StringComparer.OrdinalIgnoreCase)
    {
        // Humanity: the Empire's armies, its brotherhood chapters, and the human underworld.
        ["Badland Nomads"] = "Humanity",
        ["Battle Brothers"] = "Humanity",
        ["Berserker Clans"] = "Humanity",
        ["Blessed Sisters"] = "Humanity",
        ["Blood Brothers"] = "Humanity",
        ["Blood Prime Brothers"] = "Humanity",
        ["Brute Coalitions"] = "Humanity",
        ["City Runners"] = "Humanity",
        ["Custodian Brothers"] = "Humanity",
        ["DAO Union"] = "Humanity",
        ["Human Defense Force"] = "Humanity",
        ["Human Inquisition"] = "Humanity",
        ["Knight Brothers"] = "Humanity",
        ["Knight Prime Brothers"] = "Humanity",
        ["Machine Cults"] = "Humanity",
        ["Mega-Corps"] = "Humanity",
        ["Mercenaries"] = "Humanity",
        ["Prime Brothers"] = "Humanity",
        ["Psycho Cults"] = "Humanity",
        ["Rebel Guerrillas"] = "Humanity",
        ["Security Forces"] = "Humanity",
        ["Shadow Leagues"] = "Humanity",
        ["Watch Brothers"] = "Humanity",
        ["Watch Prime Brothers"] = "Humanity",
        ["Wolf Brothers"] = "Humanity",
        ["Wolf Prime Brothers"] = "Humanity",
        ["Worker Unions"] = "Humanity",

        // Chaos: the wormhole powers, their daemons, and the humans who fell to them.
        ["Change Disciples"] = "Chaos",
        ["Dark Brothers"] = "Chaos",
        ["Dark Prime Brothers"] = "Chaos",
        ["Havoc Brothers"] = "Chaos",
        ["Infected Colonies"] = "Chaos",
        ["Lust Disciples"] = "Chaos",
        ["Plague Disciples"] = "Chaos",
        ["War Disciples"] = "Chaos",
        ["Wormhole Daemons of Change"] = "Chaos",
        ["Wormhole Daemons of Lust"] = "Chaos",
        ["Wormhole Daemons of Plague"] = "Chaos",
        ["Wormhole Daemons of War"] = "Chaos",

        // Xenos: everything that is neither human nor of the wormhole.
        ["Alien Hives"] = "Xenos",
        ["Dark Elf Raiders"] = "Xenos",
        ["Dwarf Guilds"] = "Xenos",
        ["Elven Jesters"] = "Xenos",
        ["Eternal Dynasty"] = "Xenos",
        ["Goblin Reclaimers"] = "Xenos",
        ["High Elf Fleets"] = "Xenos",
        ["Jackals"] = "Xenos",
        ["Orc Marauders"] = "Xenos",
        ["Ratmen Clans"] = "Xenos",
        ["Robot Legions"] = "Xenos",
        ["Saurian Starhost"] = "Xenos",
        ["Shortling Federations"] = "Xenos",
        ["Soul-Snatcher Cults"] = "Xenos",
    };

    /// <summary>Which of <see cref="FactionGroups"/> a faction belongs to.</summary>
    public static string GroupOf(SqFaction faction) =>
        GroupByFaction.TryGetValue(faction.Faction, out string? group) ? group : "Other";

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "StarQuest");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var heroes = JsonSerializer.Deserialize<SqHeroFile>(File.ReadAllText(Path.Combine(Folder, "StarQuestHeroes.json")), Json);
            _factions = heroes?.Factions?.Where(f => f.Heroes.Count > 0).OrderBy(f => f.Faction).ToList() ?? new();
            _rules = JsonSerializer.Deserialize<SqRules>(File.ReadAllText(Path.Combine(Folder, "StarQuestRules.json")), Json) ?? new();
        }
        catch (Exception ex)
        {
            LoadError = $"Couldn't read the Star Quest data in {Folder}: {ex.Message}";
            _factions = new();
            _rules = new();
        }
    }

    public static SqClass? ClassById(string id) => Rules.Classes.FirstOrDefault(c => c.Id == id);

    /// <summary>The class's feature: its one "no test" skill at tier 0.</summary>
    public static SqSkill? ClassFeat(SqClass cls) =>
        Rules.Skills.FirstOrDefault(s => s.ClassId == cls.Id && s.Type == 3);

    /// <summary>The class's skills of one tier (feature excluded).</summary>
    public static IEnumerable<SqSkill> ClassSkills(SqClass cls, int tier) =>
        Rules.Skills.Where(s => s.ClassId == cls.Id && s.Type != 3 && s.Tier == tier);

    /// <summary>Skills every class may learn, of one tier.</summary>
    public static IEnumerable<SqSkill> DefaultSkills(int tier) =>
        Rules.Skills.Where(s => (s.ClassId == null || s.ClassId == "core") && s.Tier == tier);

    public static SqSkill? SkillByName(string name) => Rules.Skills.FirstOrDefault(s => s.Name == name);

    /// <summary>"Str", "Dex", "Wil", or "" for a class feature.</summary>
    public static string SkillStat(SqSkill s) => s.Type switch { 0 => "Str", 1 => "Dex", 2 => "Wil", _ => "" };

    /// <summary>A fresh level-1 hero: the army-book hero with the Star Quest base stats, no class yet.</summary>
    public static QuestHero NewHero(SqFaction faction, SqHero hero)
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
        };
        return h;
    }

    /// <summary>Gives the hero a class: the base stats, then the class bonuses, its feature, and no skills
    /// picked yet. Keeps the weapons and upgrades already chosen.</summary>
    public static void ApplyClass(QuestHero h, SqClass cls)
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
    /// Rebuilds the hero's weapons and rules from its base hero plus the chosen upgrade options (by label),
    /// in order. "Replace X" swaps the named weapon out; "Upgrade with..." adds; "Take ... attachment" adds.
    /// </summary>
    public static void ApplyUpgrades(QuestHero h, SqFaction faction, SqHero baseHero, IReadOnlyList<(SqSection Section, SqOption Option)> picks)
    {
        var (weapons, rules, cost) = Loadout(baseHero, picks);
        h.Weapons = weapons;
        h.Rules = rules.Where(r => !IsGear(r)).ToList();
        h.Gear = rules.Where(IsGear).ToList();
        if (h.Caster > 0) h.Rules.Add($"Caster({h.Caster})");
        h.Upgrades = picks.Select(p => p.Option.Label).ToList();
        h.UpgradeCost = cost;
    }

    /// <summary>The weapons, rules/gear and upgrade points the base hero ends up with after these picks.</summary>
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

    /// <summary>"Combat Shield (Shielded)" (a name, a space, then its rules) is gear; "Tough(9)" is a rule.</summary>
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
        string t = target.Length > 3 && char.IsDigit(target[0]) && target[1] == 'x' && target[2] == ' ' ? target[3..] : target;
        return N(have) == N(t);
    }

    /// <summary>True when a section lets you take several of its options ("Upgrade with any").</summary>
    public static bool IsMultiPick(SqSection s) =>
        s.Label.Contains(" any", StringComparison.OrdinalIgnoreCase);
}

// ── File shapes (keys as written by the Army Forge pull) ──────────────────────────────────────────────

public sealed class SqHeroFile
{
    public string Source { get; set; } = "";
    public List<SqFaction> Factions { get; set; } = new();
}

public sealed class SqFaction
{
    public string Uid { get; set; } = "";
    public string Faction { get; set; } = "";
    public string? Version { get; set; }
    public List<SqHero> Heroes { get; set; } = new();
    public List<SqPackage> Packages { get; set; } = new();
    public List<SqNamedText> Rules { get; set; } = new();
    public List<SqSpell> Spells { get; set; } = new();

    public SqPackage? Package(string uid) => Packages.FirstOrDefault(p => p.Uid == uid);
}

public sealed class SqHero
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Cost { get; set; }
    public int Q { get; set; }
    public int D { get; set; }
    public int T { get; set; }
    public List<string> Rules { get; set; } = new();
    public List<SqWeapon> Weapons { get; set; } = new();
    public List<string> Upgrades { get; set; } = new();
    /// <summary>Gear the hero starts with, e.g. "Combat Shield (Shielded)".</summary>
    public List<string> Gear { get; set; } = new();
}

public sealed class SqWeapon
{
    public string N { get; set; } = "";
    public int R { get; set; }
    public int A { get; set; }
    public int C { get; set; } = 1;
    public List<string> S { get; set; } = new();

    public QuestWeapon ToQuest() => new() { Name = N, Range = R, Attacks = A, Count = Math.Max(1, C), Rules = S.ToList() };
}

public sealed class SqPackage
{
    public string Uid { get; set; } = "";
    public List<SqSection> Sections { get; set; } = new();
}

public sealed class SqSection
{
    public string Label { get; set; } = "";
    public string? Variant { get; set; }
    public List<string>? Targets { get; set; }
    public List<SqOption> Opts { get; set; } = new();
}

public sealed class SqOption
{
    public string Label { get; set; } = "";
    public int? Cost { get; set; }
    public List<SqGain> Gains { get; set; } = new();
}

public sealed class SqGain
{
    public SqWeapon? W { get; set; }
    public string? Rule { get; set; }
}

public sealed class SqNamedText
{
    public string N { get; set; } = "";
    public string? D { get; set; }
}

public sealed class SqSpell
{
    public string N { get; set; } = "";
    public int? T { get; set; }
    public string? D { get; set; }
}

public sealed class SqRules
{
    public SqBaseHero BaseHero { get; set; } = new();
    public List<SqClass> Classes { get; set; } = new();
    public List<SqSkill> Skills { get; set; } = new();
    public List<SqItem> Consumables { get; set; } = new();
    public List<SqItem> Injuries { get; set; } = new();
    public List<SqItem> Conditions { get; set; } = new();
}

public sealed class SqBaseHero
{
    public int Qua { get; set; } = 5;
    public int Def { get; set; } = 6;
    public int Pow { get; set; } = 3;
    public int Str { get; set; } = 6;
    public int Dex { get; set; } = 6;
    public int Wil { get; set; } = 6;
    public int Credits { get; set; } = 30;
}

public sealed class SqClass
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BaseName { get; set; }
    public string? Description { get; set; }
    public int Qua { get; set; }
    public int Pow { get; set; }
    public int Str { get; set; }
    public int Dex { get; set; }
    public int Wil { get; set; }
    public int Caster { get; set; }
    public int Skills { get; set; }
}

public sealed class SqSkill
{
    public string Id { get; set; } = "";
    /// <summary>0 Str test, 1 Dex test, 2 Wil test, 3 class feature.</summary>
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public string Rule { get; set; } = "";
    public int Tier { get; set; }
    public string? ClassId { get; set; }
}

public sealed class SqItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? Tier { get; set; }
    public int? Cost { get; set; }
}
