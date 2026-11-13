using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Forge;
using FdgRaylib.SaveLoad;
using IOPath = System.IO.Path;

namespace FdgRaylib.Quest;

/// <summary>
/// The army campaign (Campaign Rules v3.6.0 - GF, GF: Firefight, AoF, AoF: Skirmish) without the drawing: the list,
/// the Camp's bookkeeping (both armies - you run the rival's books too, house rule B17), recruiting, the New Campaign
/// wizard and the journal. Ported from the Raylib StrikeTeamScreen for the Godot screens (Kai's Army Campaign mockup,
/// approved 2026-11-12, every item kept). The rules themselves stay in the engine: <see cref="CampaignRules"/>,
/// <see cref="CampaignEconomy"/>, <see cref="CampaignPreGame"/>, <see cref="CampaignJournal"/>.
/// </summary>
public sealed class ArmyCampaignSession
{
    public EGameSystem System { get; }
    public CampaignNumbers Numbers => CampaignNumbers.For(System);
    private readonly Random _rng;
    public int D6() => _rng.Next(1, 7);

    public sealed record Entry(string Path, BuiltArmyFile Army, EGameSystem System)
    {
        public StrikeTeamInfo Info => Army.StrikeTeam!;
    }

    private List<Entry> _all = new();

    public ArmyCampaignSession(EGameSystem system, Random? rng = null)
    {
        System = system;
        _rng = rng ?? new Random();
        Reload();
    }

    public void Reload()
    {
        ArmyPaths.EnsureGameTypeFolders();
        _all = StrikeTeamFiles.LoadAllSystems().Select(a => new Entry(a.Path, a.Army, a.System)).ToList();
    }

    /// <summary>The player's campaign armies of THIS game (the rival armies are shown inside their campaign).</summary>
    public List<Entry> Campaigns => _all.Where(a => a.Army.StrikeTeam is { IsBot: false } && a.System == System).ToList();

    /// <summary>Ordinary armies of this game a new campaign may start from (#426: each game has its own stats).</summary>
    public List<Entry> Armies => _all.Where(a => a.Army.StrikeTeam == null && a.System == System).ToList();

