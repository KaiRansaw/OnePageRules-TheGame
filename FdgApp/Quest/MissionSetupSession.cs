using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using IOPath = System.IO.Path;

namespace FdgRaylib.Quest;

/// <summary>
/// What an army campaign mission hands the lobby: the two per-mission army files, the plan's settings, and the
/// campaign it belongs to (so the game can be recorded there and the end card can send the player back).
/// The lobby is opened silently and LAUNCH pressed for you (#435: "bypass the Host Server screen").
/// </summary>
public sealed record ArmyCampaignLaunch(
    EGameSystem System,
    string CampaignPath,
    string CampaignName,
    string Title,
    string Line,
    string ArmyFile,
    string? EnemyArmyFile,
    ETerrainPlacementMode TerrainMode,
    string? MapPath,
    EDeploymentStyle Deployment,
    FDG.Missions.EMissionCardGame MissionCards,
    EChallengeBonuses ChallengeBonuses);

/// <summary>
/// Mission Setup (#435, #465) without the drawing: the plan card, both forces, difficulty, the map, and START MISSION.
/// Ported from the Raylib MissionSetupScreen for Godot (Army Campaign mockup E, approved 2026-11-12). Everything with a
/// rule behind it stays in the engine: <see cref="CampaignRules"/>, <see cref="MissionDifficulty"/>,
/// <see cref="CampaignPreGame"/>, <see cref="CampaignBot"/>.
/// </summary>
public sealed class MissionSetupSession
{
    /// <summary>Set by <see cref="Start"/> and consumed by the lobby the campaign opens.</summary>
    public static ArmyCampaignLaunch? Pending;

    public string Path { get; }
    public BuiltArmyFile? Army { get; private set; }
    public string? EnemyPath { get; private set; }
    public BuiltArmyFile? Enemy { get; private set; }
    public CampaignMissionPlan? Plan { get; private set; }
    public ETerrainPlacementMode TerrainMode { get; private set; } = ETerrainPlacementMode.Alternating;
    public string? MapPath { get; private set; }

    private readonly List<CampaignEntrant> _others;
    private readonly Random _rng;
    private int D6() => _rng.Next(1, 7);
    /// <summary>roll(n) is 1..n - the convention <see cref="CampaignBot.PickWhatToBring"/> and the solo AI share.</summary>
    private int Roll(int sides) => _rng.Next(1, Math.Max(2, sides + 1));

    public MissionSetupSession(string path, IEnumerable<CampaignEntrant> others, Random? rng = null)
    {
        Path = path;
        _others = others.ToList();
        _rng = rng ?? new Random();
        Reload();
    }

    public StrikeTeamInfo? Info => Army?.StrikeTeam;
    public StrikeTeamInfo? Foe => Enemy?.StrikeTeam;
    public EGameSystem System => Info?.GameSystem ?? EGameSystem.GrimdarkFuture;
    public CampaignNumbers Numbers => CampaignNumbers.For(System);
    public IReadOnlyList<CampaignEntrant> Others => _others;

    public void Reload()
    {
        Army = StrikeTeamFiles.Load(Path);
        Enemy = null;
        EnemyPath = Army?.StrikeTeam?.EnemyArmyFile;
        if (EnemyPath != null) Enemy = StrikeTeamFiles.Load(EnemyPath);
        if (Info is { } info)
        {
            // #465: the next mission's plan, made now if it has not been (every field can then be changed or re-rolled).
            Plan = CampaignPlans.Next(info);
            if (Plan == null)
            {
                Plan = CampaignMissionGenerator.Generate(info.Matches + 1, info.GameSystem, D6);
                CampaignPlans.Set(info, Plan);
            }
            info.Difficulty = Plan.Difficulty;
            TerrainMode = Plan.TerrainMode;
            MapPath = CampaignScriptPaths.MapFor(Plan.MapName);
            if (TerrainMode == ETerrainPlacementMode.LoadFromFile && MapPath == null) TerrainMode = ETerrainPlacementMode.AutoFromLayout;
            StrikeTeamFiles.Save(Path, Army!);
        }
        // The rival's force is filled from the difficulty the moment the screen opens.
        FillEnemyForce(randomize: false);
    }

