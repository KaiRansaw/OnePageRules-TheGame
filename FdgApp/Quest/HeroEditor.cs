using FDG;
using FDG.Quest;
using IOPath = System.IO.Path;

namespace FdgRaylib.Quest;

/// <summary>
/// The Hero Builder with no drawing in it (ported from the Raylib <c>HeroBuilderScreen</c>'s editor logic, so the
/// Godot screen is a view over this). One Quest game's saved heroes, and a hero being made or edited in the four
/// steps: base hero and name, class, starter skills, gear - plus its 3D look. A hero that has played is locked:
/// rename, new skills and look only, so progress can't be lost by rebuilding it.
/// </summary>
public sealed class HeroEditor
{
    public EGameSystem System { get; }
    public QuestDataSet Data => QuestDataSet.For(System);
    public string GameType => QuestGameTypes.For(System);

    public List<(string Path, QuestHero Hero)> Saved { get; private set; } = new();
    private List<QuestCampaign> _campaigns = new();

    // The hero being built.
    public string? EditingPath { get; private set; }
    public QuestHero? Locked { get; private set; }
    public SqFaction? Faction { get; private set; }
    public SqHero? Base { get; private set; }
    public SqClass? Class { get; private set; }
    public HashSet<string> Skills { get; } = new();
    /// <summary>(package uid, section index) -> chosen option indices.</summary>
    private readonly Dictionary<(string, int), List<int>> _picks = new();
    public string Name { get; set; } = "";
    /// <summary>The picked 3D look ("Faction/Unit/file.glb"), or null for the base hero's first version.</summary>
    public string? Look { get; set; }

    public HeroEditor(EGameSystem system) { System = system; Reload(); }

    /// <summary>When set, only these hero files are listed (a Quest campaign's shop: its party only).</summary>
    public IReadOnlyCollection<string>? OnlyHeroes { get; set; }

    public void Reload()
    {
        Saved = HeroPaths.LoadAll(GameType);
        if (OnlyHeroes is { } only)
        {
            var keep = only.Select(KeyOf).ToHashSet();
            Saved = Saved.Where(s => keep.Contains(KeyOf(s.Path)))
                .OrderBy(s => only.Select(KeyOf).ToList().IndexOf(KeyOf(s.Path))).ToList();
        }
        _campaigns = CampaignPaths.LoadAll(GameType).Select(e => e.Campaign).ToList();
    }

    private static string KeyOf(string path)
    {
        try { return IOPath.GetFullPath(path).ToUpperInvariant(); }
        catch (Exception) { return path.ToUpperInvariant(); }
    }

    public QuestCampaign? CampaignOf(string heroPath) => CampaignPaths.CampaignOf(GameType, heroPath, _campaigns);

    // ---------------------------------------------------------------- start

    public void StartNew()
    {
        EditingPath = null;
        Locked = null;
        Faction = null; Base = null; Class = null;
        Skills.Clear(); _picks.Clear();
        Name = ""; Look = null;
    }

    public void StartEdit(int index)
    {
        if (index < 0 || index >= Saved.Count) return;
        StartNew();
        var (path, hero) = Saved[index];
        EditingPath = path;
        Name = hero.Name;
        Look = hero.Look;
        Faction = Data.Factions.FirstOrDefault(f => f.Uid == hero.FactionUid);
        Base = Faction?.Heroes.FirstOrDefault(h => h.Id == hero.BaseHeroId);
        Class = Data.ClassById(hero.ClassId);
        bool played = hero.Xp > 0 || hero.Level > 1 || hero.Missions > 0;
        if (played || Faction == null || Base == null || Class == null)
        {
            Locked = hero.Clone();
            return;
        }
        foreach (string s in hero.Skills) Skills.Add(s);
        // Map the saved upgrade labels back onto the base hero's sections.
        var labels = hero.Upgrades.ToList();
        foreach (var (key, section) in Sections())
            for (int o = 0; o < section.Opts.Count; o++)
            {
                int at = labels.IndexOf(section.Opts[o].Label);
                if (at < 0) continue;
                labels.RemoveAt(at);
                if (!_picks.TryGetValue(key, out var list)) _picks[key] = list = new List<int>();
                list.Add(o);
            }
    }

    // ---------------------------------------------------------------- choices

    public void PickBase(SqFaction faction, SqHero hero)
    {
        if (Locked != null) return;
        if (Base != hero) { _picks.Clear(); Look = null; }
        Faction = faction;
        Base = hero;
    }