    /// <summary>A campaign's standing enemy (#428), or null.</summary>
    public Entry? EnemyOf(StrikeTeamInfo info) =>
        info.EnemyArmyFile is { } path ? _all.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)) : null;

    public Entry? Find(string path) => _all.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));

    public static bool Save(string path, BuiltArmyFile army) => StrikeTeamFiles.Save(path, army);

    // ------------------------------------------------------------------ the list

    public CampaignPreGame.CampaignStanding StandingOf(Entry e) => CampaignPreGame.Standing(e.Info, EnemyOf(e.Info)?.Info);

    public static int Waiting(BuiltArmyFile army) => army.Units.Count(u => CampaignRules.Sheet(u)?.Pending.Count > 0);

    /// <summary>A list card's lines: "Iron Wardens vs The Red Brood" and "4 / 7 VP - 3 missions - choices waiting".</summary>
    public (string Title, string Line) ListRow(Entry e)
    {
        StrikeTeamInfo info = e.Info;
        var standing = StandingOf(e);
        string result = standing.Outcome switch
        {
            ECampaignOutcome.YouWon => "  WON",
            ECampaignOutcome.RivalWon => "  LOST",
            ECampaignOutcome.Draw => "  DRAWN",
            _ => "",
        };
        string title = (EnemyOf(info)?.Info is { } foe ? $"{info.Name} vs {foe.Name}" : info.Name) + result;
        string vp = standing.HasTarget ? $"{info.Vp} / {standing.VpTarget} VP" : $"Endless - {info.Vp} VP";
        string line = $"{vp} - {Plural(info.Matches, "mission")}"
                      + (standing.IsOver ? " - finished" : Waiting(e.Army) > 0 ? " - choices waiting" : "");
        return (title, line);
    }

    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    /// <summary>Who is sitting out injured, as one line.</summary>
    public static string SittingOut(BuiltArmyFile army)
    {
        var resting = army.Units.Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u))).Where(p => p.Sheet is { MissionsOut: > 0 }).ToList();
        return resting.Count == 0 ? "Nobody" : string.Join(", ", resting.Select(r => $"{r.Unit.Name} ({r.Sheet!.MissionsOut} more)"));
    }

    /// <summary>The files a campaign is made of: both armies and their "this mission only" copies.</summary>
    public static IReadOnlyList<string> CampaignFiles(string path, StrikeTeamInfo info)
    {
        var files = new List<string> { path, IOPath.ChangeExtension(path, ".fdgmission") };
        if (info.EnemyArmyFile is { Length: > 0 } enemy)
        {
            files.Add(enemy);
            files.Add(IOPath.ChangeExtension(enemy, ".fdgmission"));
        }
        return files;
    }

    /// <summary>Deletes a campaign: BOTH armies (decision 21). Null, or why not everything went.</summary>
    public string? Delete(Entry e)
    {
        bool failed = false;
        foreach (string file in CampaignFiles(e.Path, e.Info))
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        }
        Reload();
        return failed ? "Couldn't delete every file of that campaign." : null;
    }

    // ------------------------------------------------------------------ the camp

    /// <summary>A roster card's tag: "pick a trait", "recovering (n)", or null.</summary>
    public static string? RosterTag(StrikeUnit? sheet) =>
        sheet == null ? null
        : sheet.Pending.Count > 0 ? "pick a trait"
        : sheet.MissionsOut > 0 ? $"recovering ({sheet.MissionsOut})"
        : null;

    /// <summary>What a waiting choice asks, in a line, and its heading.</summary>
    public static (string Heading, string Ask) ChoiceText(string pending) => pending switch
    {
        CampaignTables.Specialist => ("pick a trait", "Specialist: +1 to hit in melee, or when shooting?"),
        CampaignTables.FastLearner => ("pick a trait", "Fast Learner: pick any other trait."),
        CampaignTables.LuckyBreak => ("pick an injury", "Lucky Break: pick any other injury."),
        CampaignTables.LearnedLesson => ("pick a talent", "Learned Lesson: pick any other talent."),
        _ => ("pick a skill set", "Pick a skill set (its first skill is rolled straight away)."),
    };

    /// <summary>Makes a waiting choice. Returns the history line.</summary>
    public string Choose(Entry e, UnitFileEntry unit, string pending, string option)
    {
        string line = CampaignRules.Choose(unit, pending, option, D6);
        e.Info.History.Add(line);
        Save(e.Path, e.Army);
        return line;
    }

    /// <summary>Renames a unit. Null, or why not.</summary>
    public static string? Rename(Entry e, UnitFileEntry unit, string name)
    {
        if (CampaignRules.Rename(e.Army, unit, name, e.Army.Book) is { } why) return why;
        return Save(e.Path, e.Army) ? null : "Couldn't save the campaign army file.";
    }

    /// <summary>One upgrade option as the Camp shows it: buy / sell / drop at campaign prices from the treasury.</summary>
    public sealed record UpgradeLine(UpgradeSection Section, UpgradeOption Option, bool Owned, int Price, bool Affordable, string Label);

    /// <summary>The unit's upgrades at campaign prices (null with the reason when it has none).</summary>
    public static (List<UpgradeLine>? Lines, string? Why) Upgrades(Entry e, UnitFileEntry unit)
    {
        BuiltArmyFile army = e.Army;
        if (army.Book == null) return (null, "No book attached to this army.");
        if (CampaignEconomy.Loadout(unit) is not { } loadout) return (null, "This unit wasn't built from a book, so its upgrades can't be changed.");
        RosterUnit? roster = army.Book.Units.FirstOrDefault(r => r.Id == loadout.RosterUnitId);
        if (roster == null) return (null, "The book no longer has this unit.");
        if (roster.Sections.Count == 0) return (null, "Nothing to buy for this unit.");
        int treasury = e.Info.BankedPoints;
        var lines = new List<UpgradeLine>();
        foreach (UpgradeSection section in roster.Sections)
        {
            bool nothingToReplace = section.Variant == UpgradeVariant.Replace && ForgeRules.AvailableExcludingSection(army.Book, loadout, section) == 0;
            foreach (UpgradeOption option in section.Options)
            {
                bool owned = loadout.Choices.Any(c => c.SectionId == section.Id && c.OptionId == option.Id);
                if (nothingToReplace && !owned) continue;
                int price = CampaignEconomy.PriceOfChange(army.Book, unit, Toggled(loadout, section.Id, option.Id, !owned));
                string label = owned
                    ? (price > 0 ? $"Drop - costs {price}" : price < 0 ? $"Sell +{-price}" : "Drop (free)")
                    : (price > 0 ? $"Buy {price}" : price < 0 ? $"Take +{-price}" : "Take (free)");
                lines.Add(new UpgradeLine(section, option, owned, price, owned || price <= treasury, label));
            }
        }
        return (lines, null);
    }

    /// <summary>Buys, sells or drops one upgrade. Returns (ok, message).</summary>
    public static (bool Ok, string Message) ChangeUpgrade(Entry e, UnitFileEntry unit, UpgradeLine line)
    {
        if (e.Army.Book == null || CampaignEconomy.Loadout(unit) is not { } loadout) return (false, "This unit can't be changed.");
        CampaignPurchase result = CampaignEconomy.ChangeUpgrades(e.Army, unit, e.Army.Book, Toggled(loadout, line.Section.Id, line.Option.Id, !line.Owned));
        if (result.Ok) { e.Info.History.Add(result.Message); Save(e.Path, e.Army); }
        return (result.Ok, result.Message);
    }

    private static BuilderUnit Toggled(BuilderUnit from, string sectionId, string optionId, bool on)
    {
        var copy = new BuilderUnit
        {
            RosterUnitId = from.RosterUnitId, ModelCount = from.ModelCount, Id = from.Id,
            JoinsUnitId = from.JoinsUnitId, CombinedWithId = from.CombinedWithId,
            Choices = from.Choices.Where(c => c.SectionId != sectionId)
                .Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count }).ToList(),
        };
        if (on) copy.Choices.Add(new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 });
        return copy;
    }

    /// <summary>Disbands a unit: its XP and upgrades are gone. Returns the message.</summary>
    public static string Disband(Entry e, UnitFileEntry unit)
    {
        CampaignPurchase result = CampaignEconomy.Disband(e.Army, unit);
        e.Info.History.Add(result.Message);
        Save(e.Path, e.Army);
        return result.Message;
    }

    /// <summary>Rolls the next mission's special objective (book, step 1). Returns it.</summary>
    public string RollSpecialObjective(Entry e)
    {
        bool caster = e.Army.Units.Any(u => u.SpecialRules.Any(r => r.PrintableName.Contains("Caster", StringComparison.OrdinalIgnoreCase)));
        e.Info.SpecialObjective = CampaignPreGame.RollSpecialObjective(new CampaignEntrant(e.Info.Name, e.Army.TotalPoints, caster), D6);
        Save(e.Path, e.Army);
        return e.Info.SpecialObjective;
    }

    /// <summary>The other campaign armies of this game (they set the mission cap, #426).</summary>
    public List<CampaignEntrant> OthersThan(Entry e) =>
        Campaigns.Where(t => t.Path != e.Path && t.Info.GameSystem == e.Info.GameSystem)
            .Select(t => new CampaignEntrant(t.Info.Name, t.Army.TotalPoints, false)).ToList();

    // ------------------------------------------------------------------ recruiting

    public static bool IsHeroRoster(RosterUnit roster) =>
        roster.Rules.Any(r => r is SpecialRuleEntry_Core c && FDG.Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(c.Name));

    /// <summary>
    /// Why another of <paramref name="roster"/> can't go in the cart, or null (Campaign Rules v3.6.0, 6. Recruit Units:
    /// one new Hero after each mission, at most 3 Heroes; regular units without limit; the treasury pays).
    /// </summary>
    public static string? CannotAddToCart(BuiltArmyFile army, RosterUnit roster, int unitPrice, IReadOnlyList<bool> cartIsHero, int cartTotal)
    {
        bool hero = IsHeroRoster(roster);
        if (hero && CampaignEconomy.HeroRecruitBlockedReason(army) is { } heroReason) return heroReason;
        if (hero && cartIsHero.Any(h => h))
            return army.StrikeTeam is { Matches: 0 } ? "Before the first mission an army may have only one Hero." : "Only one new Hero may be recruited after each mission.";
        if (hero && CampaignEconomy.HeroCount(army) + cartIsHero.Count(h => h) >= CampaignNumbers.MaxHeroes)
            return $"An army may only have {CampaignNumbers.MaxHeroes} Heroes at once.";
        int left = (army.StrikeTeam?.BankedPoints ?? 0) - cartTotal;
        return unitPrice > left ? $"Costs {unitPrice} pts - the treasury has {left} pts left after the cart." : null;
    }

    /// <summary>The recruit cart for one army: roster ids, and which one (if any) starts at 5 XP.</summary>
    public sealed class Cart
    {
        public List<string> Ids { get; } = new();
        public int Veteran { get; set; } = -1;

        public void Remove(int index)
        {
            Ids.RemoveAt(index);
            if (Veteran == index) Veteran = -1;
            else if (Veteran > index) Veteran--;
        }

        public void Clear() { Ids.Clear(); Veteran = -1; }
    }

    public int CartTotal(Entry e, BookFile book, Cart cart, IReadOnlyDictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)> cards) =>
        cart.Ids.Sum(id => cards.TryGetValue(id, out var c) ? c.Unit.PointCost : book.Units.FirstOrDefault(r => r.Id == id)?.BasePointCost ?? 0)
        + (cart.Veteran >= 0 && cart.Veteran < cart.Ids.Count && !CampaignEconomy.VeteranRecruitUsed(e.Army) ? Numbers.VeteranRecruitCost : 0);

    public string? WhyNot(Entry e, BookFile book, RosterUnit roster, Cart cart, IReadOnlyDictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)> cards) =>
        CannotAddToCart(e.Army, roster, cards.TryGetValue(roster.Id, out var c) ? c.Unit.PointCost : roster.BasePointCost,
            cart.Ids.Select(id => book.Units.FirstOrDefault(r => r.Id == id) is { } r && IsHeroRoster(r)).ToList(),
            CartTotal(e, book, cart, cards));

    /// <summary>Buys everything in the cart. Returns the history lines.</summary>
    public List<string> ConfirmRecruits(Entry e, BookFile book, Cart cart)
    {
        var lines = new List<string>();
        List<string> ids = cart.Ids.ToList();
        for (int i = 0; i < ids.Count; i++)
        {
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == ids[i]);
            if (roster == null) continue;
            var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
            lines.Add(CampaignEconomy.Recruit(e.Army, book, wanted, Numbers, veteran: i == cart.Veteran, d6: D6).Message);
        }
        e.Info.History.AddRange(lines);
        Save(e.Path, e.Army);
        cart.Clear();
        Reload();
        return lines;
    }

    // ------------------------------------------------------------------ journal

    public string? ExportJournal(Entry e, out string? error)
    {
        error = null;
        string text = CampaignJournal.Export(e.Info, EnemyOf(e.Info)?.Info, GameSystemInfo.DisplayName(e.Info.GameSystem));
        string file = IOPath.Combine(IOPath.GetDirectoryName(e.Path) ?? "", IOPath.GetFileNameWithoutExtension(e.Path) + " - Journal.txt");
        try { File.WriteAllText(file, text.Replace("\n", Environment.NewLine)); return file; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; return null; }
    }

    // ------------------------------------------------------------------ a new campaign (the 4-tab wizard)

    public enum EPlanning { AsIGo, Ahead, FromSaved }

    /// <summary>Everything the New Campaign tabs hold until START CAMPAIGN - nothing touches the disk before.</summary>
    public sealed class Draft
    {
        public ECampaignType Type { get; set; } = ECampaignType.PointLimited;
        public int VpTarget { get; set; } = 7;
        public bool NoPermadeath { get; set; }
        public string Story { get; set; } = "";
        public string Name { get; set; } = "";
        public BuiltArmyFile? Yours { get; set; }
        public string? YoursSource { get; set; }
        public string EnemyName { get; set; } = "";
        public BuiltArmyFile? Enemy { get; set; }
        public EPlanning Planning { get; set; } = EPlanning.AsIGo;
        public List<CampaignMissionPlan> Plans { get; } = new();
        public string? ScriptPath { get; set; }
        public string Premise { get; set; } = "";
    }

    public string? YourNameProblem(Draft d)
    {
        string name = d.Name.Trim();
        if (name.Length == 0) return "Name your army.";
        if (Campaigns.Any(t => string.Equals(t.Info.Name, name, StringComparison.OrdinalIgnoreCase))) return "A campaign army with that name exists.";
        return null;
    }

    public static string? EnemyNameProblem(Draft d)
    {
        string name = d.EnemyName.Trim();
        if (name.Length == 0) return "Name the enemy army.";
        if (string.Equals(name, d.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return "Give the enemy a different name from yours.";
        return null;
    }

    public static string? MissionsProblem(Draft d) => d.Planning switch
    {
        EPlanning.Ahead when d.Plans.Count == 0 => "Missions: add at least one, or plan each as you go.",
        EPlanning.FromSaved when d.ScriptPath == null => "Missions: pick a saved campaign.",
        _ => null,
    };

    /// <summary>What still stands between the wizard and START CAMPAIGN, or null.</summary>
    public string? NewProblem(Draft d)
    {
        if (d.Yours == null) return "Your army: build it in the Army Forge or use one you have.";
        if (YourNameProblem(d) is { } a) return "Your army: " + a;
        if (d.Enemy == null) return "The enemy: build it or let the game pick one.";
        if (EnemyNameProblem(d) is { } b) return "The enemy: " + b;
        return MissionsProblem(d);
    }

    public bool TabDone(Draft d, int tab) => tab switch
    {
        0 => true,
        1 => d.Yours != null && YourNameProblem(d) == null,
        2 => d.Enemy != null && EnemyNameProblem(d) == null,
        _ => MissionsProblem(d) == null,
    };

    /// <summary>Why a saved army can't start this campaign, or null.</summary>
    public string? CannotStart(BuiltArmyFile army) => CampaignRules.CannotStart(army, Numbers);

    /// <summary>Tab 2: start from an army already saved (a fresh copy - the original stays). Null, or why not.</summary>
    public string? UseExisting(Draft d, Entry source)
    {
        if (CannotStart(source.Army) is { } why) return why;
        if (StrikeTeamFiles.Load(source.Path) is not { } copy) return "Couldn't read that army file.";
        d.Yours = copy;
        d.YoursSource = source.Path;
        if (d.Name.Trim().Length == 0) d.Name = copy.Name;
        return null;
    }

    public List<Entry> EnemyCandidates(Draft d) =>
        Armies.Where(a => a.Path != d.YoursSource && CannotStart(a.Army) == null).ToList();

    /// <summary>Tab 3: "Pick one for me" - a saved army of this game at random, never the player's own. Null, or why not.</summary>
    public string? PickEnemyForMe(Draft d)
    {
        var candidates = EnemyCandidates(d);
        if (candidates.Count == 0) return "No other army in the folder fits the treasury - build the enemy instead.";
        Entry pick = candidates[_rng.Next(candidates.Count)];
        if (StrikeTeamFiles.Load(pick.Path) is not { } copy) return "Couldn't read that army file.";
        d.Enemy = copy;
        d.EnemyName = string.Equals(copy.Name, d.Name.Trim(), StringComparison.OrdinalIgnoreCase) ? copy.Name + " (rival)" : copy.Name;
        return null;
    }

    public int RollVpTarget() => CampaignPreGame.RollCampaignLength(D6);

    // Mission planning (tab 4)

    public void AddPlan(Draft d) => d.Plans.Add(CampaignMissionGenerator.Generate(d.Plans.Count + 1, System, D6));

    public void RerollPlan(Draft d, int index)
    {
        CampaignMissionPlan old = d.Plans[index];
        CampaignMissionPlan fresh = CampaignMissionGenerator.Generate(old.Mission, System, D6);
        fresh.Notes = old.Notes;
        d.Plans[index] = fresh;
    }

    public static void RemovePlan(Draft d, int index)
    {
        d.Plans.RemoveAt(index);
        for (int i = 0; i < d.Plans.Count; i++) d.Plans[i].Mission = i + 1;
    }

    public IReadOnlyList<(string Path, CampaignScript Script)> SavedScripts() => CampaignScriptPaths.List(System);

    public static void LoadScript(Draft d, string path, CampaignScript script)
    {
        d.ScriptPath = path;
        d.Plans.Clear();
        foreach (CampaignMissionPlan plan in script.Missions.OrderBy(p => p.Mission)) d.Plans.Add(plan.Clone());
        for (int i = 0; i < d.Plans.Count; i++) d.Plans[i].Mission = i + 1;
        d.Premise = script.Premise;
    }

    public string? SavePlansAsScript(Draft d) => CampaignScriptPaths.Save(new CampaignScript
    {
        Name = d.Name.Trim().Length > 0 ? d.Name.Trim() + " plan" : "Campaign plan",
        GameSystem = System,
        Premise = CampaignJournal.Clean(d.Story),
        Missions = d.Plans.Select(p => p.Clone()).ToList(),
    });

    /// <summary>
    /// START CAMPAIGN: writes both campaign armies, the enemy first and yours last (carrying the link), so a failure part
    /// way never leaves your campaign pointing at a missing enemy. Returns your army's path, or null with the reason.
    /// </summary>
    public string? Finish(Draft d, out string? error)
    {
        error = NewProblem(d);
        if (error != null || d.Yours is not { } yours || d.Enemy is not { } enemy) return null;
        CampaignNumbers n = Numbers;
        string name = d.Name.Trim(), enemyName = d.EnemyName.Trim();

        string enemyPath = StrikeTeamFiles.NewPath(System, enemyName);
        CampaignRules.Start(enemy, enemyName, enemyPath, n, System, d.Type, d.NoPermadeath, isBot: true, vpTarget: d.VpTarget);
        enemy.Selections = null;
        if (!StrikeTeamFiles.Save(enemyPath, enemy)) { error = "Couldn't save the enemy army file."; return null; }

        string path = StrikeTeamFiles.NewPath(System, name);
        CampaignRules.Start(yours, name, path, n, System, d.Type, d.NoPermadeath, vpTarget: d.VpTarget);
        yours.Selections = null;
        yours.StrikeTeam!.EnemyArmyFile = enemyPath;
        CampaignJournal.SetCampaignNote(yours.StrikeTeam, d.Story);
        if (d.Planning != EPlanning.AsIGo && d.Plans.Count > 0)
            new CampaignScript { Name = name, GameSystem = System, Premise = d.Premise, Missions = d.Plans.Select(p => p.Clone()).ToList() }.ApplyTo(yours.StrikeTeam);
        if (!StrikeTeamFiles.Save(path, yours))
        {
            try { File.Delete(enemyPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            error = "Couldn't save the campaign army file.";
            return null;
        }
        Reload();
        return path;
    }
}