    private void SavePlan()
    {
        if (Info is not { } info || Plan == null) return;
        CampaignPlans.Set(info, Plan);
        StrikeTeamFiles.Save(Path, Army!);
    }

    private void SaveArmy() { if (Army != null) StrikeTeamFiles.Save(Path, Army); }
    private void SaveEnemy() { if (EnemyPath != null && Enemy != null) StrikeTeamFiles.Save(EnemyPath, Enemy); }

    // ------------------------------------------------------------------ the forces

    /// <summary>Units of an army that may come, with their sheets.</summary>
    public static List<(UnitFileEntry Unit, StrikeUnit Sheet)> Fieldable(ArmyListFile army) =>
        army.Units.Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(x => x.Sheet != null && CampaignRules.CanBringToMission(x.Unit))
            .Select(x => (x.Unit, x.Sheet!)).ToList();

    /// <summary>Every campaign unit of an army, fieldable or not, for the force cards.</summary>
    public static List<(UnitFileEntry Unit, StrikeUnit Sheet)> Cards(ArmyListFile army) =>
        army.Units.Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(x => x.Sheet != null).Select(x => (x.Unit, x.Sheet!)).ToList();

    /// <summary>Whether a unit is ticked. An empty Bringing list still means "all of it" (#427).</summary>
    public static bool IsComing(StrikeTeamInfo info, StrikeUnit sheet) => info.Bringing.Count == 0 || info.Bringing.Contains(sheet.Id);

    public static int PointsComing(ArmyListFile army, StrikeTeamInfo info) => Fieldable(army).Where(x => IsComing(info, x.Sheet)).Sum(x => x.Unit.PointCost);
    public static int UnitsComing(ArmyListFile army, StrikeTeamInfo info) => Fieldable(army).Count(x => IsComing(info, x.Sheet));

    /// <summary>A force card's tag: "coming", "stays home", or "recovering (n)".</summary>
    public static string ForceTag(StrikeTeamInfo info, UnitFileEntry unit, StrikeUnit sheet) =>
        CampaignRules.CannotBringToMission(unit) != null ? $"recovering ({sheet.MissionsOut})"
        : IsComing(info, sheet) ? "coming" : "stays home";

    /// <summary>Flips one unit between coming and staying home. The first change writes the whole fieldable list out.</summary>
    public static void ToggleComing(ArmyListFile army, StrikeTeamInfo info, StrikeUnit sheet)
    {
        if (info.Bringing.Count == 0) info.Bringing = Fieldable(army).Select(x => x.Sheet.Id).ToList();
        if (!info.Bringing.Remove(sheet.Id)) info.Bringing.Add(sheet.Id);
    }

    /// <summary>Clicks one of your cards. Your force sets the rival's floor, so theirs is re-sized straight away.</summary>
    public void ToggleYours(UnitFileEntry unit, StrikeUnit sheet)
    {
        if (Army == null || Info == null || CampaignRules.CannotBringToMission(unit) != null) return;
        ToggleComing(Army, Info, sheet);
        SaveArmy();
        FillEnemyForce(randomize: false);
    }

    /// <summary>Clicks one of theirs (a hand pick overrides the difficulty fill until it runs again).</summary>
    public void ToggleTheirs(UnitFileEntry unit, StrikeUnit sheet)
    {
        if (Enemy == null || Foe == null || CampaignRules.CannotBringToMission(unit) != null) return;
        ToggleComing(Enemy, Foe, sheet);
        SaveEnemy();
    }

    public int YoursComing => Army != null && Info != null ? PointsComing(Army, Info) : 0;
    public int YourUnitsComing => Army != null && Info != null ? UnitsComing(Army, Info) : 0;
    public int TheirsComing => Enemy != null && Foe != null ? PointsComing(Enemy, Foe) : 0;
    public int TheirUnitsComing => Enemy != null && Foe != null ? UnitsComing(Enemy, Foe) : 0;
    public int HardCap => MissionDifficulty.HardCap(YoursComing, Numbers);