    public void PickClass(SqClass cls)
    {
        if (Locked != null || Class == cls) return;
        Class = cls;
        Skills.Clear();
    }

    /// <summary>Ticks or unticks a starter skill; a full set refuses more.</summary>
    public bool ToggleSkill(string name)
    {
        if (Skills.Remove(name)) return true;
        if (Class == null || Skills.Count >= Class.Skills) return false;
        Skills.Add(name);
        return true;
    }

    /// <summary>A played hero's new skill (from a "+1 Skill" level-up).</summary>
    public bool LearnSkill(string name)
    {
        if (Locked == null || Locked.Skills.Contains(name) || Locked.Skills.Count >= Locked.SkillSlots) return false;
        Locked.Skills.Add(name);
        return true;
    }

    public bool UnlearnNewSkill(string name, IReadOnlyCollection<string> original) =>
        Locked != null && !original.Contains(name) && Locked.Skills.Remove(name);

    public IEnumerable<((string Uid, int Index) Key, SqSection Section)> Sections()
    {
        if (Faction == null || Base == null) yield break;
        foreach (string uid in Base.Upgrades)
        {
            SqPackage? p = Faction.Package(uid);
            if (p == null) continue;
            for (int i = 0; i < p.Sections.Count; i++) yield return ((uid, i), p.Sections[i]);
        }
    }

    public bool IsPicked((string, int) key, int option) => _picks.TryGetValue(key, out var l) && l.Contains(option);

    /// <summary>Pick-one sections swap their pick (clicking the picked one clears it); pick-any sections toggle.</summary>
    public void TogglePick((string, int) key, SqSection section, int option)
    {
        if (Locked != null) return;
        if (!_picks.TryGetValue(key, out var chosen)) _picks[key] = chosen = new List<int>();
        if (QuestDataSet.IsMultiPick(section))
        {
            if (!chosen.Remove(option)) chosen.Add(option);
        }
        else if (chosen.Contains(option)) chosen.Clear();
        else { chosen.Clear(); chosen.Add(option); }
        if (chosen.Count == 0) _picks.Remove(key);
    }

    /// <summary>
    /// Every gear section with whether it can be used: a "replace X" needs an X still there after the picks
    /// above it (the Raylib editor's rule). <c>Why</c> says what it needs when it can't.
    /// </summary>
    public List<(((string, int) Key, SqSection Section) Entry, bool Usable, string? Why)> GearSections()
    {
        var result = new List<(((string, int), SqSection), bool, string?)>();
        if (Base == null) return result;
        var sofar = new List<(SqSection, SqOption)>();
        foreach (var entry in Sections())
        {
            var (lw, lr, _) = QuestDataSet.Loadout(Base, sofar);
            bool ok = QuestDataSet.HasTargets(entry.Section, lw, lr);
            string? why = ok ? null : $"Needs {string.Join(" and ", entry.Section.Targets ?? new List<string>())} - an upgrade above swapped it.";
            if (ok && _picks.TryGetValue(entry.Key, out var have))
                foreach (int o in have.OrderBy(i => i))
                    if (o >= 0 && o < entry.Section.Opts.Count) sofar.Add((entry.Section, entry.Section.Opts[o]));
            result.Add((entry, ok, why));
        }
        return result;
    }

    // ---------------------------------------------------------------- the hero

    /// <summary>The hero as it stands with the current choices (null before a base hero is picked).</summary>
    public QuestHero? Compose()
    {
        if (Locked != null)
        {
            var h = Locked.Clone();
            h.Name = Name.Trim();
            h.Look = Look;
            h.SourceFile = null;
            return h;
        }
        if (Faction == null || Base == null) return null;
        QuestHero hero = Data.NewHero(Faction, Base);
        hero.Name = Name.Trim();
        if (Class != null) Data.ApplyClass(hero, Class);
        Data.ApplyUpgrades(hero, Faction, Base, ValidPicks());
        QuestProgression.SetToughRule(hero);
        hero.Skills = Skills.ToList();
        hero.Look = Look;
        return hero;
    }

    private List<(SqSection, SqOption)> ValidPicks()
    {
        var picks = new List<(SqSection, SqOption)>();
        if (Base == null) return picks;
        foreach (var (key, section) in Sections())
        {
            if (!_picks.TryGetValue(key, out var chosen) || chosen.Count == 0) continue;
            var (w, r, _) = QuestDataSet.Loadout(Base, picks);
            if (!QuestDataSet.HasTargets(section, w, r)) continue;
            foreach (int o in chosen.OrderBy(i => i))
                if (o >= 0 && o < section.Opts.Count) picks.Add((section, section.Opts[o]));
        }
        return picks;
    }

