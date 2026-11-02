using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// #435 - Mission Setup: the campaign's step 1, on one screen.
///
/// <para>
/// Replaces the old walk of "Play next mission" -> Host Server dialog -> the lobby's table settings.
/// Kai's call, 2026-11-01: <i>"clicking on start mission will open straight to the mission screen (bypass
/// the Host Server screen that currently comes up)"</i> and <i>"The Table is gone from the campaign
/// flow."</i> Three panels and a button, in the order the book's own Mission Setup step runs: your force,
/// their force, the map.
/// </para>
///
/// <para>
/// <b>It still launches through the lobby</b>, it just never shows it: Start Mission opens a host quietly
/// on the saved settings, seats both campaign armies the way the campaign screen already did, and presses
/// LAUNCH for you. A second launch route would be a second place for army loading, bot seating, the launch
/// gate and the save hook to drift out of step. If the lobby finds a real reason not to launch (a missing
/// army file, a bad list) the player lands in it with the greyed LAUNCH button explaining why, which is
/// better than a dead button here.
/// </para>
///
/// <para>
/// Deliberately plain ImGui under the working agreement: the layout decision is permanent and lives in
/// <c>claude/campaign-flow-spec.md</c>, this drawing code is not. Everything with a rule behind it -
/// which units may come, how big the rival's force should be, what the Underdog Bonus pays - is in
/// <see cref="CampaignRules"/>, <see cref="MissionDifficulty"/> and <see cref="CampaignPreGame"/>, which
/// are tested on their own.
/// </para>
/// </summary>
public sealed class MissionSetupScreen : IAppScreen
{
    /// <summary>Back to the campaign screen, nothing started.</summary>
    public Action? OnBack;

    /// <summary>
    /// Start Mission: everything is written and recorded, open a host for this game system and launch it.
    /// Program.cs does the hosting; this screen has already set the pending army, enemy, map and
    /// auto-launch on <see cref="LobbyScreen"/>.
    /// </summary>
    public Action<EGameSystem>? OnStart;

    private string _path = "";
    private BuiltArmyFile? _army;
    private string? _enemyPath;
    private BuiltArmyFile? _enemy;
    private string _message = "";
    private bool _messageIsError;

    /// <summary>Other campaign armies of the same game, which is what sets the mission cap (#426).</summary>
    private List<CampaignEntrant> _others = new();

