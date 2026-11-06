using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The campaign screen (main menu > Firefight Campaign). Kai's layout call, 2026-10-30: three columns
/// with nothing behind a tab - the roster, the selected unit, and the next mission - plus a card grid
/// for recruiting.
/// <list type="bullet">
/// <item><b>List</b> - the campaign armies on disk, and starting a new one.</item>
/// <item><b>Camp</b> - roster | selected unit (with its upgrades) | next mission. Everything visible.</item>
/// <item><b>Recruit</b> - one card per unit the book sells, with a cart along the bottom.</item>
/// </list>
/// Deliberately plain: under the working agreement the ImGui that draws a layout is throwaway, so this
/// is one pass to make the campaign playable and no more. The rules behind every button live in
/// <see cref="CampaignRules"/>, <see cref="CampaignEconomy"/> and <see cref="CampaignPreGame"/>, which
/// are the permanent half and are tested on their own.
/// </summary>
public sealed class StrikeTeamScreen : IAppScreen
{
    public Action? OnBack;
    /// <summary>
    /// #435 - "Play next mission": opens Mission Setup for this campaign army. The whole launch - which
    /// units come, the rival's force, the map, the host - belongs to
    /// <see cref="MissionSetupScreen"/> now; this screen only says which campaign.
    /// <para>
    /// Carries the other campaign armies of the same game alongside the path, because they are what set
    /// the mission cap (#426) and only this screen has the full list on disk.
    /// </para>
    /// </summary>
    public Action<string, List<CampaignEntrant>>? OnPlay;

    internal enum EView { List, New, Camp, Recruit, Journal }

    // #448 - the journal's edit buffers. Kept apart from the campaign file and only committed through
    // CampaignJournal when a box loses focus, the mission changes or the view is left - committing on
    // every keystroke would run Clean() mid-typing and eat a trailing space before the next word.
    private string _journalCampaignBuf = "";
    private string _journalMissionBuf = "";
    private int _journalMission = 1;
    private EView _view = EView.List;

    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> _all = new();

    /// <summary>
    /// The player's campaign armies, across all four systems with a campaign book. Bot armies (#428)
    /// are campaign armies too and live in the same folder - the lobby has to be able to pick them up -
    /// but they are the opponent, not something to manage, so they are kept out of this list and shown
    /// instead on the camp screen of the campaign they belong to.
    /// </summary>
    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> Teams =>
        _all.Where(a => a.Army.StrikeTeam is { IsBot: false }).ToList();

    /// <summary>
    /// The ordinary armies a new campaign may start from. Scoped to the system being created under
    /// (#426): every mode has its own army-builder stats, so a Firefight list is not a legal start for
    /// a Grimdark Future campaign even though both folders sit under armies\.
    /// </summary>
    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> Armies =>
        _all.Where(a => a.Army.StrikeTeam == null && a.System == _newSystem).ToList();

    /// <summary>This campaign's standing enemy (#428), or null when it has none.</summary>
    private (string Path, BuiltArmyFile Army, EGameSystem System)? EnemyOf(StrikeTeamInfo info) =>
        info.EnemyArmyFile is { } path
            ? _all.Cast<(string Path, BuiltArmyFile Army, EGameSystem System)?>()
                  .FirstOrDefault(a => a!.Value.Path == path)
            : null;
    private int _selected = -1;
    private int _unit;
    private bool _confirmDelete;
    private bool _confirmDisband;
    private string _message = "";
    private bool _messageIsError;

    private string _newName = "";
    private int _newArmy = -1;
    /// <summary>Which game's campaign is being created (#426). Its price list, its armies folder.</summary>
    private EGameSystem _newSystem = EGameSystem.GrimdarkFuture;
    /// <summary>#427 - the books' No Permadeath option, picked at creation and fixed afterwards.</summary>
    private bool _newNoPermadeath;

    /// <summary>
    /// #445 - the end condition for the campaign being created. Only Endless and PointLimited are
    /// offered (campaign-flow-spec section 3a); the book's mission- and week-limited types are
    /// deliberately left out.
    /// </summary>
    private ECampaignType _newType = ECampaignType.PointLimited;

    /// <summary>
    /// #445 - the VP target, when the type is PointLimited. Starts at 7, the top of the book's own
    /// D3+4 range, because a target is the default choice and 7 is the longest of the three.
    /// </summary>
    private int _newVpTarget = 7;

    /// <summary>Recruits waiting to be confirmed: the roster unit id, and whether it is the 5-XP one.</summary>
    private readonly List<string> _cart = new();
    private bool _cartVeteran;
    /// <summary>One new Hero per mission: set when a Hero is recruited, cleared after a mission.</summary>
    private bool _heroRecruited;

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    private static readonly Random Rng = new();
    private static int D6() => Rng.Next(1, 7);

    /// <summary>
    /// Opened from a system's mode screen (Kai's mock, 2026-11-02), so "Campaign" under Grimdark Future
    /// starts a Grimdark Future campaign rather than whatever was picked last. Only the CREATE default
    /// is scoped: the list still shows every campaign on the machine, because hiding the others would
    /// mean a player who picked the wrong tile thinks their campaign is gone. The per-mode campaign
    /// screens in claude/campaign-flow-spec.md sections 1-4 are the real answer and are not built yet.
    /// </summary>
    public void Open(EGameSystem system) => _newSystem = system;

    private bool _returnToCamp;

    /// <summary>The next time this screen is shown, open the selected campaign's camp rather than the
    /// list - Mission Setup's Back (it went to the campaign list - found 2026-11-06).</summary>
    public void ReturnToCampOnShow() => _returnToCamp = true;

    public void OnShown()
    {
        Reload();
        if (_returnToCamp)
        {
            _returnToCamp = false;
            if (Current != null) { _view = EView.Camp; return; }
        }
        // #452: back from the Army Forge in the middle of making a campaign - stay in the wizard.
        if (_returningFromForge)
        {
            _returningFromForge = false;
            _view = EView.New;
            return;
        }
        _view = EView.List;
        _message = "";
    }

    private void Reload()
    {
        ArmyPaths.EnsureGameTypeFolders();
        _all = StrikeTeamFiles.LoadAllSystems();
        int count = Teams.Count;
        if (_selected >= count) _selected = count - 1;
        if (_selected < 0 && count > 0) _selected = 0;
        _confirmDelete = false;
        _confirmDisband = false;
    }

    private (string Path, BuiltArmyFile Army, EGameSystem System)? Current
    {
        get { var t = Teams; return _selected >= 0 && _selected < t.Count ? t[_selected] : null; }
    }

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    // -- Whose army (#455) ----------------------------------------------------------------------------
    //
    // campaign-flow-spec section 4, Kai's call confirmed 2026-11-05: "the bot never spends its own points".
    // The player runs BOTH armies' books between missions - casualties and XP happen to both by themselves,
    // but the enemy's trait picks, upgrades and recruits are the player's job, like running an opponent in
    // a solo game. So the camp screen can point at either army, and must make obvious which one it is.

    /// <summary>True while the camp screen is managing this campaign's enemy rather than the player.</summary>
    private bool _managingEnemy;

    /// <summary>The army the camp screen's roster, unit panel and recruiting are working on: the player's,
    /// or - when <see cref="_managingEnemy"/> - the campaign's standing enemy. The mission column and the
    /// journal always belong to the player and read <see cref="Current"/>.</summary>
    private (string Path, BuiltArmyFile Army, EGameSystem System)? Managed =>
        _managingEnemy && Current is { } c && EnemyOf(c.Army.StrikeTeam!) is { } enemy ? enemy : Current;

    /// <summary>Whether the managed army really is the enemy (false when it was asked for but there is none).</summary>
    private bool ManagingEnemyNow => _managingEnemy && Current is { } c && EnemyOf(c.Army.StrikeTeam!) != null;

    /// <summary>
    /// The book allows one new Hero per army per mission. Tracked per side, since the player now recruits
    /// for both: buying the enemy a Hero must not use up the player's allowance.
    /// </summary>
    private bool _enemyHeroRecruited;
    private bool HeroRecruitedForManaged
    {
        get => ManagingEnemyNow ? _enemyHeroRecruited : _heroRecruited;
        set { if (ManagingEnemyNow) _enemyHeroRecruited = value; else _heroRecruited = value; }
    }

    private static readonly Vector4 EnemyTint = new(1f, 0.45f, 0.40f, 1f);

