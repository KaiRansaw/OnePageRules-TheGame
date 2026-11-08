using System.Text.Json;
using System.Text.Json.Serialization;
using FDG.Quest;

namespace FdgRaylib.Quest;
/// <summary>
/// The GF: Star Quest view of <see cref="QuestDataSet"/>, kept because this is what the Hero Builder,
/// the party loader and three fixtures have always called.
///
/// <para>
/// #418 moved the actual dataset into <see cref="QuestDataSet"/>, one instance per Quest system, so
/// that AoF: Quest could have a roster of its own. Everything here now forwards to
/// <c>QuestDataSet.For(EGameSystem.GFStarQuest)</c>. <b>New code should take a
/// <see cref="QuestDataSet"/> (or the system it is drawing) rather than call this</b>, or it silently
/// means "Star Quest" in a screen the player opened for AoF: Quest - which is exactly the bug class
/// #417 and #418 exist to close.
/// </para>
/// </summary>
public static class StarQuestData
{
    /// <summary>The dataset this view forwards to.</summary>
    public static QuestDataSet Set => QuestDataSet.For(FDG.EGameSystem.GFStarQuest);

    public static string? LoadError => Set.LoadError;

    public static IReadOnlyList<SqFaction> Factions => Set.Factions;

    public static SqRules Rules => Set.Rules;

    /// <summary>The 12 playable classes (not the "Default Skills" bucket).</summary>
    public static IEnumerable<SqClass> Classes => Set.Classes;

    /// <summary>The Army Forge race groups, in Army Forge's order (see <see cref="QuestDataSet.FactionGroups"/>).</summary>
    public static IReadOnlyList<string> FactionGroups => Set.FactionGroups;

    /// <summary>Which Army Forge race group a faction belongs to.</summary>
    public static string GroupOf(SqFaction faction) => Set.GroupOf(faction);

    public static string Folder => Set.Folder;

    public static SqClass? ClassById(string id) => Set.ClassById(id);

    /// <summary>The class's feature: its one "no test" skill at tier 0.</summary>
    public static SqSkill? ClassFeat(SqClass cls) => Set.ClassFeat(cls);

    /// <summary>The class's skills of one tier (feature excluded).</summary>
    public static IEnumerable<SqSkill> ClassSkills(SqClass cls, int tier) => Set.ClassSkills(cls, tier);

    /// <summary>Skills every class may learn, of one tier.</summary>
    public static IEnumerable<SqSkill> DefaultSkills(int tier) => Set.DefaultSkills(tier);

    public static SqSkill? SkillByName(string name) => Set.SkillByName(name);

    /// <summary>"Str", "Dex", "Wil", or "" for a class feature.</summary>
    public static string SkillStat(SqSkill s) => QuestDataSet.SkillStat(s);

    /// <summary>A fresh level-1 hero: the army-book hero with the Star Quest base stats, no class
    /// yet.</summary>
    public static QuestHero NewHero(SqFaction faction, SqHero hero) => Set.NewHero(faction, hero);

    /// <summary>Gives the hero a class: the base stats, then the class bonuses, its feature, and no
    /// skills picked yet. Keeps the weapons and upgrades already chosen.</summary>
    public static void ApplyClass(QuestHero h, SqClass cls) => Set.ApplyClass(h, cls);

    /// <summary>Rebuilds the hero's weapons and rules from its base hero plus the chosen upgrade
    /// options (by label), in order.</summary>
    public static void ApplyUpgrades(QuestHero h, SqFaction faction, SqHero baseHero,
        IReadOnlyList<(SqSection Section, SqOption Option)> picks) =>
        Set.ApplyUpgrades(h, faction, baseHero, picks);

    /// <summary>The weapons, rules/gear and upgrade points the base hero ends up with after these
    /// picks.</summary>
    public static (List<QuestWeapon> Weapons, List<string> Rules, int Cost) Loadout(SqHero baseHero,
        IReadOnlyList<(SqSection Section, SqOption Option)> picks) =>
        QuestDataSet.Loadout(baseHero, picks);

    /// <summary>A "Replace X" or "Take one X attachment" section only applies once the hero has every
    /// X (a weapon or piece of gear it started with or took in an earlier upgrade).</summary>
    public static bool HasTargets(SqSection section, List<QuestWeapon> weapons, List<string> rules) =>
        QuestDataSet.HasTargets(section, weapons, rules);

    /// <summary>"Combat Shield (Shielded)" (a name, a space, then its rules) is gear; "Tough(9)" is a
    /// rule.</summary>
    public static bool IsGear(string s) => QuestDataSet.IsGear(s);

    /// <summary>True when a section lets you take several of its options ("Upgrade with any").</summary>
    public static bool IsMultiPick(SqSection s) => QuestDataSet.IsMultiPick(s);
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
    /// <summary>Army Forge's raceGroup for the faction ("Aliens", "Humans"...) - what the Hero Builder's
    /// faction picker groups by (<see cref="QuestDataSet.GroupOf"/>).</summary>
    public string? Race { get; set; }
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
