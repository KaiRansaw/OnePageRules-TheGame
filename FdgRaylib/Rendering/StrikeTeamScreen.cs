using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The army campaign screen (Campaign Rules v3.6.0) for GF, GF: Firefight, AoF and AoF: Skirmish. UI rework
/// batch F, to Kai's "Campaign Mockup" (wish list, "Army Campaign + Mission Setup", 2026-11-07):
/// <list type="bullet">
/// <item><b>List</b> - only the game the player came from; the selected campaign's scoreboard on the right.
/// Delete asks first and removes BOTH armies.</item>
/// <item><b>New</b> - four tabs: Rules, Your army, The enemy, Missions. No game picker: the game is the
/// button the player pressed.</item>
/// <item><b>Camp</b> - selected unit | Your army / Enemy army roster as portrait cards | the campaign and
/// next mission. "Who's coming" is NOT here - it lives only on Mission Setup.</item>
/// <item><b>Recruit</b> - the Army Forge roster (its category boxes and portrait cards) with a cart and the
/// treasury; the book's one-new-Hero-per-mission greys out extra Heroes.</item>
/// <item><b>Journal</b> - campaign page, mission list, the game's own record and the player's account.</item>
/// </list>
/// Rental drawing under the working agreement (plain ImGui). The rules behind every button live in the
/// engine's <see cref="CampaignRules"/>, <see cref="CampaignEconomy"/> and <see cref="CampaignPreGame"/>.
/// </summary>
public sealed class StrikeTeamScreen : IAppScreen
{
    public Action? OnBack;

    /// <summary>
    /// #435 - "PLAY NEXT MISSION": opens Mission Setup for this campaign army, with the other campaign armies
    /// of the same game (they set the mission cap, #426).
    /// </summary>
    public Action<string, List<CampaignEntrant>>? OnPlay;

    internal enum EView { List, New, Camp, Recruit, Journal }

    private EView _view = EView.List;

    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> _all = new();

    /// <summary>
    /// The game this screen was opened for (the tile and mode button the player pressed). The list shows only
    /// its campaigns, and a new campaign is always one of this game (wish list: "No game picker").
    /// </summary>
    private EGameSystem _system = EGameSystem.GrimdarkFuture;

    /// <summary>The player's campaign armies of THIS game. Bot armies are the opponent and are shown on the
    /// camp screen of the campaign they belong to, not listed.</summary>
    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> Teams =>
        _all.Where(a => a.Army.StrikeTeam is { IsBot: false } && a.System == _system).ToList();

    /// <summary>Ordinary armies of this game that a new campaign may start from (#426: each mode has its own
    /// army-builder stats, so another game's list is not a legal start).</summary>
    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> Armies =>
        _all.Where(a => a.Army.StrikeTeam == null && a.System == _system).ToList();

    /// <summary>This campaign's standing enemy (#428), or null when it has none.</summary>
    private (string Path, BuiltArmyFile Army, EGameSystem System)? EnemyOf(StrikeTeamInfo info) =>
        info.EnemyArmyFile is { } path
            ? _all.Cast<(string Path, BuiltArmyFile Army, EGameSystem System)?>()
                  .FirstOrDefault(a => a!.Value.Path == path)
            : null;

    private int _selected = -1;
    private int _unit;
    private string _message = "";
    private bool _messageIsError;

    // Modals: Delete and Disband both ask first (wish list "Delete asks").
    private bool _openDelete;
    private bool _openDisband;
    private const string DeleteTitle = "Delete campaign?";
    private const string DisbandTitle = "Disband unit?";

