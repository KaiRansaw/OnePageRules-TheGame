using FDG.Rules.Dispatch;
using FDG;
using System.Numerics;
using FDG.Quest;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Hero Builder, one per Quest game (GF: Star Quest and AoF: Quest). Batch E rebuilt it in the Army
/// Forge look from Kai's "Hero Builder Mockup" (wish list, 2026-11-07):
/// <list type="bullet">
/// <item>A top bar: Back, the screen's name, the game, Shop and + New Hero.</item>
/// <item>The live <b>hero sheet</b> always on the left: portrait, level / class, XP / money / points,
/// Qua Def Tou Pow Str Dex Wil, weapons, rules, skills, items, injuries - hover = book wording.</item>
/// <item>On the right one of three views: <b>My Heroes</b> (portrait cards; Edit, Delete - asks, and warns
/// when the hero is in a campaign), <b>New / Edit Hero</b> in four tabbed steps (1 Hero &amp; Name,
/// 2 Class, 3 Starter Skills, 4 Gear) with a line that always says what is still missing and Next /
/// SAVE HERO, or the <b>Shop</b>.</item>
/// </list>
/// Factions are grouped and ordered exactly as Army Forge does it (<see cref="QuestDataSet.FactionGroups"/>).
/// A hero that has already played (XP, a level-up or a finished mission) can only be renamed and given
/// its new skills here - its build is locked so progress can't be lost by rebuilding it.
/// <para>Rental layout (plain ImGui). What each part shows, and where the buttons go, is the permanent part.</para>
/// </summary>
public sealed class HeroBuilderScreen : IAppScreen
{
    public Action? OnBack;

    /// <summary>
    /// #418 - which Quest system's heroes this screen is editing. It used to be a <c>const</c> reading
    /// "GF Star Quest" that five other files reached into, which is precisely why AoF: Quest heroes
    /// could not exist: the screen, the save folder, the party picker and the headless party loader all
    /// agreed on one system by definition.
    /// </summary>
    public EGameSystem System { get; }

    /// <summary>This system's hero roster and rules.</summary>
    private QuestDataSet Data => QuestDataSet.For(System);

    /// <summary>The folder name this system's heroes are filed under.</summary>
    public string GameType => QuestGameTypes.For(System);

    /// <summary>"c" for Star Quest's credits, "g" for AoF: Quest's gold (#418).</summary>
    private string Money => FDG.Quest.QuestItemSkin.CurrencySuffix(System);

    /// <summary>"credits" or "gold", for a sentence rather than a value.</summary>
    private string MoneyWord => FDG.Quest.QuestItemSkin.CurrencyWord(System);

    public HeroBuilderScreen(EGameSystem system = EGameSystem.GFStarQuest) => System = system;

    private enum EView { List, Edit, Shop }
    private EView _view = EView.List;

    /// <summary>The four steps of New Hero, in order (the tab labels).</summary>
    internal static readonly string[] StepNames = { "1. Hero & Name", "2. Class", "3. Starter Skills", "4. Gear" };
    private int _step;

    // Shop (#414). Kai's call: the books only open a shop between waves and between chapters, so a
    // one-off party could earn credits with nowhere to spend them until its next game's first wave
    // ended. This is a convenience beyond the books rather than a rule, which is why it lives here on
    // the hero sheet and changes nothing about the two in-game shops.
    private int _shopHero;

    // My Heroes
    private List<(string Path, QuestHero Hero)> _saved = new();
    private List<QuestCampaign> _campaigns = new();
    private int _selected = -1;
    private int _deleteIndex = -1;
    private bool _openDelete;

    // Editor
    private string? _editingPath;
    private QuestHero? _locked;              // a hero that has played: rename only (and new skills)
    private SqFaction? _faction;
    private SqFaction? _browseFaction;       // the faction whose heroes step 1 is showing
    private SqHero? _base;
    private SqClass? _class;
    private readonly HashSet<string> _skills = new();
    // (package uid, section index) -> chosen option indices
    private readonly Dictionary<(string, int), List<int>> _picks = new();
    private string _name = "";
    private string _filter = "";
    private string _message = "";
    private bool _messageIsError;