    /// <summary>"Bringing 450 / 600 pts in 4 units." and, when over the cap, why.</summary>
    public (string Line, string? Problem) YourForceLine()
    {
        int yours = YoursComing, count = YourUnitsComing;
        if (_others.Count == 0) return ($"Bringing {yours} pts in {ArmyCampaignSession.Plural(count, "unit")}.", null);
        int cap = CampaignPreGame.MaxPointsForMission(_others, Numbers);
        return ($"Bringing {yours} / {cap} pts in {ArmyCampaignSession.Plural(count, "unit")}.", CampaignPreGame.CannotBring(yours, _others, Numbers, count));
    }

    public string TheirForceLine() =>
        $"They bring {TheirsComing} pts in {ArmyCampaignSession.Plural(TheirUnitsComing, "unit")} - at most {HardCap} allowed.";

    /// <summary>The Underdog Bonus line (book: one per UnderdogPer pts behind the largest force), and whether it's good news.</summary>
    public (string Line, bool Good) UnderdogLine()
    {
        if (Info is not { } info || Foe is not { } foe) return ("", false);
        int yours = YoursComing, theirs = TheirsComing;
        var forces = new List<CampaignEntrant> { new(info.Name, yours, false), new(foe.Name, theirs, false) };
        int mine = CampaignPreGame.UnderdogPoints(yours, forces, Numbers);
        if (mine > 0)
            return ($"You are the underdog: {mine} underdog point(s) this mission. One per {Numbers.UnderdogPer} pts behind the largest force; each shifts every die in one roll by 1.", true);
        return theirs < yours
            ? ("They are the smaller force, and a bot is never given underdog points, so nobody has any.", false)
            : ("Even forces, so no underdog points for either side.", false);
    }

    /// <summary>When their roster can't reach the difficulty's size yet, the reason; else null.</summary>
    public string? CantReachLine()
    {
        if (Enemy == null || Info == null) return null;
        int owned = Fieldable(Enemy).Sum(x => x.Unit.PointCost);
        int target = MissionDifficulty.EnemyPointsTarget(Info.Difficulty, YoursComing, Numbers);
        return owned < target
            ? $"They only own {owned} pts they can field, so they can't reach {MissionDifficulty.Name(Info.Difficulty)}'s {target} pts yet. Difficulty is a ceiling - they play harder as their roster grows."
            : null;
    }

    /// <summary>Re-picks the rival's force for the difficulty now selected. Idempotent; runs whenever your force changes.</summary>
    public void FillEnemyForce(bool randomize)
    {
        if (Army == null || Info is not { } info || Enemy == null || Foe is not { } foe) return;
        int target = MissionDifficulty.EnemyPointsTarget(info.Difficulty, PointsComing(Army, info), Numbers);
        foe.Bringing = new List<string>();
        CampaignBot.PickWhatToBring(Enemy, target, randomize ? Roll : null);
        SaveEnemy();
    }

    // ------------------------------------------------------------------ difficulty and bonuses

    public void SetDifficulty(EMissionDifficulty option)
    {
        if (Info is not { } info) return;
        info.Difficulty = option;
        if (Plan != null) Plan.Difficulty = option;
        SavePlan();
        FillEnemyForce(randomize: false);
    }

    public void RerollDifficulty() => SetDifficulty(CampaignMissionGenerator.RollDifficulty(D6));

    public bool ChallengeBonusesApply => Info != null && Foe != null && ChallengeBonuses.Applies(System);

    public void SetChallengeBonuses(EChallengeBonuses value)
    {
        if (Info is not { } info) return;
        info.ChallengeBonuses = value;
        SaveArmy();
    }

    // ------------------------------------------------------------------ the plan card

    public void SetTitle(string text)
    {
        if (Plan == null) return;
        Plan.Title = CampaignJournal.Clean(text);
        SavePlan();
    }

    /// <summary>Generate all: a new deployment, mission type and difficulty. The map and notes stay.</summary>
    public void GenerateAll()
    {
        if (Plan == null || Info is not { } info) return;
        CampaignMissionPlan fresh = CampaignMissionGenerator.Generate(Plan.Mission, System, D6);
        fresh.TerrainMode = Plan.TerrainMode;
        fresh.MapName = Plan.MapName;
        Plan = fresh;
        info.Difficulty = Plan.Difficulty;
        SavePlan();
        FillEnemyForce(randomize: false);
    }