    /// <summary>What is still missing before the hero can be saved, in step order, or null.</summary>
    public string? SaveProblem() => SaveProblem(Name, Locked != null, Base != null, Class?.Skills, Skills.Count);

    public static string? SaveProblem(string name, bool locked, bool hasBase, int? classSkills, int skillsPicked)
    {
        if (locked) return name.Trim().Length == 0 ? "Give the hero a name (step 1)." : null;
        if (!hasBase) return "Pick a base hero (step 1).";
        if (name.Trim().Length == 0) return "Give the hero a name (step 1).";
        if (classSkills is not int need) return "Pick a class (step 2).";
        if (skillsPicked < need)
        {
            int more = need - skillsPicked;
            return $"Pick {more} more starter skill{(more == 1 ? "" : "s")} (step 3).";
        }
        return null;
    }

    /// <summary>Writes the hero. Returns null and the saved path, or the reason it couldn't.</summary>
    public (string? Path, string? Error) Save()
    {
        if (SaveProblem() is { } problem) return (null, problem);
        QuestHero? hero = Compose();
        if (hero == null) return (null, "Pick a base hero first.");
        try
        {
            string folder = HeroPaths.FolderFor(GameType);
            Directory.CreateDirectory(folder);
            string path = IOPath.Combine(folder, HeroPaths.FileNameFor(hero.Name));
            bool renamed = EditingPath != null && !string.Equals(IOPath.GetFullPath(path), IOPath.GetFullPath(EditingPath), StringComparison.OrdinalIgnoreCase);
            if ((EditingPath == null || renamed) && File.Exists(path)) return (null, "A hero with that name already exists.");
            hero.SourceFile = path;
            File.WriteAllText(path, hero.ToJson());
            if (renamed)
            {
                File.Delete(EditingPath!);
                RenameInCampaigns(EditingPath!, path);
            }
            EditingPath = path;
            Reload();
            return (path, null);
        }
        catch (Exception ex) { return (null, "Couldn't save: " + ex.Message); }
    }

    private void RenameInCampaigns(string oldPath, string newPath)
    {
        string oldKey = IOPath.GetFullPath(oldPath);
        foreach ((string file, QuestCampaign c) in CampaignPaths.LoadAll(GameType))
        {
            bool changed = false;
            for (int i = 0; i < c.HeroFiles.Count; i++)
            {
                if (!string.Equals(IOPath.GetFullPath(c.HeroFiles[i]), oldKey, StringComparison.OrdinalIgnoreCase)) continue;
                c.HeroFiles[i] = newPath;
                changed = true;
            }
            if (changed) CampaignPaths.Save(file, c);
        }
    }

    public string? Delete(int index)
    {
        if (index < 0 || index >= Saved.Count) return "No hero picked.";
        try { File.Delete(Saved[index].Path); }
        catch (Exception ex) { return "Couldn't delete: " + ex.Message; }
        Reload();
        return null;
    }

    /// <summary>Saves a hero's sheet back to its file (the shop's buys and gifts). False when it couldn't.</summary>
    public bool SaveSheet(string path, QuestHero hero) => CampaignPaths.SaveHero(path, hero);

    // ---------------------------------------------------------------- words

    public string Money => QuestItemSkin.CurrencySuffix(System);
    public string MoneyWord => QuestItemSkin.CurrencyWord(System);

    public static string ClassBonusText(SqClass c)
    {
        var parts = new List<string>();
        if (c.Qua > 0) parts.Add($"+{c.Qua} Qua");
        if (c.Pow > 0) parts.Add($"+{c.Pow} Pow");
        if (c.Str > 0) parts.Add($"+{c.Str} Str");
        if (c.Dex > 0) parts.Add($"+{c.Dex} Dex");
        if (c.Wil > 0) parts.Add($"+{c.Wil} Wil");
        if (c.Caster > 0) parts.Add($"Caster({c.Caster})");
        parts.Add($"{c.Skills} skill{(c.Skills == 1 ? "" : "s")}");
        return string.Join(", ", parts.Take(parts.Count - 1)) + " - " + parts[^1];
    }

    /// <summary>A faction's heroes that match a search: every hero when the faction name matches.</summary>
    public static List<SqHero> Matching(SqFaction faction, string search)
    {
        if (search.Length == 0) return faction.Heroes.ToList();
        if (faction.Faction.Contains(search, StringComparison.OrdinalIgnoreCase)) return faction.Heroes.ToList();
        return faction.Heroes.Where(h => h.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
