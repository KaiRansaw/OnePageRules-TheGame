using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using FdgRaylib.SaveLoad;
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

    /// <summary>#465 - the plan card for this mission (title, map, deployment, mission type, difficulty).</summary>
    private CampaignMissionPlan? _plan;
    private string _title = "";
    private string _notes = "";

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

        // #465: the next mission's plan - made now if it has not been (Generate is the default; every
        // field can then be changed or re-rolled). Its difficulty and map drive the panels beside it.
        if (_army?.StrikeTeam is { } info)
        {
            _plan = CampaignPlans.Next(info);
            if (_plan == null)
            {
                _plan = CampaignMissionGenerator.Generate(info.Matches + 1, info.GameSystem, D6);
                CampaignPlans.Set(info, _plan);
            }
            info.Difficulty = _plan.Difficulty;
            _terrainMode = _plan.TerrainMode;
            _mapPath = CampaignScriptPaths.MapFor(_plan.MapName);
            if (_terrainMode == ETerrainPlacementMode.LoadFromFile && _mapPath == null) _terrainMode = ETerrainPlacementMode.AutoFromLayout;
            _title = _plan.Title;
            _notes = CampaignJournal.NoteFor(info, _plan.Mission);
            StrikeTeamFiles.Save(_path, _army);
        }

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

    private void SavePlan()
    {
        if (_army?.StrikeTeam is not { } info || _plan == null) return;
        CampaignPlans.Set(info, _plan);
        StrikeTeamFiles.Save(_path, _army);
    }

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

        // #465: the mission plan first (it holds the map), then the two forces.
        ImGui.SetCursorPos(new Vector2(margin, top));
        ImGui.BeginChild("##plan", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawPlan(info);
        ImGui.Spacing();
        DrawMap();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin * 2f + w, top));
        ImGui.BeginChild("##yours", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawYourForce(info, n);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(margin * 3f + w * 2f, top));
        ImGui.BeginChild("##theirs", new Vector2(w, h), ImGuiChildFlags.Borders);
        DrawTheirForce(info, n);
        ImGui.EndChild();

        DrawBottomBar(info, margin, screenWidth, screenHeight, bottom);
        ImGui.End();
        ImGui.PopStyleColor();
    }

    // The force cards (wish list: "your force and their force as portrait cards (click to bring / leave)").
    private readonly UnitPortraitCards _yourCards = new();
    private readonly UnitPortraitCards _theirCards = new();

    /// <summary>A force card's tag: "coming", "stays home", or why it can't come. Pure, so it is pinned by
    /// a test.</summary>
    internal static string ForceTag(StrikeTeamInfo info, UnitFileEntry unit, StrikeUnit sheet) =>
        CampaignRules.CannotBringToMission(unit) != null ? $"recovering ({sheet.MissionsOut})"
        : IsComing(info, sheet) ? "coming"
        : "stays home";

    /// <summary>Flips one unit between coming and staying home. An empty Bringing list means "all of it"
    /// (#427), so the first change writes the whole fieldable list out first.</summary>
    internal static void ToggleComing(ArmyListFile army, StrikeTeamInfo info, StrikeUnit sheet)
    {
        if (info.Bringing.Count == 0) info.Bringing = Fieldable(army).Select(x => x.Sheet.Id).ToList();
        if (!info.Bringing.Remove(sheet.Id)) info.Bringing.Add(sheet.Id);
    }

    /// <summary>Draws an army as force cards and returns the sheet whose card was clicked (a fieldable one), or null.</summary>
    private static StrikeUnit? DrawForceCards(UnitPortraitCards widget, string id, BuiltArmyFile army, StrikeTeamInfo info)
    {
        var rows = army.Units
            .Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(x => x.Sheet != null)
            .Select(x => (x.Unit, Sheet: x.Sheet!))
            .ToList();
        var cards = rows.Select(r =>
        {
            string tag = ForceTag(info, r.Unit, r.Sheet);
            bool resting = CampaignRules.CannotBringToMission(r.Unit) != null;
            bool coming = !resting && IsComing(info, r.Sheet);
            string facts = $"{r.Unit.PointCost} pts" + (r.Sheet.Level > 0 ? $" - Lv {r.Sheet.Level}" : "");
            UnitFileEntry unit = r.Unit;
            return new UnitPortraitCards.Card(unit.Name, CampaignRules.UnitTypeOf(unit, army.Book), facts,
                () => UnitPortraitCards.StatsTooltip(unit).Replace("Click for the full unit.",
                    resting ? CampaignRules.CannotBringToMission(unit)! : coming ? "Click to leave it at home." : "Click to bring it."),
                tag, resting ? Bad : coming ? Good : TextDim, Dimmed: !coming);
        }).ToList();
        UnitPortraitCards.CardEvent hit = widget.DrawCardGrid(id, cards, army.Faction, -1);
        if (hit.Clicked < 0) return null;
        var picked = rows[hit.Clicked];
        return CampaignRules.CannotBringToMission(picked.Unit) == null ? picked.Sheet : null;
    }

    private void DrawYourForce(StrikeTeamInfo info, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "YOUR FORCE - click a card to bring it or leave it at home");
        ImGui.Separator();

        float under = ImGui.GetTextLineHeightWithSpacing() * 6f;
        ImGui.BeginChild("##yourcards", new Vector2(0, -under), ImGuiChildFlags.None);
        if (DrawForceCards(_yourCards, "##yours", _army!, info) is { } sheet)
        {
            ToggleComing(_army!, info, sheet);
            StrikeTeamFiles.Save(_path, _army!);
            // Your force sets the rival's floor, so its size moves the moment this does.
            FillEnemyForce(randomize: false);
        }
        ImGui.EndChild();

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
        if (UiCombo.Begin("##difficulty", MissionDifficulty.Name(info.Difficulty)))
        {
            foreach (EMissionDifficulty option in MissionDifficulty.All)
            {
                bool selected = MissionDifficulty.Resolve(info.Difficulty) == option;
                if (ImGui.Selectable(MissionDifficulty.Name(option), selected) && !selected)
                {
                    info.Difficulty = option;
                    if (_plan != null) _plan.Difficulty = option;
                    SavePlan();
                    FillEnemyForce(randomize: false);
                }
                if (ImGui.IsItemHovered())
                {
                    UiText.Tooltip(MissionDifficulty.Describe(option, yours, n) + "\n"
                                   + MissionDifficulty.DescribeReward(option, n));
                }
            }
            ImGui.EndCombo();
        }
        UiText.Wrapped(MissionDifficulty.Describe(info.Difficulty, yours, n));
        // What it pays, on screen rather than only in the tooltip: the setting changes the reward as
        // well as the fight, and choosing one without seeing both is choosing half a decision.
        UiText.Disabled(MissionDifficulty.DescribeReward(info.Difficulty, n));

        // The Solo & Co-Op books' own Challenge Bonuses - they buff how the rival PLAYS, where Difficulty
        // sizes what it brings, so the two stack.
        if (ChallengeBonuses.Applies(info.GameSystem))
        {
            EChallengeBonuses edited = ChallengeBonusesUi.Draw(info.ChallengeBonuses, "##mission");
            if (edited != info.ChallengeBonuses)
            {
                info.ChallengeBonuses = edited;
                StrikeTeamFiles.Save(_path, _army!);
            }
        }

        if (UiButton.NavigateSmall("Randomize"))
        {
            FillEnemyForce(randomize: true);
            Say("Rolled a fresh force for them at the same difficulty.");
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Fills their force at random from the units they own, to the same\n"
                           + "difficulty. Without it they bring their most experienced units first.");

        ImGui.Separator();
        float under = ImGui.GetTextLineHeightWithSpacing() * 7f;
        ImGui.BeginChild("##theircards", new Vector2(0, -under), ImGuiChildFlags.None);
        if (DrawForceCards(_theirCards, "##theirs", _enemy, foe) is { } sheet)
        {
            ToggleComing(_enemy, foe, sheet);
            if (_enemyPath != null) StrikeTeamFiles.Save(_enemyPath, _enemy);
        }
        ImGui.EndChild();

        ImGui.Separator();
        int theirs = PointsComing(_enemy, foe);
        int theirCount = UnitsComing(_enemy, foe);
        int hardCap = MissionDifficulty.HardCap(yours, n);
        ImGui.TextUnformatted($"They bring {theirs} pts in {theirCount} unit(s) - at most {hardCap} allowed.");
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

    /// <summary>
    /// #465 - the plan card. Generate rolls every field from the books; each field can be re-rolled or
    /// picked by hand; the notes are this mission's journal page.
    /// </summary>
    private void DrawPlan(StrikeTeamInfo info)
    {
        UiText.Colored(TextDim, $"THE MISSION ({info.Matches + 1})");
        ImGui.Separator();
        if (_plan == null) return;
        EGameSystem system = info.GameSystem;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##plantitle", ref _title, 80)) { }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _plan.Title = CampaignJournal.Clean(_title);
            _title = _plan.Title;
            SavePlan();
        }

        if (UiButton.NavigateSmall("Generate all"))
        {
            int mission = _plan.Mission;
            CampaignMissionPlan fresh = CampaignMissionGenerator.Generate(mission, system, D6);
            fresh.TerrainMode = _plan.TerrainMode;
            fresh.MapName = _plan.MapName;
            _plan = fresh;
            _title = _plan.Title;
            info.Difficulty = _plan.Difficulty;
            SavePlan();
            FillEnemyForce(randomize: false);
            Say("Rolled a new mission.");
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Rolls a new deployment style, mission type and difficulty from the books.\nThe map and your notes stay.");
        ImGui.Spacing();

        // Deployment style
        if (AdvancedDeployment.Available(system))
        {
            ImGui.TextUnformatted("Deployment");
            ImGui.SetNextItemWidth(-80f);
            if (UiCombo.Begin("##plandeploy", AdvancedDeployment.Name(_plan.DeploymentStyle), ImGuiComboFlags.HeightLarge))
            {
                foreach (EDeploymentStyle style in Enum.GetValues<EDeploymentStyle>())
                {
                    if (ImGui.Selectable(AdvancedDeployment.Name(style), style == _plan.DeploymentStyle))
                    {
                        _plan.DeploymentStyle = style;
                        SavePlan();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Reroll##deploy"))
            {
                _plan.DeploymentStyle = CampaignMissionGenerator.RollDeployment(system, D6);
                SavePlan();
            }
        }

        // Mission type
        if (FDG.Missions.MissionCardCatalog.Available(system))
        {
            ImGui.TextUnformatted("Mission type");
            ImGui.SetNextItemWidth(-80f);
            string label = _plan.MissionCards == FDG.Missions.EMissionCardGame.Off
                ? "Core objectives" : FDG.Missions.MissionCardCatalog.Name(_plan.MissionCards);
            if (UiCombo.Begin("##plantype", label, ImGuiComboFlags.HeightLarge))
            {
                foreach (FDG.Missions.EMissionCardGame game in Enum.GetValues<FDG.Missions.EMissionCardGame>())
                {
                    string name = game == FDG.Missions.EMissionCardGame.Off ? "Core objectives" : FDG.Missions.MissionCardCatalog.Name(game);
                    if (ImGui.Selectable(name, game == _plan.MissionCards))
                    {
                        _plan.MissionCards = game;
                        SavePlan();
                    }
                    if (ImGui.IsItemHovered()) UiText.Tooltip(CampaignMissionGenerator.DescribeMissionType(game));
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Reroll##type"))
            {
                _plan.MissionCards = CampaignMissionGenerator.RollMissionType(system, D6);
                SavePlan();
            }
            UiText.Wrapped(CampaignMissionGenerator.DescribeMissionType(_plan.MissionCards));
        }

        // Difficulty (set beside their force, which it sizes) - re-rollable here.
        ImGui.TextUnformatted($"Difficulty: {MissionDifficulty.Name(_plan.Difficulty)}");
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Reroll##diff"))
        {
            _plan.Difficulty = CampaignMissionGenerator.RollDifficulty(D6);
            info.Difficulty = _plan.Difficulty;
            SavePlan();
            FillEnemyForce(randomize: false);
        }

        ImGui.Spacing();
        UiText.Colored(TextDim, "Your notes (this mission's journal page)");
        ImGui.InputTextMultiline("##plannotes", ref _notes, CampaignJournal.MaxNoteLength,
            new Vector2(-1f, ImGui.GetTextLineHeight() * 5f));
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CampaignJournal.SetNote(info, _plan.Mission, _notes);
            _notes = CampaignJournal.NoteFor(info, _plan.Mission);
            StrikeTeamFiles.Save(_path, _army!);
        }

        if (UiButton.NavigateSmall("Save campaign as a file"))
        {
            if (CampaignScriptPaths.Save(CampaignScript.From(info, info.Name)) is { } saved)
                Say($"Saved to {CampaignScriptPaths.DirectoryName}\\{Path.GetFileName(saved)} - start a new campaign from it any time.");
            else
                Say("Couldn't save the campaign file.", true);
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Saves every mission planned so far, with your notes, as a campaign you can\nplay again with fresh armies (New Campaign -> start from a saved campaign).");
        ImGui.Separator();
    }

    /// <summary>
    /// "Select Map...": opens the shared Select Map screen (the lobby's) for this mission. Program.cs wires
    /// it and hands the pick back through <see cref="SetMap"/>.
    /// </summary>
    public Action<MapPickerScreen.Request>? OnSelectMap;

    /// <summary>The map picked on Select Map: a path, or null for Random (the table is laid out for you).</summary>
    public void SetMap(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            _mapPath = null;
            if (_terrainMode == ETerrainPlacementMode.LoadFromFile) _terrainMode = ETerrainPlacementMode.AutoFromLayout;
        }
        else
        {
            _mapPath = path;
            _terrainMode = ETerrainPlacementMode.LoadFromFile;
        }
        if (_plan != null)
        {
            _plan.TerrainMode = _terrainMode;
            _plan.MapName = _mapPath == null ? null : Path.GetFileNameWithoutExtension(_mapPath);
            SavePlan();
        }
    }

    private void DrawMap()
    {
        UiText.Colored(TextDim, "THE MAP");
        ImGui.Separator();

        bool custom = _terrainMode == ETerrainPlacementMode.LoadFromFile && !string.IsNullOrEmpty(_mapPath);
        ImGui.TextUnformatted("Map: " + (custom ? Path.GetFileNameWithoutExtension(_mapPath!) : "Random"));
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Select Map..."))
        {
            EGameSystem system = _army?.StrikeTeam?.GameSystem ?? EGameSystem.GrimdarkFuture;
            OnSelectMap?.Invoke(new MapPickerScreen.Request(system, EGameMode.Standard, ETableBackground.Forest,
                _plan?.DeploymentStyle ?? EDeploymentStyle.Core, 2, custom ? _mapPath : null));
        }
        if (ImGui.IsItemHovered())
            UiText.TooltipWrapped("Opens Select Map: Random, or one of your maps made for this game's table size.", 320f);

        if (!custom)
        {
            ImGui.TextUnformatted("Terrain");
            ImGui.SetNextItemWidth(-1f);
            if (UiCombo.Begin("##terrainmode", TerrainModeLabel(_terrainMode)))
            {
                foreach (ETerrainPlacementMode mode in new[]
                         {
                             ETerrainPlacementMode.AutoFromLayout,
                             ETerrainPlacementMode.Alternating,
                             ETerrainPlacementMode.AlternatingPoints,
                         })
                {
                    if (ImGui.Selectable(TerrainModeLabel(mode), mode == _terrainMode))
                    {
                        _terrainMode = mode;
                        if (_plan != null) { _plan.TerrainMode = mode; SavePlan(); }
                    }
                    if (ImGui.IsItemHovered()) UiText.TooltipWrapped(TerrainModeHelp(mode), 320f);
                }
                ImGui.EndCombo();
            }
        }
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
        LobbyScreen.PendingChallengeBonuses = _enemy?.StrikeTeam != null ? info.ChallengeBonuses : EChallengeBonuses.None;
        // #465: the plan's deployment style and mission type.
        if (_plan != null)
        {
            LobbyScreen.PendingDeploymentStyle = _plan.DeploymentStyle;
            LobbyScreen.PendingMissionCardGame = _plan.MissionCards;
        }
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