    public bool DeploymentAvailable => AdvancedDeployment.Available(System);
    public void SetDeployment(EDeploymentStyle style) { if (Plan == null) return; Plan.DeploymentStyle = style; SavePlan(); }
    public void RerollDeployment() { if (Plan == null) return; Plan.DeploymentStyle = CampaignMissionGenerator.RollDeployment(System, D6); SavePlan(); }

    public bool MissionTypeAvailable => FDG.Missions.MissionCardCatalog.Available(System);
    public static string MissionTypeName(FDG.Missions.EMissionCardGame game) =>
        game == FDG.Missions.EMissionCardGame.Off ? "Core objectives" : FDG.Missions.MissionCardCatalog.Name(game);
    public void SetMissionType(FDG.Missions.EMissionCardGame game) { if (Plan == null) return; Plan.MissionCards = game; SavePlan(); }
    public void RerollMissionType() { if (Plan == null) return; Plan.MissionCards = CampaignMissionGenerator.RollMissionType(System, D6); SavePlan(); }

    public string Notes => Info != null && Plan != null ? CampaignJournal.NoteFor(Info, Plan.Mission) : "";

    public void SetNotes(string text)
    {
        if (Info is not { } info || Plan == null) return;
        CampaignJournal.SetNote(info, Plan.Mission, text);
        SaveArmy();
    }

    /// <summary>Saves every mission planned so far as a campaign to replay. The file's path, or null.</summary>
    public string? SaveAsScript() => Info is { } info ? CampaignScriptPaths.Save(CampaignScript.From(info, info.Name)) : null;

    // ------------------------------------------------------------------ the map

    public bool CustomMap => TerrainMode == ETerrainPlacementMode.LoadFromFile && !string.IsNullOrEmpty(MapPath);
    public string MapName => CustomMap ? IOPath.GetFileNameWithoutExtension(MapPath!) : "Random";

