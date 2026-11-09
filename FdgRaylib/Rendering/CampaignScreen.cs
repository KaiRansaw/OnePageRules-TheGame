using FDG;
using System.Numerics;
using FDG.Quest;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Quest Campaigns, one screen per Quest game (GF: Star Quest and AoF: Quest), from the Campaign Builder.
/// Batch E rebuilt it from Kai's "Quest Campaigns Mockup" (Main, Structure, Journal - wish list
/// 2026-11-07):
/// <list type="bullet">
/// <item><b>List</b>: your campaigns with chapter progress on the left (every campaign autosaves - pick it
/// and carry on). On the right the party as portrait cards (level, class, wounds, power, money, items,
/// injuries), the chapters (done / in progress / not started, a difficulty pick and Start chapter) and the
/// history. Bottom: Delete (asks), Journal, Shop (between chapters only), Edit structure, PLAY NEXT
/// MISSION - which goes straight into a lobby set to Mission mode with the party filled in.</item>
/// <item><b>New campaign</b>: a name and 2-4 heroes. A hero belongs to ONE campaign, so heroes already in
/// another are greyed out.</item>
/// <item><b>Structure</b>: Generate everything (the book's roll), Build it myself, or Start from a saved
/// campaign; per chapter a difficulty, enemy force and theme; per mission its objectives and notes. Save as
/// a campaign file. Unplayed missions stay editable after the campaign starts.</item>
/// <item><b>Journal</b>: the campaign's story page, the chapter / mission list, and per mission what the
/// game recorded plus your own account (the briefing, before it's played). Export journal.</item>
/// <item><b>Shop</b>: the Hero Builder's shop, limited to the party, only between chapters.</item>
/// </list>
/// Rental layout (plain ImGui).
/// </summary>
public sealed class CampaignScreen : IAppScreen
{
    public Action? OnBack;
    /// <summary>Opens the lobby (Mission mode) for the next mission.</summary>
    public Action? OnPlay;

    /// <summary>#418 - which Quest system's campaigns this screen manages (its heroes live in the same
    /// per-system folder).</summary>
    public EGameSystem System { get; }

    private string GameType => QuestGameTypes.For(System);
    private string Money => QuestItemSkin.CurrencySuffix(System);
    private string MoneyWord => QuestItemSkin.CurrencyWord(System);

    public CampaignScreen(EGameSystem system = EGameSystem.GFStarQuest) => System = system;

    private enum EView { List, New, Structure, Journal, Shop }
    private EView _view = EView.List;

    private List<(string Path, QuestCampaign Campaign)> _campaigns = new();
    private int _selected = -1;
    private readonly Dictionary<int, int> _difficultyPick = new();
    private string _message = "";
    private bool _messageIsError;
    private bool _openDelete;

    // New campaign
    private string _newName = "";
    private List<(string Path, QuestHero Hero)> _heroes = new();
    private readonly List<string> _newParty = new();

    // Structure (a new campaign's draft, or a saved campaign being edited in place)
    private QuestCampaign? _draft;
    private string? _draftPath;            // null = a new campaign, not yet written
    private string _structureMode = "";
    private List<(string Path, QuestCampaign Plan)> _plans = new();
    private ArmyCatalog? _armies;
    private bool _openPlans;

    // Journal
    private int _journalChapter;
    private int _journalMission;

    // Shop
    private int _shopHero;