    private readonly UnitPortraitCards _cards = new();

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);
    private static readonly Vector4 Title = new(0.55f, 0.85f, 0.80f, 1f);

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public void OnShown()
    {
        Reload();
        _view = EView.List;
        _message = "";
    }

    private void Reload()
    {
        _saved = HeroPaths.LoadAll(GameType);
        _campaigns = CampaignPaths.LoadAll(GameType).Select(e => e.Campaign).ToList();
        if (_selected >= _saved.Count) _selected = _saved.Count - 1;
        if (_selected < 0 && _saved.Count > 0) _selected = 0;
    }

    private QuestCampaign? CampaignOf(string heroPath) => CampaignPaths.CampaignOf(GameType, heroPath, _campaigns);

    // ── Frame ─────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##HeroBuilder", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawTopBar();
        ImGui.Separator();

        Vector2 avail = ImGui.GetContentRegionAvail();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float leftW = MathF.Min(MathF.Max(UiChrome.Em(22f), avail.X * 0.30f), avail.X * 0.45f);

        // The hero sheet, always: the hero being built, the selected hero, or the one in the shop.
        ImGui.BeginChild("##sheet", new Vector2(leftW, avail.Y), ImGuiChildFlags.Borders);
        DrawSheet(SheetHero());
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##right", new Vector2(0, avail.Y), ImGuiChildFlags.None);
        Vector2 right = ImGui.GetContentRegionAvail();
        switch (_view)
        {
            case EView.List: DrawList(right); break;
            case EView.Edit: DrawEditor(right); break;
            case EView.Shop: DrawShop(right); break;
        }
        ImGui.EndChild();

        DrawDeleteModal();
        ImGui.End();
    }

    private QuestHero? SheetHero() => _view switch
    {
        EView.Edit => Compose(),
        EView.Shop => _saved.Count > 0 ? _saved[Math.Clamp(_shopHero, 0, _saved.Count - 1)].Hero : null,
        _ => _selected >= 0 && _selected < _saved.Count ? _saved[_selected].Hero : null,
    };

    /// <summary>Back, the screen's name, the game, the status line; Shop and + New Hero on the right.</summary>
    private void DrawTopBar()
    {
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput
            && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup);
        float rowH = UiChrome.ButtonSize("Back").Y;
        // Back goes to the screen you came from; inside the editor or the shop it first goes back to the
        // hero list (the same as Cancel), because that is the screen those were opened from.
        if (UiButton.Back("Back", UiChrome.ButtonSize("Back")) || esc)
        {
            if (_view == EView.List) OnBack?.Invoke();
            else { _view = EView.List; _message = ""; Reload(); }
        }
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowH - ImGui.GetTextLineHeight()) / 2f);
        string title = _view switch
        {
            EView.Shop => "HERO SHOP",
            EView.Edit => _editingPath == null ? "NEW HERO" : "EDIT HERO",
            _ => "MY HEROES",
        };
        UiText.Colored(Title, title, wrap: false);
        ImGui.SameLine();
        UiText.Disabled($"   {GameSystemInfo.DisplayName(System)}   -   {_saved.Count} hero{(_saved.Count == 1 ? "" : "es")}", wrap: false);
        if (Data.LoadError is { } err) { ImGui.SameLine(); UiText.Colored(Bad, "   " + err, wrap: false); }

        string[] buttons = { "Shop", "+ New Hero" };
        float rightW = buttons.Sum(b => UiChrome.ButtonSize(b).X) + ImGui.GetStyle().ItemSpacing.X * buttons.Length;
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - rightW));
        // SameLine put the cursor at the title's lowered line; lift it back level with Back.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (rowH - ImGui.GetTextLineHeight()) / 2f);
        // #414: the shop. Disabled with no heroes, because there is nobody to shop for.
        ImGui.BeginDisabled(_saved.Count == 0 || _view == EView.Edit);
        if (UiButton.Navigate("Shop", UiChrome.ButtonSize("Shop")))
        {
            _shopHero = Math.Max(0, _selected);
            _view = EView.Shop;
            _message = "";
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped(_saved.Count == 0 ? "Make a hero first." : _view == EView.Edit
                ? "Save or cancel the hero you are building first."
                : $"Buy, sell and hand things (and {MoneyWord}) between your heroes.", 320f);
        ImGui.SameLine();
        ImGui.BeginDisabled(_view == EView.Edit);
        if (UiButton.Confirm("+ New Hero", UiChrome.ButtonSize("+ New Hero"))) StartNew();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.TooltipWrapped("Build a new hero in four steps: hero and name, class, starter skills, gear.", 320f);

        if (_message.Length > 0) UiText.Colored(_messageIsError ? Bad : Good, _message);
    }

    // ── My Heroes ─────────────────────────────────────────────────────────────────────────────────────

    private void DrawList(Vector2 size)
    {
        float buttonH = UiChrome.ButtonSize("Edit selected").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
        UiText.Colored(TextBright, "MY HEROES - click to view, double-click to edit");
        ImGui.BeginChild("##heroCards", new Vector2(0, size.Y - buttonH - ImGui.GetTextLineHeightWithSpacing()), ImGuiChildFlags.Borders);
        if (_saved.Count == 0)
            UiText.Disabled("No heroes yet. Click \"+ New Hero\" to make your first one.");
        else
        {
            var cards = _saved.Select(e =>
            {
                QuestHero h = e.Hero;
                QuestCampaign? camp = CampaignOf(e.Path);
                return new UnitPortraitCards.Card(h.Name, h.BaseHeroName, $"Lvl {h.Level} {h.Class}",
                    () => HeroTooltip(h, camp), camp != null ? "in campaign" : null, Faction: h.Faction);
            }).ToList();
            // A hero's picture is filed under its base hero and its own faction, the same files the table uses.
            UnitPortraitCards.CardEvent hit = _cards.DrawCardGrid("heroes", cards, "", _selected);
            if (hit.Clicked >= 0) _selected = hit.Clicked;
            if (hit.DoubleClicked >= 0) StartEdit(hit.DoubleClicked);
        }
        ImGui.EndChild();

        bool has = _selected >= 0 && _selected < _saved.Count;
        ImGui.BeginDisabled(!has);
        if (UiButton.Navigate("Edit selected", UiChrome.ButtonSize("Edit selected"))) StartEdit(_selected);
        ImGui.SameLine();
        if (UiButton.Back("Delete", UiChrome.ButtonSize("Delete")))
        {
            _deleteIndex = _selected;
            _openDelete = true;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        UiText.Disabled("Delete asks first, and warns if the hero is in a campaign.", wrap: false);
    }

    private string HeroTooltip(QuestHero h, QuestCampaign? campaign)
    {
        var lines = new List<string>
        {
            $"{h.Name} - Level {h.Level} {h.Class}",
            $"{h.Faction} - {h.BaseHeroName}",
            $"XP {h.Xp} - {h.Credits}{Money} - {h.Points} pts",
            $"Qua {h.Quality}+  Def {h.Defense}+  Tou {h.Tough}  Pow {h.Power}  Str {h.Strength}+  Dex {h.Dexterity}+  Wil {h.Willpower}+",
        };
        if (h.Items.Count > 0) lines.Add("Items: " + string.Join(", ", h.Items));
        if (h.Injuries.Count > 0) lines.Add("Injuries: " + string.Join(", ", h.Injuries));
        if (campaign != null) lines.Add($"In campaign \"{campaign.Name}\".");
        return string.Join("\n", lines);
    }

    private const string DeleteTitle = "Delete hero?";

    private void DrawDeleteModal()
    {
        if (_openDelete) { ImGui.OpenPopup(DeleteTitle); _openDelete = false; }
        if (!ImGui.BeginPopupModal(DeleteTitle, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (_deleteIndex < 0 || _deleteIndex >= _saved.Count) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        (string path, QuestHero hero) = _saved[_deleteIndex];
        ImGui.TextUnformatted($"Delete {hero.Name}? This can't be undone.");
        if (CampaignOf(path) is { } camp)
            UiText.Colored(Bad, $"{hero.Name} is in the campaign \"{camp.Name}\". That campaign will be missing this hero.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Delete", UiChrome.ButtonSize("Delete")))
        {
            Delete(_deleteIndex);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (UiButton.Navigate("Cancel", UiChrome.ButtonSize("Cancel"))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void Delete(int index)
    {
        string name = _saved[index].Hero.Name;
        try
        {
            File.Delete(_saved[index].Path);
            _message = $"Deleted {name}.";
            _messageIsError = false;
        }
        catch (Exception ex) { _message = "Couldn't delete: " + ex.Message; _messageIsError = true; }
        Reload();
    }

    // ── Shop (#414) ───────────────────────────────────────────────────────────────────────────────────
    //
    // Kai's call, and a convenience rather than a rule: the books open a shop between Horde waves and
    // between campaign chapters, so a hero that finished a one-off game with credits had nowhere to spend
    // them until its next game's first wave ended. Nothing about the two in-game shops changes.

    private void DrawShop(Vector2 size)
    {
        if (_saved.Count == 0) { _view = EView.List; return; }
        QuestShopPanel.Draw(System, _saved, ref _shopHero, moneyHandOver: true, SaveSheet, size.X, size.Y);
    }

    /// <summary>Writes one hero sheet back to its file, reporting a failure rather than losing it quietly.</summary>
    private void SaveSheet(string path, QuestHero hero)
    {
        if (CampaignPaths.SaveHero(path, hero)) return;
        _message = $"Could not save {hero.Name} - is the file read-only?";
        _messageIsError = true;
    }

    // ── Editor logic ──────────────────────────────────────────────────────────────────────────────────

    private void StartNew()
    {
        _editingPath = null;
        _locked = null;
        _faction = null; _base = null; _class = null; _browseFaction = null;
        _skills.Clear(); _picks.Clear();
        _name = ""; _filter = ""; _message = "";
        _step = 0;
        _view = EView.Edit;
    }

    private void StartEdit(int index)
    {
        if (index < 0 || index >= _saved.Count) return;
        StartNew();
        var (path, hero) = _saved[index];
        _editingPath = path;
        _name = hero.Name;
        _faction = Data.Factions.FirstOrDefault(f => f.Uid == hero.FactionUid);
        _browseFaction = _faction;
        _base = _faction?.Heroes.FirstOrDefault(h => h.Id == hero.BaseHeroId);
        _class = Data.ClassById(hero.ClassId);
        bool played = hero.Xp > 0 || hero.Level > 1 || hero.Missions > 0;
        if (played || _faction == null || _base == null || _class == null)
        {
            _locked = hero.Clone();
            return;
        }
        foreach (string s in hero.Skills) _skills.Add(s);
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

    private IEnumerable<((string, int) Key, SqSection Section)> Sections()
    {
        if (_faction == null || _base == null) yield break;
        foreach (string uid in _base.Upgrades)
        {
            SqPackage? p = _faction.Package(uid);
            if (p == null) continue;
            for (int i = 0; i < p.Sections.Count; i++) yield return ((uid, i), p.Sections[i]);
        }
    }

    /// <summary>The hero as it stands with the editor's current choices.</summary>
    private QuestHero? Compose()
    {
        if (_locked != null)
        {
            var h = _locked.Clone();
            h.Name = _name.Trim();
            h.SourceFile = null;
            return h;
        }
        if (_faction == null || _base == null) return null;
        QuestHero hero = Data.NewHero(_faction, _base);
        hero.Name = _name.Trim();
        if (_class != null) Data.ApplyClass(hero, _class);
        Data.ApplyUpgrades(hero, _faction, _base, ValidPicks());
        QuestProgression.SetToughRule(hero);
        hero.Skills = _skills.ToList();
        return hero;
    }

    /// <summary>The chosen upgrades in order, skipping any whose "replace X" no longer has an X (an earlier
    /// pick that gave it was undone).</summary>
    private List<(SqSection, SqOption)> ValidPicks()
    {
        var picks = new List<(SqSection, SqOption)>();
        if (_base == null) return picks;
        foreach (var (key, section) in Sections())
        {
            if (!_picks.TryGetValue(key, out var chosen) || chosen.Count == 0) continue;
            var (w, r, _) = QuestDataSet.Loadout(_base, picks);
            if (!QuestDataSet.HasTargets(section, w, r)) continue;
            foreach (int o in chosen.OrderBy(i => i))
                if (o >= 0 && o < section.Opts.Count) picks.Add((section, section.Opts[o]));
        }
        return picks;
    }

    /// <summary>What is still missing before the hero can be saved, in step order, or null.</summary>
    private string? SaveProblem() => SaveProblem(_name, _locked != null, _base != null, _class?.Skills, _skills.Count);

    /// <summary>
    /// The missing-line wording, pure so it is pinned by a test: name and hero (step 1), class (step 2),
    /// starter skills (step 3). Gear (step 4) is optional and never missing.
    /// </summary>
    internal static string? SaveProblem(string name, bool locked, bool hasBase, int? classSkills, int skillsPicked)
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

    private void Save()
    {
        QuestHero? hero = Compose();
        if (hero == null) return;
        try
        {
            string folder = HeroPaths.FolderFor(GameType);
            string path = Path.Combine(folder, HeroPaths.FileNameFor(hero.Name));
            bool renamed = _editingPath != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_editingPath), StringComparison.OrdinalIgnoreCase);
            if ((_editingPath == null || renamed) && File.Exists(path))
            {
                _message = "A hero with that name already exists.";
                _messageIsError = true;
                return;
            }
            hero.SourceFile = path;
            File.WriteAllText(path, hero.ToJson());
            if (renamed)
            {
                File.Delete(_editingPath!);
                RenameInCampaigns(_editingPath!, path);
            }
            _editingPath = path;
            Reload();
            _selected = _saved.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            _view = EView.List;
            _message = $"Saved {hero.Name}.";
            _messageIsError = false;
        }
        catch (Exception ex)
        {
            _message = "Couldn't save: " + ex.Message;
            _messageIsError = true;
        }
    }

    /// <summary>A renamed hero gets a new file; the campaign that holds it follows it to the new file, so
    /// renaming never quietly drops a hero from its campaign.</summary>
    private void RenameInCampaigns(string oldPath, string newPath)
    {
        string oldKey = Path.GetFullPath(oldPath);
        foreach ((string file, QuestCampaign c) in CampaignPaths.LoadAll(GameType))
        {
            bool changed = false;
            for (int i = 0; i < c.HeroFiles.Count; i++)
            {
                if (!string.Equals(Path.GetFullPath(c.HeroFiles[i]), oldKey, StringComparison.OrdinalIgnoreCase)) continue;
                c.HeroFiles[i] = newPath;
                changed = true;
            }
            if (changed) CampaignPaths.Save(file, c);
        }
    }

    // ── Editor drawing: four tabbed steps ─────────────────────────────────────────────────────────────

    private void DrawEditor(Vector2 size)
    {
        // The step tabs. A hero that has played only has its name (step 1) and its new skills (step 3).
        for (int i = 0; i < StepNames.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool sel = i == _step;
            bool off = _locked != null && (i == 1 || i == 3);
            ImGui.BeginDisabled(off);
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.65f, 1f));
            string label = _locked != null && i == 2 ? "3. New Skills" : StepNames[i];
            if (ImGui.Button($"{label}##step{i}", new Vector2(UiChrome.Em(10f), 0f))) _step = i;
            if (sel) ImGui.PopStyleColor();
            ImGui.EndDisabled();
            if (off && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiText.Tooltip("This hero has already played, so its build is locked.");
        }
        if (_locked != null && (_step == 1 || _step == 3)) _step = 0;

        float footH = UiChrome.ButtonSize("Next").Y + ImGui.GetStyle().ItemSpacing.Y * 2f;
        ImGui.BeginChild("##stepBody", new Vector2(0, ImGui.GetContentRegionAvail().Y - footH), ImGuiChildFlags.Borders);
        switch (_step)
        {
            case 0: DrawStepHero(); break;
            case 1: DrawStepClass(); break;
            case 2: if (_locked != null) DrawNewSkillPicks(_locked); else DrawStepSkills(); break;
            default: DrawStepGear(); break;
        }
        ImGui.EndChild();

        // Footer: Cancel, what is still missing, Next / SAVE HERO.
        if (UiButton.Back("Cancel", UiChrome.ButtonSize("Cancel"))) { _view = EView.List; _message = ""; }
        string? problem = SaveProblem();
        bool last = _step == StepNames.Length - 1 || (_locked != null && _step == 2);
        string next = last ? "SAVE HERO" : "Next";
        ImGui.SameLine();
        string line = problem ?? (last ? "Ready to save." : "Everything needed is done - Next, or save from the last step.");
        UiText.Colored(problem != null ? Gold : Good, line, wrap: false);
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - UiChrome.ButtonSize(next).X - ImGui.GetStyle().WindowPadding.X));
        if (last)
        {
            ImGui.BeginDisabled(problem != null);
            if (UiButton.Confirm(next, UiChrome.ButtonSize(next))) Save();
            ImGui.EndDisabled();
        }
        else if (UiButton.Navigate(next, UiChrome.ButtonSize(next)))
        {
            _step++;
            if (_locked != null && _step == 1) _step = 2;
        }
    }

    private static void Header(string text)
    {
        ImGui.Spacing();
        UiText.Colored(Gold, text);
        ImGui.Separator();
    }

    /// <summary>A faction's heroes that match the search box: every hero if the faction name matches.</summary>
    private static List<SqHero> Matching(SqFaction faction, string search)
    {
        if (search.Length == 0) return faction.Heroes.ToList();
        if (faction.Faction.Contains(search, StringComparison.OrdinalIgnoreCase)) return faction.Heroes.ToList();
        return faction.Heroes.Where(h => h.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Step 1: the hero's name, then the factions (Army Forge's groups and order) on the left and the
    /// chosen faction's base heroes as portrait cards on the right.</summary>
    private void DrawStepHero()
    {
        Header("NAME");
        ImGui.SetNextItemWidth(UiChrome.Em(20f));
        ImGui.InputTextWithHint("##heroName", "Hero name", ref _name, 40);

        if (_locked != null)
        {
            Header("HERO");
            UiText.Colored(TextBright, $"{_locked.BaseHeroName}");
            UiText.Disabled(_locked.Faction);
            UiText.Disabled("This hero has already played a mission, so its build is locked. Renaming still works.");
            return;
        }

        Header("BASE HERO");
        UiText.Disabled("Factions grouped exactly as Army Forge groups them. Pick a base hero. Locked once the hero has played a mission (renaming still works).");
        ImGui.SetNextItemWidth(UiChrome.Em(20f));
        ImGui.InputTextWithHint("##filter", "Search factions or heroes...", ref _filter, 64);

        Vector2 avail = ImGui.GetContentRegionAvail();
        float listW = MathF.Min(UiChrome.Em(18f), avail.X * 0.4f);
        ImGui.BeginChild("##factionList", new Vector2(listW, avail.Y), ImGuiChildFlags.Borders);
        string f = _filter.Trim();
        foreach (string group in Data.FactionGroups)
        {
            // Inside a group the books keep Army Forge's order - the order the hero file holds them in.
            var factions = Data.Factions.Where(fc => Data.GroupOf(fc) == group && Matching(fc, f).Count > 0).ToList();
            if (factions.Count == 0) continue;
            UiText.Colored(Accent, group.ToUpperInvariant());
            foreach (SqFaction fac in factions)
            {
                bool sel = _browseFaction == fac;
                if (ImGui.Selectable($"   {fac.Faction}  ({Matching(fac, f).Count})##{fac.Uid}", sel)) _browseFaction = fac;
            }
            ImGui.Spacing();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##baseHeroes", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        if (_browseFaction == null)
            UiText.Disabled("Pick a faction on the left to see its heroes.");
        else
        {
            SqFaction fac = _browseFaction;
            UiText.Colored(TextBright, $"{Data.GroupOf(fac)}  >  {fac.Faction}");
            List<SqHero> heroes = Matching(fac, f);
            var cards = heroes.Select(h => new UnitPortraitCards.Card(h.Name, h.Name, $"{h.Cost} pts",
                () => BaseHeroTooltip(h))).ToList();
            int selected = _faction == fac && _base != null ? heroes.IndexOf(_base) : -1;
            UnitPortraitCards.CardEvent hit = _cards.DrawCardGrid("base", cards, fac.Faction, selected);
            if (hit.Clicked >= 0)
            {
                SqHero pick = heroes[hit.Clicked];
                if (_base != pick) _picks.Clear();
                _faction = fac;
                _base = pick;
            }
        }
        ImGui.EndChild();
    }

    private static string BaseHeroTooltip(SqHero hero)
    {
        var lines = new List<string> { $"{hero.Name} - {hero.Cost} pts" };
        foreach (SqWeapon wpn in hero.Weapons) lines.Add(wpn.ToQuest().Label());
        if (hero.Rules.Count > 0) lines.Add(string.Join(", ", hero.Rules));
        if (hero.Gear.Count > 0) lines.Add("Gear: " + string.Join(", ", hero.Gear));
        return string.Join("\n", lines);
    }

    /// <summary>Step 2: the classes as cards; hover a card for its full feature text.</summary>
    private void DrawStepClass()
    {
        Header("CLASS");
        UiText.Disabled("Pick a class. Hover a card for its full feature text.");
        float avail = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((avail + 8f) / (UiChrome.Em(16f) + 8f)));
        float cardW = (avail - 8f * (columns - 1)) / columns;
        float lh = ImGui.GetTextLineHeight();
        int i = 0;
        foreach (SqClass cls in Data.Classes)
        {
            if (i % columns != 0) ImGui.SameLine(0f, 8f);
            DrawClassCard(cls, cardW, lh * 3.4f + 12f);
            i++;
        }
    }

    /// <summary>Step 3: the class's starter skills; full ones grey out.</summary>
    private void DrawStepSkills()
    {
        if (_class == null)
        {
            Header("STARTER SKILLS");
            UiText.Disabled("Pick a class first (step 2) - the class decides the skills.");
            return;
        }
        Header($"STARTER SKILLS  (pick {_class.Skills})");
        UiText.Disabled($"Pick {_class.Skills} starter skill{(_class.Skills == 1 ? "" : "s")}. Full ones grey out.");
        SqSkill? feat = Data.ClassFeat(_class);
        if (feat != null)
        {
            ImGui.PushTextWrapPos(0f);
            UiText.Colored(Accent, $"{feat.Name} (class feature, always known)");
            UiText.Disabled(feat.Rule);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }
        foreach (SqSkill s in Data.ClassSkills(_class, 0))
        {
            bool on = _skills.Contains(s.Name);
            bool full = !on && _skills.Count >= _class.Skills;
            ImGui.BeginDisabled(full);
            if (ImGui.Checkbox($"{s.Name} ({QuestDataSet.SkillStat(s)})##sk{s.Id}", ref on))
            {
                if (on) _skills.Add(s.Name); else _skills.Remove(s.Name);
            }
            ImGui.EndDisabled();
            ImGui.Indent(26f);
            ImGui.PushTextWrapPos(0f);
            UiText.Disabled(s.Rule);
            ImGui.PopTextWrapPos();
            ImGui.Unindent(26f);
        }
    }

    /// <summary>Step 4: the optional gear upgrades - the base hero's own upgrade options, with the same
    /// "replace / upgrade with" sections the Army Forge shows.</summary>
    private void DrawStepGear()
    {
        Header("GEAR UPGRADES  (optional, adds points)");
        if (_base == null)
        {
            UiText.Disabled("Pick a base hero first (step 1) - the hero decides the gear.");
            return;
        }
        bool any = false;
        var sofar = new List<(SqSection, SqOption)>();
        foreach (var (key, section) in Sections())
        {
            any = true;
            bool multi = QuestDataSet.IsMultiPick(section);
            var (lw, lr, _) = QuestDataSet.Loadout(_base, sofar);
            if (!QuestDataSet.HasTargets(section, lw, lr))
            {
                UiText.Disabled($"{section.Label}  (needs {string.Join(" and ", section.Targets ?? new List<string>())} first)");
                ImGui.Spacing();
                continue;
            }
            if (_picks.TryGetValue(key, out var have))
                foreach (int o in have.OrderBy(i => i))
                    if (o >= 0 && o < section.Opts.Count) sofar.Add((section, section.Opts[o]));
            UiText.Colored(TextBright, section.Label);
            if (!_picks.TryGetValue(key, out var chosen)) chosen = new List<int>();
            ImGui.Indent(10f);
            if (!multi)
            {
                bool none = chosen.Count == 0;
                if (ImGui.RadioButton($"None##{key.Item1}{key.Item2}", none)) _picks.Remove(key);
            }
            for (int o = 0; o < section.Opts.Count; o++)
            {
                SqOption opt = section.Opts[o];
                string label = $"{opt.Label}  +{opt.Cost ?? 0}pts##{key.Item1}{key.Item2}o{o}";
                bool on = chosen.Contains(o);
                if (multi)
                {
                    if (ImGui.Checkbox(label, ref on))
                    {
                        if (on) chosen.Add(o); else chosen.Remove(o);
                        _picks[key] = chosen;
                    }
                }
                else if (ImGui.RadioButton(label, on))
                    _picks[key] = new List<int> { o };
            }
            ImGui.Unindent(10f);
            ImGui.Spacing();
        }
        if (!any) UiText.Disabled("This hero has no gear options.");
    }

    /// <summary>A hero that levelled up with "+1 Skill" picks its new skills here: its class's skills and the
    /// skills every class may learn, up to the tier its level has unlocked.</summary>
    private void DrawNewSkillPicks(QuestHero hero)
    {
        int free = hero.SkillSlots - hero.Skills.Count;
        SqClass? cls = Data.ClassById(hero.ClassId);
        if (free <= 0 || cls == null)
        {
            Header("NEW SKILLS");
            UiText.Disabled("No new skills to pick. A level-up that gives \"+1 Skill\" opens a pick here.");
            return;
        }
        int tierCap = QuestProgression.TierUnlocked(hero.Level);
        Header($"NEW SKILLS  (pick {free})");
        UiText.Disabled($"Level {hero.Level} unlocks tier {tierCap} skills.");
        for (int tier = 0; tier <= tierCap; tier++)
        {
            var options = Data.ClassSkills(cls, tier).Concat(Data.DefaultSkills(tier))
                .Where(s => !hero.Skills.Contains(s.Name)).ToList();
            if (options.Count == 0) continue;
            UiText.Colored(TextBright, tier == 0 ? "Starter skills" : $"Tier {tier}");
            foreach (SqSkill s in options)
            {
                bool on = false;
                if (ImGui.Checkbox($"{s.Name} ({QuestDataSet.SkillStat(s)})##new{s.Id}", ref on) && on && hero.Skills.Count < hero.SkillSlots)
                    hero.Skills.Add(s.Name);
                ImGui.Indent(26f);
                ImGui.PushTextWrapPos(0f);
                UiText.Disabled(s.Rule);
                ImGui.PopTextWrapPos();
                ImGui.Unindent(26f);
            }
        }
    }

    private void DrawClassCard(SqClass cls, float w, float h)
    {
        Vector2 p0 = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##cls{cls.Id}", new Vector2(w, h)) && _class != cls)
        {
            _class = cls;
            _skills.Clear();
        }
        bool hovered = ImGui.IsItemHovered();
        bool sel = _class == cls;
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 p1 = p0 + new Vector2(w, h);
        dl.AddRectFilled(p0, p1, U(hovered ? new Vector4(0.16f, 0.16f, 0.18f, 1f) : new Vector4(0.11f, 0.11f, 0.13f, 1f)), 5f);
        dl.AddRect(p0, p1, U(sel ? Good : new Vector4(0.28f, 0.28f, 0.32f, 1f)), 5f, ImDrawFlags.None, sel ? 2.5f : 1f);
        float lh = ImGui.GetTextLineHeight();
        // Star Quest renames every class (Soldier, Biomancer, Infiltrator...), so the familiar fantasy
        // name rides along after it - otherwise there is nothing on screen that reads as "Fighter".
        string title = string.IsNullOrEmpty(cls.BaseName) || cls.BaseName == cls.Name
            ? cls.Name : $"{cls.Name}  ({cls.BaseName})";
        SqSkill? feat = Data.ClassFeat(cls);
        dl.AddText(p0 + new Vector2(8f, 6f), U(TextBright), Fit(title, w - 16f));
        dl.AddText(p0 + new Vector2(8f, 6f + lh + 2f), U(Gold), Fit(ClassBonusText(cls), w - 16f));
        dl.AddText(p0 + new Vector2(8f, 6f + (lh + 2f) * 2), U(TextDim), Fit(feat != null ? "Feature: " + feat.Name : cls.Description ?? "", w - 16f));
        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            if (!string.IsNullOrEmpty(cls.Description)) ImGui.TextUnformatted(cls.Description);
            if (feat != null) UiText.Colored(Accent, $"{feat.Name}: {feat.Rule}", wrap: false);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static string Fit(string s, float width)
    {
        if (ImGui.CalcTextSize(s).X <= width) return s;
        while (s.Length > 3 && ImGui.CalcTextSize(s + "...").X > width) s = s[..^1];
        return s + "...";
    }

    private static string ClassBonusText(SqClass c)
    {
        var parts = new List<string>();
        if (c.Qua > 0) parts.Add($"Qua +{c.Qua}");
        if (c.Str > 0) parts.Add($"Str +{c.Str}");
        if (c.Dex > 0) parts.Add($"Dex +{c.Dex}");
        if (c.Wil > 0) parts.Add($"Wil +{c.Wil}");
        if (c.Pow > 0) parts.Add($"Pow +{c.Pow}");
        if (c.Caster > 0) parts.Add($"Caster({c.Caster})");
        parts.Add($"{c.Skills} Skill{(c.Skills == 1 ? "" : "s")}");
        return string.Join("  ", parts);
    }

    // ── Hero sheet ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A hero's rules as hoverable names (Kai, 2026-11-05: the hero sheet's special rules had no tooltips).
    /// The text is this Quest game's own: the hero's faction rule from its hero book first, else OPR's wording
    /// for THIS system only (<see cref="OfficialRuleText.OwnText"/>) - never Grimdark Future's or Age of
    /// Fantasy's, which word several of these differently.
    /// </summary>
    private IReadOnlyList<RuleHoverText.Segment> RuleSegments(QuestHero hero, IEnumerable<string> rules)
    {
        SqFaction? faction = Data.Factions.FirstOrDefault(f => f.Uid == hero.FactionUid);
        var segments = new List<RuleHoverText.Segment>();
        foreach (string rule in rules)
        {
            if (segments.Count > 0) segments.Add(new RuleHoverText.Segment(", ", null, null));
            segments.Add(new RuleHoverText.Segment(rule, rule, QuestRuleText.For(rule, System, faction)));
        }
        return segments;
    }

    private void DrawSheet(QuestHero? hero)
    {
        Header("HERO SHEET");
        if (hero == null)
        {
            UiText.Disabled(_view == EView.Edit ? "Pick a base hero (step 1) to see its sheet." : "Pick a hero to see its sheet.");
            return;
        }
        float portrait = MathF.Min(UiChrome.Em(7f), ImGui.GetContentRegionAvail().X * 0.4f);
        _cards.DrawPortrait(hero.BaseHeroName, hero.Faction, new Vector2(portrait, portrait));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(0f);
        UiText.Colored(TextBright, hero.Name.Length > 0 ? hero.Name : "(no name yet)");
        UiText.Colored(Gold, $"Level {hero.Level}  {(hero.Class.Length > 0 ? hero.Class : "(no class)")}");
        UiText.Disabled($"{hero.Faction} - {hero.BaseHeroName}");
        int next = hero.Level < QuestProgression.MaxLevel ? QuestProgression.XpForLevel[hero.Level + 1] : hero.Xp;
        UiText.Disabled($"XP {hero.Xp} / {next} - {hero.Credits} {MoneyWord} - {hero.Points} pts");
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.Spacing();

        // Stat boxes.
        var stats = new List<(string, string)>
        {
            ("Qua", $"{hero.Quality}+"), ("Def", $"{hero.Defense}+"), ("Tou", $"{hero.Tough}"), ("Pow", $"{hero.Power}"),
            ("Str", $"{hero.Strength}+"), ("Dex", $"{hero.Dexterity}+"), ("Wil", $"{hero.Willpower}+"),
        };
        if (hero.Caster > 0) stats.Add(("Caster", $"{hero.Caster}"));
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        float avail = ImGui.GetContentRegionAvail().X;
        float box = MathF.Min(64f, (avail - (stats.Count - 1) * 4f) / stats.Count);
        float lh = ImGui.GetTextLineHeight();
        Vector2 o = ImGui.GetCursorScreenPos();
        for (int i = 0; i < stats.Count; i++)
        {
            Vector2 b0 = o + new Vector2(i * (box + 4f), 0f), b1 = b0 + new Vector2(box, lh * 2.4f);
            dl.AddRectFilled(b0, b1, U(new Vector4(0.13f, 0.13f, 0.16f, 1f)), 4f);
            Vector2 ls = ImGui.CalcTextSize(stats[i].Item1), vs = ImGui.CalcTextSize(stats[i].Item2);
            dl.AddText(new Vector2(b0.X + (box - ls.X) / 2f, b0.Y + 3f), U(TextDim), stats[i].Item1);
            dl.AddText(new Vector2(b0.X + (box - vs.X) / 2f, b0.Y + lh + 5f), U(TextBright), stats[i].Item2);
        }
        ImGui.Dummy(new Vector2(avail, lh * 2.4f + 4f));

        Header("WEAPONS");
        if (ImGui.BeginTable("##weapons", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Weapon", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn("RNG", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("ATK", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("Rules", ImGuiTableColumnFlags.WidthStretch, 2.4f);
            ImGui.TableHeadersRow();
            foreach (QuestWeapon wpn in hero.Weapons)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted((wpn.Count > 1 ? $"{wpn.Count}x " : "") + wpn.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(wpn.Range > 0 ? $"{wpn.Range}\"" : "-");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"A{wpn.Attacks}");
                ImGui.TableNextColumn();
                if (wpn.Rules.Count > 0)
                    RuleHoverText.DrawWrapped(RuleSegments(hero, wpn.Rules), ImGui.GetContentRegionAvail().X);
                else ImGui.TextUnformatted("-");
            }
            ImGui.EndTable();
        }

        Header("SPECIAL RULES");
        if (hero.Rules.Count > 0)
            RuleHoverText.DrawWrapped(RuleSegments(hero, hero.Rules), ImGui.GetContentRegionAvail().X);
        else ImGui.TextUnformatted("-");
        ImGui.PushTextWrapPos(0f);
        if (hero.Gear.Count > 0) UiText.Disabled("Gear: " + string.Join(", ", hero.Gear));
        ImGui.PopTextWrapPos();

        Header("SKILLS");
        var feat = hero.ClassFeat.Length > 0 ? Data.SkillByName(hero.ClassFeat) : null;
        if (feat != null)
        {
            UiText.Colored(Accent, $"{feat.Name} (class feature, always known)");
            if (ImGui.IsItemHovered()) UiText.TooltipWrapped(feat.Rule);
        }
        foreach (string name in hero.Skills)
        {
            SqSkill? s = Data.SkillByName(name);
            UiText.Colored(Accent, s != null ? $"{s.Name} ({QuestDataSet.SkillStat(s)})" : name);
            if (s != null && ImGui.IsItemHovered()) UiText.TooltipWrapped(s.Rule);
        }
        if (feat == null && hero.Skills.Count == 0) UiText.Disabled("Pick a class to get skills.");
        ImGui.PushTextWrapPos(0f);
        UiText.Disabled("Common skills (every hero): Discover, Push, Scurry.");
        if (hero.Caster > 0)
            UiText.Disabled($"Casts spells from the {hero.Faction} spell list.");
        ImGui.PopTextWrapPos();

        Header("ITEMS / INJURIES");
        if (hero.Items.Count == 0 && hero.Injuries.Count == 0) UiText.Disabled("None.");
        foreach (IGrouping<string, string> g in hero.Items.GroupBy(i => i))
        {
            ImGui.TextUnformatted(g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
            if (QuestItems.Find(g.Key) is { } info && ImGui.IsItemHovered()) UiText.TooltipWrapped(info.Description);
        }
        foreach (string injury in hero.Injuries) UiText.Colored(Bad, "Injury: " + injury);
        ImGui.Spacing();
        UiText.Disabled("Updates live while you build. Hover any rule, skill or item for its book wording.");
    }
}