    private ETerrainPlacementMode _terrainMode = ETerrainPlacementMode.Alternating;
    private string? _mapPath;

    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);

    private static readonly Random Rng = new();
    private static int D6() => Rng.Next(1, 7);
    /// <summary>roll(n) is 1..n - the convention <c>CampaignBot.PickWhatToBring</c> and the solo AI share.</summary>
    private static int Roll(int sides) => Rng.Next(1, Math.Max(2, sides + 1));

    /// <summary>
    /// Opened by the campaign screen with the campaign it is launching. Everything else is read from
    /// disk here, so this screen owns one copy of the truth and writes its choices straight back.
    /// </summary>
    public void Open(string path, IEnumerable<CampaignEntrant> others)
    {
        _path = path;
        _others = others.ToList();
        _message = "";
        _messageIsError = false;
        Reload();
    }

    private void Reload()
    {
        _army = StrikeTeamFiles.Load(_path);
        _enemy = null;
        _enemyPath = _army?.StrikeTeam?.EnemyArmyFile;
        if (_enemyPath != null) _enemy = StrikeTeamFiles.Load(_enemyPath);

        // The rival's force is filled from the difficulty band the moment the screen opens, so the
        // player sees a legal, sized force rather than an empty list they have to populate themselves.
        // Pressing Randomize or changing the difficulty re-fills it; ticking a box by hand overrides it.
        FillEnemyForce(randomize: false);
    }

    public void OnShown()
    {
        if (_path.Length > 0) Reload();
    }

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    private CampaignNumbers Numbers => CampaignNumbers.For(_army?.StrikeTeam?.GameSystem
                                                           ?? EGameSystem.GrimdarkFuture);

    // -- The two forces -----------------------------------------------------------------------------

    /// <summary>Units of an army that may come, with their sheets.</summary>
    private static List<(UnitFileEntry Unit, StrikeUnit Sheet)> Fieldable(ArmyListFile army) =>
        army.Units
            .Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(x => x.Sheet != null && CampaignRules.CanBringToMission(x.Unit))
            .Select(x => (x.Unit, x.Sheet!))
            .ToList();

    /// <summary>Whether a unit is ticked. An empty Bringing list still means "all of it" (#427).</summary>
    private static bool IsComing(StrikeTeamInfo info, StrikeUnit sheet) =>
        info.Bringing.Count == 0 || info.Bringing.Contains(sheet.Id);

    private static int PointsComing(ArmyListFile army, StrikeTeamInfo info) =>
        Fieldable(army).Where(x => IsComing(info, x.Sheet)).Sum(x => x.Unit.PointCost);

    private static int UnitsComing(ArmyListFile army, StrikeTeamInfo info) =>
        Fieldable(army).Count(x => IsComing(info, x.Sheet));

    /// <summary>
    /// Re-picks the rival's force for the difficulty now selected. The player's ticked force sets the
    /// floor, so this runs again whenever that changes - which is why it is cheap and idempotent rather
    /// than something the player has to remember to press.
    /// </summary>
    private void FillEnemyForce(bool randomize)
    {
        if (_army?.StrikeTeam is not { } info || _enemy?.StrikeTeam is not { } foe) return;

        int yours = PointsComing(_army, info);
        int target = MissionDifficulty.EnemyPointsTarget(info.Difficulty, yours, Numbers);
        // Cleared first: PickWhatToBring writes the result onto the army's own Bringing list, and a
        // stale list from the last mission would otherwise be read as a hand-picked force.
        foe.Bringing = new List<string>();
        CampaignBot.PickWhatToBring(_enemy, target, randomize ? Roll : null);
        if (_enemyPath != null) StrikeTeamFiles.Save(_enemyPath, _enemy);
    }

    // -- Drawing ------------------------------------------------------------------------------------

    public void Draw(int screenWidth, int screenHeight)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenWidth, screenHeight), ImGuiCond.Always);
        ImGui.Begin("##MissionSetup",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse);

        if (_army?.StrikeTeam is not { } info)
        {
            UiText.Colored(Bad, "Couldn't read this campaign army.");
            if (UiButton.Navigate("Back", new Vector2(160f, 40f))) OnBack?.Invoke();
            ImGui.End();
            ImGui.PopStyleColor();
            return;
        }

        CampaignNumbers n = Numbers;
        string foeName = _enemy?.StrikeTeam?.Name ?? "no standing enemy";
        UiText.Colored(Accent, $"MISSION SETUP - {info.Name} vs {foeName}");
        ImGui.SameLine();
        UiText.Disabled($"   {GameSystemInfo.DisplayName(info.GameSystem)}   mission {info.Matches + 1}"
                        + $"   {info.Vp} VP");
        ImGui.Separator();

        float margin = 16f;
        float bottom = 64f;
        float top = ImGui.GetCursorPosY();
        float h = screenHeight - top - bottom;
        float w = (screenWidth - margin * 4f) / 3f;

        ImGui.SetCursorPos(new Vector2(margin, top));
        ImGui.BeginChild("##yours", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawYourForce(info, n);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin * 2f + w, top));
        ImGui.BeginChild("##theirs", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawTheirForce(info, n);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin * 3f + w * 2f, top));
        ImGui.BeginChild("##map", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawMap();
        ImGui.EndChild();

        DrawBottomBar(info, margin, screenWidth, screenHeight, bottom);
        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawYourForce(StrikeTeamInfo info, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "YOUR FORCE");
        ImGui.Separator();

        foreach (UnitFileEntry unit in _army!.Units)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) continue;

            // #427: a unit recovering under No Permadeath cannot be taken, and says why.
            if (CampaignRules.CannotBringToMission(unit) is { } resting)
            {
                ImGui.PushID(sheet.Id);
                UiText.Disabled($"{unit.Name}  ({unit.PointCost} pts)");
                ImGui.SameLine();
                UiText.Colored(Bad, resting);
                ImGui.PopID();
                continue;
            }

            bool coming = IsComing(info, sheet);
            bool was = coming;
            ImGui.PushID(sheet.Id);
            string label = $"{unit.Name}  ({unit.PointCost} pts)"
                           + (sheet.Level > 0 ? $"  [lvl {sheet.Level}]" : "");
            if (UiButton.Checkbox(label, ref coming) && coming != was)
            {
                if (info.Bringing.Count == 0)
                {
                    info.Bringing = _army.Units.Select(u => CampaignRules.Sheet(u)?.Id ?? "")
                        .Where(s => s.Length > 0).ToList();
                }
                if (coming) { if (!info.Bringing.Contains(sheet.Id)) info.Bringing.Add(sheet.Id); }
                else info.Bringing.Remove(sheet.Id);
                StrikeTeamFiles.Save(_path, _army);
                // Your force sets the rival's floor, so its size moves the moment this does.
                FillEnemyForce(randomize: false);
            }
            ImGui.PopID();
        }

        ImGui.Separator();
        int yours = PointsComing(_army, info);
        int count = UnitsComing(_army, info);
        if (_others.Count == 0)
        {
            ImGui.TextUnformatted($"Bringing {yours} pts in {count} unit(s).");
        }
        else
        {
            int cap = CampaignPreGame.MaxPointsForMission(_others, n);
            ImGui.TextUnformatted($"Bringing {yours} / {cap} pts in {count} unit(s).");
            if (CampaignPreGame.CannotBring(yours, _others, n, count) is { } why) UiText.Colored(Bad, why);
        }

        if (info.SpecialObjective.Length > 0)
        {
            ImGui.Separator();
            UiText.Colored(TextDim, "Your special objective");
            UiText.Colored(Gold, info.SpecialObjective);
            UiText.Wrapped(CampaignPreGame.ObjectiveText(info.SpecialObjective));
            UiText.Disabled($"Worth +{n.SpecialObjectiveBonus} pts into your treasury.");
        }
    }

    private void DrawTheirForce(StrikeTeamInfo info, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "THEIR FORCE");
        ImGui.Separator();

        if (_enemy?.StrikeTeam is not { } foe)
        {
            UiText.Wrapped("This campaign has no standing enemy, so the lobby rolls a fresh opponent "
                           + "each mission and nothing carries over on their side. Difficulty has "
                           + "nothing to size.");
            return;
        }

        int yours = PointsComing(_army!, info);

        // Difficulty
        ImGui.TextUnformatted("Difficulty");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##difficulty", MissionDifficulty.Name(info.Difficulty)))
        {
            foreach (EMissionDifficulty option in MissionDifficulty.All)
            {
                bool selected = MissionDifficulty.Resolve(info.Difficulty) == option;
                if (ImGui.Selectable(MissionDifficulty.Name(option), selected) && !selected)
                {
                    info.Difficulty = option;
                    StrikeTeamFiles.Save(_path, _army!);
                    FillEnemyForce(randomize: false);
                }
                if (ImGui.IsItemHovered())
                    UiText.Tooltip(MissionDifficulty.Describe(option, yours, n));
            }
            ImGui.EndCombo();
        }
        UiText.Wrapped(MissionDifficulty.Describe(info.Difficulty, yours, n));

        if (UiButton.NavigateSmall("Randomize"))
        {
            FillEnemyForce(randomize: true);
            Say("Rolled a fresh force for them at the same difficulty.");
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Fills their force at random from the units they own, to the same\n"
                           + "difficulty. Without it they bring their most experienced units first.");

        ImGui.Separator();
        foreach ((UnitFileEntry unit, StrikeUnit sheet) in Fieldable(_enemy))
        {
            bool coming = IsComing(foe, sheet);
            bool was = coming;
            ImGui.PushID("foe" + sheet.Id);
            string label = $"{unit.Name}  ({unit.PointCost} pts)"
                           + (sheet.Level > 0 ? $"  [lvl {sheet.Level}]" : "");
            if (UiButton.Checkbox(label, ref coming) && coming != was)
            {
                if (foe.Bringing.Count == 0)
                {
                    foe.Bringing = Fieldable(_enemy).Select(x => x.Sheet.Id).ToList();
                }
                if (coming) { if (!foe.Bringing.Contains(sheet.Id)) foe.Bringing.Add(sheet.Id); }
                else foe.Bringing.Remove(sheet.Id);
                if (_enemyPath != null) StrikeTeamFiles.Save(_enemyPath, _enemy);
            }
            ImGui.PopID();
        }

        ImGui.Separator();
        int theirs = PointsComing(_enemy, foe);
        int theirCount = UnitsComing(_enemy, foe);
        int hardCap = MissionDifficulty.HardCap(yours, n);
        ImGui.TextUnformatted($"They bring {theirs} / {hardCap} pts in {theirCount} unit(s).");
        if (theirs > hardCap)
            UiText.Colored(Bad, $"Over the cap by {theirs - hardCap} pts - untick something.");

        // The Underdog Bonus this gap pays, which is the other half of what difficulty means.
        var forces = new List<CampaignEntrant>
        {
            new(info.Name, yours, false),
            new(foe.Name, theirs, false),
        };
        int mine = CampaignPreGame.UnderdogPoints(yours, forces, n);
        if (mine > 0)
        {
            UiText.Colored(Good, $"You are the underdog: {mine} underdog point(s) this mission.");
            UiText.Disabled($"One per {n.UnderdogPer} pts behind the largest force. Each shifts every "
                            + "die in one roll by 1, yours or theirs - one point per roll.");
        }
        else if (theirs < yours)
        {
            UiText.Disabled("They are the smaller force, and a bot is never given underdog points, so "
                            + "nobody has any this mission.");
        }
        else
        {
            UiText.Disabled("Even forces, so no underdog points for either side.");
        }

        int owned = Fieldable(_enemy).Sum(x => x.Unit.PointCost);
        int target = MissionDifficulty.EnemyPointsTarget(info.Difficulty, yours, n);
        if (owned < target)
        {
            UiText.Disabled($"They only own {owned} pts they can field, so they cannot reach "
                            + $"{MissionDifficulty.Name(info.Difficulty)}'s {target} pts yet. Difficulty "
                            + "is a ceiling - they will play harder as their roster grows.");
        }
    }

    private void DrawMap()
    {
        UiText.Colored(TextDim, "THE MAP");
        ImGui.Separator();

        ImGui.TextUnformatted("Terrain");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##terrainmode", TerrainModeLabel(_terrainMode)))
        {
            foreach (ETerrainPlacementMode mode in new[]
                     {
                         ETerrainPlacementMode.AutoFromLayout,
                         ETerrainPlacementMode.Alternating,
                         ETerrainPlacementMode.AlternatingPoints,
                         ETerrainPlacementMode.LoadFromFile,
                     })
            {
                if (ImGui.Selectable(TerrainModeLabel(mode), mode == _terrainMode)) _terrainMode = mode;
            }
            ImGui.EndCombo();
        }

        if (_terrainMode == ETerrainPlacementMode.LoadFromFile)
        {
            ImGui.TextUnformatted("Map");
            ImGui.SetNextItemWidth(-1f);
            string display = string.IsNullOrEmpty(_mapPath)
                ? "(choose a map)"
                : Path.GetFileNameWithoutExtension(_mapPath);
            if (ImGui.BeginCombo("##mappick", display, ImGuiComboFlags.HeightLarge))
            {
                IReadOnlyList<string> maps = FdgRaylib.SaveLoad.MapPaths.List();
                if (maps.Count == 0) UiText.Disabled("No saved maps yet - make one in the map maker.");
                foreach (string map in maps)
                {
                    bool selected = string.Equals(map, _mapPath, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(map)}##{map}", selected))
                        _mapPath = map;
                }
                ImGui.EndCombo();
            }
            if (string.IsNullOrEmpty(_mapPath))
                UiText.Colored(Bad, "Pick a map, or choose another terrain mode.");
        }

        ImGui.Separator();
        UiText.Wrapped(TerrainModeHelp(_terrainMode));
    }

    /// <summary>What each terrain mode does. Game text, so ASCII only (CLAUDE.md).</summary>
    private static string TerrainModeHelp(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout =>
            "The table is laid out for you before the mission starts - nothing to place.",
        ETerrainPlacementMode.Alternating =>
            "You and the enemy take turns placing one piece of terrain each, the books' own way.",
        ETerrainPlacementMode.AlternatingPoints =>
            "You and the enemy take turns spending a terrain budget - one big piece or several small.",
        _ => "Plays on a map you made in the map maker, exactly as you drew it.",
    };

    private static string TerrainModeLabel(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout => "Auto From Layout",
        ETerrainPlacementMode.Alternating => "Alternating: One Per",
        ETerrainPlacementMode.AlternatingPoints => "Alternating: Points",
        ETerrainPlacementMode.LoadFromFile => "Custom Map",
        _ => mode.ToString(),
    };

    private void DrawBottomBar(StrikeTeamInfo info, float margin, int screenWidth, int screenHeight,
                               float bottom)
    {
        Vector2 btn = new Vector2(190f, 42f);
        float by = screenHeight - bottom + 10f;

        ImGui.SetCursorPos(new Vector2(margin, by));
        if (UiButton.Navigate("Back", btn)) OnBack?.Invoke();

        ImGui.SetCursorPos(new Vector2(screenWidth - margin - btn.X, by));
        bool ready = Blocker(info) == null;
        ImGui.BeginDisabled(!ready);
        if (UiButton.Confirm("Start Mission", btn)) StartMission(info);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && Blocker(info) is { } blocked)
            UiText.Tooltip(blocked);

        if (_message.Length == 0) return;
        Vector2 ts = ImGui.CalcTextSize(_message);
        ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(_messageIsError ? Bad : Good, _message);
    }

    /// <summary>
    /// Why Start Mission is greyed, or null when it is live. ImGui-free so the wording is testable, the
    /// same shape as <c>LobbyScreen.LaunchTooltip</c>.
    /// </summary>
    internal string? Blocker(StrikeTeamInfo info)
    {
        if (_army == null) return "No campaign army loaded.";
        if (UnitsComing(_army, info) == 0) return "Tick at least one of your units.";
        if (_terrainMode == ETerrainPlacementMode.LoadFromFile && string.IsNullOrEmpty(_mapPath))
            return "Pick a map, or choose another terrain mode.";

        if (_enemy?.StrikeTeam is { } foe)
        {
            if (UnitsComing(_enemy, foe) == 0) return "Tick at least one of their units.";
            int over = PointsComing(_enemy, foe)
                       - MissionDifficulty.HardCap(PointsComing(_army, info), Numbers);
            if (over > 0) return $"Their force is {over} pts over the mission cap.";
        }
        return null;
    }

    // -- Starting it --------------------------------------------------------------------------------

    /// <summary>
    /// Writes both sides' mission files, hands them and the map to the lobby, and asks for a host.
    /// <para>
    /// The per-mission copy is a <c>.fdgmission</c> holding only the units coming, which is what settles
    /// Kai's <i>"I want to double check that the armies we load in with are just the units we chose to
    /// take, not the entire army"</i>. Every sheet in the copy keeps its own id and its TeamFile, so the
    /// after-game write-back lands on the real campaign army rather than on the copy; units left at home
    /// never reach the table, so they take no casualty roll and earn no XP, which is what the book wants.
    /// </para>
    /// </summary>
    private void StartMission(StrikeTeamInfo info)
    {
        if (_army == null) return;
        if (WriteMissionFile(_path, _army, info) is not { } mine)
        {
            Say("Couldn't write your mission army file.", true);
            return;
        }

        string? theirs = null;
        if (_enemyPath != null && _enemy?.StrikeTeam is { } foe)
        {
            CampaignBot.RollSpecialObjective(_enemy, D6);
            StrikeTeamFiles.Save(_enemyPath, _enemy);
            theirs = WriteMissionFile(_enemyPath, _enemy, foe);
            if (theirs == null) { Say("Couldn't write their mission army file.", true); return; }
        }

        LobbyScreen.PendingCampaignArmy = mine;
        LobbyScreen.PendingCampaignEnemyArmy = theirs;
        LobbyScreen.PendingTerrainMode = _terrainMode;
        LobbyScreen.PendingTerrainLayoutPath = _mapPath;
        // The point of this screen: the player never sees the lobby or the table.
        LobbyScreen.PendingAutoLaunch = true;
        OnStart?.Invoke(info.GameSystem);
    }

    /// <summary>
    /// The "this mission only" copy, or the campaign file itself when the whole army is coming. Returns
    /// null only when the file could not be read or written.
    /// </summary>
    private static string? WriteMissionFile(string path, ArmyListFile army, StrikeTeamInfo info)
    {
        var keep = new HashSet<string>(
            Fieldable(army).Where(x => IsComing(info, x.Sheet)).Select(x => x.Sheet.Id),
            StringComparer.Ordinal);
        if (keep.Count == 0) return null;
        if (keep.Count == army.Units.Count) return path;

        if (StrikeTeamFiles.Load(path) is not { } copy) return null;
        copy.Units.RemoveAll(u => CampaignRules.Sheet(u) is not { } sh || !keep.Contains(sh.Id));
        string play = Path.ChangeExtension(path, ".fdgmission");
        return StrikeTeamFiles.Save(play, copy) ? play : null;
    }
}