    /// <summary>The Your army / Enemy army switch, at the top of the roster column.</summary>
    private void DrawWhoseArmy()
    {
        bool hasEnemy = Current is { } c && EnemyOf(c.Army.StrikeTeam!) != null;
        if (UiButton.NavigateSmall(ManagingEnemyNow ? "  Your army  " : "> Your army <")) SwitchSide(false);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasEnemy);
        if (UiButton.NavigateSmall(ManagingEnemyNow ? "> Enemy army <" : "  Enemy army  ")) SwitchSide(true);
        ImGui.EndDisabled();
        if (!hasEnemy) UiText.Disabled("This campaign has no standing enemy.");
        else if (ManagingEnemyNow)
            UiText.Colored(EnemyTint, "You are doing the ENEMY's books: its trait picks, upgrades and recruits.");
        else if (Current is { } me && EnemyOf(me.Army.StrikeTeam!) is { } foe)
        {
            // The bot no longer makes its own picks, so the player has to be told they are waiting.
            int waiting = foe.Army.Units.Sum(u => CampaignRules.Sheet(u)?.Pending.Count ?? 0);
            UiText.Disabled($"The enemy has {foe.Army.StrikeTeam!.BankedPoints} pts unspent - switch to Enemy army to spend them.");
            if (waiting > 0) UiText.Colored(EnemyTint, $"The enemy has {waiting} choice(s) waiting on you.");
        }
        ImGui.Separator();
    }

    private void SwitchSide(bool enemy)
    {
        if (_managingEnemy == enemy) return;
        _managingEnemy = enemy;
        _unit = 0;
        _cart.Clear();
        _cartVeteran = false;
        _renameFor = null;
        _message = "";
    }

    private void SaveCurrent(string path, BuiltArmyFile army)
    {
        if (!StrikeTeamFiles.Save(path, army)) Say("Couldn't save the campaign army file.", true);
    }

    private static CampaignNumbers NumbersFor(BuiltArmyFile army) =>
        CampaignNumbers.For(army.StrikeTeam?.GameSystem ?? EGameSystem.GFFirefight);

    // -- Frame -------------------------------------------------------------------------------------

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##Campaign", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = screenW * 0.02f;
        float titlePx = screenH * 0.045f;
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();

        (string title, string sub) = HeaderText();
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.03f), U(TextBright), title);
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.03f + titlePx + 4f), U(TextDim), sub);

        float top = screenH * 0.125f;
        float bottomBarH = screenH * 0.085f;
        float bodyH = screenH - top - bottomBarH;
        float w = screenW - margin * 2f;

        switch (_view)
        {
            case EView.New: DrawNew(margin, top, w, bodyH); break;
            case EView.Camp: DrawCamp(margin, top, w, bodyH); break;
            case EView.Recruit: DrawRecruit(margin, top, w, bodyH); break;
            case EView.Journal: DrawJournal(margin, top, w, bodyH); break;
            default: DrawList(margin, top, w, bodyH); break;
        }
        DrawBottomBar(screenW, screenH, margin, bottomBarH);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private (string, string) HeaderText()
    {
        if (_view == EView.New)
            return _newStep switch
            {
                ENewStep.NameYours => ("NEW CAMPAIGN - YOUR ARMY",
                    $"{GameSystemInfo.DisplayName(_newSystem)}. Name your army, then choose your enemy."),
                ENewStep.NameEnemy => ("NEW CAMPAIGN - YOUR ENEMY",
                    $"{GameSystemInfo.DisplayName(_newSystem)}. Name the enemy army, then Finish to start the campaign."),
                _ => ("NEW CAMPAIGN",
                    $"{GameSystemInfo.DisplayName(_newSystem)}. How it ends, how losses work, the story, and your army."),
            };
        if (Current is { } cur && _view is EView.Camp or EView.Recruit or EView.Journal)
        {
            StrikeTeamInfo info = cur.Army.StrikeTeam!;
            string head = _view switch
            {
                EView.Recruit => "RECRUIT - " + (ManagingEnemyNow && Managed is { } m
                    ? "FOR THE ENEMY: " + m.Army.StrikeTeam!.Name.ToUpperInvariant()
                    : info.Name.ToUpperInvariant()),
                EView.Journal => "JOURNAL - " + info.Name.ToUpperInvariant(),
                _ => info.Name.ToUpperInvariant(),
            };
            // #445: the standing's own wording ("First to 7 VP" / "Endless") rather than the enum's
            // name, which printed "PointLimited campaign" in the header.
            return (head,
                $"{GameSystemInfo.DisplayName(info.GameSystem)}  -  {StandingOf(cur.Army).Describe()}" +
                (info.NoPermadeath ? " (No Permadeath)" : "") + "  -  " +
                $"{info.Matches} mission(s) played  -  treasury {info.BankedPoints} pts  -  {info.Vp} VP  -  army {cur.Army.TotalPoints} pts");
        }
        return ("CAMPAIGN", "A campaign army keeps its units, XP, traits and injuries from mission to mission.");
    }

    // -- The list of campaign armies ----------------------------------------------------------------

    /// <summary>
    /// campaign-flow-spec section 2: the list on the left, the selected campaign on the right. The
    /// detail panel is the campaign's scoreboard - both armies, both scores, both treasuries - which is
    /// what makes a campaign read as a rivalry instead of a filename.
    /// </summary>
    private void DrawList(float x, float y, float w, float h)
    {
        float gap = 12f;
        float listW = w * 0.44f;

        DrawTeamList(x, y, listW, h);

        ImGui.SetCursorPos(new Vector2(x + listW + gap, y));
        ImGui.BeginChild("##campaignDetail", new Vector2(w - listW - gap, h), ImGuiChildFlags.Borders);
        if (Current is { } sel) DrawCampaignDetail(sel.Army);
        else UiText.Disabled(Teams.Count == 0
            ? "No campaigns yet. \"New Campaign\" starts one."
            : "Pick a campaign on the left.");
        ImGui.EndChild();
    }

    private void DrawTeamList(float x, float y, float w, float h)
    {
        var teams = Teams;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##teamList", new Vector2(w, h), ImGuiChildFlags.Borders);
        if (teams.Count == 0) UiText.Disabled("No campaign armies yet.\nClick \"New Campaign\".");
        for (int i = 0; i < teams.Count; i++)
        {
            StrikeTeamInfo info = teams[i].Army.StrikeTeam!;
            bool pending = teams[i].Army.Units.Any(u => CampaignRules.Sheet(u)?.Pending.Count > 0);
            // #445 - a finished campaign says so here as well as on its camp screen. Without it the
            // list is a row of campaigns that all look playable and one of them silently is not.
            CampaignPreGame.CampaignStanding standing = StandingOf(teams[i].Army);
            string vp = standing.HasTarget ? $"{info.Vp}/{standing.VpTarget} VP" : $"{info.Vp} VP";
            string result = standing.Outcome switch
            {
                ECampaignOutcome.YouWon => "   WON",
                ECampaignOutcome.RivalWon => "   LOST",
                ECampaignOutcome.Draw => "   DRAWN",
                _ => "",
            };
            string line = $"{info.Name}{result}\n   {GameSystemInfo.DisplayName(teams[i].System)} - {vp} - "
                + $"{info.Matches} mission(s) - army {teams[i].Army.TotalPoints} pts - treasury {info.BankedPoints} pts"
                + (pending ? "   (choices waiting)" : "");
            if (ImGui.Selectable(line + $"##t{i}", i == _selected, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
            {
                _selected = i;
                _unit = 0;
                _confirmDelete = false;
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenCamp(i);
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// The selected campaign, as campaign-flow-spec section 2 asks for it: both army names, both VP
    /// against the target, both treasuries INCLUDING the unspent balance, the next mission number, the
    /// win/loss record, and who is sitting out injured.
    ///
    /// <para>
    /// The rival's unspent balance is on here because the spec is emphatic about it, and the reason is
    /// worth keeping: the bot earns treasury by the book whether or not it is spent, so without the
    /// number on screen it banks quietly and then one mission the difficulty jumps for no visible
    /// reason.
    /// </para>
    /// </summary>
    private void DrawCampaignDetail(BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        var foe = EnemyOf(info);
        StrikeTeamInfo? foeInfo = foe?.Army.StrikeTeam;
        CampaignPreGame.CampaignStanding standing = StandingOf(army);

        // A campaign is a rivalry, so the heading is both names - "Iron Reavers vs The Hollow Choir",
        // not "Campaign 3".
        UiText.Colored(TextBright, foeInfo != null ? $"{info.Name}  vs  {foeInfo.Name}" : info.Name);
        UiText.Disabled($"{GameSystemInfo.DisplayName(info.GameSystem)}"
                        + (info.NoPermadeath ? " - No Permadeath" : ""));

        // #448 - the opening of the campaign's own journal page, so picking a campaign shows its story
        // as well as its score.
        if (info.Journal.Length > 0)
        {
            const int excerpt = 280;
            string text = info.Journal.Length <= excerpt ? info.Journal : info.Journal[..excerpt].TrimEnd() + "...";
            ImGui.Spacing();
            UiText.Wrapped(text);
        }
        ImGui.Separator();

        // -- The scoreboard --
        UiText.Colored(standing.HasTarget ? Accent : TextDim, standing.Describe());
        if (standing.IsOver)
            UiText.Colored(standing.Outcome == ECampaignOutcome.YouWon ? Good : Bad,
                standing.Outcome switch
                {
                    ECampaignOutcome.YouWon => "WON",
                    ECampaignOutcome.RivalWon => "LOST",
                    _ => "DRAWN",
                });

        (int wins, int losses) = CampaignRules.RecordOf(info);
        ScoreRow(info.Name, standing.YourVp, info.BankedPoints, army.TotalPoints, wins, losses, Gold);
        if (foeInfo != null && foe is { } f)
        {
            (int foeWins, int foeLosses) = CampaignRules.RecordOf(foeInfo);
            ScoreRow(foeInfo.Name, standing.RivalVp, foeInfo.BankedPoints, f.Army.TotalPoints,
                foeWins, foeLosses, TextDim);
        }
        else
        {
            UiText.Disabled("No standing enemy, so there is nobody else scoring - add another army of "
                            + "this game and start a new campaign to get one.");
        }

        ImGui.Separator();
        if (standing.IsOver)
            UiText.Disabled($"{info.Matches} mission(s) played. This campaign is finished.");
        else
            UiText.Disabled($"Next up: mission {info.Matches + 1}. {info.Matches} played.");

        // -- Who cannot fight --
        // Only under No Permadeath does a unit come back at all; with permadeath on, a killed unit is
        // simply gone and there is never anything to list.
        var resting = army.Units
            .Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(p => p.Sheet is { MissionsOut: > 0 })
            .ToList();
        ImGui.Separator();
        UiText.Colored(TextDim, "SITTING OUT");
        if (resting.Count == 0) UiText.Disabled("Nobody - the whole army can field.");
        else
            foreach (var (unit, sheet) in resting)
                UiText.Colored(Bad, $"{unit.Name} - out for {sheet!.MissionsOut} more mission(s)");

        var waiting = army.Units.Where(u => CampaignRules.Sheet(u)?.Pending.Count > 0).ToList();
        if (waiting.Count > 0)
        {
            ImGui.Separator();
            UiText.Colored(Accent, $"{waiting.Count} unit(s) are waiting on you to pick a trait.");
            UiText.Disabled("Open the campaign to deal with them.");
        }

        if (info.History.Count > 0)
        {
            ImGui.Separator();
            UiText.Colored(TextDim, "LATELY");
            foreach (string line in Enumerable.Reverse(info.History).Take(4)) UiText.Wrapped(line);
        }
    }

    /// <summary>One army's line on the campaign scoreboard. Both sides get the same shape.</summary>
    private static void ScoreRow(string name, int vp, int treasury, int armyPoints, int wins, int losses,
                                 Vector4 nameColour)
    {
        ImGui.Spacing();
        UiText.Colored(nameColour, name);
        ImGui.TextUnformatted($"   {vp} VP   -   {wins}W / {losses}L");
        UiText.Disabled($"   Army {armyPoints} pts, unspent treasury {treasury} pts");
    }

    // -- Journal (#448) ------------------------------------------------------------------------------

    /// <summary>Opens the journal on the next mission - the page most likely to be written next.</summary>
    private void OpenJournal()
    {
        if (Current is not { } cur) return;
        StrikeTeamInfo info = cur.Army.StrikeTeam!;
        _journalCampaignBuf = info.Journal;
        _journalMission = info.Matches + 1;
        _journalMissionBuf = CampaignJournal.NoteFor(info, _journalMission);
        _view = EView.Journal;
        _message = "";
    }

    /// <summary>
    /// Writes both edit buffers into the campaign and saves the file, if anything changed. Called when a
    /// box loses focus, when the mission page changes, and when the journal is left.
    /// </summary>
    private void CommitJournal()
    {
        if (Current is not { } cur) return;
        (string path, BuiltArmyFile army, _) = cur;
        StrikeTeamInfo info = army.StrikeTeam!;

        string campaign = CampaignJournal.Clean(_journalCampaignBuf);
        string mission = CampaignJournal.Clean(_journalMissionBuf);
        if (campaign == info.Journal && mission == CampaignJournal.NoteFor(info, _journalMission)) return;

        CampaignJournal.SetCampaignNote(info, _journalCampaignBuf);
        CampaignJournal.SetNote(info, _journalMission, _journalMissionBuf);
        SaveCurrent(path, army);
    }

    /// <summary>
    /// #450 - writes the whole journal to a plain text file beside the campaign's own file, named after
    /// it, and says where. Overwrites the last export: it is a snapshot of the journal as it stands, and a
    /// folder filling up with dated copies is not something Kai asked for.
    /// </summary>
    private void ExportJournal()
    {
        CommitJournal();
        if (Current is not { } cur) return;
        (string path, BuiltArmyFile army, _) = cur;
        StrikeTeamInfo info = army.StrikeTeam!;

        string text = CampaignJournal.Export(info, EnemyOf(info)?.Army.StrikeTeam,
            GameSystemInfo.DisplayName(info.GameSystem));
        string file = Path.Combine(Path.GetDirectoryName(path) ?? "",
            Path.GetFileNameWithoutExtension(path) + " - Journal.txt");
        try
        {
            File.WriteAllText(file, text.Replace("\n", Environment.NewLine));
            Say($"Journal saved to {file}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say("Couldn't write the journal file: " + e.Message, true);
        }
    }

    /// <summary>
    /// The journal: the campaign's own page on the left, the missions on the right with the selected
    /// one's page beside them. Deliberately the cheapest version that works - Kai said he was unsure how
    /// he wants to write these, so this is something to react to in play, not a design.
    /// </summary>
    private void DrawJournal(float x, float y, float w, float h)
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        StrikeTeamInfo info = cur.Army.StrikeTeam!;

        float gap = 12f;
        float leftW = w * 0.40f;
        float listW = w * 0.16f;
        float pageW = w - leftW - listW - gap * 2f;
        float boxH = h - ImGui.GetTextLineHeightWithSpacing() * 3.5f;

        // The campaign's page.
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##journalCampaign", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, "THE CAMPAIGN");
        UiText.Disabled("The story so far - who these armies are and why they fight.");
        ImGui.InputTextMultiline("##campaignNote", ref _journalCampaignBuf, CampaignJournal.MaxNoteLength,
            new Vector2(-1f, boxH));
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitJournal();
        ImGui.EndChild();

        // The missions.
        ImGui.SetCursorPos(new Vector2(x + leftW + gap, y));
        ImGui.BeginChild("##journalMissions", new Vector2(listW, h), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, "MISSIONS");
        ImGui.Separator();
        foreach (int mission in CampaignJournal.Missions(info))
        {
            bool written = CampaignJournal.NoteFor(info, mission).Length > 0;
            string label = $"Mission {mission}" + (CampaignJournal.IsPlayed(info, mission) ? "" : " (next)")
                           + (written ? "  *" : "");
            if (ImGui.Selectable($"{label}##jm{mission}", mission == _journalMission) && mission != _journalMission)
            {
                CommitJournal();
                _journalMission = mission;
                _journalMissionBuf = CampaignJournal.NoteFor(info, mission);
            }
        }
        ImGui.EndChild();

        // The selected mission's page.
        ImGui.SetCursorPos(new Vector2(x + leftW + listW + gap * 2f, y));
        ImGui.BeginChild("##journalMission", new Vector2(pageW, h), ImGuiChildFlags.Borders);
        bool played = CampaignJournal.IsPlayed(info, _journalMission);
        UiText.Colored(TextDim, $"MISSION {_journalMission}" + (played ? "" : " - NOT PLAYED YET"));

        // #450 - the game's own record, above the player's words and not editable: it is the facts, and
        // the page below is the story told about them.
        IReadOnlyList<string> record = CampaignJournal.RecordFor(info, _journalMission);
        float recordH = 0f;
        if (record.Count > 0)
        {
            recordH = Math.Min(h * 0.38f, ImGui.GetTextLineHeightWithSpacing() * (record.Count + 2.5f));
            ImGui.BeginChild("##missionRecord", new Vector2(-1f, recordH), ImGuiChildFlags.Borders);
            UiText.Colored(Accent, "WHAT HAPPENED");
            foreach (string line in record) UiText.Wrapped("- " + line);
            ImGui.EndChild();
        }
        else if (played)
        {
            UiText.Disabled("No record - this mission was played before the game kept one.");
        }

        UiText.Disabled(played
            ? "Your account - the story of the battle."
            : "The briefing - what the army expects, before it is fought.");
        ImGui.InputTextMultiline("##missionNote", ref _journalMissionBuf, CampaignJournal.MaxNoteLength,
            new Vector2(-1f, Math.Max(ImGui.GetTextLineHeight() * 4f, boxH - recordH)));
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitJournal();
        ImGui.EndChild();
    }

    private void OpenCamp(int index)
    {
        _selected = index;
        _managingEnemy = false;   // #455: a campaign always opens on the player's own army
        _unit = 0;
        _cart.Clear();
        _cartVeteran = false;
        _view = EView.Camp;
        _message = "";
    }

    // -- Camp: three columns ------------------------------------------------------------------------

    private void DrawCamp(float x, float y, float w, float h)
    {
        if (Current is not { } cur || Managed is not { } managed) { _view = EView.List; return; }
        (string path, BuiltArmyFile army, _) = cur;
        CampaignNumbers n = NumbersFor(army);
        (string mPath, BuiltArmyFile mArmy, _) = managed;

        float gap = 12f;
        float leftW = w * 0.25f;
        float rightW = w * 0.29f;
        float midW = w - leftW - rightW - gap * 2f;

        ImGui.SetCursorPos(new Vector2(x, y));
        // #455: the enemy's columns are tinted so it is never in doubt whose books are being done.
        // Read once: DrawWhoseArmy can switch sides in between, and reading it again for the pop popped a
        // colour that was never pushed - the "Enemy army" tab crashed the game (found 2026-11-06).
        bool tinted = ManagingEnemyNow;
        if (tinted) ImGui.PushStyleColor(ImGuiCol.Border, EnemyTint);
        ImGui.BeginChild("##roster", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        DrawWhoseArmy();
        DrawRoster(mPath, mArmy);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + gap, y));
        ImGui.BeginChild("##unit", new Vector2(midW, h), ImGuiChildFlags.Borders);
        DrawUnitDetail(mPath, mArmy, n);
        ImGui.EndChild();
        if (tinted) ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(x + leftW + midW + gap * 2f, y));
        ImGui.BeginChild("##mission", new Vector2(rightW, h), ImGuiChildFlags.Borders);
        DrawNextMission(path, army, n);
        ImGui.EndChild();
    }

    // Column 1 ---------------------------------------------------------------------------------------

    private void DrawRoster(string path, BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        string whose = army.StrikeTeam?.IsBot == true ? $"ENEMY: {army.StrikeTeam.Name.ToUpperInvariant()}" : "YOUR ARMY";
        UiText.Colored(army.StrikeTeam?.IsBot == true ? EnemyTint : TextDim,
            $"{whose} - {army.Units.Count} UNIT(S), {CampaignEconomy.HeroCount(army)} HERO(ES)");
        ImGui.Separator();

        for (int i = 0; i < army.Units.Count; i++)
        {
            UnitFileEntry unit = army.Units[i];
            StrikeUnit? sheet = CampaignRules.Sheet(unit);
            string tag = sheet?.IsHero == true ? " [hero]" : "";
            // #453: a renamed unit says what it is on its second line.
            string type = sheet != null && CampaignRules.HasOwnName(unit, army.Book)
                ? CampaignRules.UnitTypeOf(unit, army.Book) + " - " : "";
            string second = sheet == null
                ? "no campaign sheet"
                : $"   {type}Lv {sheet.Level} - {sheet.Xp} XP - {unit.ModelCount} model(s) - {unit.PointCost} pts";
            if (sheet is { MissionsOut: > 0 })
                second = $"   RECOVERING - out for {sheet.MissionsOut} more mission(s)";
            if (sheet?.Pending.Count > 0) second = "   WAITING ON YOU: pick a trait";

            if (ImGui.Selectable($"{unit.Name}{tag}\n{second}##u{i}", i == _unit, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
                _unit = i;
        }

        ImGui.Separator();
        if (UiButton.Navigate("+ Recruit a unit"))
        {
            if (army.Book == null) Say("This army has no book attached, so there is nothing to recruit from.", true);
            else { _cart.Clear(); _cartVeteran = false; _view = EView.Recruit; _message = ""; }
        }
        // #448 - the campaign journal, beside the other between-missions errand.
        if (UiButton.Navigate("Journal")) OpenJournal();
        int noted = info.MissionNotes?.Count ?? 0;
        UiText.Disabled(info.Journal.Length == 0 && noted == 0
            ? "Nothing written yet."
            : $"{noted} mission note(s)" + (info.Journal.Length > 0 ? ", campaign page written" : ""));
        UiText.Disabled($"Treasury {info.BankedPoints} pts");
    }

    // Column 2 ---------------------------------------------------------------------------------------

    // #453: the rename box's text, and which unit it belongs to, so switching units reloads it rather
    // than carrying one unit's half-typed name onto the next.
    private string _renameBuf = "";
    private string? _renameFor;

    private void DrawRename(string path, BuiltArmyFile army, UnitFileEntry unit, StrikeUnit sheet)
    {
        if (_renameFor != sheet.Id) { _renameFor = sheet.Id; _renameBuf = unit.Name; }

        ImGui.SetNextItemWidth(260f);
        bool enter = ImGui.InputText("##rename", ref _renameBuf, CampaignRules.MaxUnitNameLength,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        bool changed = !string.Equals(_renameBuf.Trim(), unit.Name, StringComparison.Ordinal);
        ImGui.BeginDisabled(!changed);
        bool clicked = UiButton.NavigateSmall("Rename");
        ImGui.EndDisabled();
        if (!(enter || clicked) || !changed) return;

        string old = unit.Name;
        if (CampaignRules.Rename(army, unit, _renameBuf, army.Book) is { } why) { Say(why, true); return; }
        _renameBuf = unit.Name;
        SaveCurrent(path, army);
        Say($"{old} is now {unit.Name}.");
    }

    private void DrawUnitDetail(string path, BuiltArmyFile army, CampaignNumbers n)
    {
        if (_unit < 0 || _unit >= army.Units.Count) { UiText.Disabled("Pick a unit on the left."); return; }
        UnitFileEntry unit = army.Units[_unit];
        if (CampaignRules.Sheet(unit) is not { } sheet) { UiText.Disabled("This unit has no campaign sheet."); return; }

        // #453: the unit's own name large, what it is small underneath ("Vargas Squad" / Assault Grunts),
        // and a box to rename it.
        ImGui.SetWindowFontScale(1.35f);
        UiText.Colored(Accent, unit.Name + (sheet.IsHero ? "   [hero]" : ""));
        ImGui.SetWindowFontScale(1f);
        UiText.Disabled(CampaignRules.UnitTypeOf(unit, army.Book));
        DrawRename(path, army, unit, sheet);
        ImGui.TextUnformatted($"{unit.ModelCount} model(s)   Quality {unit.Quality}+   Defense {unit.Defense}+   " +
                              $"{unit.PointCost} pts (book {CampaignEconomy.BookCost(unit)}, campaign {sheet.CostChange:+0;-0;0})");

        int next = Math.Min(CampaignNumbers.MaxXp, (sheet.Level + 1) * CampaignNumbers.XpPerLevel);
        ImGui.TextUnformatted($"Level {sheet.Level}   {sheet.Xp} / {CampaignNumbers.MaxXp} XP" +
                              (sheet.Xp >= CampaignNumbers.MaxXp ? "   (fully levelled)" : $"   next level at {next}")
                              + $"   {sheet.Games} game(s)");
        ImGui.Separator();

        Line("Traits", sheet.Traits.Select(t => t.Replace("Trait: ", "")));
        if (sheet.IsHero)
        {
            Line("Skill sets", sheet.SkillSets);
            Line("Skills", sheet.Skills);
            Line("Talents", sheet.Talents);
        }
        Line("Injuries", sheet.Injuries);
        Line("Weapons", unit.Weapons.Select(Describe));

        foreach (string pending in sheet.Pending.ToList()) DrawChoice(path, army, unit, sheet, pending);

        ImGui.Separator();
        DrawUpgrades(path, army, unit, n);

        ImGui.Separator();
        if (UiButton.Back(_confirmDisband ? "Really disband?" : "Disband unit"))
        {
            if (_confirmDisband)
            {
                CampaignPurchase result = CampaignEconomy.Disband(army, unit);
                army.StrikeTeam!.History.Add(result.Message);
                SaveCurrent(path, army);
                Say(result.Message);
                _confirmDisband = false;
                _unit = Math.Max(0, _unit - 1);
                Reload();
            }
            else _confirmDisband = true;
        }
        if (_confirmDisband) { ImGui.SameLine(); UiText.Colored(Bad, "Its XP and upgrades are gone, and nothing comes back."); }
    }

    private static string Describe(WeaponFileEntry w)
    {
        string range = w.RangeInches > 0 ? $"{w.RangeInches}\", " : "";
        return $"{w.Name} ({range}A{w.Attacks})";
    }

    private static void Line(string label, IEnumerable<string> items)
    {
        string text = string.Join(", ", items);
        UiText.Colored(TextDim, label + ":");
        ImGui.SameLine();
        if (text.Length == 0) UiText.Disabled("none"); else UiText.Wrapped(text);
    }

    private void DrawChoice(string path, BuiltArmyFile army, UnitFileEntry unit, StrikeUnit sheet, string pending)
    {
        string what = pending switch
        {
            CampaignTables.Specialist => "Specialist: +1 to hit in melee, or when shooting?",
            CampaignTables.FastLearner => "Fast Learner: pick any other trait.",
            CampaignTables.LuckyBreak => "Lucky Break: pick any other injury.",
            CampaignTables.LearnedLesson => "Learned Lesson: pick any other talent.",
            _ => "Pick a skill set (its first skill is rolled straight away).",
        };
        UiText.Colored(Gold, what);
        foreach (string option in CampaignRules.Options(sheet, pending))
        {
            ImGui.SameLine();
            if (UiButton.NavigateSmall(option.Replace("Trait: ", "") + "##" + pending))
            {
                string line = CampaignRules.Choose(unit, pending, option, D6);
                army.StrikeTeam!.History.Add(line);
                SaveCurrent(path, army);
                Say(line);
            }
        }
    }

    /// <summary>
    /// The unit's upgrade sections, one row per option, priced by what the change would cost. Every
    /// price and every purchase goes through <see cref="CampaignEconomy"/> - this only draws it.
    /// </summary>
    private void DrawUpgrades(string path, BuiltArmyFile army, UnitFileEntry unit, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "UPGRADES");
        if (army.Book == null) { UiText.Disabled("No book attached to this army."); return; }
        if (CampaignEconomy.Loadout(unit) is not { } loadout)
        {
            UiText.Disabled("This unit wasn't built from a book, so its upgrades can't be changed.");
            return;
        }
        RosterUnit? roster = army.Book.Units.FirstOrDefault(r => r.Id == loadout.RosterUnitId);
        if (roster == null) { UiText.Disabled("The book no longer has this unit."); return; }
        if (roster.Sections.Count == 0) { UiText.Disabled("Nothing to buy for this unit."); return; }

        int treasury = army.StrikeTeam!.BankedPoints;
        foreach (UpgradeSection section in roster.Sections)
        {
            ImGui.PushID(section.Id);
            // A "Replace X" section on a unit that no longer carries X has nothing to buy - the Army Forge
            // says so, and the camp offered "(-0 pts)" buttons that did nothing (2026-11-06).
            bool nothingToReplace = section.Variant == UpgradeVariant.Replace
                && ArmyForgeScreen.AvailableExcludingSection(army.Book, loadout, section) == 0;
            UiText.Disabled(nothingToReplace ? $"{section.Label}  (none to replace)" : section.Label);
            foreach (UpgradeOption option in section.Options)
            {
                bool owned = loadout.Choices.Any(c => c.SectionId == section.Id && c.OptionId == option.Id);
                if (nothingToReplace && !owned) continue;
                BuilderUnit wanted = Toggled(loadout, section.Id, option.Id, !owned);
                int price = CampaignEconomy.PriceOfChange(army.Book, unit, wanted);
                bool affordable = price <= treasury;

                // What it costs or gives back, in plain words: "Buy X (-40 pts)" read as a discount.
                string label = owned
                    ? (price > 0 ? $"Drop {option.Label} (costs {price} pts)" : price < 0 ? $"Sell {option.Label} (get {-price} pts back)" : $"Drop {option.Label} (free)")
                    : (price > 0 ? $"Buy {option.Label} ({price} pts)" : price < 0 ? $"Take {option.Label} (get {-price} pts back)" : $"Take {option.Label} (free)");

                ImGui.BeginDisabled(!owned && !affordable);
                if (UiButton.NavigateSmall(label + "##" + option.Id))
                {
                    CampaignPurchase result = CampaignEconomy.ChangeUpgrades(army, unit, army.Book, wanted);
                    if (result.Ok) { army.StrikeTeam.History.Add(result.Message); SaveCurrent(path, army); }
                    Say(result.Message, !result.Ok);
                }
                ImGui.EndDisabled();
                if (!owned && !affordable)
                {
                    ImGui.SameLine();
                    UiText.Disabled($"needs {price - treasury} more");
                }
                if (option.CostUnpriced)
                {
                    ImGui.SameLine();
                    UiText.Disabled("(OPR publishes no price for this one)");
                }
            }
            ImGui.PopID();
        }
    }

    /// <summary>A copy of the loadout with one option switched on or off. One pick per section.</summary>
    private static BuilderUnit Toggled(BuilderUnit from, string sectionId, string optionId, bool on)
    {
        var copy = new BuilderUnit
        {
            RosterUnitId = from.RosterUnitId,
            ModelCount = from.ModelCount,
            Id = from.Id,
            JoinsUnitId = from.JoinsUnitId,
            CombinedWithId = from.CombinedWithId,
            Choices = from.Choices.Where(c => c.SectionId != sectionId)
                .Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count })
                .ToList(),
        };
        if (on) copy.Choices.Add(new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 });
        return copy;
    }

    // Column 3 ---------------------------------------------------------------------------------------

    /// <summary>
    /// #445 - where this campaign stands. One helper rather than two call sites working it out, because
    /// the mission column and the "Play next mission" button must never disagree about whether the
    /// campaign is finished.
    /// </summary>
    private CampaignPreGame.CampaignStanding StandingOf(BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        return CampaignPreGame.Standing(info, EnemyOf(info)?.Army.StrikeTeam);
    }

    private void DrawNextMission(string path, BuiltArmyFile army, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        CampaignPreGame.CampaignStanding standing = StandingOf(army);

        // #445 - the campaign scoreboard, above the mission. A campaign is a rivalry, so both totals
        // belong together and in the same place every time: reading your own VP without theirs says
        // nothing about whether you are winning.
        UiText.Colored(TextDim, "CAMPAIGN");
        ImGui.Separator();
        UiText.Colored(standing.HasTarget ? Accent : TextDim, standing.Describe());
        string rivalName = EnemyOf(info)?.Army.StrikeTeam?.Name ?? "No enemy yet";
        ImGui.TextUnformatted($"{info.Name}: {standing.YourVp} VP");
        UiText.Colored(TextDim, $"{rivalName}: {standing.RivalVp} VP");
        UiText.Disabled($"Mission {info.Matches + 1} - {info.Matches} played");

        if (standing.IsOver)
        {
            ImGui.Separator();
            UiText.Colored(standing.Outcome == ECampaignOutcome.YouWon ? Good : Bad,
                standing.Outcome switch
                {
                    ECampaignOutcome.YouWon => "CAMPAIGN WON",
                    ECampaignOutcome.RivalWon => "CAMPAIGN LOST",
                    _ => "CAMPAIGN DRAWN",
                });
            UiText.Wrapped(standing.Outcome switch
            {
                ECampaignOutcome.YouWon =>
                    $"{info.Name} reached {standing.VpTarget} VP. Nothing more to play - start a new "
                    + "campaign, or keep this one around as the record of it.",
                ECampaignOutcome.RivalWon =>
                    $"{rivalName} reached {standing.VpTarget} VP first.",
                _ => $"Both armies reached {standing.VpTarget} VP in the same mission on equal VP. The "
                     + "book has no tiebreak for that, so it stands as a draw.",
            });
            // Everything below is about a mission that is not going to happen.
            return;
        }

        UiText.Colored(TextDim, "NEXT MISSION");
        ImGui.Separator();

        // Special objective
        UiText.Colored(TextDim, "Your special objective");
        if (info.SpecialObjective.Length == 0)
        {
            UiText.Disabled("Not rolled yet.");
            if (UiButton.NavigateSmall("Roll it"))
            {
                bool caster = army.Units.Any(u => u.SpecialRules.Any(r => r.PrintableName.Contains("Caster", StringComparison.OrdinalIgnoreCase)));
                info.SpecialObjective = CampaignPreGame.RollSpecialObjective(
                    new CampaignEntrant(info.Name, army.TotalPoints, caster), D6);
                SaveCurrent(path, army);
                Say($"Special objective: {info.SpecialObjective}.");
            }
        }
        else
        {
            UiText.Colored(Gold, info.SpecialObjective);
            UiText.Wrapped(CampaignPreGame.ObjectiveText(info.SpecialObjective));
            UiText.Disabled($"Worth +{n.SpecialObjectiveBonus} pts into your treasury.");
        }

        ImGui.Separator();

        // Who is coming
        // Only campaign armies of the SAME game can meet across a table, so only they set the cap
        // (#426): a 300-pt Skirmish army would otherwise drag a Grimdark Future mission's limit down to
        // 625 pts, and the two can never play each other.
        var others = Teams.Where(t => t.Path != path && t.Army.StrikeTeam!.GameSystem == info.GameSystem)
            .Select(t => new CampaignEntrant(t.Army.StrikeTeam!.Name, t.Army.TotalPoints, false)).ToList();
        int bringing = 0;
        UiText.Colored(TextDim, "Who is coming");
        foreach (UnitFileEntry unit in army.Units)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) continue;

            // #427: a unit recovering under No Permadeath cannot be taken, and says why rather than
            // just refusing the click.
            if (CampaignRules.CannotBringToMission(unit) is { } resting)
            {
                ImGui.PushID(sheet.Id);
                UiText.Disabled($"{unit.Name}  ({unit.PointCost} pts)");
                ImGui.SameLine();
                UiText.Colored(Bad, resting);
                ImGui.PopID();
                continue;
            }

            bool coming = info.Bringing.Count == 0 || info.Bringing.Contains(sheet.Id);
            bool was = coming;
            ImGui.PushID(sheet.Id);
            if (UiButton.Checkbox($"{unit.Name}  ({unit.PointCost} pts)", ref coming) && coming != was)
            {
                if (info.Bringing.Count == 0)
                    info.Bringing = army.Units.Select(u => CampaignRules.Sheet(u)?.Id ?? "").Where(s => s.Length > 0).ToList();
                if (coming) { if (!info.Bringing.Contains(sheet.Id)) info.Bringing.Add(sheet.Id); }
                else info.Bringing.Remove(sheet.Id);
                SaveCurrent(path, army);
            }
            ImGui.PopID();
            if (coming) bringing += unit.PointCost;
        }

        // Recovering units are never counted: StartMission filters them out the same way, so the
        // number on screen is the number that will actually deploy (#427).
        int comingCount = army.Units.Count(u => CampaignRules.Sheet(u) is { } sh
            && CampaignRules.CanBringToMission(u)
            && (info.Bringing.Count == 0 || info.Bringing.Contains(sh.Id)));
        if (others.Count == 0)
        {
            ImGui.TextUnformatted($"Bringing {bringing} pts in {comingCount} unit(s).");
            UiText.Disabled($"No other {GameSystemInfo.DisplayName(info.GameSystem)} campaign army on this computer, "
                            + "so there is no cap to measure against yet.");
        }
        else
        {
            int cap = CampaignPreGame.MaxPointsForMission(others, n);
            ImGui.TextUnformatted($"Bringing {bringing} / {cap} pts in {comingCount} unit(s).");
            string? why = CampaignPreGame.CannotBring(bringing, others, n, comingCount);
            if (why != null) UiText.Colored(Bad, why);
            else UiText.Disabled($"The cap is {n.ArmyCapOverLowest} pts over the smallest army.");
        }

        // #428: the standing enemy. The player never manages it, but they should be able to watch it
        // grow - a campaign whose opponent improves invisibly is indistinguishable from one whose
        // opponent does not improve at all.
        ImGui.Separator();
        UiText.Colored(TextDim, "Your enemy");
        if (EnemyOf(info) is { } foe && foe.Army.StrikeTeam is { } foeInfo)
        {
            UiText.Colored(Gold, foeInfo.Name);
            int cap = bringing + n.ArmyCapOverLowest;
            int foeBrings = foe.Army.Units.Where(CampaignRules.CanBringToMission).Sum(u => u.PointCost);
            ImGui.TextUnformatted($"{foe.Army.Units.Count} unit(s), {foe.Army.TotalPoints} pts, "
                                  + $"treasury {foeInfo.BankedPoints} pts, {foeInfo.Vp} VP");
            UiText.Disabled(foeBrings > cap
                ? $"It will bring its best up to {cap} pts ({n.ArmyCapOverLowest} over what you bring)."
                : "It will bring everything it has.");
            int levels = foe.Army.Units.Sum(u => CampaignRules.Sheet(u)?.Level ?? 0);
            if (levels > 0) UiText.Disabled($"{levels} level(s) earned across its army so far.");
        }
        else
        {
            UiText.Disabled("This campaign has no standing enemy, so the lobby rolls a fresh one each "
                            + "mission and nothing carries over on their side.");
        }

        ImGui.Separator();
        UiText.Colored(TextDim, "How to play it");
        UiText.Wrapped("\"Play next mission\" opens a lobby with your army already picked"
                       + (EnemyOf(info) != null ? " and your enemy already seated" : "")
                       + $", set to {GameSystemInfo.DisplayName(info.GameSystem)} with the points limit lifted "
                       + "clear of both armies. Press LAUNCH when the table settings suit you. The result is "
                       + "written back here after the game.");

        if (info.History.Count > 0)
        {
            ImGui.Separator();
            UiText.Colored(TextDim, "Last mission");
            foreach (string line in Enumerable.Reverse(info.History).Take(6)) UiText.Wrapped(line);
        }
    }

    // -- Recruit: the card grid ---------------------------------------------------------------------

    private void DrawRecruit(float x, float y, float w, float h)
    {
        if (Managed is not { } cur || cur.Army.Book is not { } book) { _view = EView.Camp; return; }
        (string path, BuiltArmyFile army, _) = cur;
        CampaignNumbers n = NumbersFor(army);
        StrikeTeamInfo info = army.StrikeTeam!;

        // Room for everything the cart draws - its list, the veteran tick, the total, the warning and the
        // Confirm button. Four text lines left Confirm below the panel's edge (found 2026-11-06).
        float cartH = ImGui.GetTextLineHeightWithSpacing() * 4f + ImGui.GetFrameHeightWithSpacing() * 2f
            + ImGui.GetStyle().WindowPadding.Y * 2f;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##cards", new Vector2(w, h - cartH - 10f), ImGuiChildFlags.Borders);

        int columns = Math.Max(1, (int)(w / 330f));
        float cardW = (w - 24f) / columns - 10f;
        float cardH = ImGui.GetTextLineHeightWithSpacing() * 9f;

        int drawn = 0;
        foreach (RosterUnit roster in book.Units)
        {
            if (drawn % columns != 0) ImGui.SameLine();
            drawn++;
            ImGui.PushID(roster.Id);
            ImGui.BeginChild("##card" + roster.Id, new Vector2(cardW, cardH), ImGuiChildFlags.Borders);
            DrawCard(path, army, book, roster, n, info);
            ImGui.EndChild();
            ImGui.PopID();
        }
        if (drawn == 0) UiText.Disabled("This book has no units.");
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x, y + h - cartH));
        ImGui.BeginChild("##cart", new Vector2(w, cartH), ImGuiChildFlags.Borders);
        DrawCart(path, army, book, n);
        ImGui.EndChild();
    }

    private void DrawCard(string path, BuiltArmyFile army, BookFile book, RosterUnit roster,
                          CampaignNumbers n, StrikeTeamInfo info)
    {
        bool isHero = roster.Rules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });
        UiText.Colored(Accent, roster.Name + (isHero ? "   [hero]" : ""));
        ImGui.TextUnformatted($"Quality {roster.Quality}+   Defense {roster.Defense}+   {roster.BaseModelCount} model(s)");

        WeaponFileEntry? ranged = roster.Weapons.Where(wp => wp.RangeInches > 0).OrderByDescending(wp => wp.RangeInches).FirstOrDefault();
        WeaponFileEntry? melee = roster.Weapons.Where(wp => wp.RangeInches == 0).OrderByDescending(wp => wp.Attacks).FirstOrDefault();
        UiText.Disabled(ranged != null ? "Ranged: " + Describe(ranged) : "Ranged: none");
        UiText.Disabled(melee != null ? "Melee: " + Describe(melee) : "Melee: none");
        // Kai, 2026-11-02: every rule name in the game is gold, underlined and hoverable, so the recruit
        // card's rule list goes through the Forge's flow rather than printing plain text.
        var shown = roster.Rules.Take(4).ToList();
        if (shown.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(shown), RuleGlossary.Build(book), ImGuiCol.TextDisabled);
        else
            UiText.Wrapped("No special rules");

        ImGui.TextUnformatted($"{roster.BasePointCost} pts");

        var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        string? why = CampaignEconomy.CannotRecruit(army, book, wanted, isHero && HeroRecruitedForManaged);
        int inCart = _cart.Count(c => c == roster.Id);

        ImGui.BeginDisabled(why != null);
        if (UiButton.NavigateSmall(inCart > 0 ? $"In cart x{inCart} - add another" : "Recruit"))
            _cart.Add(roster.Id);
        ImGui.EndDisabled();
        if (why != null) UiText.Colored(Bad, why);
        if (inCart > 0)
        {
            ImGui.SameLine();
            if (UiButton.NavigateSmall("-##rm" + roster.Id)) _cart.Remove(roster.Id);
        }
    }

    private void DrawCart(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        int total = 0;
        foreach (string id in _cart)
            total += book.Units.FirstOrDefault(r => r.Id == id)?.BasePointCost ?? 0;
        if (_cartVeteran) total += n.VeteranRecruitCost;

        if (_cart.Count == 0) UiText.Disabled("Nothing in the cart yet - pick a card above.");
        else UiText.Wrapped("In the cart: " + string.Join(", ",
            _cart.Select(id => book.Units.FirstOrDefault(r => r.Id == id)?.Name ?? id)));

        bool veteran = _cartVeteran;
        if (UiButton.Checkbox($"One recruit starts at 5 XP (+{n.VeteranRecruitCost} pts)", ref veteran)) _cartVeteran = veteran;

        ImGui.TextUnformatted($"Total {total} pts   -   treasury {info.BankedPoints} pts   -   leaves {info.BankedPoints - total} pts");
        if (total > info.BankedPoints) UiText.Colored(Bad, "That is more than the treasury holds.");

        ImGui.BeginDisabled(_cart.Count == 0 || total > info.BankedPoints);
        if (UiButton.Confirm("Confirm recruits")) ConfirmRecruits(path, army, book, n);
        ImGui.EndDisabled();
    }

    private void ConfirmRecruits(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        bool veteranLeft = _cartVeteran;
        var lines = new List<string>();
        foreach (string id in _cart.ToList())
        {
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == id);
            if (roster == null) continue;
            bool isHero = roster.Rules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });
            var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, wanted, n,
                veteran: veteranLeft, heroAlreadyRecruitedThisMission: isHero && HeroRecruitedForManaged, d6: D6);
            lines.Add(result.Message);
            if (!result.Ok) continue;
            if (veteranLeft) veteranLeft = false;
            if (isHero) HeroRecruitedForManaged = true;
        }

        army.StrikeTeam!.History.AddRange(lines);
        SaveCurrent(path, army);
        _cart.Clear();
        _cartVeteran = false;
        Reload();
        _view = EView.Camp;
        Say(string.Join("  ", lines));
    }

    // -- New campaign army --------------------------------------------------------------------------

    // -- New campaign (#452) ------------------------------------------------------------------------
    //
    // campaign-flow-spec sections 3a-3c, with Kai's answers of 2026-11-03:
    //   - your army is either BUILT in the locked Army Forge or started from one you already have;
    //   - the enemy is built by you too, or "Pick one for me" takes an army from the folder at random;
    //   - the campaign's opening journal note is asked for here, and may be skipped.
    // The player's army is always made first (the spec: "non-negotiable").

    internal enum ENewStep { Setup, NameYours, NameEnemy }
    private ENewStep _newStep = ENewStep.Setup;

    /// <summary>True: build the army in the locked Army Forge. False: start from one in the folder.</summary>
    private bool _newBuildFresh = true;
    private string _newOpeningNote = "";
    /// <summary>#465 - a saved campaign plan to start from, or null to plan missions as you go.</summary>
    private string? _newScriptPath;

    /// <summary>The armies chosen so far, not yet written anywhere. Nothing touches the disk until Finish.</summary>
    private BuiltArmyFile? _draftYours;
    private BuiltArmyFile? _draftEnemy;

    /// <summary>The folder army the player's draft was copied from, so "Pick one for me" never hands the
    /// enemy the very same list. Null when the player's army was built fresh.</summary>
    private string? _draftYoursSource;
    private string _enemyName = "";

    /// <summary>
    /// Set by the Army Forge callbacks just before the renderer brings this screen back. OnShown normally
    /// returns to the list, which would throw away a campaign that is half made; this keeps the wizard.
    /// </summary>
    private bool _returningFromForge;

    /// <summary>
    /// Opens the locked Army Forge for a campaign army. Wired in Program.cs, which routes the build's
    /// callbacks back to this screen. False when that game has no books to build from.
    /// </summary>
    public Func<ArmyForgeScreen.CampaignBuild, bool>? OnBuildArmy;

    internal void StartNew()
    {
        _view = EView.New;
        _newStep = ENewStep.Setup;
        _newName = "";
        _newArmy = -1;
        _newOpeningNote = "";
        _newScriptPath = null;
        _draftYours = _draftEnemy = null;
        _draftYoursSource = null;
        _enemyName = "";
        Reload();
        _message = "";
    }

    private void DrawNew(float x, float y, float w, float h)
    {
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##newTeam", new Vector2(w, h), ImGuiChildFlags.Borders);
        switch (_newStep)
        {
            case ENewStep.Setup: DrawNewSetup(w); break;
            case ENewStep.NameYours: DrawNameYours(w); break;
            case ENewStep.NameEnemy: DrawNameEnemy(w); break;
        }
        ImGui.EndChild();
    }

    private void DrawNewSetup(float w)
    {
        CampaignNumbers n = CampaignNumbers.For(_newSystem);

        // #426: which game's campaign. Each of the four has its own price list and its own armies
        // folder, and the two move together - picking a system re-scopes the list below it.
        UiText.Colored(TextDim, "Game");
        foreach (EGameSystem system in StrikeTeamFiles.CampaignSystems)
        {
            if (ImGui.RadioButton(GameSystemInfo.DisplayName(system), _newSystem == system) && _newSystem != system)
            {
                _newSystem = system;
                _newArmy = -1;
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Spacing();

        // #445 - how the campaign ends. Asked before the army, which is the order the spec puts it in:
        // it is the one choice that decides what the whole campaign is for.
        UiText.Colored(TextDim, "How it ends");
        if (ImGui.RadioButton("Race to a VP target", _newType == ECampaignType.PointLimited))
            _newType = ECampaignType.PointLimited;
        ImGui.SameLine();
        if (ImGui.RadioButton("Endless", _newType == ECampaignType.Endless))
            _newType = ECampaignType.Endless;

        if (_newType == ECampaignType.PointLimited)
        {
            ImGui.SetNextItemWidth(110f);
            ImGui.InputInt("VP to win", ref _newVpTarget);
            _newVpTarget = Math.Clamp(_newVpTarget, 1, 999);
            ImGui.SameLine();
            // The book's own way of setting it. Offered as a button beside a free field rather than
            // forced, which is the house rule recorded in claude/house-rules.md.
            if (UiButton.NavigateSmall("Roll it (D3+4)"))
                _newVpTarget = CampaignPreGame.RollCampaignLength(D6);
            UiText.Wrapped($"The first army to {_newVpTarget} VP wins the campaign. A won mission pays 1-4 VP "
                           + "depending on the difficulty you play it at, so that is roughly "
                           + $"{Math.Max(2, (_newVpTarget + 1) / 2)}-{_newVpTarget} missions. The book rolls "
                           + "this as D3+4; you can pick any number.");
        }
        else
        {
            UiText.Wrapped("The campaign never ends. VP pile up as a running score and you stop whenever "
                           + "you like.");
        }
        ImGui.Spacing();

        // The books' No Permadeath option, fixed for the life of the campaign (Kai's call, 2026-11-01).
        UiText.Colored(TextDim, "Losses");
        UiButton.Checkbox("No Permadeath", ref _newNoPermadeath);
        UiText.Wrapped(_newNoPermadeath
            ? "A unit that would be killed survives instead: it loses the XP it earned that mission and "
              + "sits out the next D3 missions. Nothing is ever lost for good."
            : "A unit that is killed is gone, with its XP, traits and upgrades. This is the default in "
              + "the book.");
        ImGui.Spacing();

        // #465 - the missions: planned one at a time on Mission Setup, or taken from a saved campaign.
        UiText.Colored(TextDim, "Missions");
        if (ImGui.RadioButton("Plan each mission as I go", _newScriptPath == null)) _newScriptPath = null;
        var scripts = FdgRaylib.SaveLoad.CampaignScriptPaths.List(_newSystem);
        foreach (var (scriptPath, script) in scripts)
        {
            if (ImGui.RadioButton($"Start from \"{script.Name}\" ({script.Missions.Count} mission(s))##{scriptPath}",
                    _newScriptPath == scriptPath))
                _newScriptPath = scriptPath;
        }
        UiText.Disabled(scripts.Count == 0
            ? "Each mission is generated from the books (or picked by you) on Mission Setup. Save a campaign from there to replay it later."
            : "A saved campaign brings its missions and notes; you can still change or re-roll each one when you get to it.");
        ImGui.Spacing();

        // #452 - the campaign's opening journal note (Kai: "Yes, and let me skip it").
        UiText.Colored(TextDim, "The story (optional)");
        UiText.Disabled("Why these two armies are fighting. Skip it if you like - it is the first page of "
                        + "the campaign's Journal and can be written any time.");
        ImGui.InputTextMultiline("##openingNote", ref _newOpeningNote, CampaignJournal.MaxNoteLength,
            new Vector2(Math.Min(720f, w - 24f), ImGui.GetTextLineHeight() * 5f));
        ImGui.Spacing();

        UiText.Colored(TextDim, "Your army");
        UiText.Wrapped($"The treasury is {n.Treasury} pts: spend at least {n.MinimumSpend}, with at most one "
                       + "hero. Whatever is left over you keep to spend between missions.");
        if (ImGui.RadioButton("Build a new army in the Army Forge", _newBuildFresh)) _newBuildFresh = true;
        if (ImGui.RadioButton("Start from an army I already have", !_newBuildFresh)) _newBuildFresh = false;

        if (!_newBuildFresh)
        {
            var armies = Armies;
            if (armies.Count == 0)
                UiText.Disabled($"No {GameSystemInfo.DisplayName(_newSystem)} armies in "
                                + $"armies\\{ArmyPaths.FolderNameFor(_newSystem)} yet - build one instead.");
            for (int i = 0; i < armies.Count; i++)
            {
                BuiltArmyFile a = armies[i].Army;
                string? why = CampaignRules.CannotStart(a, n);
                if (ImGui.Selectable($"{a.Name}   ({a.Faction}, {a.TotalPoints} pts, {a.Units.Count} units)##a{i}", i == _newArmy))
                    _newArmy = i;
                if (why != null) { ImGui.SameLine(); UiText.Colored(Bad, why); }
            }
        }
    }

    /// <summary>What an army is, in a few lines - shown while it is being named.</summary>
    private static void ArmySummary(BuiltArmyFile army, CampaignNumbers n)
    {
        ImGui.TextUnformatted($"{army.Faction} - {army.Units.Count} unit(s), {army.TotalPoints} pts, "
                              + $"{army.Units.Count(CampaignRules.IsHero)} hero(es)");
        UiText.Disabled($"{Math.Max(0, n.Treasury - army.TotalPoints)} pts left in the treasury to spend later.");
        foreach (UnitFileEntry unit in army.Units.Take(12))
            UiText.Disabled($"   {unit.Name}  ({unit.ModelCount} model(s), {unit.PointCost} pts)");
        if (army.Units.Count > 12) UiText.Disabled($"   ...and {army.Units.Count - 12} more");
    }

    private void DrawNameYours(float w)
    {
        CampaignNumbers n = CampaignNumbers.For(_newSystem);
        UiText.Colored(TextBright, "NAME YOUR ARMY");
        ImGui.SetNextItemWidth(Math.Min(420f, w * 0.5f));
        ImGui.InputText("##teamName", ref _newName, 48);
        ImGui.Spacing();
        if (_draftYours != null) ArmySummary(_draftYours, n);

        ImGui.Separator();
        UiText.Colored(TextDim, "YOUR ENEMY");
        UiText.Wrapped("Build the enemy's army yourself, or let the game pick one of your "
                       + $"{GameSystemInfo.DisplayName(_newSystem)} armies for you. Either way it plays under "
                       + "the same treasury rules, and the bot runs it at the table.");
    }

    private void DrawNameEnemy(float w)
    {
        CampaignNumbers n = CampaignNumbers.For(_newSystem);
        UiText.Colored(TextBright, "NAME ENEMY ARMY");
        ImGui.SetNextItemWidth(Math.Min(420f, w * 0.5f));
        ImGui.InputText("##enemyName", ref _enemyName, 48);
        ImGui.Spacing();
        if (_draftEnemy != null) ArmySummary(_draftEnemy, n);

        ImGui.Separator();
        UiText.Disabled($"Your army: {_newName.Trim()}" + (_draftYours != null ? $" ({_draftYours.Faction}, {_draftYours.TotalPoints} pts)" : ""));
        UiText.Disabled(_newType == ECampaignType.PointLimited ? $"First to {_newVpTarget} VP wins." : "Endless campaign.");
    }

    private string? SetupProblem()
    {
        if (_newBuildFresh) return null;
        var armies = Armies;
        if (_newArmy < 0 || _newArmy >= armies.Count) return "Pick a starting army.";
        return CampaignRules.CannotStart(armies[_newArmy].Army, CampaignNumbers.For(_newSystem));
    }

    internal string? YourNameProblem()
    {
        string name = _newName.Trim();
        if (name.Length == 0) return "Name your army.";
        if (Teams.Any(t => string.Equals(t.Army.StrikeTeam!.Name, name, StringComparison.OrdinalIgnoreCase)))
            return "A campaign army with that name exists.";
        return null;
    }

    internal string? EnemyNameProblem()
    {
        string name = _enemyName.Trim();
        if (name.Length == 0) return "Name the enemy army.";
        if (string.Equals(name, _newName.Trim(), StringComparison.OrdinalIgnoreCase))
            return "Give the enemy a different name from yours.";
        return null;
    }

    /// <summary>Ordinary armies of this game that could start a campaign as the enemy.</summary>
    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> EnemyCandidates()
    {
        CampaignNumbers n = CampaignNumbers.For(_newSystem);
        return Armies.Where(a => a.Path != _draftYoursSource && CampaignRules.CannotStart(a.Army, n) == null).ToList();
    }

    private ArmyForgeScreen.CampaignBuild ForgeBuild(string title, Action<BuiltArmyFile> took) =>
        new(_newSystem, title, CampaignNumbers.For(_newSystem),
            army => { took(army); _returningFromForge = true; },
            () => _returningFromForge = true);

    internal void ContinueFromSetup()
    {
        if (_newBuildFresh)
        {
            bool opened = OnBuildArmy?.Invoke(ForgeBuild("Your Army", army =>
            {
                _draftYours = army;
                _draftYoursSource = null;
                _newName = army.Name;
                _newStep = ENewStep.NameYours;
            })) ?? false;
            if (!opened) Say($"There are no {GameSystemInfo.DisplayName(_newSystem)} army books to build from.", true);
            return;
        }

        var source = Armies[_newArmy];
        // A fresh copy of the file, so the original list stays exactly as it was.
        if (StrikeTeamFiles.Load(source.Path) is not { } copy) { Say("Couldn't read that army file.", true); return; }
        _draftYours = copy;
        _draftYoursSource = source.Path;
        _newName = copy.Name;
        _newStep = ENewStep.NameYours;
    }

    internal void BuildEnemy()
    {
        bool opened = OnBuildArmy?.Invoke(ForgeBuild("Enemy Army", army =>
        {
            _draftEnemy = army;
            _enemyName = army.Name;
            _newStep = ENewStep.NameEnemy;
        })) ?? false;
        if (!opened) Say($"There are no {GameSystemInfo.DisplayName(_newSystem)} army books to build from.", true);
    }

    /// <summary>
    /// Kai's "pick one for me" - an army from this game's folder at random, the way #428 always chose the
    /// enemy. Taken at random so two campaigns started from the same folder do not face the same list.
    /// </summary>
    internal void PickEnemyForMe()
    {
        var candidates = EnemyCandidates();
        if (candidates.Count == 0) return;
        var pick = candidates[Rng.Next(candidates.Count)];
        if (StrikeTeamFiles.Load(pick.Path) is not { } copy) { Say("Couldn't read that army file.", true); return; }
        _draftEnemy = copy;
        _enemyName = string.Equals(copy.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? copy.Name + " (rival)" : copy.Name;
        _newStep = ENewStep.NameEnemy;
    }

    /// <summary>
    /// Writes both campaign armies. The enemy is saved first and the player's last, carrying the link to
    /// it, so a failure part way never leaves a player campaign pointing at an enemy file that does not
    /// exist - and a failed player save removes the enemy it would have orphaned.
    /// </summary>
    internal void Finish()
    {
        if (_draftYours is not { } yours || _draftEnemy is not { } enemy) return;
        CampaignNumbers n = CampaignNumbers.For(_newSystem);
        string name = _newName.Trim();
        string enemyName = _enemyName.Trim();

        string enemyPath = StrikeTeamFiles.NewPath(_newSystem, enemyName);
        // The same type and target as the player's army: the end condition belongs to the campaign, not
        // to one side of it, and two files that disagreed would make whichever one a reader opened the
        // wrong answer (see StrikeTeamInfo.VpTarget).
        CampaignRules.Start(enemy, enemyName, enemyPath, n, _newSystem, _newType,
            _newNoPermadeath, isBot: true, vpTarget: _newVpTarget);
        // Not editable in Army Forge any more: re-compiling would wipe the campaign changes. Each unit's
        // own selections were copied onto its sheet by Start, which is what upgrades work against.
        enemy.Selections = null;
        if (!StrikeTeamFiles.Save(enemyPath, enemy)) { Say("Couldn't save the enemy army file.", true); return; }

        string path = StrikeTeamFiles.NewPath(_newSystem, name);
        CampaignRules.Start(yours, name, path, n, _newSystem,
            _newType, _newNoPermadeath, vpTarget: _newVpTarget);
        yours.Selections = null;
        yours.StrikeTeam!.EnemyArmyFile = enemyPath;
        CampaignJournal.SetCampaignNote(yours.StrikeTeam, _newOpeningNote);
        // #465: a saved campaign's missions and notes.
        if (_newScriptPath != null)
        {
            try
            {
                if (CampaignScript.FromJson(File.ReadAllText(_newScriptPath)) is { } script) script.ApplyTo(yours.StrikeTeam);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Say("Couldn't read that saved campaign - starting with no planned missions.", true);
            }
        }
        if (!StrikeTeamFiles.Save(path, yours))
        {
            try { File.Delete(enemyPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Say("Couldn't save the campaign army file.", true);
            return;
        }

        _draftYours = _draftEnemy = null;
        Reload();
        _selected = Teams.FindIndex(t => t.Path == path);
        OpenCamp(_selected);
        Say($"{name} vs {enemyName} - campaign started, with {yours.StrikeTeam.BankedPoints} pts left in your treasury.");
    }

    private void Delete(string path)
    {
        try { File.Delete(path); Say("Campaign army deleted."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say("Couldn't delete the file.", true); }
        Reload();
    }

    // -- Bottom bar ---------------------------------------------------------------------------------

    // #452 test seams: the wizard's state, readable and settable without drawing a frame. The wizard writes
    // files to the player's disk, so it is driven end to end in CampaignCreationFlowTests rather than
    // trusted by eye.
    internal EView ViewForTest => _view;
    internal ENewStep NewStepForTest => _newStep;
    internal string NewNameForTest { get => _newName; set => _newName = value; }
    internal string EnemyNameForTest { get => _enemyName; set => _enemyName = value; }
    internal string OpeningNoteForTest { get => _newOpeningNote; set => _newOpeningNote = value; }
    internal bool BuildFreshForTest { get => _newBuildFresh; set => _newBuildFresh = value; }
    internal void SetEndForTest(ECampaignType type, int vpTarget) { _newType = type; _newVpTarget = vpTarget; }
    internal string? CurrentPathForTest => Current?.Path;
    internal string? ManagedPathForTest => Managed?.Path;
    internal bool ManagingEnemyForTest => ManagingEnemyNow;
    internal void SwitchSideForTest(bool enemy) => SwitchSide(enemy);
    internal bool HeroRecruitedForManagedForTest { get => HeroRecruitedForManaged; set => HeroRecruitedForManaged = value; }
    internal void OpenCampForTest(string path)
    {
        int index = Teams.FindIndex(t => string.Equals(Path.GetFullPath(t.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (index >= 0) OpenCamp(index);
    }

    /// <summary>#452 - the new-campaign wizard's buttons, one set per step.</summary>
    private void DrawNewBar(float rx, float by, Vector2 btn, bool esc, float margin)
    {
        float gap = 10f;
        switch (_newStep)
        {
            case ENewStep.Setup:
            {
                if (UiButton.Back("Cancel", btn) || esc) _view = EView.List;
                ImGui.SetCursorPos(new Vector2(rx, by));
                string? problem = SetupProblem();
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm("Continue", btn)) ContinueFromSetup();
                ImGui.EndDisabled();
                RightLabel(rx, by, btn, problem ?? (_newBuildFresh
                    ? "Opens the Army Forge to build your army"
                    : "Next: name your army"), TextDim);
                break;
            }
            case ENewStep.NameYours:
            {
                if (UiButton.Back("Back", btn) || esc) { _newStep = ENewStep.Setup; _draftYours = null; _draftYoursSource = null; }
                string? problem = YourNameProblem();
                bool canPick = EnemyCandidates().Count > 0;

                ImGui.SetCursorPos(new Vector2(rx, by));
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm("Build Enemy Army", btn)) BuildEnemy();
                ImGui.EndDisabled();

                ImGui.SetCursorPos(new Vector2(rx - btn.X - gap, by));
                ImGui.BeginDisabled(problem != null || !canPick);
                if (UiButton.Navigate("Pick one for me", btn)) PickEnemyForMe();
                ImGui.EndDisabled();

                RightLabel(rx - btn.X - gap, by, btn, problem ?? (canPick ? "Your enemy next"
                    : $"No other {GameSystemInfo.DisplayName(_newSystem)} army in the folder fits the treasury - build one"), TextDim);
                break;
            }
            case ENewStep.NameEnemy:
            {
                if (UiButton.Back("Back", btn) || esc) { _newStep = ENewStep.NameYours; _draftEnemy = null; }
                ImGui.SetCursorPos(new Vector2(rx, by));
                string? problem = EnemyNameProblem();
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm("Finish", btn)) Finish();
                ImGui.EndDisabled();
                RightLabel(rx, by, btn, problem ?? "Starts the campaign", TextDim);
                break;
            }
        }
        DrawMessage(margin, by, btn);
    }

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        // Never narrower than the longest label plus the plate's arrows - at 1280x720 13% of the width put
        // the arrows over "Play next mission" and "Campaign armies" (2026-11-06).
        float btnH = barH * 0.55f;
        float longest = new[] { "Play next mission", "Campaign armies", "Back to camp", "New Campaign", "Continue" }
            .Max(l => ImGui.CalcTextSize(l).X);
        Vector2 btn = new(MathF.Max(screenW * 0.13f, longest + ImGui.GetStyle().FramePadding.X * 2f + UiButton.CapsWidth(btnH) + 8f), btnH);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput;
        float rx = screenW - margin - btn.X;

        ImGui.SetCursorPos(new Vector2(margin, by));
        switch (_view)
        {
            case EView.New:
                DrawNewBar(rx, by, btn, esc, margin);
                break;

            case EView.Recruit:
                if (UiButton.Back("Back to camp", btn) || esc) { _cart.Clear(); _view = EView.Camp; }
                break;

            case EView.Journal:
                // Escape is not taken while a text box has the keyboard - it would throw away the
                // paragraph being typed rather than leave the page.
                if (UiButton.Back("Back to camp", btn) || esc) { CommitJournal(); _view = EView.Camp; }
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Navigate("Export journal", btn)) ExportJournal();
                RightLabel(rx, by, btn, "Saves the whole journal as a text file you can read anywhere", TextDim);
                DrawMessage(margin, by, btn);
                break;

            case EView.Camp:
                if (UiButton.Back("Campaign armies", btn) || esc) _view = EView.List;
                ImGui.SetCursorPos(new Vector2(rx, by));
                // #445 - a decided campaign has no next mission. Disabled rather than hidden, so the
                // button does not move and the label can say why it is out.
                bool decided = Current is { } camp && StandingOf(camp.Army).IsOver;
                ImGui.BeginDisabled(decided);
                if (UiButton.Confirm("Play next mission", btn)) GoToMissionSetup();
                ImGui.EndDisabled();
                RightLabel(rx, by, btn, decided
                    ? "This campaign is over - see the result on the right"
                    : "Opens Mission Setup: your force, theirs, the map", TextDim);
                DrawMessage(margin, by, btn);
                break;

            default:
                if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Confirm("New Campaign", btn)) StartNew();
                if (Current is { } cur)
                {
                    ImGui.SetCursorPos(new Vector2(rx - btn.X - 10f, by));
                    if (UiButton.Navigate("Open", btn)) OpenCamp(_selected);
                    ImGui.SetCursorPos(new Vector2(rx - btn.X * 2f - 20f, by));
                    if (UiButton.Back(_confirmDelete ? "Really delete?" : "Delete", btn))
                    {
                        if (_confirmDelete) Delete(cur.Path);
                        else _confirmDelete = true;
                    }
                }
                DrawMessage(margin, by, btn);
                break;
        }
    }

    // -- Starting the mission -----------------------------------------------------------------------

    /// <summary>
    /// #435 - hands this campaign to Mission Setup, which owns the launch from here: which units come
    /// on both sides, the difficulty band that sizes the rival's force, the map, and the host.
    ///
    /// <para>
    /// This screen used to do all of that itself, in a <c>StartMission</c> that wrote both mission files
    /// and asked for a lobby. It moved because the choices it was making had nowhere to be shown: the
    /// rival's force was picked silently at the books' cap (so every mission was played on Legendary),
    /// and the map was whatever the lobby happened to be set to. Those are the three things Kai's flow
    /// spec puts on one screen, so they live on that screen.
    /// </para>
    ///
    /// <para>
    /// The other campaign armies of the SAME game go with it: only campaign armies of one game can meet
    /// across a table, so only they set the mission cap (#426). A 300-pt Skirmish army would otherwise
    /// drag a Grimdark Future mission's limit down to 625 pts between two armies that can never play
    /// each other.
    /// </para>
    /// </summary>
    private void GoToMissionSetup()
    {
        if (Current is not { } cur) return;
        (string path, BuiltArmyFile army, _) = cur;
        StrikeTeamInfo info = army.StrikeTeam!;

        List<CampaignEntrant> others = Teams
            .Where(t => t.Path != path && t.Army.StrikeTeam!.GameSystem == info.GameSystem)
            .Select(t => new CampaignEntrant(t.Army.StrikeTeam!.Name, t.Army.TotalPoints, false))
            .ToList();

        // A new mission: the one-new-Hero-per-mission allowance comes back, for both armies.
        _heroRecruited = false;
        _enemyHeroRecruited = false;
        OnPlay?.Invoke(path, others);
    }

    private void DrawMessage(float margin, float by, Vector2 btn)
    {
        if (_message.Length == 0) return;
        Vector2 ts = ImGui.CalcTextSize(_message);
        ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(_messageIsError ? Bad : Good, _message);
    }

    private static void RightLabel(float rx, float by, Vector2 btn, string text, Vector4 color)
    {
        Vector2 ts = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos(new Vector2(rx - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(color, text);
    }
}