    private readonly UnitPortraitCards _cards = new();
    private readonly Random _rng = new();

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);
    private static readonly Vector4 Title = new(0.55f, 0.85f, 0.80f, 1f);

    public void OnShown()
    {
        Reload();
        _view = EView.List;
        _message = "";
    }

    private void Reload()
    {
        _campaigns = CampaignPaths.LoadAll(GameType);
        _heroes = HeroPaths.LoadAll(GameType);
        if (_selected >= _campaigns.Count) _selected = _campaigns.Count - 1;
        if (_selected < 0 && _campaigns.Count > 0) _selected = 0;
    }

    private (string Path, QuestCampaign Campaign)? Current =>
        _selected >= 0 && _selected < _campaigns.Count ? _campaigns[_selected] : null;

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    private List<(string File, QuestHero? Hero)> Party(QuestCampaign c) =>
        c.HeroFiles.Select(f => (f, CampaignPaths.LoadHero(f))).ToList();

    // ── Frame ─────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##Campaigns", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawTopBar();
        ImGui.Separator();

        float footH = UiChrome.ButtonSize("PLAY NEXT MISSION").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
        Vector2 body = ImGui.GetContentRegionAvail() - new Vector2(0f, footH);
        ImGui.BeginChild("##campaignBody", body, ImGuiChildFlags.None);
        switch (_view)
        {
            case EView.List: DrawList(); break;
            case EView.New: DrawNew(); break;
            case EView.Structure: DrawStructure(); break;
            case EView.Journal: DrawJournal(); break;
            case EView.Shop: DrawShop(); break;
        }
        ImGui.EndChild();
        DrawFooter();

        DrawDeleteModal();
        ImGui.End();
    }

    private void DrawTopBar()
    {
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup);
        float rowH = UiChrome.ButtonSize("Back").Y;
        if (UiButton.Back("Back", UiChrome.ButtonSize("Back")) || esc) GoBack();
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowH - ImGui.GetTextLineHeight()) / 2f);
        string title = _view switch
        {
            EView.New => "NEW CAMPAIGN",
            EView.Structure => _draftPath == null ? "NEW CAMPAIGN - STRUCTURE" : "CAMPAIGN STRUCTURE",
            EView.Journal => "JOURNAL" + (Current is { } j ? " - " + j.Campaign.Name : ""),
            EView.Shop => "CAMPAIGN SHOP",
            _ => "QUEST CAMPAIGNS",
        };
        UiText.Colored(Title, title, wrap: false);
        ImGui.SameLine();
        UiText.Disabled($"   {GameSystemInfo.DisplayName(System)}", wrap: false);

        if (_view == EView.List)
        {
            float w = UiChrome.ButtonSize("+ New Campaign").X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - w));
            // SameLine put the cursor at the title's lowered line; lift it back level with Back.
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (rowH - ImGui.GetTextLineHeight()) / 2f);
            if (UiButton.Confirm("+ New Campaign", UiChrome.ButtonSize("+ New Campaign"))) StartNew();
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Name a campaign, pick its party (2-4 heroes), then plan its chapters.", 320f);
        }
        else if (_view == EView.Journal && Current != null)
        {
            float w = UiChrome.ButtonSize("Export journal").X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - w));
            // SameLine put the cursor at the title's lowered line; lift it back level with Back.
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (rowH - ImGui.GetTextLineHeight()) / 2f);
            if (UiButton.Navigate("Export journal", UiChrome.ButtonSize("Export journal"))) ExportJournal();
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Write the whole journal to a text file beside the campaign's file.", 320f);
        }

        if (_message.Length > 0) UiText.Colored(_messageIsError ? Bad : Good, _message);
    }

    /// <summary>Back goes to the screen this one was opened from: a sub-view goes back to the list (the New
    /// campaign's structure back to its first page), the list back to the previous screen.</summary>
    private void GoBack()
    {
        _message = "";
        switch (_view)
        {
            case EView.List: OnBack?.Invoke(); break;
            case EView.Structure when _draftPath == null: _view = EView.New; break;
            case EView.Structure: SaveDraft(); _draft = null; _view = EView.List; Reload(); break;
            default: _view = EView.List; Reload(); break;
        }
    }

    // ── Campaign list + details ───────────────────────────────────────────────────────────────────────

    private void DrawList()
    {
        Vector2 avail = ImGui.GetContentRegionAvail();
        float leftW = MathF.Max(UiChrome.Em(16f), avail.X * 0.24f);
        ImGui.BeginChild("##campaignList", new Vector2(leftW, avail.Y), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, "CAMPAIGNS");
        if (_campaigns.Count == 0) UiText.Disabled("No campaigns yet.\nClick \"+ New Campaign\".");
        for (int i = 0; i < _campaigns.Count; i++)
        {
            QuestCampaign c = _campaigns[i].Campaign;
            if (ImGui.Selectable($"{c.Name}\n   {ProgressLine(c)}##c{i}", i == _selected, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
                _selected = i;
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##campaignDetail", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        if (Current is { } cur) DrawDetail(cur.Path, cur.Campaign);
        else UiText.Disabled("Pick a campaign on the left, or start a new one.");
        ImGui.EndChild();
    }

    internal static string ProgressLine(QuestCampaign c)
    {
        string status = c.Finished ? "complete" : c.Active is { } a ? $"chapter {a.Number}" : "between chapters";
        return $"{c.Chapters.Count(ch => ch.Done)}/{c.Chapters.Count} chapters - {status}";
    }

    private void DrawDetail(string path, QuestCampaign c)
    {
        UiText.Colored(Gold, c.Name, wrap: false);
        ImGui.SameLine();
        if (c.Finished) UiText.Colored(Good, "   CAMPAIGN COMPLETE", wrap: false);
        else if (c.Active is { } a)
            UiText.Colored(Accent, $"   Chapter {a.Number}: mission {a.MissionsWon + 1} of {a.Missions} - {QuestJournal.DifficultyName(a.Difficulty)}", wrap: false);
        else UiText.Colored(TextDim, "   Between chapters - pick the next one below.", wrap: false);
        ImGui.Spacing();

        UiText.Colored(TextDim, "PARTY - hover a hero for its sheet");
        DrawPartyCards(c);
        ImGui.Separator();

        UiText.Colored(TextDim, "CHAPTERS - play them in any order; a started chapter must be finished first");
        for (int i = 0; i < c.Chapters.Count; i++)
        {
            CampaignChapter ch = c.Chapters[i];
            ImGui.PushID(i);
            string state = ch.Done ? $"done - {QuestJournal.DifficultyName(ch.Difficulty)}"
                : i == c.CurrentChapter ? $"in progress: {ch.MissionsWon} of {ch.Missions} - {QuestJournal.DifficultyName(ch.Difficulty)}"
                : "not started";
            Vector4 col = ch.Done ? TextDim : i == c.CurrentChapter ? Gold : TextBright;
            UiText.Colored(col, $"Chapter {ch.Number}   -   {ch.Missions} mission{(ch.Missions == 1 ? "" : "s")}   -   {state}"
                + (ch.Restarts > 0 ? $"   (restarts: {ch.Restarts})" : "")
                + (ch.Theme.Trim().Length > 0 ? $"   - {ch.Theme.Trim()}" : ""), wrap: false);
            if (c.CanStart(i))
            {
                ImGui.SameLine();
                int pick;
                if (ch.PlannedDifficulty > 0)
                {
                    pick = ch.PlannedDifficulty - 1;
                    UiText.Disabled($"  {QuestJournal.DifficultyName(ch.PlannedDifficulty)} (planned)", wrap: false);
                }
                else
                {
                    if (!_difficultyPick.ContainsKey(i)) _difficultyPick[i] = 0;
                    pick = _difficultyPick[i];
                    ImGui.SetNextItemWidth(UiChrome.Em(9f));
                    if (ImGui.Combo("##diff", ref pick, QuestJournal.Difficulties, QuestJournal.Difficulties.Length)) _difficultyPick[i] = pick;
                    if (ImGui.IsItemHovered())
                        UiText.TooltipWrapped("Difficulty for every mission of this chapter: enemy waves worth 25% / 50% / 75% / 100% of the party's points, with bigger rewards.", 340f);
                }
                ImGui.SameLine();
                if (UiButton.NavigateSmall("Start chapter")) StartChapter(path, c, i, pick + 1);
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Start this chapter. Its missions are played one after another until it is done.", 320f);
            }
            ImGui.PopID();
        }
        ImGui.Separator();

        UiText.Colored(TextDim, "HISTORY");
        if (c.History.Count == 0) UiText.Disabled("Nothing yet.");
        foreach (string line in Enumerable.Reverse(c.History).Take(8)) UiText.Bullet(line);
    }

    private void DrawPartyCards(QuestCampaign c)
    {
        float portrait = UiChrome.Em(5.5f);
        float cardW = UiChrome.Em(13f);
        int n = 0;
        foreach ((string file, QuestHero? hero) in Party(c))
        {
            if (n++ > 0) ImGui.SameLine(0f, 10f);
            if (hero == null)
            {
                UiText.Colored(Bad, $"missing: {Path.GetFileName(file)}", wrap: false);
                continue;
            }
            ImGui.BeginGroup();
            _cards.DrawPortrait(hero.BaseHeroName, hero.Faction, new Vector2(portrait, portrait));
            bool inChapter = c.Active != null;
            int wounds = inChapter ? Math.Max(0, hero.Tough - hero.WoundsTaken) : hero.Tough;
            int power = inChapter && hero.PowerLeft >= 0 ? hero.PowerLeft : hero.Power;
            UiText.Colored(TextBright, Fit(hero.Name, cardW), wrap: false);
            UiText.Disabled(Fit($"Lvl {hero.Level} {hero.Class}", cardW), wrap: false);
            UiText.Disabled($"Wounds {wounds}/{hero.Tough} - Power {power}/{hero.Power}", wrap: false);
            UiText.Colored(Gold, $"{hero.Credits} {MoneyWord} - {hero.Items.Count} item{(hero.Items.Count == 1 ? "" : "s")}", wrap: false);
            if (hero.Injuries.Count > 0) UiText.Colored(Bad, Fit(string.Join(", ", hero.Injuries), cardW), wrap: false);
            ImGui.EndGroup();
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped(HeroSheet(hero), 420f);
        }
        if (n == 0) UiText.Disabled("No heroes.");
    }

    private string HeroSheet(QuestHero h)
    {
        var lines = new List<string>
        {
            $"{h.Name} - Level {h.Level} {h.Class}",
            $"{h.Faction} - {h.BaseHeroName}",
            $"XP {h.Xp} - {h.Credits}{Money} - {h.Points} pts",
            $"Qua {h.Quality}+  Def {h.Defense}+  Tou {h.Tough}  Pow {h.Power}  Str {h.Strength}+  Dex {h.Dexterity}+  Wil {h.Willpower}+",
        };
        foreach (QuestWeapon w in h.Weapons) lines.Add(w.Label());
        if (h.Rules.Count > 0) lines.Add("Rules: " + string.Join(", ", h.Rules));
        if (h.Skills.Count > 0 || h.ClassFeat.Length > 0)
            lines.Add("Skills: " + string.Join(", ", new[] { h.ClassFeat }.Where(s => s.Length > 0).Concat(h.Skills)));
        lines.Add("Items: " + (h.Items.Count == 0 ? "-" : string.Join(", ", h.Items)));
        lines.Add("Injuries: " + (h.Injuries.Count == 0 ? "-" : string.Join(", ", h.Injuries)));
        return string.Join("\n", lines);
    }

    private static string Fit(string s, float width)
    {
        if (ImGui.CalcTextSize(s).X <= width) return s;
        while (s.Length > 3 && ImGui.CalcTextSize(s + "...").X > width) s = s[..^1];
        return s + "...";
    }

    private void StartChapter(string path, QuestCampaign c, int index, int difficulty)
    {
        c.StartChapter(index, difficulty);
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null) continue;
            hero.CampaignName = c.Name;
            hero.CampaignDifficulty = c.Chapters[index].Difficulty;
            hero.WoundsTaken = 0;
            hero.PowerLeft = -1;
            hero.LastOutcome = "";
            CampaignPaths.SaveHero(file, hero);
        }
        if (CampaignPaths.Save(path, c)) Say($"Chapter {c.Chapters[index].Number} started.");
        else Say("Couldn't save the campaign file.", true);
        Reload();
    }

    /// <summary>
    /// PLAY NEXT MISSION: readies the heroes (the chapter's difficulty, the mission's planned objectives) and
    /// opens the lobby in Mission mode with the party filled in, and the chapter's enemy army on the bot seat
    /// when the structure names one.
    /// </summary>
    private void PlayNext(QuestCampaign c)
    {
        CampaignChapter? ch = c.Active;
        if (ch == null) return;
        CampaignMission? mission = c.NextMission;
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null) continue;
            hero.CampaignName = c.Name;
            hero.CampaignDifficulty = ch.Difficulty;
            hero.CampaignPrimary = mission?.Primary ?? 0;
            hero.CampaignSecondary = mission?.Secondary ?? 0;
            CampaignPaths.SaveHero(file, hero);
        }
        LobbyScreen.PendingParty = c.HeroFiles.ToList();
        LobbyScreen.PendingQuestEnemyArmy = ChapterEnemyArmy(c, ch);
        OnPlay?.Invoke();
    }

    /// <summary>
    /// The army file the chapter's enemy seat gets: a saved army it names, or - for a chapter set to an army
    /// book - the whole book built fresh now, so it always matches the current book. Null leaves the pick to
    /// the lobby (also when the named file or book has gone missing).
    /// </summary>
    private string? ChapterEnemyArmy(QuestCampaign c, CampaignChapter ch)
    {
        if (ch.EnemyBook.Length > 0)
        {
            string outDir = Path.Combine(CampaignPaths.FolderFor(QuestGameTypes.For(System)), "enemy forces",
                Path.GetFileNameWithoutExtension(HeroPaths.FileNameFor(c.Name)));
            return QuestEnemyCollections.WriteFromBook(System, ch.EnemyBook, outDir);
        }
        return ch.EnemyArmy.Length > 0 && File.Exists(ch.EnemyArmy) ? ch.EnemyArmy : null;
    }

    private const string DeleteTitle = "Delete campaign?";

    private void DrawDeleteModal()
    {
        if (_openDelete) { ImGui.OpenPopup(DeleteTitle); _openDelete = false; }
        if (!ImGui.BeginPopupModal(DeleteTitle, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (Current is not { } cur) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        ImGui.TextUnformatted($"Delete the campaign \"{cur.Campaign.Name}\"? Its journal goes with it. This can't be undone.");
        UiText.Disabled("The heroes are kept, with everything they carry, and are free to join another campaign.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Delete", UiChrome.ButtonSize("Delete"))) { Delete(cur.Path, cur.Campaign); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (UiButton.Navigate("Cancel", UiChrome.ButtonSize("Cancel"))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void Delete(string path, QuestCampaign c)
    {
        try { File.Delete(path); } catch { Say("Couldn't delete the campaign file.", true); return; }
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null || hero.CampaignName != c.Name) continue;
            hero.CampaignName = "";
            hero.CampaignDifficulty = 0;
            hero.CampaignPrimary = 0;
            hero.CampaignSecondary = 0;
            hero.WoundsTaken = 0;
            hero.PowerLeft = -1;
            CampaignPaths.SaveHero(file, hero);
        }
        Reload();
        Say($"Deleted \"{c.Name}\".");
    }

    // ── New campaign: name and party ──────────────────────────────────────────────────────────────────

    private void StartNew()
    {
        _view = EView.New;
        _newName = "";
        _newParty.Clear();
        _heroes = HeroPaths.LoadAll(GameType);
        _draft = null;
        _draftPath = null;
        _message = "";
    }

    private void DrawNew()
    {
        UiText.Colored(TextDim, "CAMPAIGN NAME");
        ImGui.SetNextItemWidth(UiChrome.Em(22f));
        ImGui.InputText("##cname", ref _newName, 40);
        ImGui.Spacing();
        UiText.Colored(TextDim, $"PARTY - pick 2 to 4 heroes ({_newParty.Count} picked). Heroes already in another campaign are greyed out - a hero belongs to one campaign.");
        if (_heroes.Count == 0) UiText.Disabled($"No heroes yet - make some with Build Heroes ({GameSystemInfo.DisplayName(System)}) first.");

        var campaigns = _campaigns.Select(e => e.Campaign).ToList();
        var cards = new List<UnitPortraitCards.Card>();
        var taken = new List<QuestCampaign?>();
        foreach ((string path, QuestHero hero) in _heroes)
        {
            QuestCampaign? camp = CampaignPaths.CampaignOf(GameType, path, campaigns);
            taken.Add(camp);
            bool on = _newParty.Contains(path);
            cards.Add(new UnitPortraitCards.Card(hero.Name, hero.BaseHeroName,
                camp != null ? "taken" : $"Lvl {hero.Level} {hero.Class}",
                () => camp != null
                    ? $"{hero.Name} is in the campaign \"{camp.Name}\". A hero belongs to one campaign."
                    : $"{hero.Name} - Level {hero.Level} {hero.Class}\n{hero.Faction} - {hero.BaseHeroName}\n{hero.Points} pts\n\nClick to {(on ? "take out of" : "add to")} the party.",
                on ? "IN PARTY" : camp != null ? $"in \"{camp.Name}\"" : null,
                camp != null ? Gold : null, Dimmed: camp != null, Faction: hero.Faction));
        }
        // The grid's selection border marks one card; the party is shown by the IN PARTY tags instead.
        UnitPortraitCards.CardEvent hit = _cards.DrawCardGrid("party", cards, "", -1);
        if (hit.Clicked >= 0 && taken[hit.Clicked] == null)
        {
            string path = _heroes[hit.Clicked].Path;
            if (_newParty.Contains(path)) _newParty.Remove(path);
            else if (_newParty.Count < 4) _newParty.Add(path);
            else Say("A party is 2 to 4 heroes - take one out first.", true);
        }
    }

    private string? NewProblem()
    {
        if (_newName.Trim().Length == 0) return "Give the campaign a name.";
        if (_campaigns.Any(c => string.Equals(c.Campaign.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase))) return "That name is taken.";
        if (_newParty.Count < 2) return "Pick at least 2 heroes.";
        return null;
    }

    /// <summary>From the name-and-party page to the structure: starts with the book's roll, which the player
    /// can keep, re-roll, empty out or replace with a saved plan.</summary>
    private void OpenNewStructure()
    {
        // Back from the structure to the name-and-party page and Next again keeps the structure as it was.
        if (_draft != null && _draftPath == null) { OpenStructureCommon(); return; }
        _draft = QuestCampaign.Create(_newName.Trim(), _newParty, _rng);
        _draft.GameType = GameType;
        _draft.History.Clear();
        _draftPath = null;
        _structureMode = "gen";
        OpenStructureCommon();
    }

    private void OpenEditStructure(string path, QuestCampaign c)
    {
        _draft = c;
        _draftPath = path;
        _structureMode = "";
        OpenStructureCommon();
    }

    private void OpenStructureCommon()
    {
        _plans = CampaignPaths.LoadPlans(GameType);
        _armies ??= new ArmyCatalog();
        _view = EView.Structure;
        _message = "";
    }

    private void CreateCampaign()
    {
        if (_draft == null) return;
        _draft.Name = _newName.Trim();
        _draft.HeroFiles = _newParty.ToList();
        _draft.History.Clear();
        _draft.History.Add($"Campaign started: {_draft.Chapters.Count} chapters, {_draft.Chapters.Sum(x => x.Missions)} missions.");
        string path = CampaignPaths.FileFor(GameType, _draft.Name);
        if (!CampaignPaths.Save(path, _draft)) { Say("Couldn't save the campaign file.", true); return; }
        string name = _draft.Name;
        _draft = null;
        Reload();
        _selected = _campaigns.FindIndex(e => e.Path == path);
        _view = EView.List;
        Say($"\"{name}\" created and saved.");
    }

    /// <summary>A structure being edited on a saved campaign writes itself back (every campaign autosaves).</summary>
    private void SaveDraft()
    {
        if (_draft == null || _draftPath == null) return;
        if (!CampaignPaths.Save(_draftPath, _draft)) Say("Couldn't save the campaign file.", true);
    }

    // ── Structure ─────────────────────────────────────────────────────────────────────────────────────

    private void DrawStructure()
    {
        if (_draft is not { } c) { _view = EView.List; return; }
        bool changed = false;
        string party = string.Join(", ", (_draftPath == null ? _newParty : c.HeroFiles)
            .Select(f => _heroes.FirstOrDefault(h => h.Path == f).Hero?.Name ?? CampaignPaths.LoadHero(f)?.Name ?? Path.GetFileNameWithoutExtension(f)));
        UiText.Colored(Gold, $"{(_draftPath == null ? _newName.Trim() : c.Name)} - party: {party}", wrap: false);

        // The three starts. Only before anything has been played: after that they would throw progress away.
        bool locked = c.HasStarted;
        ImGui.BeginDisabled(locked);
        if (ModeButton("Generate everything", _structureMode == "gen"))
        {
            c.Generate(_rng);
            _structureMode = "gen";
            changed = true;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(locked ? "The campaign has started - change the unplayed missions below instead."
                : "Roll the whole campaign the book's way: chapters, missions and every mission's objectives. Press again to re-roll.", 340f);
        ImGui.SameLine();
        if (ModeButton("Build it myself", _structureMode == "craft"))
        {
            c.Chapters.Clear();
            c.AddChapter();
            _structureMode = "craft";
            changed = true;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(locked ? "The campaign has started - change the unplayed missions below instead."
                : "Start empty: one chapter of one mission. Add chapters and missions one at a time.", 340f);
        ImGui.SameLine();
        if (ModeButton("Start from a saved campaign", _structureMode == "saved")) { _plans = CampaignPaths.LoadPlans(GameType); _openPlans = true; }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(locked ? "The campaign has started - change the unplayed missions below instead."
                : "Use a campaign plan saved earlier with \"Save as a campaign file\": its chapters, missions and notes.", 340f);
        ImGui.EndDisabled();
        if (DrawPlansPopup(c)) changed = true;

        UiText.Disabled(QuestJournal.BookStructureLine(System));
        UiText.Disabled(_structureMode switch
        {
            "gen" => "Rolled by the book: D3+2 chapters, D3+1 missions each, objectives from the book's tables. Change anything you like.",
            "craft" => "Start empty: add chapters and missions one at a time; pick each objective from the book's tables, Roll it, or leave it to roll when the mission starts.",
            "saved" => "From a saved campaign. Change anything you like.",
            _ => "Everything not yet played stays editable: add, remove or change missions you haven't played. Played ones are locked.",
        });
        UiText.Disabled("Book counts are D3+2 chapters and D3+1 missions; any other number of chapters or missions is a house rule.");

        ImGui.BeginChild("##chapters", new Vector2(0, 0), ImGuiChildFlags.Borders);
        int removeChapter = -1;
        for (int i = 0; i < c.Chapters.Count; i++)
        {
            ImGui.PushID(i);
            if (DrawChapterEditor(c, i, ref removeChapter)) changed = true;
            ImGui.PopID();
        }
        if (removeChapter >= 0 && c.RemoveChapter(removeChapter)) changed = true;
        if (UiButton.Navigate("+ Add chapter")) { c.AddChapter(); changed = true; }
        if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Add a chapter of one mission at the end.", 300f);
        int total = c.Chapters.Sum(ch => ch.Missions);
        UiText.Disabled($"Total: {c.Chapters.Count} chapter{(c.Chapters.Count == 1 ? "" : "s")}, {total} mission{(total == 1 ? "" : "s")}. "
                        + "A mission left at \"Roll when it starts\" rolls its objective on the book's table when it starts; a chapter's difficulty "
                        + "left at \"Pick when it starts\" is picked with Start chapter. The book recommends a different theme and enemy force per chapter.");
        ImGui.EndChild();

        if (changed) SaveDraft();
    }

    private static bool ModeButton(string label, bool on)
    {
        if (on) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.65f, 1f));
        bool pressed = ImGui.Button(label, new Vector2(UiChrome.Em(14f), 0f));
        if (on) ImGui.PopStyleColor();
        return pressed;
    }

    private const string PlansTitle = "Start from a saved campaign";

    private bool DrawPlansPopup(QuestCampaign c)
    {
        if (_openPlans) { ImGui.OpenPopup(PlansTitle); _openPlans = false; }
        bool applied = false;
        if (!ImGui.BeginPopupModal(PlansTitle, ImGuiWindowFlags.AlwaysAutoResize)) return false;
        if (_plans.Count == 0)
            UiText.Disabled("No saved campaigns yet. Use \"Save as a campaign file\" on a structure to make one.", wrap: false);
        foreach ((string path, QuestCampaign plan) in _plans)
        {
            int missions = plan.Chapters.Sum(ch => ch.Missions);
            if (ImGui.Selectable($"{plan.Name}  -  {plan.Chapters.Count} chapters, {missions} missions##{path}"))
            {
                if (c.ApplyPlan(plan))
                {
                    _structureMode = "saved";
                    applied = true;
                    Say($"Started from \"{plan.Name}\".");
                }
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.Spacing();
        if (UiButton.Navigate("Cancel", UiChrome.ButtonSize("Cancel"))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return applied;
    }

    private static readonly string[] PlannedDifficultyOptions = { "Pick when it starts", "Beginner", "Intermediate", "Expert", "Legendary" };

    /// <summary>One chapter's editor: difficulty, enemy force, theme, Remove chapter, then its missions.
    /// Returns true when something changed.</summary>
    private bool DrawChapterEditor(QuestCampaign c, int i, ref int removeChapter)
    {
        CampaignChapter ch = c.Chapters[i];
        bool changed = false;
        bool editable = c.CanEditChapter(i);
        string state = ch.Done ? "  (done)" : i == c.CurrentChapter ? "  (in progress)" : "";
        UiText.Colored(Gold, $"CHAPTER {ch.Number}{state}", wrap: false);

        ImGui.BeginDisabled(!editable);
        ImGui.SameLine();
        UiText.Disabled("  Difficulty:", wrap: false);
        ImGui.SameLine();
        int diff = ch.Done || i == c.CurrentChapter ? ch.Difficulty : ch.PlannedDifficulty;
        ImGui.SetNextItemWidth(UiChrome.Em(10f));
        if (ImGui.Combo("##pdiff", ref diff, PlannedDifficultyOptions, PlannedDifficultyOptions.Length) && editable)
        {
            ch.PlannedDifficulty = diff;
            changed = true;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped("The difficulty of every mission in this chapter: enemy waves worth 25% / 50% / 75% / 100% of the party's points, "
                                  + "with bigger rewards. \"Pick when it starts\" asks at Start chapter.", 340f);

        ImGui.SameLine();
        UiText.Disabled("  Enemy force:", wrap: false);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiChrome.Em(14f));
        bool fromBook = ch.EnemyBook.Length > 0;
        string preview = fromBook ? (ch.EnemyName.Length > 0 ? ch.EnemyName : Path.GetFileNameWithoutExtension(ch.EnemyBook))
            : ch.EnemyArmy.Length == 0 ? "Pick in the lobby"
            : ch.EnemyName.Length > 0 ? ch.EnemyName : Path.GetFileNameWithoutExtension(ch.EnemyArmy);
        if (!fromBook && ch.EnemyArmy.Length > 0 && !File.Exists(ch.EnemyArmy)) preview += " (file missing)";
        if (ImGui.BeginCombo("##enemy", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable("Pick in the lobby", ch.EnemyArmy.Length == 0 && !fromBook))
            {
                ch.EnemyArmy = ""; ch.EnemyName = ""; ch.EnemyBook = ""; changed = true;
            }

            // An army book: the whole roster, built fresh from the current book each mission.
            ImGui.SeparatorText("From an army book");
            foreach (QuestEnemyCollections.BookChoice b in QuestEnemyCollections.BooksFor(System))
            {
                if (ImGui.Selectable($"{b.Faction} (whole book)##book{b.FileName}", fromBook && b.FileName == ch.EnemyBook))
                {
                    ch.EnemyBook = b.FileName;
                    ch.EnemyArmy = "";
                    ch.EnemyName = $"{b.Faction} (whole book)";
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    UiText.TooltipWrapped($"Every unit in the {b.Faction} book. The waves are drawn from it to each wave's points, "
                                          + "and it is built again from the book every mission, so it is always up to date.", 340f);
            }

            ImGui.SeparatorText("Your saved armies");
            if (_armies is not { IsLoaded: true }) UiText.Disabled("Reading the armies folder...");
            else
            {
                var armies = ArmyPaths.EntriesFor(System, _armies.Entries).ToList();
                if (armies.Count == 0) UiText.Disabled($"No {GameSystemInfo.DisplayName(System)} armies saved yet.");
                foreach (ArmyCatalogEntry e in armies)
                {
                    if (ImGui.Selectable($"{e.FileName}  ({e.Faction}, {e.Points} pts)##{e.Path}", !fromBook && e.Path == ch.EnemyArmy))
                    {
                        ch.EnemyArmy = e.Path;
                        ch.EnemyBook = "";
                        ch.EnemyName = $"{e.FileName} ({e.Faction})";
                        changed = true;
                    }
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped($"The enemy force this chapter is fought against: a whole {GameSystemInfo.DisplayName(System)} army book, "
                                  + "or one of your saved armies. PLAY NEXT MISSION puts it on the enemy seat. "
                                  + "\"Pick in the lobby\" leaves the choice to the lobby.", 340f);
        ImGui.SameLine();
        ImGui.BeginDisabled(c.Chapters.Count <= 1);
        if (UiButton.NavigateSmall("Remove chapter")) removeChapter = i;
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(editable ? (c.Chapters.Count <= 1 ? "A campaign keeps at least one chapter." : "Remove this chapter and its missions.")
                : "A chapter that has started can't be changed or removed.", 300f);

        UiText.Disabled("Theme:", wrap: false);
        ImGui.SameLine();
        string theme = ch.Theme;
        ImGui.SetNextItemWidth(UiChrome.Em(30f));
        if (ImGui.InputTextWithHint("##theme", "optional note - where, who, why", ref theme, 200)) ch.Theme = theme;
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGui.Indent(UiChrome.Em(1.5f));
        int removeMission = -1;
        for (int m = 0; m < ch.MissionPlan.Count; m++)
        {
            ImGui.PushID(m);
            if (DrawMissionEditor(c, i, m, ref removeMission)) changed = true;
            ImGui.PopID();
        }
        if (removeMission >= 0 && c.RemoveMission(i, removeMission)) changed = true;
        ImGui.BeginDisabled(!c.CanAddMission(i));
        if (UiButton.NavigateSmall("+ Add mission")) { c.AddMission(i); changed = true; }
        ImGui.EndDisabled();
        ImGui.Unindent(UiChrome.Em(1.5f));
        ImGui.Separator();
        return changed;
    }

    private static readonly string[] PrimaryOptions =
        Enumerable.Range(0, 7).Select(QuestJournal.PrimaryName).ToArray();

    private static readonly string[] SecondaryOptions =
        Enumerable.Range(0, 7).Select(QuestJournal.SecondaryName).ToArray();

    private bool DrawMissionEditor(QuestCampaign c, int chapter, int m, ref int removeMission)
    {
        CampaignMission mission = c.Chapters[chapter].MissionPlan[m];
        bool changed = false;
        bool editable = c.CanEditMission(chapter, m);
        UiText.Colored(TextBright, $"Mission {m + 1}{(mission.Won ? "  (played)" : "")}", wrap: false);
        ImGui.BeginDisabled(!editable);
        ImGui.SameLine();
        UiText.Disabled("  Primary:", wrap: false);
        ImGui.SameLine();
        int p = Math.Clamp(mission.Primary, 0, 6);
        ImGui.SetNextItemWidth(UiChrome.Em(10f));
        if (ImGui.Combo("##prim", ref p, PrimaryOptions, PrimaryOptions.Length)) { mission.Primary = p; changed = true; }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped("This mission's primary objective, from the book's Primary Objective table (D6). \"Roll when it starts\" rolls it at mission set-up.", 340f);
        ImGui.SameLine();
        UiText.Disabled("  Secondary:", wrap: false);
        ImGui.SameLine();
        int s = Math.Clamp(mission.Secondary, 0, 6);
        ImGui.SetNextItemWidth(UiChrome.Em(10f));
        if (ImGui.Combo("##sec", ref s, SecondaryOptions, SecondaryOptions.Length)) { mission.Secondary = s; changed = true; }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped("This mission's secondary objective, from the book's Secondary Objective table (D6). It comes up once the primary is done. \"Roll when it starts\" rolls it then.", 340f);
        ImGui.SameLine();
        if (UiButton.NavigateSmall("Roll"))
        {
            mission.Primary = _rng.Next(1, 7);
            mission.Secondary = _rng.Next(1, 7);
            changed = true;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped("Roll both objectives now, a D6 each on the book's tables.", 300f);
        ImGui.SameLine();
        ImGui.BeginDisabled(c.Chapters[chapter].MissionPlan.Count <= 1);
        if (UiButton.NavigateSmall("X")) removeMission = m;
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(editable ? (c.Chapters[chapter].MissionPlan.Count <= 1 ? "A chapter keeps at least one mission - remove the chapter instead." : "Remove this mission.")
                : "This mission has been played, so it is locked.", 300f);

        string notes = mission.Notes;
        ImGui.SetNextItemWidth(UiChrome.Em(36f));
        if (ImGui.InputTextWithHint("##notes", mission.Won ? "your account of the mission" : "mission notes / briefing", ref notes, 2000)) mission.Notes = notes;
        if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
        return changed;
    }

    // ── Journal ───────────────────────────────────────────────────────────────────────────────────────

    private void DrawJournal()
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        QuestCampaign c = cur.Campaign;
        Vector2 avail = ImGui.GetContentRegionAvail();
        float storyW = avail.X * 0.30f, listW = avail.X * 0.20f;

        ImGui.BeginChild("##story", new Vector2(storyW, avail.Y), ImGuiChildFlags.Borders);
        UiText.Colored(Gold, "THE CAMPAIGN");
        UiText.Disabled("The story so far - who the heroes are and why they fight.");
        string story = c.Story;
        if (ImGui.InputTextMultiline("##storyText", ref story, 8000, new Vector2(-1f, ImGui.GetContentRegionAvail().Y)))
            c.Story = story;
        if (ImGui.IsItemDeactivatedAfterEdit()) SaveCampaign(cur.Path, c);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##jlist", new Vector2(listW, avail.Y), ImGuiChildFlags.Borders);
        UiText.Colored(Gold, "CHAPTERS");
        for (int i = 0; i < c.Chapters.Count; i++)
        {
            CampaignChapter ch = c.Chapters[i];
            UiText.Colored(TextBright, $"Chapter {ch.Number}", wrap: false);
            for (int m = 0; m < ch.MissionPlan.Count; m++)
            {
                CampaignMission mission = ch.MissionPlan[m];
                bool next = ReferenceEquals(mission, c.NextMission);
                string mark = mission.Records.Count > 0 ? "  *" : next ? " (next)" : "";
                if (ImGui.Selectable($"   Mission {m + 1}{mark}##j{i}_{m}", _journalChapter == i && _journalMission == m))
                {
                    _journalChapter = i;
                    _journalMission = m;
                }
            }
        }
        UiText.Disabled("* = played");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##jentry", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        if (_journalChapter < 0 || _journalChapter >= c.Chapters.Count
            || _journalMission < 0 || _journalMission >= c.Chapters[_journalChapter].MissionPlan.Count)
        {
            _journalChapter = 0;
            _journalMission = 0;
        }
        if (c.Chapters.Count > 0)
        {
            CampaignChapter ch = c.Chapters[_journalChapter];
            CampaignMission mission = ch.MissionPlan[_journalMission];
            UiText.Colored(Gold, $"CHAPTER {ch.Number} - MISSION {_journalMission + 1}");
            if (ch.Theme.Trim().Length > 0) UiText.Disabled("Theme: " + ch.Theme.Trim());
            UiText.Disabled($"Planned: primary {QuestJournal.PrimaryName(mission.Primary)}, secondary {QuestJournal.SecondaryName(mission.Secondary)}.");
            ImGui.Spacing();
            UiText.Colored(TextDim, "WHAT HAPPENED - written by the game");
            if (mission.Records.Count == 0) UiText.Disabled("Not played yet.");
            for (int r = 0; r < mission.Records.Count; r++)
            {
                if (mission.Records.Count > 1) UiText.Colored(TextBright, $"Attempt {r + 1}");
                foreach (string line in QuestJournal.RecordLines(mission.Records[r], System)) UiText.Wrapped(line);
                ImGui.Spacing();
            }
            ImGui.Separator();
            UiText.Colored(TextDim, mission.Records.Count > 0 ? "YOUR ACCOUNT - the story of the mission"
                : "THE BRIEFING - before it's played (it becomes the start of your account)");
            string notes = mission.Notes;
            if (ImGui.InputTextMultiline("##missionNotes", ref notes, 8000, new Vector2(-1f, ImGui.GetContentRegionAvail().Y)))
                mission.Notes = notes;
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveCampaign(cur.Path, c);
        }
        ImGui.EndChild();
    }

    private void SaveCampaign(string path, QuestCampaign c)
    {
        if (!CampaignPaths.Save(path, c)) Say("Couldn't save the campaign file.", true);
    }

    private void OpenJournal()
    {
        _view = EView.Journal;
        _message = "";
        if (Current is { } cur && cur.Campaign.Active is { } a)
        {
            _journalChapter = cur.Campaign.CurrentChapter;
            _journalMission = Math.Min(a.MissionsWon, a.MissionPlan.Count - 1);
        }
        else { _journalChapter = 0; _journalMission = 0; }
    }

    /// <summary>Writes the whole journal to "&lt;campaign&gt; - Journal.txt" beside the campaign's file (the last
    /// export is overwritten - it is a snapshot, the same as the army campaign's export).</summary>
    private void ExportJournal()
    {
        if (Current is not { } cur) return;
        SaveCampaign(cur.Path, cur.Campaign);
        var names = Party(cur.Campaign).Select(p => p.Hero?.Name ?? Path.GetFileNameWithoutExtension(p.File));
        string text = QuestJournal.Export(cur.Campaign, System, names);
        string file = Path.Combine(Path.GetDirectoryName(cur.Path) ?? "", Path.GetFileNameWithoutExtension(cur.Path) + " - Journal.txt");
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

    // ── Shop (between chapters) ───────────────────────────────────────────────────────────────────────

    private void DrawShop()
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        var party = Party(cur.Campaign).Where(p => p.Hero != null).Select(p => (p.File, p.Hero!)).ToList();
        if (party.Count == 0) { UiText.Disabled("No heroes."); return; }
        UiText.Disabled("Heroes may buy and sell before starting or after finishing a chapter - not between its missions.");
        Vector2 avail = ImGui.GetContentRegionAvail();
        // The campaign's shop hands no money between heroes: the book's between-mission hand-over is items
        // and credits during a chapter, which the in-game shop covers. Only the Hero Builder's (a
        // convenience) moves money.
        QuestShopPanel.Draw(System, party, ref _shopHero, moneyHandOver: false,
            (file, hero) => { if (!CampaignPaths.SaveHero(file, hero)) Say($"Could not save {hero.Name}.", true); },
            avail.X, avail.Y);
    }

    // ── Footer ────────────────────────────────────────────────────────────────────────────────────────

    private void DrawFooter()
    {
        float rightEdge = ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
        switch (_view)
        {
            case EView.List:
            {
                if (Current is not { } cur) { UiText.Disabled("Start a campaign with \"+ New Campaign\" (top right)."); break; }
                QuestCampaign c = cur.Campaign;
                if (UiButton.Back("Delete", UiChrome.ButtonSize("Delete"))) _openDelete = true;
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Delete this campaign (asks first). The heroes are kept.", 300f);
                ImGui.SameLine();
                if (UiButton.Navigate("Journal", UiChrome.ButtonSize("Journal"))) OpenJournal();
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped("The campaign's story, and what happened in every mission.", 300f);
                ImGui.SameLine();
                bool shopOpen = c.Active == null && !c.Finished;
                ImGui.BeginDisabled(!shopOpen);
                if (UiButton.Navigate("Shop", UiChrome.ButtonSize("Shop"))) { _view = EView.Shop; _shopHero = 0; _message = ""; }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    UiText.TooltipWrapped(shopOpen ? "Buy and sell for the party." : c.Finished ? "The campaign is complete."
                        : "The shop opens only between chapters - finish this one first.", 300f);
                ImGui.SameLine();
                if (UiButton.Navigate("Edit structure", UiChrome.ButtonSize("Edit structure"))) OpenEditStructure(cur.Path, c);
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped("Change the chapters and missions you haven't played yet, or save the plan as a campaign file.", 320f);
                ImGui.SameLine();
                UiText.Disabled("Shop opens only between chapters. Delete asks first.", wrap: false);

                string play = "PLAY NEXT MISSION";
                ImGui.SameLine();
                ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), rightEdge - UiChrome.ButtonSize(play).X));
                ImGui.BeginDisabled(c.Active == null);
                if (UiButton.Confirm(play, UiChrome.ButtonSize(play))) PlayNext(c);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    UiText.TooltipWrapped(c.Active != null
                        ? "Opens the lobby in Mission mode with the party filled in. The difficulty, objectives and the heroes' wounds and power carry over; the result comes back here."
                        : c.Finished ? "The campaign is complete." : "Start a chapter first (in the chapter list).", 340f);
                break;
            }
            case EView.New:
            {
                if (UiButton.Back("Cancel", UiChrome.ButtonSize("Cancel"))) { _view = EView.List; _message = ""; }
                string? problem = NewProblem();
                ImGui.SameLine();
                UiText.Colored(problem != null ? Gold : Good, problem ?? "Next: plan the chapters and missions.", wrap: false);
                string next = "Next: Structure";
                ImGui.SameLine();
                ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), rightEdge - UiChrome.ButtonSize(next).X));
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm(next, UiChrome.ButtonSize(next))) OpenNewStructure();
                ImGui.EndDisabled();
                break;
            }
            case EView.Structure:
            {
                if (_draft is not { } c) break;
                if (UiButton.Back("Back", UiChrome.ButtonSize("Back"))) GoBack();
                ImGui.SameLine();
                if (UiButton.Navigate("Save as a campaign file", UiChrome.ButtonSize("Save as a campaign file")))
                {
                    if (_draftPath == null) c.Name = _newName.Trim();
                    string? saved = CampaignPaths.SavePlan(GameType, c);
                    if (saved != null) Say($"Saved the plan to {saved}"); else Say("Couldn't write the campaign file.", true);
                }
                if (ImGui.IsItemHovered())
                    UiText.TooltipWrapped("Save this structure (chapters, missions, objectives, notes - no heroes, no progress) to replay it later with \"Start from a saved campaign\".", 340f);
                string? problem = c.StructureProblem();
                ImGui.SameLine();
                UiText.Colored(problem != null ? Gold : TextDim, problem ?? "Everything stays editable after you start - add, remove or change missions you haven't played yet.", wrap: false);
                string done = _draftPath == null ? "CREATE CAMPAIGN" : "Done";
                ImGui.SameLine();
                ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), rightEdge - UiChrome.ButtonSize(done).X));
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm(done, UiChrome.ButtonSize(done)))
                {
                    if (_draftPath == null) CreateCampaign();
                    else { SaveDraft(); _draft = null; _view = EView.List; Reload(); Say("Structure saved."); }
                }
                ImGui.EndDisabled();
                break;
            }
            case EView.Journal:
            case EView.Shop:
                if (UiButton.Back(_view == EView.Shop ? "Done" : "Back to campaigns", UiChrome.ButtonSize("Back to campaigns"))) GoBack();
                break;
        }
    }
}
