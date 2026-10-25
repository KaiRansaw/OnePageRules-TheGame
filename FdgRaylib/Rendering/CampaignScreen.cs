using System.Numerics;
using FDG.Quest;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Star Quest campaigns (main menu > Star Quest Campaign), from the Beginner's Guide Campaign Builder:
/// <list type="bullet">
/// <item>New campaign: a name and a party of 2-4 saved heroes; D3+2 chapters of D3 missions are rolled.</item>
/// <item>Between chapters: pick any unfinished chapter and its difficulty, and visit the shop.</item>
/// <item>In a chapter: "Play next mission" readies the heroes (difficulty, carried wounds/power) and opens Play
/// with the party already picked; the mission's result comes back here after the game.</item>
/// </list>
/// </summary>
public sealed class CampaignScreen : IAppScreen
{
    public Action? OnBack;
    /// <summary>Opens the game setup (Play) for the next mission.</summary>
    public Action? OnPlay;

    private const string GameType = HeroBuilderScreen.GameType;

    private enum EView { List, New, Shop }
    private EView _view = EView.List;

    private List<(string Path, QuestCampaign Campaign)> _campaigns = new();
    private int _selected = -1;
    private bool _confirmDelete;
    private readonly Dictionary<int, int> _difficultyPick = new();
    private string _message = "";
    private bool _messageIsError;

    // New campaign
    private string _newName = "";
    private List<(string Path, QuestHero Hero)> _heroes = new();
    private readonly List<string> _newParty = new();