    // The shared portrait cards (Batch B): the camp roster, the recruit roster and the wizard previews.
    private readonly UnitPortraitCards _cards = new();

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);
    private static readonly Vector4 EnemyTint = new(1f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 EnemyBg = new(0.20f, 0.08f, 0.08f, 1f);

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    private static readonly Random Rng = new();
    private static int D6() => Rng.Next(1, 7);

    /// <summary>Opened from a game's mode screen: this game's campaigns, and this game's new campaign.</summary>
    public void Open(EGameSystem system) => _system = system;

    private bool _returnToCamp;

    /// <summary>The next time this screen is shown, open the selected campaign's camp rather than the
    /// list - Mission Setup's Back.</summary>
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
    }

    private (string Path, BuiltArmyFile Army, EGameSystem System)? Current
    {
        get { var t = Teams; return _selected >= 0 && _selected < t.Count ? t[_selected] : null; }
    }

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    // -- Whose army (#455) ----------------------------------------------------------------------------
    //
    // campaign-flow-spec section 4: "the bot never spends its own points". The player runs BOTH armies'
    // books between missions, so the camp can point at either army and must make obvious which one it is.

    private bool _managingEnemy;

    /// <summary>The army the roster, unit panel and recruiting work on: the player's, or the enemy's.</summary>
    private (string Path, BuiltArmyFile Army, EGameSystem System)? Managed =>
        _managingEnemy && Current is { } c && EnemyOf(c.Army.StrikeTeam!) is { } enemy ? enemy : Current;

    private bool ManagingEnemyNow => _managingEnemy && Current is { } c && EnemyOf(c.Army.StrikeTeam!) != null;

    private void SwitchSide(bool enemy)
    {
        if (_managingEnemy == enemy) return;
        _managingEnemy = enemy;
        _unit = 0;
        _cart.Clear();
        _cartVeteran = false;
        _renameFor = null;
        _recruitSelected = null;
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
        DrawDeleteModal();
        DrawDisbandModal();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private (string, string) HeaderText()
    {
        string game = GameSystemInfo.DisplayName(_system);
        if (_view == EView.New)
            return ("NEW CAMPAIGN", $"{game}. The game comes from the button you pressed.");
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
            return (head,
                $"{GameSystemInfo.DisplayName(info.GameSystem)}  -  {StandingOf(cur.Army).Describe()}" +
                (info.NoPermadeath ? " (No Permadeath)" : "") + "  -  " +
                $"{info.Matches} mission(s) played  -  treasury {info.BankedPoints} pts");
        }
        return ("CAMPAIGN", $"{game}. A campaign army keeps its units, XP, traits and injuries from mission to mission.");
    }

    // -- The list of campaigns ----------------------------------------------------------------------

    private void DrawList(float x, float y, float w, float h)
    {
        float gap = 12f;
        float listW = w * 0.32f;

        DrawTeamList(x, y, listW, h);

        ImGui.SetCursorPos(new Vector2(x + listW + gap, y));
        ImGui.BeginChild("##campaignDetail", new Vector2(w - listW - gap, h), ImGuiChildFlags.Borders);
        if (Current is { } sel) DrawCampaignDetail(sel.Army);
        else UiText.Disabled(Teams.Count == 0
            ? $"No {GameSystemInfo.DisplayName(_system)} campaigns yet. \"+ New Campaign\" starts one."
            : "Pick a campaign on the left.");
        ImGui.EndChild();
    }

    /// <summary>A list row's two lines: "You vs Them  WON" and "4 / 7 VP - 4 missions - choices waiting".
    /// Pure so the wording is pinned by a test.</summary>
    internal static (string Title, string Line) ListRow(StrikeTeamInfo info, StrikeTeamInfo? foe,
        CampaignPreGame.CampaignStanding standing, bool choicesWaiting)
    {
        string result = standing.Outcome switch
        {
            ECampaignOutcome.YouWon => "  WON",
            ECampaignOutcome.RivalWon => "  LOST",
            ECampaignOutcome.Draw => "  DRAWN",
            _ => "",
        };
        string title = (foe != null ? $"{info.Name} vs {foe.Name}" : info.Name) + result;
        string vp = standing.HasTarget ? $"{info.Vp} / {standing.VpTarget} VP" : $"Endless - {info.Vp} VP";
        string line = $"{vp} - {info.Matches} mission(s)"
                      + (standing.IsOver ? " - finished" : choicesWaiting ? " - choices waiting" : "");
        return (title, line);
    }

    private void DrawTeamList(float x, float y, float w, float h)
    {
        var teams = Teams;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##teamList", new Vector2(w, h), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, GameSystemInfo.DisplayName(_system).ToUpperInvariant() + " CAMPAIGNS");
        ImGui.Separator();
        if (teams.Count == 0) UiText.Disabled("None yet. Click \"+ New Campaign\".");
        for (int i = 0; i < teams.Count; i++)
        {
            StrikeTeamInfo info = teams[i].Army.StrikeTeam!;
            bool pending = teams[i].Army.Units.Any(u => CampaignRules.Sheet(u)?.Pending.Count > 0);
            (string title, string line) = ListRow(info, EnemyOf(info)?.Army.StrikeTeam, StandingOf(teams[i].Army), pending);
            if (ImGui.Selectable($"{title}\n   {line}##t{i}", i == _selected, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
            {
                _selected = i;
                _unit = 0;
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenCamp(i);
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// The selected campaign's scoreboard (wish list): both sides' VP, W/L, army value and unspent treasury;
    /// the next mission; who is injured; trait picks waiting; recent history.
    /// </summary>
    private void DrawCampaignDetail(BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        var foe = EnemyOf(info);
        StrikeTeamInfo? foeInfo = foe?.Army.StrikeTeam;
        CampaignPreGame.CampaignStanding standing = StandingOf(army);

        ImGui.SetWindowFontScale(1.35f);
        UiText.Colored(TextBright, foeInfo != null ? $"{info.Name}  vs  {foeInfo.Name}" : info.Name);
        ImGui.SetWindowFontScale(1f);
        UiText.Colored(Gold, standing.Describe() + (info.NoPermadeath ? " - No Permadeath" : ""));
        if (standing.IsOver)
            UiText.Colored(standing.Outcome == ECampaignOutcome.YouWon ? Good : Bad,
                standing.Outcome switch
                {
                    ECampaignOutcome.YouWon => "WON",
                    ECampaignOutcome.RivalWon => "LOST",
                    _ => "DRAWN",
                });
        ImGui.Separator();

        // The scoreboard: two boxes side by side, the same shape for both sides.
        float boxW = (ImGui.GetContentRegionAvail().X - 12f) / 2f;
        float boxH = ImGui.GetTextLineHeightWithSpacing() * 5f;
        (int wins, int losses) = CampaignRules.RecordOf(info);
        ImGui.BeginChild("##scoreYou", new Vector2(boxW, boxH), ImGuiChildFlags.Borders);
        ScoreBox("YOU", info.Name, standing.YourVp, wins, losses, army.TotalPoints, info.BankedPoints, Gold);
        ImGui.EndChild();
        ImGui.SameLine(0f, 12f);
        ImGui.BeginChild("##scoreRival", new Vector2(boxW, boxH), ImGuiChildFlags.Borders);
        if (foeInfo != null && foe is { } f)
        {
            (int fw, int fl) = CampaignRules.RecordOf(foeInfo);
            ScoreBox("RIVAL", foeInfo.Name, standing.RivalVp, fw, fl, f.Army.TotalPoints, foeInfo.BankedPoints, EnemyTint);
        }
        else UiText.Disabled("No standing enemy - this campaign was made before campaigns had one.");
        ImGui.EndChild();

        ImGui.Spacing();
        var resting = army.Units
            .Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
            .Where(p => p.Sheet is { MissionsOut: > 0 })
            .ToList();
        string injured = resting.Count == 0
            ? "Nobody is sitting out."
            : "Sitting out injured: " + string.Join(", ", resting.Select(r => $"{r.Unit.Name} ({r.Sheet!.MissionsOut} more mission(s))")) + ".";
        UiText.Wrapped(standing.IsOver
            ? $"{info.Matches} mission(s) played. This campaign is finished."
            : $"Next up: mission {info.Matches + 1}. {injured}");

        int waiting = army.Units.Count(u => CampaignRules.Sheet(u)?.Pending.Count > 0);
        if (waiting > 0) UiText.Colored(Gold, $"{waiting} unit(s) are waiting on you to pick a trait.");
        int foeWaiting = foe?.Army.Units.Count(u => CampaignRules.Sheet(u)?.Pending.Count > 0) ?? 0;
        if (foeWaiting > 0) UiText.Colored(EnemyTint, $"The enemy has {foeWaiting} unit(s) waiting on a trait pick.");

        if (info.Journal.Length > 0)
        {
            const int excerpt = 280;
            string text = info.Journal.Length <= excerpt ? info.Journal : info.Journal[..excerpt].TrimEnd() + "...";
            ImGui.Spacing();
            UiText.Colored(TextDim, "THE STORY");
            UiText.Wrapped(text);
        }

        if (info.History.Count > 0)
        {
            ImGui.Spacing();
            UiText.Colored(TextDim, "LATELY");
            foreach (string line in Enumerable.Reverse(info.History).Take(5)) UiText.Wrapped(line);
        }
    }

    private static void ScoreBox(string who, string name, int vp, int wins, int losses, int armyPts, int treasury, Vector4 colour)
    {
        UiText.Colored(TextDim, who);
        UiText.Colored(colour, name);
        ImGui.SetWindowFontScale(1.4f);
        ImGui.TextUnformatted($"{vp} VP");
        ImGui.SetWindowFontScale(1f);
        UiText.Disabled($"{wins} W / {losses} L - army {armyPts} pts - treasury {treasury} pts unspent");
    }

    // -- Delete (asks, removes both armies) ----------------------------------------------------------

    private void DrawDeleteModal()
    {
        if (_openDelete) { ImGui.OpenPopup(DeleteTitle); _openDelete = false; }
        if (!ImGui.BeginPopupModal(DeleteTitle, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (Current is not { } cur) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        StrikeTeamInfo info = cur.Army.StrikeTeam!;
        string foe = EnemyOf(info)?.Army.StrikeTeam?.Name ?? "";
        UiText.Colored(TextBright, foe.Length > 0 ? $"Delete \"{info.Name} vs {foe}\"?" : $"Delete \"{info.Name}\"?", wrap: false);
        UiText.Disabled("Both armies are deleted, with their journal and mission plans. This can't be undone.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Delete", UiChrome.ButtonSize("  Delete  "))) { DeleteCampaign(cur.Path, info); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (UiButton.Navigate("Cancel", UiChrome.ButtonSize("  Cancel  ")) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    /// <summary>
    /// The files a campaign is made of: the player's army, its enemy, and the "this mission only" copies
    /// Mission Setup writes beside each. Pure so the list is pinned by a test - the old Delete removed the
    /// player's file and left the enemy behind (godot-screen-map Part 4).
    /// </summary>
    internal static IReadOnlyList<string> CampaignFiles(string path, StrikeTeamInfo info)
    {
        var files = new List<string> { path, Path.ChangeExtension(path, ".fdgmission") };
        if (info.EnemyArmyFile is { Length: > 0 } enemy)
        {
            files.Add(enemy);
            files.Add(Path.ChangeExtension(enemy, ".fdgmission"));
        }
        return files;
    }

    internal void DeleteCampaign(string path, StrikeTeamInfo info)
    {
        bool failed = false;
        foreach (string file in CampaignFiles(path, info))
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
        }
        Reload();
        Say(failed ? "Couldn't delete every file of that campaign." : "Campaign deleted - both armies are gone.", failed);
    }

    // -- Journal (#448) ------------------------------------------------------------------------------

    private string _journalCampaignBuf = "";
    private string _journalMissionBuf = "";
    private int _journalMission = 1;

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

    private void DrawJournal(float x, float y, float w, float h)
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        StrikeTeamInfo info = cur.Army.StrikeTeam!;

        float gap = 12f;
        float leftW = w * 0.40f;
        float listW = w * 0.16f;
        float pageW = w - leftW - listW - gap * 2f;
        float boxH = h - ImGui.GetTextLineHeightWithSpacing() * 3.5f;

        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##journalCampaign", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, "THE CAMPAIGN");
        UiText.Disabled("The story so far - who these armies are and why they fight.");
        ImGui.InputTextMultiline("##campaignNote", ref _journalCampaignBuf, CampaignJournal.MaxNoteLength,
            new Vector2(-1f, boxH));
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitJournal();
        ImGui.EndChild();

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

        ImGui.SetCursorPos(new Vector2(x + leftW + listW + gap * 2f, y));
        ImGui.BeginChild("##journalMission", new Vector2(pageW, h), ImGuiChildFlags.Borders);
        bool played = CampaignJournal.IsPlayed(info, _journalMission);
        UiText.Colored(TextDim, $"MISSION {_journalMission}" + (played ? "" : " - NOT PLAYED YET"));

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

    // -- Camp: selected unit | roster cards | campaign + next mission --------------------------------

    private void DrawCamp(float x, float y, float w, float h)
    {
        if (Current is not { } cur || Managed is not { } managed) { _view = EView.List; return; }
        (string path, BuiltArmyFile army, _) = cur;
        (string mPath, BuiltArmyFile mArmy, _) = managed;
        CampaignNumbers n = NumbersFor(army);

        float gap = 12f;
        float leftW = w * 0.27f;
        float rightW = w * 0.25f;
        float midW = w - leftW - rightW - gap * 2f;

        // Read once: the switch can change sides mid-frame, and popping a colour that was never pushed
        // crashed the game once (2026-11-06).
        bool tinted = ManagingEnemyNow;
        if (tinted)
        {
            ImGui.PushStyleColor(ImGuiCol.Border, EnemyTint);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, EnemyBg);
        }

        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##unit", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        DrawUnitDetail(mPath, mArmy, n);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + gap, y));
        ImGui.BeginChild("##roster", new Vector2(midW, h), ImGuiChildFlags.Borders);
        DrawRosterColumn(mArmy);
        ImGui.EndChild();

        if (tinted) ImGui.PopStyleColor(2);

        ImGui.SetCursorPos(new Vector2(x + leftW + midW + gap * 2f, y));
        ImGui.BeginChild("##mission", new Vector2(rightW, h), ImGuiChildFlags.Borders);
        DrawNextMission(path, army, n);
        ImGui.EndChild();
    }

    // Middle -----------------------------------------------------------------------------------------

    /// <summary>A roster card's tag (wish list: "pick a trait" / "recovering"), or null for none.</summary>
    internal static string? RosterTag(StrikeUnit? sheet) =>
        sheet == null ? null
        : sheet.Pending.Count > 0 ? "pick a trait"
        : sheet.MissionsOut > 0 ? $"recovering ({sheet.MissionsOut})"
        : null;

    private void DrawRosterColumn(BuiltArmyFile army)
    {
        bool hasEnemy = Current is { } c && EnemyOf(c.Army.StrikeTeam!) != null;
        bool enemy = ManagingEnemyNow;

        // The Your army / Enemy army switch.
        if (UiButton.NavigateSmall(enemy ? "  Your army  " : "> Your army <")) SwitchSide(false);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasEnemy);
        if (UiButton.NavigateSmall(enemy ? "> Enemy army <" : "  Enemy army  ")) SwitchSide(true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        string treasury = $"Treasury {army.StrikeTeam!.BankedPoints} pts";
        float tw = ImGui.CalcTextSize(treasury).X;
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - tw - ImGui.GetStyle().WindowPadding.X - 4f));
        UiText.Colored(Gold, treasury, wrap: false);

        if (!hasEnemy) UiText.Disabled("This campaign has no standing enemy.");
        else if (enemy)
            UiText.Colored(EnemyTint, "You are doing the ENEMY's books: its trait picks, upgrades and recruits.");
        else if (Current is { } me && EnemyOf(me.Army.StrikeTeam!) is { } foe)
        {
            int waiting = foe.Army.Units.Count(u => CampaignRules.Sheet(u)?.Pending.Count > 0);
            UiText.Disabled($"The enemy has {foe.Army.StrikeTeam!.BankedPoints} pts unspent and {waiting} choice(s) waiting.");
        }
        UiText.Colored(enemy ? EnemyTint : TextDim,
            $"{(enemy ? "ENEMY: " + army.StrikeTeam.Name.ToUpperInvariant() : "YOUR ARMY")} - {army.Units.Count} unit(s), "
            + $"{CampaignEconomy.HeroCount(army)} hero(es), {army.TotalPoints} pts");
        ImGui.Separator();

        float buttonsH = UiChrome.ButtonSize("+ Recruit units").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
        ImGui.BeginChild("##rostercards", new Vector2(0, -buttonsH), ImGuiChildFlags.None);
        var cards = new List<UnitPortraitCards.Card>(army.Units.Count);
        foreach (UnitFileEntry unit in army.Units)
        {
            StrikeUnit? sheet = CampaignRules.Sheet(unit);
            string? tag = RosterTag(sheet);
            string facts = sheet == null ? $"{unit.PointCost} pts" : $"Lv {sheet.Level} - {sheet.Xp} XP";
            cards.Add(new UnitPortraitCards.Card(unit.Name, CampaignRules.UnitTypeOf(unit, army.Book), facts,
                () => UnitPortraitCards.StatsTooltip(unit), tag,
                sheet?.Pending.Count > 0 ? Gold : Bad, Dimmed: sheet is { MissionsOut: > 0 }));
        }
        UnitPortraitCards.CardEvent hit = _cards.DrawCardGrid("##camproster", cards, army.Faction, _unit);
        if (hit.Clicked >= 0) { _unit = hit.Clicked; _renameFor = null; }
        if (army.Units.Count == 0) UiText.Disabled("No units left. Recruit some.");
        ImGui.EndChild();

        if (UiButton.Navigate("+ Recruit units", UiChrome.ButtonSize("  + Recruit units  "))) OpenRecruit();
        ImGui.SameLine();
        if (UiButton.Navigate("Journal", UiChrome.ButtonSize("  + Recruit units  "))) OpenJournal();
    }

    // Left: the selected unit ------------------------------------------------------------------------

    private string _renameBuf = "";
    private string? _renameFor;

    private void DrawRename(string path, BuiltArmyFile army, UnitFileEntry unit, StrikeUnit sheet)
    {
        if (_renameFor != sheet.Id) { _renameFor = sheet.Id; _renameBuf = unit.Name; }

        ImGui.SetNextItemWidth(UiChrome.Em(11f));
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
        UiText.Colored(TextDim, "SELECTED UNIT");
        ImGui.Separator();
        if (_unit < 0 || _unit >= army.Units.Count) { UiText.Disabled("Click a unit card."); return; }
        UnitFileEntry unit = army.Units[_unit];
        if (CampaignRules.Sheet(unit) is not { } sheet) { UiText.Disabled("This unit has no campaign sheet."); return; }

        float disbandH = UiChrome.ButtonSize("Disband unit").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
        ImGui.BeginChild("##unitbody", new Vector2(0, -disbandH), ImGuiChildFlags.None);

        string type = CampaignRules.UnitTypeOf(unit, army.Book);
        _cards.DrawPortrait(type, army.Faction, new Vector2(UiChrome.Em(5f), UiChrome.Em(6.5f)));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(0f);
        ImGui.SetWindowFontScale(1.25f);
        UiText.Colored(Accent, unit.Name + (sheet.IsHero ? "  [hero]" : ""));
        ImGui.SetWindowFontScale(1f);
        UiText.Disabled(type);
        UiText.Colored(Gold, $"Level {sheet.Level} - {sheet.Xp} / {CampaignNumbers.MaxXp} XP - {sheet.Games} game(s)");
        ImGui.TextUnformatted($"{unit.PointCost} pts (book {CampaignEconomy.BookCost(unit)}, campaign {sheet.CostChange:+0;-0;0})");
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        DrawRename(path, army, unit, sheet);
        ForgeUnitDetail.DrawStatPills(unit);
        ImGui.Separator();

        Line("Traits", sheet.Traits.Select(t => t.Replace("Trait: ", "")), null);
        if (sheet.IsHero)
        {
            Line("Skill sets", sheet.SkillSets, null);
            Line("Skills", sheet.Skills, null);
            Line("Talents", sheet.Talents, null);
        }
        Line("Injuries", sheet.Injuries, sheet.Injuries.Count > 0 ? Bad : null);
        Line("Weapons", unit.Weapons.Select(Describe), null);
        if (sheet.MissionsOut > 0) UiText.Colored(Bad, $"Recovering - sits out {sheet.MissionsOut} more mission(s).");

        // The highlighted trait pick (mockup: a gold box).
        foreach (string pending in sheet.Pending.ToList()) DrawChoice(path, army, unit, sheet, pending);

        ImGui.Separator();
        DrawUpgrades(path, army, unit, n);
        ImGui.EndChild();

        if (UiButton.Back("Disband unit", new Vector2(-1f, UiChrome.ButtonSize("Disband unit").Y))) _openDisband = true;
    }

    private void DrawDisbandModal()
    {
        if (_openDisband) { ImGui.OpenPopup(DisbandTitle); _openDisband = false; }
        if (!ImGui.BeginPopupModal(DisbandTitle, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (Managed is not { } m || _unit < 0 || _unit >= m.Army.Units.Count) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        (string path, BuiltArmyFile army, _) = m;
        UnitFileEntry unit = army.Units[_unit];
        UiText.Colored(TextBright, $"Disband {unit.Name}?", wrap: false);
        UiText.Disabled("Its XP and upgrades are gone, and nothing comes back.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Disband", UiChrome.ButtonSize("  Disband  ")))
        {
            CampaignPurchase result = CampaignEconomy.Disband(army, unit);
            army.StrikeTeam!.History.Add(result.Message);
            SaveCurrent(path, army);
            Say(result.Message);
            _unit = Math.Max(0, _unit - 1);
            Reload();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (UiButton.Navigate("Cancel", UiChrome.ButtonSize("  Cancel  ")) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string Describe(WeaponFileEntry w)
    {
        string range = w.RangeInches > 0 ? $"{w.RangeInches}\", " : "";
        return $"{w.Name} ({range}A{w.Attacks})";
    }

    private static void Line(string label, IEnumerable<string> items, Vector4? colour)
    {
        string text = string.Join(", ", items);
        UiText.Colored(TextDim, label + ":");
        ImGui.SameLine();
        if (text.Length == 0) UiText.Disabled("none");
        else if (colour is { } c) UiText.Colored(c, text);
        else UiText.Wrapped(text);
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

        ImGui.Spacing();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.Indent(6f);
        ImGui.Dummy(new Vector2(0f, 2f));
        ImGui.BeginGroup();
        UiText.Colored(Gold, "WAITING ON YOU: " + (pending is CampaignTables.LuckyBreak ? "pick an injury"
            : pending is CampaignTables.LearnedLesson ? "pick a talent"
            : pending is CampaignTables.Specialist or CampaignTables.FastLearner ? "pick a trait" : "pick a skill set"));
        UiText.Wrapped(what);
        bool first = true;
        foreach (string option in CampaignRules.Options(sheet, pending))
        {
            string label = option.Replace("Trait: ", "") + "##" + pending;
            float bw = ImGui.CalcTextSize(option).X + ImGui.GetStyle().FramePadding.X * 2f + 24f;
            if (!first && ImGui.GetContentRegionAvail().X > bw) ImGui.SameLine();
            first = false;
            if (UiButton.NavigateSmall(label))
            {
                string line = CampaignRules.Choose(unit, pending, option, D6);
                army.StrikeTeam!.History.Add(line);
                SaveCurrent(path, army);
                Say(line);
            }
        }
        ImGui.EndGroup();
        ImGui.Unindent(6f);
        Vector2 p1 = new(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X,
                         ImGui.GetItemRectMax().Y + 4f);
        ImGui.GetWindowDrawList().AddRect(p0, p1, U(Gold), 3f, ImDrawFlags.None, 1.5f);
        ImGui.Dummy(new Vector2(0f, 4f));
    }

    /// <summary>The unit's upgrade sections at campaign prices, paid from the treasury. Every price and every
    /// purchase goes through <see cref="CampaignEconomy"/> - this only draws it.</summary>
    private void DrawUpgrades(string path, BuiltArmyFile army, UnitFileEntry unit, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "UPGRADES (campaign prices, from the treasury)");
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
                if (!owned && !affordable) UiText.Disabled($"   needs {price - treasury} more");
                if (option.CostUnpriced) UiText.Disabled("   (OPR publishes no price for this one)");
            }
            ImGui.PopID();
        }
    }

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

    // Right: the campaign and the next mission -------------------------------------------------------

    private CampaignPreGame.CampaignStanding StandingOf(BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        return CampaignPreGame.Standing(info, EnemyOf(info)?.Army.StrikeTeam);
    }

    private void DrawNextMission(string path, BuiltArmyFile army, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        CampaignPreGame.CampaignStanding standing = StandingOf(army);
        string rivalName = EnemyOf(info)?.Army.StrikeTeam?.Name ?? "No enemy";

        float playH = UiChrome.ButtonSize("PLAY NEXT MISSION").Y * 1.3f + ImGui.GetStyle().ItemSpacing.Y * 2f;
        ImGui.BeginChild("##missionbody", new Vector2(0, -playH), ImGuiChildFlags.None);

        UiText.Colored(TextDim, "CAMPAIGN");
        ImGui.Separator();
        UiText.Colored(standing.HasTarget ? Accent : TextDim, standing.Describe());
        UiText.Wrapped($"You {standing.YourVp} VP  -  {rivalName} {standing.RivalVp} VP");

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
                ECampaignOutcome.YouWon => $"{info.Name} reached {standing.VpTarget} VP. Nothing more to play.",
                ECampaignOutcome.RivalWon => $"{rivalName} reached {standing.VpTarget} VP first.",
                _ => $"Both armies reached {standing.VpTarget} VP in the same mission on equal VP. The "
                     + "book has no tiebreak for that, so it stands as a draw.",
            });
        }
        else
        {
            ImGui.Spacing();
            UiText.Colored(TextDim, $"NEXT MISSION - {info.Matches + 1}");
            ImGui.Separator();
            if (CampaignPlans.Next(info) is { } plan && plan.Title.Length > 0) UiText.Wrapped(plan.Title);

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
                UiText.Colored(Gold, $"{info.SpecialObjective} - worth +{n.SpecialObjectiveBonus} pts");
                UiText.Wrapped(CampaignPreGame.ObjectiveText(info.SpecialObjective));
            }

            ImGui.Spacing();
            UiText.Colored(TextDim, "Enemy");
            if (EnemyOf(info) is { } foe && foe.Army.StrikeTeam is { } foeInfo)
            {
                UiText.Colored(EnemyTint, foeInfo.Name);
                UiText.Wrapped($"{foe.Army.Units.Count} unit(s), {foe.Army.TotalPoints} pts, treasury {foeInfo.BankedPoints} pts");
                int levels = foe.Army.Units.Sum(u => CampaignRules.Sheet(u)?.Level ?? 0);
                if (levels > 0) UiText.Disabled($"{levels} level(s) earned across its army so far.");
                UiText.Disabled("How much of it comes is set by the difficulty on Mission Setup.");
            }
            else UiText.Disabled("No standing enemy.");
        }

        if (info.History.Count > 0)
        {
            ImGui.Spacing();
            UiText.Colored(TextDim, "LAST MISSION");
            ImGui.Separator();
            foreach (string line in Enumerable.Reverse(info.History).Take(6)) UiText.Wrapped(line);
        }
        ImGui.EndChild();

        ImGui.BeginDisabled(standing.IsOver);
        if (UiButton.Confirm("PLAY NEXT MISSION", new Vector2(-1f, UiChrome.ButtonSize("PLAY NEXT MISSION").Y * 1.3f)))
            GoToMissionSetup();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(standing.IsOver
                ? "This campaign is over."
                : "Opens Mission Setup: the mission, who's coming on both sides, difficulty, START MISSION.", 320f);
    }

    // -- Recruit: the Army Forge roster with a cart ---------------------------------------------------

    /// <summary>Roster unit ids waiting to be confirmed, in the order they were added.</summary>
    private readonly List<string> _cart = new();
    private bool _cartVeteran;
    private string? _recruitSelected;

    // The roster of the managed army's own book, in Army Forge's category boxes - rebuilt per book.
    private BookFile? _recruitBook;
    private IReadOnlyList<ForgeRosterCategories.Group> _recruitGroups = Array.Empty<ForgeRosterCategories.Group>();
    private Dictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)> _recruitCards = new();
    private RuleGlossary _recruitGlossary = RuleGlossary.Empty;

    private void OpenRecruit()
    {
        if (Managed is not { } m) return;
        if (m.Army.Book == null) { Say("This army has no book attached, so there is nothing to recruit from.", true); return; }
        _cart.Clear();
        _cartVeteran = false;
        _recruitSelected = null;
        _view = EView.Recruit;
        _message = "";
    }

    private void UseRecruitBook(BookFile book)
    {
        if (ReferenceEquals(book, _recruitBook)) return;
        _recruitBook = book;
        _recruitGroups = ForgeRosterCategories.GroupBook(book);
        _recruitCards = ArmyForgeScreen.BuildRosterCards(book);
        _recruitGlossary = RuleGlossary.Build(book);
    }

    private static bool IsHeroRoster(RosterUnit roster) =>
        roster.Rules.Any(r => r is SpecialRuleEntry_Core c && FDG.Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(c.Name));

    private int CartTotal(BookFile book) =>
        _cart.Sum(id => _recruitCards.TryGetValue(id, out var c) ? c.Unit.PointCost
            : book.Units.FirstOrDefault(r => r.Id == id)?.BasePointCost ?? 0)
        + (_cartVeteran && _cart.Count > 0 && Managed is { } m && !CampaignEconomy.VeteranRecruitUsed(m.Army)
            ? NumbersFor(m.Army).VeteranRecruitCost : 0);

    /// <summary>
    /// Why another of <paramref name="roster"/> can't go in the cart, or null. Campaign Rules v3.6.0,
    /// 6. Recruit Units: "Players may recruit up to one new Hero after each mission, and may only have 3
    /// Heroes in their army at once" and "may recruit as many regular units as they want". The cart counts:
    /// a Hero already in it uses the allowance, and its points come out of the treasury first.
    /// </summary>
    internal static string? CannotAddToCart(BuiltArmyFile army, RosterUnit roster, int unitPrice,
        IReadOnlyList<bool> cartIsHero, int cartTotal)
    {
        // Worked out from the card's own numbers rather than CampaignEconomy.CannotRecruit, which compiles the
        // unit - once per card per frame was too much. Confirm still goes through CampaignEconomy.Recruit,
        // which checks everything again.
        bool hero = IsHeroRoster(roster);
        if (hero && CampaignEconomy.HeroRecruitBlockedReason(army) is { } heroReason)
            return heroReason;
        if (hero && cartIsHero.Any(h => h))
            return army.StrikeTeam is { Matches: 0 }
                ? "Before the first mission an army may have only one Hero."
                : "Only one new Hero may be recruited after each mission.";
        if (hero && CampaignEconomy.HeroCount(army) + cartIsHero.Count(h => h) >= CampaignNumbers.MaxHeroes)
            return $"An army may only have {CampaignNumbers.MaxHeroes} Heroes at once.";
        int left = (army.StrikeTeam?.BankedPoints ?? 0) - cartTotal;
        return unitPrice > left ? $"Costs {unitPrice} pts - the treasury has {left} pts left after the cart." : null;
    }

    private string? WhyNot(BuiltArmyFile army, BookFile book, RosterUnit roster) =>
        CannotAddToCart(army, roster, _recruitCards.TryGetValue(roster.Id, out var c) ? c.Unit.PointCost : roster.BasePointCost,
            _cart.Select(id => book.Units.FirstOrDefault(r => r.Id == id) is { } r && IsHeroRoster(r)).ToList(),
            CartTotal(book));

    private void DrawRecruit(float x, float y, float w, float h)
    {
        if (Managed is not { } cur || cur.Army.Book is not { } book) { _view = EView.Camp; return; }
        (string path, BuiltArmyFile army, _) = cur;
        UseRecruitBook(book);
        CampaignNumbers n = NumbersFor(army);

        float gap = 12f;
        float cartH = ImGui.GetTextLineHeightWithSpacing() * 3f + ImGui.GetFrameHeightWithSpacing() * 2f
                      + ImGui.GetStyle().WindowPadding.Y * 2f;
        float leftW = w * 0.27f;
        float bodyH = h - cartH - gap;

        bool tinted = ManagingEnemyNow;
        if (tinted) { ImGui.PushStyleColor(ImGuiCol.Border, EnemyTint); ImGui.PushStyleColor(ImGuiCol.ChildBg, EnemyBg); }

        // Left: the clicked roster unit, as the Forge shows it, with + ADD TO CART pinned at the bottom.
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##recruitunit", new Vector2(leftW, bodyH), ImGuiChildFlags.Borders);
        RosterUnit? picked = _recruitSelected == null ? null : book.Units.FirstOrDefault(r => r.Id == _recruitSelected);
        if (picked != null && _recruitCards.TryGetValue(picked.Id, out var shown))
        {
            float addH = UiChrome.ButtonSize("+ ADD TO CART").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
            ImGui.BeginChild("##recruitbody", new Vector2(0, -addH), ImGuiChildFlags.None);
            _cards.DrawPortrait(picked.Name, book.Faction, new Vector2(UiChrome.Em(5f), UiChrome.Em(6.5f)));
            ImGui.SameLine();
            ImGui.BeginGroup();
            UiText.Disabled("FROM THE ROSTER");
            ImGui.PushTextWrapPos(0f);
            UiText.Colored(TextBright, picked.Name);
            ImGui.PopTextWrapPos();
            UiText.Disabled($"{ForgeRosterCategories.TitleFor(picked, book.GameSystem)} - {shown.Unit.ModelCount} model(s)");
            UiText.Colored(Gold, $"{shown.Unit.PointCost} pts");
            ImGui.EndGroup();
            ForgeUnitDetail.DrawStatPills(shown.Unit);
            ForgeUnitDetail.DrawGear(shown.Unit, shown.Items, _recruitGlossary);
            ImGui.EndChild();
            string? why = WhyNot(army, book, picked);
            ImGui.BeginDisabled(why != null);
            if (UiButton.Confirm("+ ADD TO CART", new Vector2(-1f, UiChrome.ButtonSize("+ ADD TO CART").Y))) _cart.Add(picked.Id);
            ImGui.EndDisabled();
            if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped(why, 320f);
        }
        else UiText.Disabled("Click a unit card to see it here. Double-click a card to put it in the cart.");
        ImGui.EndChild();

        // Right: the roster in Army Forge's boxes, two columns.
        ImGui.SetCursorPos(new Vector2(x + leftW + gap, y));
        ImGui.BeginChild("##recruitroster", new Vector2(w - leftW - gap, bodyH), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, $"UNIT ROSTER - {book.Name}");
        UiText.Disabled("Greyed cards can't be recruited right now - hover one to see why. One new Hero after each mission.");
        ImGui.Separator();
        if (ImGui.BeginTable("##recruitcols", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();
            var columns = ArmyForgeScreen.SplitIntoColumns(_recruitGroups.Select(g => g.Units.Count).ToList());
            for (int col = 0; col < 2; col++)
            {
                ImGui.TableSetColumnIndex(col);
                foreach (int g in columns[col]) DrawRecruitBox(army, book, _recruitGroups[g]);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        if (tinted) ImGui.PopStyleColor(2);

        ImGui.SetCursorPos(new Vector2(x, y + h - cartH));
        ImGui.BeginChild("##cart", new Vector2(w, cartH), ImGuiChildFlags.Borders);
        DrawCart(path, army, book, n);
        ImGui.EndChild();
    }

    private void DrawRecruitBox(BuiltArmyFile army, BookFile book, ForgeRosterCategories.Group group)
    {
        ImGui.PushID(group.Title);
        UiText.Colored(ImGuiTheme.HeaderAccent, group.Title.ToUpperInvariant(), wrap: false);
        ImGui.Separator();
        var cards = new List<UnitPortraitCards.Card>(group.Units.Count);
        foreach (RosterUnit roster in group.Units)
        {
            UnitFileEntry unit = _recruitCards[roster.Id].Unit;
            string? why = WhyNot(army, book, roster);
            int inCart = _cart.Count(id => id == roster.Id);
            (string name, string facts) = UnitPortraitCards.CardCaption(unit);
            cards.Add(new UnitPortraitCards.Card(name, roster.Name, facts,
                () => UnitPortraitCards.StatsTooltip(unit) + (why != null ? "\n\nCan't recruit: " + why : "\n\nDouble-click to put it in the cart."),
                inCart > 0 ? $"in cart x{inCart}" : null, Good, Dimmed: why != null));
        }
        int selected = group.Units.ToList().FindIndex(u => u.Id == _recruitSelected);
        UnitPortraitCards.CardEvent hit = _cards.DrawCardGrid("box", cards, book.Faction, selected);
        if (hit.Clicked >= 0) _recruitSelected = group.Units[hit.Clicked].Id;
        if (hit.DoubleClicked >= 0)
        {
            RosterUnit r = group.Units[hit.DoubleClicked];
            if (WhyNot(army, book, r) is { } why) Say(why, true); else _cart.Add(r.Id);
        }
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawCart(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        int total = CartTotal(book);

        UiText.Colored(TextDim, "CART", wrap: false);
        ImGui.SameLine();
        if (_cart.Count == 0) UiText.Disabled("empty - double-click a card, or + ADD TO CART", wrap: false);
        for (int i = 0; i < _cart.Count; i++)
        {
            string name = book.Units.FirstOrDefault(r => r.Id == _cart[i])?.Name ?? _cart[i];
            ImGui.SameLine();
            if (UiButton.NavigateSmall($"{name}  x##cart{i}")) { _cart.RemoveAt(i); break; }
            if (ImGui.IsItemHovered()) UiText.Tooltip("Take it out of the cart.");
        }

        bool used = CampaignEconomy.VeteranRecruitUsed(army);
        bool veteran = _cartVeteran && !used;
        ImGui.BeginDisabled(used || _cart.Count == 0);
        if (UiButton.Checkbox($"The first unit in the cart starts at 5 XP (+{n.VeteranRecruitCost} pts)", ref veteran)) _cartVeteran = veteran;
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(used
                ? "Already used since the last mission. Campaign Rules v3.6.0: one of the new recruited units may start with 5 XP."
                : "Campaign Rules v3.6.0: one of the new recruited units may also start with 5 XP if the player pays for it.", 360f);

        ImGui.TextUnformatted($"Total {total} pts  -  treasury {info.BankedPoints} pts  -  leaves {info.BankedPoints - total} pts");
        ImGui.SameLine();
        float bw = UiChrome.ButtonSize("  CONFIRM RECRUITS  ").X;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - bw - ImGui.GetStyle().WindowPadding.X);
        ImGui.BeginDisabled(_cart.Count == 0 || total > info.BankedPoints);
        if (UiButton.Confirm("CONFIRM RECRUITS", UiChrome.ButtonSize("  CONFIRM RECRUITS  "))) ConfirmRecruits(path, army, book, n);
        ImGui.EndDisabled();
    }

    internal void ConfirmRecruits(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        bool veteranLeft = _cartVeteran;
        var lines = new List<string>();
        foreach (string id in _cart.ToList())
        {
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == id);
            if (roster == null) continue;
            var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
            // The one-Hero allowance and the 5 XP recruit are recorded on the army file by Recruit itself.
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, wanted, n, veteran: veteranLeft, d6: D6);
            lines.Add(result.Message);
            if (result.Ok && veteranLeft) veteranLeft = false;
        }

        army.StrikeTeam!.History.AddRange(lines);
        SaveCurrent(path, army);
        _cart.Clear();
        _cartVeteran = false;
        Reload();
        _view = EView.Camp;
        Say(string.Join("  ", lines));
    }

    // -- New campaign: four tabs (#452, batch F) ------------------------------------------------------
    //
    // Wish list: "1 Rules (VP target with Roll D3+4, or Endless; No Permadeath; story note) - 2 Your army
    // (build in the Army Forge campaign mode, or use one you have; name it) - 3 The enemy (build in the
    // Forge or Pick one for me; name it) - 4 Missions (plan as I go / plan ahead / start from a saved
    // campaign)". Nothing touches the disk until START CAMPAIGN.

    internal enum ENewTab { Rules, YourArmy, Enemy, Missions }
    internal enum EMissionPlanning { AsIGo, Ahead, FromSaved }

    private ENewTab _newTab = ENewTab.Rules;
    private EMissionPlanning _planning = EMissionPlanning.AsIGo;

    private string _newName = "";
    private bool _newNoPermadeath;
    private ECampaignType _newType = ECampaignType.PointLimited;
    private int _newVpTarget = 7;
    private string _newOpeningNote = "";
    private bool _showExisting;

    private BuiltArmyFile? _draftYours;
    private BuiltArmyFile? _draftEnemy;
    private string? _draftYoursSource;
    private string _enemyName = "";

    /// <summary>The missions planned ahead (or loaded from a saved campaign), numbered 1..N.</summary>
    private readonly List<CampaignMissionPlan> _draftPlans = new();
    private string? _newScriptPath;
    private string _draftPremise = "";

    private bool _returningFromForge;

    /// <summary>Opens the locked Army Forge for a campaign army. Wired in Program.cs. False when that game
    /// has no books to build from.</summary>
    public Func<ArmyForgeScreen.CampaignBuild, bool>? OnBuildArmy;

    internal void StartNew()
    {
        _view = EView.New;
        _newTab = ENewTab.Rules;
        _planning = EMissionPlanning.AsIGo;
        _newName = "";
        _newOpeningNote = "";
        _newScriptPath = null;
        _draftPremise = "";
        _draftPlans.Clear();
        _draftYours = _draftEnemy = null;
        _draftYoursSource = null;
        _enemyName = "";
        _showExisting = false;
        Reload();
        _message = "";
    }

    private void DrawNew(float x, float y, float w, float h)
    {
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##newcampaign", new Vector2(w, h), ImGuiChildFlags.Borders);

        // The four tabs, as wide buttons; any tab can be visited in any order.
        string[] names = { "1. Rules", "2. Your army", "3. The enemy", "4. Missions" };
        float tabW = (ImGui.GetContentRegionAvail().X - 18f) / 4f;
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) ImGui.SameLine(0f, 6f);
            bool on = (int)_newTab == i;
            string done = TabDone((ENewTab)i) ? "  (done)" : "";
            if (on ? UiButton.Confirm(names[i] + done + "##tab" + i, new Vector2(tabW, UiChrome.Em(2.2f)))
                   : UiButton.Navigate(names[i] + done + "##tab" + i, new Vector2(tabW, UiChrome.Em(2.2f))))
                _newTab = (ENewTab)i;
        }
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginChild("##newtab", new Vector2(0, 0), ImGuiChildFlags.None);
        switch (_newTab)
        {
            case ENewTab.Rules: DrawRulesTab(); break;
            case ENewTab.YourArmy: DrawYourArmyTab(); break;
            case ENewTab.Enemy: DrawEnemyTab(); break;
            case ENewTab.Missions: DrawMissionsTab(); break;
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private bool TabDone(ENewTab tab) => tab switch
    {
        ENewTab.Rules => true,
        ENewTab.YourArmy => _draftYours != null && YourNameProblem() == null,
        ENewTab.Enemy => _draftEnemy != null && EnemyNameProblem() == null,
        _ => MissionsProblem() == null,
    };

    private void DrawRulesTab()
    {
        UiText.Colored(TextDim, "HOW IT ENDS");
        if (ImGui.RadioButton("Race to a VP target", _newType == ECampaignType.PointLimited)) _newType = ECampaignType.PointLimited;
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Campaign Rules v3.6.0, Point-Limited: the campaign ends once a player reaches D3+4 VP.", 360f);
        ImGui.SameLine();
        if (ImGui.RadioButton("Endless", _newType == ECampaignType.Endless)) _newType = ECampaignType.Endless;
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Campaign Rules v3.6.0, Endless: the campaign never ends, but instead players keep track of how many VPs they earned on an ongoing campaign leaderboard.", 360f);

        if (_newType == ECampaignType.PointLimited)
        {
            ImGui.SetNextItemWidth(110f);
            ImGui.InputInt("VP to win", ref _newVpTarget);
            _newVpTarget = Math.Clamp(_newVpTarget, 1, 999);
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Roll it (D3+4)")) _newVpTarget = CampaignPreGame.RollCampaignLength(D6);
            UiText.Wrapped($"The first army to {_newVpTarget} VP wins the campaign. The book rolls this as D3+4; "
                           + "any other number is a house rule.");
        }
        else UiText.Wrapped("The campaign never ends. VP pile up as a running score and you stop whenever you like.");
        ImGui.Spacing();

        UiText.Colored(TextDim, "LOSSES");
        UiButton.Checkbox("No Permadeath", ref _newNoPermadeath);
        if (ImGui.IsItemHovered())
            UiText.TooltipWrapped("Campaign Rules v3.6.0: \"Optionally you can play without units being removed from the army when they die, but instead they lose all XP earned during the match and must sit out the next D3 matches.\"", 380f);
        UiText.Wrapped(_newNoPermadeath
            ? "A unit that would be killed survives instead: it loses the XP it earned that mission and sits out the next D3 missions."
            : "A unit that is killed is gone, with its XP, traits and upgrades. This is the book's default.");
        ImGui.Spacing();

        UiText.Colored(TextDim, "THE STORY (optional)");
        UiText.Disabled("Why these two armies are fighting. The first page of the campaign's Journal - skip it if you like.");
        ImGui.InputTextMultiline("##openingNote", ref _newOpeningNote, CampaignJournal.MaxNoteLength,
            new Vector2(Math.Min(720f, ImGui.GetContentRegionAvail().X), ImGui.GetTextLineHeight() * 5f));
        ImGui.Spacing();
        UiText.Disabled($"The game is {GameSystemInfo.DisplayName(_system)} - it comes from the button you pressed.");
    }

    private void DrawYourArmyTab()
    {
        CampaignNumbers n = CampaignNumbers.For(_system);
        UiText.Wrapped($"Treasury: {n.Treasury} pts - spend at least {n.MinimumSpend}, with at most one Hero. "
                       + "Whatever is left you keep to spend between missions.");
        if (UiButton.Confirm("Build it in the Army Forge", UiChrome.ButtonSize("  Build it in the Army Forge  "))) BuildYourArmy();
        ImGui.SameLine();
        if (UiButton.Navigate("Use an army I already have", UiChrome.ButtonSize("  Use an army I already have  "))) _showExisting = !_showExisting;

        if (_showExisting)
        {
            var armies = Armies;
            ImGui.BeginChild("##existing", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 6f), ImGuiChildFlags.Borders);
            if (armies.Count == 0)
                UiText.Disabled($"No {GameSystemInfo.DisplayName(_system)} armies in armies\\{ArmyPaths.FolderNameFor(_system)} yet - build one instead.");
            for (int i = 0; i < armies.Count; i++)
            {
                BuiltArmyFile a = armies[i].Army;
                string? why = CampaignRules.CannotStart(a, n);
                ImGui.BeginDisabled(why != null);
                if (ImGui.Selectable($"{a.Name}   ({a.Faction}, {a.TotalPoints} pts, {a.Units.Count} units)##a{i}",
                        armies[i].Path == _draftYoursSource))
                    UseExistingArmy(i);
                ImGui.EndDisabled();
                if (why != null) { ImGui.SameLine(); UiText.Colored(Bad, why, wrap: false); }
            }
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Name your army:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiChrome.Em(16f));
        ImGui.InputText("##teamName", ref _newName, 48);
        if (YourNameProblem() is { } p && _draftYours != null) { ImGui.SameLine(); UiText.Colored(Bad, p, wrap: false); }
        ImGui.Spacing();
        DrawDraftPreview("yours", _draftYours, "No army yet. Build one in the Army Forge (campaign mode, treasury in the points bar), or use one you have.");
    }

    private void DrawEnemyTab()
    {
        bool canPick = EnemyCandidates().Count > 0;
        if (UiButton.Confirm("Build the enemy in the Army Forge", UiChrome.ButtonSize("  Build the enemy in the Army Forge  "))) BuildEnemy();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canPick);
        if (UiButton.Navigate("Pick one for me", UiChrome.ButtonSize("  Pick one for me  "))) PickEnemyForMe();
        ImGui.EndDisabled();
        if (!canPick && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped($"No other {GameSystemInfo.DisplayName(_system)} army in your armies folder fits the treasury - build the enemy instead.", 340f);
        UiText.Disabled("You do the enemy's bookkeeping too (its upgrades, recruits, trait picks). The bot plays it at the table.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Name the enemy:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiChrome.Em(16f));
        ImGui.InputText("##enemyName", ref _enemyName, 48);
        if (EnemyNameProblem() is { } p && _draftEnemy != null) { ImGui.SameLine(); UiText.Colored(Bad, p, wrap: false); }
        ImGui.Spacing();
        DrawDraftPreview("enemy", _draftEnemy, "No enemy yet.");
    }

    private void DrawDraftPreview(string id, BuiltArmyFile? army, string empty)
    {
        if (army != null)
        {
            CampaignNumbers n = CampaignNumbers.For(_system);
            UiText.Colored(Gold, $"{army.Faction} - {army.Units.Count} unit(s), {army.TotalPoints} pts, "
                                 + $"{army.Units.Count(CampaignRules.IsHero)} hero(es) - {Math.Max(0, n.Treasury - army.TotalPoints)} pts left in the treasury");
        }
        _cards.Draw(id, army, ImGui.GetContentRegionAvail(), empty);
    }

    private void DrawMissionsTab()
    {
        Vector2 b = UiChrome.ButtonSize("  Start from a saved campaign  ");
        if (PlanningButton("Plan each mission as I go", EMissionPlanning.AsIGo, b)) { _planning = EMissionPlanning.AsIGo; }
        ImGui.SameLine();
        if (PlanningButton("Plan missions ahead", EMissionPlanning.Ahead, b))
        {
            if (_planning != EMissionPlanning.Ahead) { _planning = EMissionPlanning.Ahead; _newScriptPath = null; }
        }
        ImGui.SameLine();
        if (PlanningButton("Start from a saved campaign", EMissionPlanning.FromSaved, b)) _planning = EMissionPlanning.FromSaved;
        ImGui.Spacing();

        switch (_planning)
        {
            case EMissionPlanning.AsIGo:
                UiText.Wrapped("Each mission is rolled from the books when you get to it, on Mission Setup - deployment, "
                               + "mission type and difficulty - and every part can be rerolled or picked by hand there.");
                return;
            case EMissionPlanning.FromSaved:
            {
                var scripts = FdgRaylib.SaveLoad.CampaignScriptPaths.List(_system);
                if (scripts.Count == 0)
                    UiText.Disabled($"No saved {GameSystemInfo.DisplayName(_system)} campaigns yet. Save one from Mission Setup (\"Save campaign as a file\"), or here with \"Plan missions ahead\".");
                foreach (var (scriptPath, script) in scripts)
                {
                    if (ImGui.RadioButton($"{script.Name} ({script.Missions.Count} mission(s))##{scriptPath}", _newScriptPath == scriptPath))
                        LoadScript(scriptPath, script);
                }
                if (_newScriptPath == null) return;
                ImGui.Separator();
                UiText.Disabled("Its missions, which you can still change:");
                break;
            }
        }
        DrawPlanner();
    }

    private bool PlanningButton(string label, EMissionPlanning which, Vector2 size) =>
        _planning == which ? UiButton.Confirm(label, size) : UiButton.Navigate(label, size);

    private void LoadScript(string path, CampaignScript script)
    {
        _newScriptPath = path;
        _draftPlans.Clear();
        foreach (CampaignMissionPlan plan in script.Missions.OrderBy(p => p.Mission)) _draftPlans.Add(plan.Clone());
        Renumber();
        _draftPremise = script.Premise;
    }

    private void Renumber()
    {
        for (int i = 0; i < _draftPlans.Count; i++) _draftPlans[i].Mission = i + 1;
    }

    /// <summary>The mission planner - the same idea as the Quest Structure screen: add missions (rolled from
    /// the books), change or reroll each part, write a briefing, remove one, save the lot as a file.</summary>
    private void DrawPlanner()
    {
        if (UiButton.Confirm("+ Add a mission", UiChrome.ButtonSize("  + Add a mission  ")))
        {
            _draftPlans.Add(CampaignMissionGenerator.Generate(_draftPlans.Count + 1, _system, D6));
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_draftPlans.Count == 0);
        if (UiButton.Navigate("Save as a campaign file", UiChrome.ButtonSize("  Save as a campaign file  ")))
        {
            var script = new CampaignScript
            {
                Name = _newName.Trim().Length > 0 ? _newName.Trim() + " plan" : "Campaign plan",
                GameSystem = _system,
                Premise = CampaignJournal.Clean(_newOpeningNote),
                Missions = _draftPlans.Select(p => p.Clone()).ToList(),
            };
            if (FdgRaylib.SaveLoad.CampaignScriptPaths.Save(script) is { } saved)
                Say($"Saved to {FdgRaylib.SaveLoad.CampaignScriptPaths.DirectoryName}\\{Path.GetFileName(saved)}.");
            else Say("Couldn't save the campaign file.", true);
        }
        ImGui.EndDisabled();
        UiText.Disabled("Missions after the last one planned are rolled when you get to them.");
        ImGui.Separator();

        int remove = -1;
        for (int i = 0; i < _draftPlans.Count; i++)
        {
            CampaignMissionPlan plan = _draftPlans[i];
            ImGui.PushID($"plan{i}");
            UiText.Colored(Gold, $"MISSION {plan.Mission}", wrap: false);
            ImGui.SameLine();
            string title = plan.Title;
            ImGui.SetNextItemWidth(UiChrome.Em(18f));
            if (ImGui.InputText("##title", ref title, 80)) plan.Title = title;
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Reroll all"))
            {
                CampaignMissionPlan fresh = CampaignMissionGenerator.Generate(plan.Mission, _system, D6);
                fresh.Notes = plan.Notes;
                _draftPlans[i] = plan = fresh;
            }
            ImGui.SameLine();
            if (UiButton.Back("Remove", UiChrome.ButtonSize("Remove"))) remove = i;

            if (AdvancedDeployment.Available(_system))
            {
                ImGui.SetNextItemWidth(UiChrome.Em(14f));
                if (UiCombo.Begin("Deployment##d", AdvancedDeployment.Name(plan.DeploymentStyle), ImGuiComboFlags.HeightLarge))
                {
                    foreach (EDeploymentStyle style in Enum.GetValues<EDeploymentStyle>())
                        if (ImGui.Selectable(AdvancedDeployment.Name(style), style == plan.DeploymentStyle)) plan.DeploymentStyle = style;
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                if (UiButton.NavigateSmall("Reroll##rd")) plan.DeploymentStyle = CampaignMissionGenerator.RollDeployment(_system, D6);
                ImGui.SameLine();
            }
            if (FDG.Missions.MissionCardCatalog.Available(_system))
            {
                ImGui.SetNextItemWidth(UiChrome.Em(14f));
                string label = plan.MissionCards == FDG.Missions.EMissionCardGame.Off ? "Core objectives" : FDG.Missions.MissionCardCatalog.Name(plan.MissionCards);
                if (UiCombo.Begin("Mission type##t", label, ImGuiComboFlags.HeightLarge))
                {
                    foreach (FDG.Missions.EMissionCardGame game in Enum.GetValues<FDG.Missions.EMissionCardGame>())
                    {
                        string name = game == FDG.Missions.EMissionCardGame.Off ? "Core objectives" : FDG.Missions.MissionCardCatalog.Name(game);
                        if (ImGui.Selectable(name, game == plan.MissionCards)) plan.MissionCards = game;
                        if (ImGui.IsItemHovered()) UiText.Tooltip(CampaignMissionGenerator.DescribeMissionType(game));
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
                if (UiButton.NavigateSmall("Reroll##rt")) plan.MissionCards = CampaignMissionGenerator.RollMissionType(_system, D6);
                ImGui.SameLine();
            }
            ImGui.SetNextItemWidth(UiChrome.Em(9f));
            if (UiCombo.Begin("Difficulty##f", MissionDifficulty.Name(plan.Difficulty)))
            {
                foreach (EMissionDifficulty option in MissionDifficulty.All)
                    if (ImGui.Selectable(MissionDifficulty.Name(option), option == plan.Difficulty)) plan.Difficulty = option;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (UiButton.NavigateSmall("Reroll##rf")) plan.Difficulty = CampaignMissionGenerator.RollDifficulty(D6);

            string notes = plan.Notes;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##notes", "Briefing (optional) - becomes this mission's journal page", ref notes, CampaignJournal.MaxNoteLength))
                plan.Notes = notes;
            ImGui.Separator();
            ImGui.PopID();
        }
        if (remove >= 0) { _draftPlans.RemoveAt(remove); Renumber(); }
        if (_draftPlans.Count == 0) UiText.Disabled("No missions planned yet - \"+ Add a mission\" rolls one from the books.");
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

    internal string? MissionsProblem() => _planning switch
    {
        EMissionPlanning.Ahead when _draftPlans.Count == 0 => "Missions: add at least one, or plan each as you go.",
        EMissionPlanning.FromSaved when _newScriptPath == null => "Missions: pick a saved campaign.",
        _ => null,
    };

    /// <summary>What still stands between the wizard and START CAMPAIGN, or null when nothing does.</summary>
    internal string? NewProblem()
    {
        if (_draftYours == null) return "Your army: build it in the Army Forge or use one you have.";
        if (YourNameProblem() is { } a) return "Your army: " + a;
        if (_draftEnemy == null) return "The enemy: build it or let the game pick one.";
        if (EnemyNameProblem() is { } b) return "The enemy: " + b;
        return MissionsProblem();
    }

    private List<(string Path, BuiltArmyFile Army, EGameSystem System)> EnemyCandidates()
    {
        CampaignNumbers n = CampaignNumbers.For(_system);
        return Armies.Where(a => a.Path != _draftYoursSource && CampaignRules.CannotStart(a.Army, n) == null).ToList();
    }

    private ArmyForgeScreen.CampaignBuild ForgeBuild(string title, Action<BuiltArmyFile> took) =>
        new(_system, title, CampaignNumbers.For(_system),
            army => { took(army); _returningFromForge = true; },
            () => _returningFromForge = true);

    /// <summary>Tab 2: open the locked Army Forge for the player's army; it comes back to this tab.</summary>
    internal void BuildYourArmy()
    {
        _newTab = ENewTab.YourArmy;
        bool opened = OnBuildArmy?.Invoke(ForgeBuild("Your Army", army =>
        {
            _draftYours = army;
            _draftYoursSource = null;
            if (_newName.Trim().Length == 0) _newName = army.Name;
            _newTab = ENewTab.YourArmy;
        })) ?? false;
        if (!opened) Say($"There are no {GameSystemInfo.DisplayName(_system)} army books to build from.", true);
    }

    /// <summary>Tab 2: start from an army already in this game's folder (a fresh copy - the original stays).</summary>
    internal void UseExistingArmy(int index)
    {
        var armies = Armies;
        if (index < 0 || index >= armies.Count) return;
        var source = armies[index];
        if (CampaignRules.CannotStart(source.Army, CampaignNumbers.For(_system)) is { } why) { Say(why, true); return; }
        if (StrikeTeamFiles.Load(source.Path) is not { } copy) { Say("Couldn't read that army file.", true); return; }
        _draftYours = copy;
        _draftYoursSource = source.Path;
        if (_newName.Trim().Length == 0) _newName = copy.Name;
        _showExisting = false;
    }

    /// <summary>Tab 3: build the enemy in the locked Army Forge.</summary>
    internal void BuildEnemy()
    {
        _newTab = ENewTab.Enemy;
        bool opened = OnBuildArmy?.Invoke(ForgeBuild("Enemy Army", army =>
        {
            _draftEnemy = army;
            if (_enemyName.Trim().Length == 0) _enemyName = army.Name;
            _newTab = ENewTab.Enemy;
        })) ?? false;
        if (!opened) Say($"There are no {GameSystemInfo.DisplayName(_system)} army books to build from.", true);
    }

    /// <summary>Tab 3: "Pick one for me" - an army of this game's folder at random, never the player's own.</summary>
    internal void PickEnemyForMe()
    {
        var candidates = EnemyCandidates();
        if (candidates.Count == 0) { Say("No other army in the folder fits the treasury - build the enemy instead.", true); return; }
        var pick = candidates[Rng.Next(candidates.Count)];
        if (StrikeTeamFiles.Load(pick.Path) is not { } copy) { Say("Couldn't read that army file.", true); return; }
        _draftEnemy = copy;
        _enemyName = string.Equals(copy.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? copy.Name + " (rival)" : copy.Name;
        _newTab = ENewTab.Enemy;
    }

    /// <summary>
    /// START CAMPAIGN: writes both campaign armies. The enemy first and the player's last, carrying the link,
    /// so a failure part way never leaves a player campaign pointing at a missing enemy file.
    /// </summary>
    internal void Finish()
    {
        if (NewProblem() != null || _draftYours is not { } yours || _draftEnemy is not { } enemy) return;
        CampaignNumbers n = CampaignNumbers.For(_system);
        string name = _newName.Trim();
        string enemyName = _enemyName.Trim();

        string enemyPath = StrikeTeamFiles.NewPath(_system, enemyName);
        CampaignRules.Start(enemy, enemyName, enemyPath, n, _system, _newType,
            _newNoPermadeath, isBot: true, vpTarget: _newVpTarget);
        enemy.Selections = null;
        if (!StrikeTeamFiles.Save(enemyPath, enemy)) { Say("Couldn't save the enemy army file.", true); return; }

        string path = StrikeTeamFiles.NewPath(_system, name);
        CampaignRules.Start(yours, name, path, n, _system,
            _newType, _newNoPermadeath, vpTarget: _newVpTarget);
        yours.Selections = null;
        yours.StrikeTeam!.EnemyArmyFile = enemyPath;
        CampaignJournal.SetCampaignNote(yours.StrikeTeam, _newOpeningNote);
        // Planned ahead, or from a saved campaign: the missions and their briefings.
        if (_planning != EMissionPlanning.AsIGo && _draftPlans.Count > 0)
        {
            new CampaignScript
            {
                Name = name,
                GameSystem = _system,
                Premise = _draftPremise,
                Missions = _draftPlans.Select(p => p.Clone()).ToList(),
            }.ApplyTo(yours.StrikeTeam);
        }
        if (!StrikeTeamFiles.Save(path, yours))
        {
            try { File.Delete(enemyPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Say("Couldn't save the campaign army file.", true);
            return;
        }

        _draftYours = _draftEnemy = null;
        _draftPlans.Clear();
        Reload();
        _selected = Teams.FindIndex(t => t.Path == path);
        OpenCamp(_selected);
        Say($"{name} vs {enemyName} - campaign started, with {yours.StrikeTeam.BankedPoints} pts left in your treasury.");
    }

    // -- Test seams --------------------------------------------------------------------------------------

    internal EView ViewForTest => _view;
    internal ENewTab NewTabForTest { get => _newTab; set => _newTab = value; }
    internal EMissionPlanning PlanningForTest { get => _planning; set => _planning = value; }
    internal List<CampaignMissionPlan> DraftPlansForTest => _draftPlans;
    internal string NewNameForTest { get => _newName; set => _newName = value; }
    internal string EnemyNameForTest { get => _enemyName; set => _enemyName = value; }
    internal string OpeningNoteForTest { get => _newOpeningNote; set => _newOpeningNote = value; }
    internal void SetEndForTest(ECampaignType type, int vpTarget) { _newType = type; _newVpTarget = vpTarget; }
    internal string? CurrentPathForTest => Current?.Path;
    internal string? ManagedPathForTest => Managed?.Path;
    internal bool ManagingEnemyForTest => ManagingEnemyNow;
    internal void SwitchSideForTest(bool enemy) => SwitchSide(enemy);
    internal IReadOnlyList<string> ListedPathsForTest => Teams.Select(t => t.Path).ToList();
    internal int ArmyIndexForTest(string path) =>
        Armies.FindIndex(a => string.Equals(Path.GetFullPath(a.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    internal void OpenCampForTest(string path)
    {
        int index = Teams.FindIndex(t => string.Equals(Path.GetFullPath(t.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (index >= 0) OpenCamp(index);
    }

    // -- Bottom bar ---------------------------------------------------------------------------------

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        float btnH = barH * 0.55f;
        float longest = new[] { "START CAMPAIGN", "+ New Campaign", "Back to camp", "Export journal" }
            .Max(l => ImGui.CalcTextSize(l).X);
        Vector2 btn = new(MathF.Max(screenW * 0.13f, longest + ImGui.GetStyle().FramePadding.X * 2f + UiButton.CapsWidth(btnH) + 8f), btnH);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput
                   && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        float rx = screenW - margin - btn.X;

        ImGui.SetCursorPos(new Vector2(margin, by));
        switch (_view)
        {
            case EView.New:
            {
                if (UiButton.Back("Cancel", btn) || esc) { _view = EView.List; _message = ""; }
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (_newTab != ENewTab.Missions)
                {
                    if (UiButton.Confirm("Next", btn)) _newTab = (ENewTab)((int)_newTab + 1);
                }
                else
                {
                    string? problem = NewProblem();
                    ImGui.BeginDisabled(problem != null);
                    if (UiButton.Confirm("START CAMPAIGN", btn)) Finish();
                    ImGui.EndDisabled();
                }
                string? missing = NewProblem();
                if (_message.Length > 0) DrawMessage(margin, by, btn);
                else RightLabel(rx, by, btn, missing ?? "Ready - START CAMPAIGN on the Missions tab.", missing != null ? Gold : Good);
                break;
            }

            case EView.Recruit:
                if (UiButton.Back("Back to camp", btn) || esc) { _cart.Clear(); _view = EView.Camp; }
                DrawMessage(margin, by, btn);
                break;

            case EView.Journal:
                if (UiButton.Back("Back to camp", btn) || esc) { CommitJournal(); _view = EView.Camp; }
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Navigate("Export journal", btn)) ExportJournal();
                RightLabel(rx, by, btn, "Saves the whole journal as a text file", TextDim);
                DrawMessage(margin, by, btn);
                break;

            case EView.Camp:
                if (UiButton.Back("Back", btn) || esc) { _view = EView.List; _message = ""; }
                DrawMessage(margin, by, btn);
                break;

            default:
                if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Confirm("+ New Campaign", btn)) StartNew();
                if (Current != null)
                {
                    ImGui.SetCursorPos(new Vector2(rx - btn.X - 10f, by));
                    if (UiButton.Navigate("OPEN", btn)) OpenCamp(_selected);
                    ImGui.SetCursorPos(new Vector2(rx - btn.X * 2f - 20f, by));
                    if (UiButton.Back("Delete", btn)) _openDelete = true;
                    if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Asks first, then deletes this campaign and BOTH its armies.", 300f);
                }
                DrawMessage(margin, by, btn);
                break;
        }
    }

    // -- Starting the mission -----------------------------------------------------------------------

    /// <summary>
    /// #435 - hands this campaign to Mission Setup, which owns the launch: which units come on both sides,
    /// the difficulty, the map. The other campaign armies of the SAME game go with it (they set the cap).
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
        OnPlay?.Invoke(path, others);
    }

    private void DrawMessage(float margin, float by, Vector2 btn)
    {
        if (_message.Length == 0) return;
        Vector2 ts = ImGui.CalcTextSize(_message);
        ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(_messageIsError ? Bad : Good, _message, wrap: false);
    }

    private static void RightLabel(float rx, float by, Vector2 btn, string text, Vector4 color)
    {
        Vector2 ts = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos(new Vector2(rx - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(color, text, wrap: false);
    }
}