    /// <summary>The map picked: a path, or null for Random (the table is laid out for you).</summary>
    public void SetMap(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            MapPath = null;
            if (TerrainMode == ETerrainPlacementMode.LoadFromFile) TerrainMode = ETerrainPlacementMode.AutoFromLayout;
        }
        else
        {
            MapPath = path;
            TerrainMode = ETerrainPlacementMode.LoadFromFile;
        }
        if (Plan == null) return;
        Plan.TerrainMode = TerrainMode;
        Plan.MapName = MapPath == null ? null : IOPath.GetFileNameWithoutExtension(MapPath);
        SavePlan();
    }

    public static readonly ETerrainPlacementMode[] RandomTerrainModes =
    {
        ETerrainPlacementMode.AutoFromLayout, ETerrainPlacementMode.Alternating, ETerrainPlacementMode.AlternatingPoints,
    };

    public void SetTerrainMode(ETerrainPlacementMode mode)
    {
        TerrainMode = mode;
        if (Plan != null) { Plan.TerrainMode = mode; SavePlan(); }
    }

    /// <summary>What each terrain mode does. Game text, so ASCII only.</summary>
    public static string TerrainModeHelp(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout => "The table is laid out for you before the mission starts - nothing to place.",
        ETerrainPlacementMode.Alternating => "You and the enemy take turns placing one piece of terrain each, the books' own way.",
        ETerrainPlacementMode.AlternatingPoints => "You and the enemy take turns spending a terrain budget - one big piece or several small.",
        _ => "Plays on a map you made in the map maker, exactly as you drew it.",
    };

    public static string TerrainModeLabel(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout => "Auto From Layout",
        ETerrainPlacementMode.Alternating => "Alternating: One Per",
        ETerrainPlacementMode.AlternatingPoints => "Alternating: Points",
        ETerrainPlacementMode.LoadFromFile => "Custom Map",
        _ => mode.ToString(),
    };

    // ------------------------------------------------------------------ starting it

    /// <summary>Why START MISSION is greyed, or null when it is live.</summary>
    public string? Blocker()
    {
        if (Army == null || Info is not { } info) return "No campaign army loaded.";
        if (UnitsComing(Army, info) == 0) return "Pick at least one of your units.";
        if (TerrainMode == ETerrainPlacementMode.LoadFromFile && string.IsNullOrEmpty(MapPath)) return "Pick a map, or choose another terrain mode.";
        if (Enemy != null && Foe is { } foe)
        {
            if (UnitsComing(Enemy, foe) == 0) return "Pick at least one of their units.";
            int over = PointsComing(Enemy, foe) - MissionDifficulty.HardCap(PointsComing(Army, info), Numbers);
            if (over > 0) return $"Their force is {over} pts over the mission cap.";
        }
        return null;
    }

    /// <summary>
    /// START MISSION: writes both sides' "this mission only" copies (only the units coming, so units left home take no
    /// casualty roll and earn no XP - the book's way), rolls the bot's special objective, and sets <see cref="Pending"/>
    /// for the lobby. Returns the launch, or null with the reason.
    /// </summary>
    public ArmyCampaignLaunch? Start(out string? error)
    {
        error = Blocker();
        if (error != null || Army == null || Info is not { } info) return null;
        if (WriteMissionFile(Path, Army, info) is not { } mine) { error = "Couldn't write your mission army file."; return null; }

        string? theirs = null;
        if (EnemyPath != null && Enemy != null && Foe is { } foe)
        {
            CampaignBot.RollSpecialObjective(Enemy, D6);
            SaveEnemy();
            theirs = WriteMissionFile(EnemyPath, Enemy, foe);
            if (theirs == null) { error = "Couldn't write their mission army file."; return null; }
        }

        CampaignMissionPlan plan = Plan ?? new CampaignMissionPlan { Mission = info.Matches + 1 };
        string title = $"{info.Name} - Mission {info.Matches + 1}" + (plan.Title.Length > 0 ? $": {plan.Title}" : "");
        string line = $"{MissionDifficulty.Name(info.Difficulty)} - {MissionTypeName(plan.MissionCards)}";
        var launch = new ArmyCampaignLaunch(System, Path, info.Name, title, line, mine, theirs, TerrainMode, MapPath,
            plan.DeploymentStyle, plan.MissionCards, Foe != null ? info.ChallengeBonuses : EChallengeBonuses.None);
        Pending = launch;
        return launch;
    }

    /// <summary>The "this mission only" copy, or the campaign file itself when the whole army is coming.</summary>
    public static string? WriteMissionFile(string path, ArmyListFile army, StrikeTeamInfo info)
    {
        var keep = new HashSet<string>(Fieldable(army).Where(x => IsComing(info, x.Sheet)).Select(x => x.Sheet.Id), StringComparer.Ordinal);
        if (keep.Count == 0) return null;
        if (keep.Count == army.Units.Count) return path;
        if (StrikeTeamFiles.Load(path) is not { } copy) return null;
        copy.Units.RemoveAll(u => CampaignRules.Sheet(u) is not { } sh || !keep.Contains(sh.Id));
        string play = IOPath.ChangeExtension(path, ".fdgmission");
        return StrikeTeamFiles.Save(play, copy) ? play : null;
    }

    // ------------------------------------------------------------------ after the game

    /// <summary>What the end card shows for the mission just played, read from the campaign files the game wrote back.</summary>
    public sealed record MissionResult(StrikeTeamInfo Info, bool Won, IReadOnlyList<string> Record, int Waiting, int RivalWaiting,
        CampaignPreGame.CampaignStanding Standing, StrikeTeamInfo? Rival);

    /// <summary>The mission just played (result, casualty rolls, XP, choices waiting), or null when nothing was recorded here.</summary>
    public static MissionResult? AfterMission(string campaignPath)
    {
        if (StrikeTeamFiles.Load(campaignPath) is not { StrikeTeam: { } info } army || info.Matches == 0) return null;
        BuiltArmyFile? rival = info.EnemyArmyFile is { } e ? StrikeTeamFiles.Load(e) : null;
        bool won = info.History.Any(h => h.StartsWith($"Match {info.Matches}: won", StringComparison.Ordinal));
        return new MissionResult(info, won, CampaignJournal.RecordFor(info, info.Matches), ArmyCampaignSession.Waiting(army),
            rival != null ? ArmyCampaignSession.Waiting(rival) : 0, CampaignPreGame.Standing(info, rival?.StrikeTeam), rival?.StrikeTeam);
    }
}