    // Shop
    private int _shopHero;

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);
    private static readonly string[] Difficulties = { "Beginner", "Intermediate", "Expert", "Legendary" };

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

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
        _confirmDelete = false;
    }

    private (string Path, QuestCampaign Campaign)? Current =>
        _selected >= 0 && _selected < _campaigns.Count ? _campaigns[_selected] : null;

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##Campaigns", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = screenW * 0.025f;
        float titlePx = screenH * 0.05f;
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.035f), U(TextBright),
            _view switch { EView.New => "NEW CAMPAIGN", EView.Shop => "SHOP", _ => "STAR QUEST CAMPAIGNS" });
        string sub = _view switch
        {
            EView.New => "Name the campaign and pick its party (2-4 heroes). D3+2 chapters of D3 missions are rolled.",
            EView.Shop => "Heroes may buy and sell items before starting or after finishing a chapter - not between missions.",
            _ => "Play the chapters in any order; a started chapter must be finished before the next.",
        };
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.035f + titlePx + 6f), U(TextDim), sub);

        float top = screenH * 0.14f;
        float bottomBarH = screenH * 0.09f;
        float bodyH = screenH - top - bottomBarH;
        float w = screenW - margin * 2f;

        switch (_view)
        {
            case EView.List: DrawList(margin, top, w, bodyH); break;
            case EView.New: DrawNew(margin, top, w, bodyH); break;
            case EView.Shop: DrawShop(margin, top, w, bodyH); break;
        }
        DrawBottomBar(screenW, screenH, margin, bottomBarH);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    // ── Campaign list + details ───────────────────────────────────────────────────────────────────────

    private void DrawList(float x, float y, float w, float h)
    {
        float leftW = w * 0.26f;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##campaignList", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        if (_campaigns.Count == 0) UiText.Disabled("No campaigns yet.\nClick \"New Campaign\".");
        for (int i = 0; i < _campaigns.Count; i++)
        {
            QuestCampaign c = _campaigns[i].Campaign;
            string status = c.Finished ? "complete" : c.Active is { } a ? $"chapter {a.Number}" : "between chapters";
            if (ImGui.Selectable($"{c.Name}\n   {c.Chapters.Count(ch => ch.Done)}/{c.Chapters.Count} chapters - {status}##c{i}",
                    i == _selected, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
            {
                _selected = i;
                _confirmDelete = false;
            }
        }
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + 14f, y));
        ImGui.BeginChild("##campaignDetail", new Vector2(w - leftW - 14f, h), ImGuiChildFlags.Borders);
        if (Current is { } cur) DrawDetail(cur.Path, cur.Campaign);
        else UiText.Disabled("Pick a campaign on the left, or start a new one.");
        ImGui.EndChild();
    }

    private List<(string File, QuestHero? Hero)> Party(QuestCampaign c) =>
        c.HeroFiles.Select(f => (f, CampaignPaths.LoadHero(f))).ToList();

    private void DrawDetail(string path, QuestCampaign c)
    {
        UiText.Colored(Gold, c.Name);
        ImGui.SameLine();
        if (c.Finished) UiText.Colored(Good, "   CAMPAIGN COMPLETE");
        else if (c.Active is { } a)
            UiText.Colored(Accent, $"   Chapter {a.Number}: mission {a.MissionsWon + 1} of {a.Missions} - {Difficulties[Math.Clamp(a.Difficulty, 1, 4) - 1]}");
        else UiText.Colored(TextDim, "   Between chapters - pick the next one below.");
        ImGui.Spacing();

        // Party
        UiText.Colored(TextDim, "PARTY");
        var party = Party(c);
        if (ImGui.BeginTable("##party", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Hero"); ImGui.TableSetupColumn("Level"); ImGui.TableSetupColumn("Wounds / Power");
            ImGui.TableSetupColumn("Credits"); ImGui.TableSetupColumn("Items"); ImGui.TableSetupColumn("Injuries");
            ImGui.TableHeadersRow();
            foreach (var (file, hero) in party)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (hero == null) { UiText.Colored(Bad, $"missing: {Path.GetFileName(file)}"); continue; }
                ImGui.TextUnformatted(hero.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{hero.Level} {hero.Class}");
                ImGui.TableNextColumn();
                string power = hero.PowerLeft < 0 ? $"{hero.Power}" : $"{hero.PowerLeft}";
                ImGui.TextUnformatted(c.Active != null ? $"{Math.Max(0, hero.Tough - hero.WoundsTaken)}/{hero.Tough}  -  {power}/{hero.Power}" : $"{hero.Tough}/{hero.Tough}  -  {hero.Power}/{hero.Power}");
                ImGui.TableNextColumn(); UiText.Colored(Gold, $"{hero.Credits}c");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(hero.Items.Count == 0 ? "-" : string.Join(", ", hero.Items.GroupBy(i => i).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key)));
                ImGui.TableNextColumn(); UiText.Colored(hero.Injuries.Count > 0 ? Bad : TextDim, hero.Injuries.Count == 0 ? "-" : string.Join(", ", hero.Injuries));
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        ImGui.Separator();

        // Chapters
        UiText.Colored(TextDim, "CHAPTERS");
        for (int i = 0; i < c.Chapters.Count; i++)
        {
            CampaignChapter ch = c.Chapters[i];
            ImGui.PushID(i);
            string state = ch.Done ? "done" : i == c.CurrentChapter ? $"in progress: {ch.MissionsWon}/{ch.Missions} missions" : "not started";
            Vector4 col = ch.Done ? Good : i == c.CurrentChapter ? Accent : TextBright;
            UiText.Colored(col, $"Chapter {ch.Number}   -   {ch.Missions} mission{(ch.Missions == 1 ? "" : "s")}   -   {state}"
                + (ch.Difficulty > 0 ? $"   ({Difficulties[ch.Difficulty - 1]})" : "") + (ch.Restarts > 0 ? $"   restarts: {ch.Restarts}" : ""));
            if (c.CanStart(i))
            {
                ImGui.SameLine();
                if (!_difficultyPick.ContainsKey(i)) _difficultyPick[i] = 0;
                int pick = _difficultyPick[i];
                ImGui.SetNextItemWidth(150f);
                if (ImGui.Combo("##diff", ref pick, Difficulties, Difficulties.Length)) _difficultyPick[i] = pick;
                if (ImGui.IsItemHovered())
                    UiText.Tooltip("Difficulty for every mission of this chapter:\nwaves worth 25% / 50% / 75% / 100% of the party's points,\nwith bigger rewards.");
                ImGui.SameLine();
                if (UiButton.NavigateSmall("Start chapter")) StartChapter(path, c, i, pick + 1);
            }
            ImGui.PopID();
        }
        ImGui.Spacing();
        ImGui.Separator();

        // Next step
        if (c.Active is { } active)
        {
            UiText.Wrapped("Next: press \"Play next mission\". In Play, choose Game Type GF: Star Quest and Game Mode Star Quest " +
                "Mission - your party is picked for you; add an enemy army (Star Quest AI) and start. The difficulty and the " +
                "heroes' wounds and power carry over automatically, and the result comes back here when the game ends.");
        }
        else if (!c.Finished)
            UiText.Wrapped("Between chapters you may visit the Shop. Then pick a chapter and its difficulty, and press Start chapter.");

        ImGui.Spacing();
        UiText.Colored(TextDim, "HISTORY");
        foreach (string line in Enumerable.Reverse(c.History).Take(8)) UiText.Bullet(line);
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

    private void PlayNext(QuestCampaign c)
    {
        CampaignChapter? ch = c.Active;
        if (ch == null) return;
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null) continue;
            hero.CampaignName = c.Name;
            hero.CampaignDifficulty = ch.Difficulty;
            CampaignPaths.SaveHero(file, hero);
        }
        LobbyScreen.PendingParty = c.HeroFiles.ToList();
        OnPlay?.Invoke();
    }

    // ── New campaign ──────────────────────────────────────────────────────────────────────────────────

    private void DrawNew(float x, float y, float w, float h)
    {
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##newCampaign", new Vector2(w * 0.6f, h), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, "CAMPAIGN NAME");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##cname", ref _newName, 40);
        ImGui.Spacing();
        UiText.Colored(TextDim, $"PARTY ({_newParty.Count} of 2-4)");
        if (_heroes.Count == 0) UiText.Disabled("No heroes yet - make some in Star Quest Heroes first.");
        foreach (var (path, hero) in _heroes)
        {
            bool on = _newParty.Contains(path);
            bool busy = !string.IsNullOrEmpty(hero.CampaignName);
            ImGui.BeginDisabled(!on && _newParty.Count >= 4);
            if (ImGui.Checkbox($"{hero.Name}   (Lvl {hero.Level} {hero.Class}, {hero.Faction}, {hero.Points} pts)##{path}", ref on))
            {
                if (on) _newParty.Add(path); else _newParty.Remove(path);
            }
            ImGui.EndDisabled();
            if (busy) { ImGui.SameLine(); UiText.Colored(Gold, $"(in campaign \"{hero.CampaignName}\")"); }
        }
        ImGui.EndChild();
    }

    private string? NewProblem()
    {
        if (_newName.Trim().Length == 0) return "Give the campaign a name.";
        if (_campaigns.Any(c => string.Equals(c.Campaign.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase))) return "That name is taken.";
        if (_newParty.Count < 2) return "Pick at least 2 heroes.";
        return null;
    }

    private void CreateCampaign()
    {
        var c = QuestCampaign.Create(_newName.Trim(), _newParty, new Random());
        string path = CampaignPaths.FileFor(GameType, c.Name);
        if (!CampaignPaths.Save(path, c)) { Say("Couldn't save the campaign file.", true); return; }
        Reload();
        _selected = _campaigns.FindIndex(e => e.Path == path);
        _view = EView.List;
        Say($"\"{c.Name}\": {c.Chapters.Count} chapters, {c.Chapters.Sum(x => x.Missions)} missions.");
    }

    // ── Shop (between chapters) ───────────────────────────────────────────────────────────────────────

    private void DrawShop(float x, float y, float w, float h)
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        var party = Party(cur.Campaign).Where(p => p.Hero != null).ToList();
        if (party.Count == 0) { UiText.Disabled("No heroes."); return; }
        _shopHero = Math.Clamp(_shopHero, 0, party.Count - 1);

        ImGui.SetCursorPos(new Vector2(x, y));
        for (int i = 0; i < party.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool sel = i == _shopHero;
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.65f, 1f));
            if (ImGui.Button($"{party[i].Hero!.Name}  ({party[i].Hero!.Credits}c)##sh{i}")) _shopHero = i;
            if (sel) ImGui.PopStyleColor();
        }
        var (file, hero) = party[_shopHero];
        int tier = QuestProgression.TierUnlocked(hero!.Level);

        float lh = ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetCursorPos(new Vector2(x, y + lh * 1.8f));
        ImGui.BeginChild("##buy", new Vector2(w * 0.62f, h - lh * 1.8f), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, $"FOR SALE (tier {tier} and below - higher tiers unlock at levels 2 / 5 / 9)");
        foreach (QuestItemInfo item in QuestItems.All.OrderBy(i => i.Tier).ThenBy(i => i.Name))
        {
            bool locked = item.Tier > tier;
            bool afford = item.Cost <= hero.Credits;
            ImGui.BeginDisabled(locked || !afford);
            if (UiButton.NavigateSmall($"Buy {item.Cost}c##b{item.Name}"))
            {
                hero.Credits -= item.Cost;
                hero.Items.Add(item.Name);
                CampaignPaths.SaveHero(file, hero);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            UiText.Colored(locked ? TextDim : TextBright, $"{item.Name}  (tier {item.Tier})");
            if (ImGui.IsItemHovered()) UiText.Tooltip(Wrap(item.Description));
        }
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + w * 0.62f + 12f, y + lh * 1.8f));
        ImGui.BeginChild("##sell", new Vector2(w * 0.38f - 12f, h - lh * 1.8f), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, $"{hero.Name.ToUpperInvariant()} CARRIES ({hero.Credits}c)");
        if (hero.Items.Count == 0) UiText.Disabled("Nothing.");
        foreach (var g in hero.Items.GroupBy(i => i).ToList())
        {
            QuestItemInfo? info = QuestItems.Find(g.Key);
            ImGui.BeginDisabled(info == null);
            if (UiButton.NavigateSmall($"Sell +{info?.SellValue ?? 0}c##s{g.Key}"))
            {
                hero.Items.Remove(g.Key);
                hero.Credits += info!.SellValue;
                CampaignPaths.SaveHero(file, hero);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextUnformatted(g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
            // Hand an item to another hero.
            foreach (var (otherFile, other) in party.Where(p => p.File != file))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"> {other!.Name}##g{g.Key}{otherFile}"))
                {
                    hero.Items.Remove(g.Key);
                    other.Items.Add(g.Key);
                    CampaignPaths.SaveHero(file, hero);
                    CampaignPaths.SaveHero(otherFile, other);
                }
            }
        }
        ImGui.EndChild();
    }

    private static string Wrap(string text)
    {
        var lines = new List<string>();
        string line = "";
        foreach (string word in text.Split(' '))
        {
            if (line.Length + word.Length > 60) { lines.Add(line); line = ""; }
            line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) lines.Add(line);
        return string.Join("\n", lines);
    }

    // ── Bottom bar ────────────────────────────────────────────────────────────────────────────────────

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        Vector2 btn = new(screenW * 0.13f, barH * 0.55f);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput;
        float rx = screenW - margin - btn.X;

        ImGui.SetCursorPos(new Vector2(margin, by));
        switch (_view)
        {
            case EView.List:
                if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Confirm("New Campaign", btn))
                {
                    _view = EView.New;
                    _newName = "";
                    _newParty.Clear();
                    _heroes = HeroPaths.LoadAll(GameType);
                    _message = "";
                }
                if (Current is { } cur)
                {
                    QuestCampaign c = cur.Campaign;
                    ImGui.SetCursorPos(new Vector2(rx - btn.X - 10f, by));
                    ImGui.BeginDisabled(c.Active == null);
                    if (UiButton.Confirm("Play next mission", btn)) PlayNext(c);
                    ImGui.EndDisabled();
                    ImGui.SetCursorPos(new Vector2(rx - (btn.X + 10f) * 2f, by));
                    ImGui.BeginDisabled(c.Active != null || c.Finished);
                    if (UiButton.Navigate("Shop", btn)) { _view = EView.Shop; _shopHero = 0; }
                    ImGui.EndDisabled();
                    ImGui.SetCursorPos(new Vector2(rx - (btn.X + 10f) * 3f, by));
                    if (UiButton.Back(_confirmDelete ? "Really delete?" : "Delete", btn))
                    {
                        if (_confirmDelete) Delete(cur.Path, c);
                        else _confirmDelete = true;
                    }
                }
                break;
            case EView.New:
                if (UiButton.Back("Cancel", btn) || esc) _view = EView.List;
                ImGui.SetCursorPos(new Vector2(rx, by));
                string? problem = NewProblem();
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm("Create", btn)) CreateCampaign();
                ImGui.EndDisabled();
                if (problem != null)
                {
                    Vector2 ts = ImGui.CalcTextSize(problem);
                    ImGui.SetCursorPos(new Vector2(rx - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
                    UiText.Colored(TextDim, problem);
                }
                break;
            case EView.Shop:
                if (UiButton.Back("Done", btn) || esc) { _view = EView.List; Reload(); }
                break;
        }

        if (_view == EView.List && _message.Length > 0)
        {
            Vector2 ts = ImGui.CalcTextSize(_message);
            ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
            UiText.Colored(_messageIsError ? Bad : Good, _message);
        }
    }

    private void Delete(string path, QuestCampaign c)
    {
        try { File.Delete(path); } catch { Say("Couldn't delete the campaign file.", true); return; }
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null || hero.CampaignName != c.Name) continue;
            hero.CampaignName = "";
            hero.CampaignDifficulty = 0;
            hero.WoundsTaken = 0;
            hero.PowerLeft = -1;
            CampaignPaths.SaveHero(file, hero);
        }
        Reload();
        Say($"Deleted \"{c.Name}\".");
    }
}
