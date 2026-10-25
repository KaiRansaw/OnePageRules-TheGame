using System.Numerics;
using FDG.Quest;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Star Quest heroes (main menu > Heroes). Two views:
/// <list type="bullet">
/// <item><b>My Heroes</b> - a card for every saved hero: level, class, XP, credits. New / Edit / Delete.</item>
/// <item><b>Hero editor</b> - pick a faction's hero, name it, give it a class (stat bonuses), pick its
/// starter skills and weapon upgrades, and see the finished hero sheet update live. Save writes a
/// <c>.fdghero</c> file to heroes/GF Star Quest.</item>
/// </list>
/// A hero that has already played (XP, a level-up or a finished mission) can only be renamed here - its
/// build is locked so progress can't be lost by rebuilding it.
/// </summary>
public sealed class HeroBuilderScreen : IAppScreen
{
    public Action? OnBack;

    public const string GameType = "GF Star Quest";

    private enum EView { List, Edit }
    private EView _view = EView.List;

    // My Heroes
    private List<(string Path, QuestHero Hero)> _saved = new();
    private int _selected = -1;
    private bool _confirmDelete;

    // Editor
    private string? _editingPath;
    private QuestHero? _locked;              // a hero that has played: rename only
    private SqFaction? _faction;
    private SqHero? _base;
    private SqClass? _class;
    private readonly HashSet<string> _skills = new();
    // (package uid, section index) -> chosen option indices
    private readonly Dictionary<(string, int), List<int>> _picks = new();
    private string _name = "";
    private string _filter = "";
    private string _message = "";
    private bool _messageIsError;

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);

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
        if (_selected >= _saved.Count) _selected = _saved.Count - 1;
        _confirmDelete = false;
    }

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##HeroBuilder", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = screenW * 0.025f;
        float titlePx = screenH * 0.05f;
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.035f), U(TextBright),
            _view == EView.List ? "STAR QUEST HEROES" : _editingPath == null ? "NEW HERO" : "EDIT HERO");
        string sub = StarQuestData.LoadError ?? (_view == EView.List
            ? $"{_saved.Count} hero{(_saved.Count == 1 ? "" : "es")}  -  {HeroPaths.FolderFor(GameType)}"
            : "Pick a hero, give it a name and a class, then choose its starter skills and gear.");
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.035f + titlePx + 6f),
            U(StarQuestData.LoadError != null ? Bad : TextDim), sub);

        float top = screenH * 0.14f;
        float bottomBarH = screenH * 0.09f;
        float bodyH = screenH - top - bottomBarH;

        if (_view == EView.List) DrawList(margin, top, screenW - margin * 2f, bodyH);
        else DrawEditor(margin, top, screenW - margin * 2f, bodyH);

        DrawBottomBar(screenW, screenH, margin, bottomBarH);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    // ── My Heroes ─────────────────────────────────────────────────────────────────────────────────────

    private void DrawList(float x, float y, float w, float h)
    {
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##heroGrid", new Vector2(w, h));
        if (_saved.Count == 0)
        {
            ImGui.SetCursorPos(new Vector2(0, h * 0.4f));
            UiText.Disabled("No heroes yet. Click \"New Hero\" to make your first one.");
        }
        const int columns = 4;
        float gap = 12f;
        float cardW = (w - ImGui.GetStyle().ScrollbarSize - gap * (columns - 1)) / columns;
        float lh = ImGui.GetTextLineHeightWithSpacing();
        float cardH = lh * 6.2f;
        for (int i = 0; i < _saved.Count; i++)
        {
            ImGui.SetCursorPos(new Vector2(i % columns * (cardW + gap), i / columns * (cardH + gap)));
            DrawHeroCard(i, _saved[i].Hero, cardW, cardH);
        }
        ImGui.EndChild();
    }

    private void DrawHeroCard(int index, QuestHero hero, float w, float h)
    {
        Vector2 p0 = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##hero{index}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        if (clicked) { _selected = index; _confirmDelete = false; }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) StartEdit(index);

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 p1 = p0 + new Vector2(w, h);
        dl.AddRectFilled(p0, p1, U(hovered ? new Vector4(0.15f, 0.15f, 0.17f, 1f) : new Vector4(0.11f, 0.11f, 0.13f, 1f)), 6f);
        dl.AddRectFilled(p0, new Vector2(p0.X + 5f, p1.Y), U(Accent), 6f, ImDrawFlags.RoundCornersLeft);
        if (index == _selected) dl.AddRect(p0, p1, U(Good), 6f, ImDrawFlags.None, 2.5f);

        float lh = ImGui.GetTextLineHeightWithSpacing();
        float tx = p0.X + 14f, ty = p0.Y + 8f;
        dl.AddText(new Vector2(tx, ty), U(TextBright), Fit(hero.Name, w - 24f));
        dl.AddText(new Vector2(tx, ty + lh), U(Gold), $"Level {hero.Level}  {hero.Class}");
        dl.AddText(new Vector2(tx, ty + lh * 2), U(TextDim), Fit($"{hero.Faction} - {hero.BaseHeroName}", w - 24f));
        int next = hero.Level < QuestProgression.MaxLevel ? QuestProgression.XpForLevel[hero.Level + 1] : hero.Xp;
        dl.AddText(new Vector2(tx, ty + lh * 3), U(TextDim), $"XP {hero.Xp}/{next}    {hero.Credits}c    {hero.Points}pts");
        dl.AddText(new Vector2(tx, ty + lh * 4), U(TextDim),
            $"Q{hero.Quality}+ D{hero.Defense}+ Pow{hero.Power} Str{hero.Strength}+ Dex{hero.Dexterity}+ Wil{hero.Willpower}+");
        if (hero.Injuries.Count > 0)
            dl.AddText(new Vector2(tx, ty + lh * 5), U(Bad), Fit("Injured: " + string.Join(", ", hero.Injuries), w - 24f));
    }

    private static string Fit(string s, float width)
    {
        if (ImGui.CalcTextSize(s).X <= width) return s;
        while (s.Length > 3 && ImGui.CalcTextSize(s + "...").X > width) s = s[..^1];
        return s + "...";
    }

    // ── Bottom bar ────────────────────────────────────────────────────────────────────────────────────

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        Vector2 btn = new(screenW * 0.13f, barH * 0.55f);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput;

        ImGui.SetCursorPos(new Vector2(margin, by));
        if (_view == EView.List)
        {
            if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
            float rx = screenW - margin - btn.X;
            ImGui.SetCursorPos(new Vector2(rx, by));
            if (UiButton.Confirm("New Hero", btn)) StartNew();
            bool has = _selected >= 0 && _selected < _saved.Count;
            ImGui.BeginDisabled(!has);
            ImGui.SetCursorPos(new Vector2(rx - btn.X - 10f, by));
            if (UiButton.Navigate("Edit", btn)) StartEdit(_selected);
            ImGui.SetCursorPos(new Vector2(rx - (btn.X + 10f) * 2f, by));
            if (UiButton.Back(_confirmDelete ? "Really delete?" : "Delete", btn))
            {
                if (_confirmDelete && has) Delete(_selected);
                else _confirmDelete = true;
            }
            ImGui.EndDisabled();
        }
        else
        {
            if (UiButton.Back("Cancel", btn) || esc) { _view = EView.List; _message = ""; }
            ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X, by));
            string? problem = SaveProblem();
            ImGui.BeginDisabled(problem != null);
            if (UiButton.Confirm("Save Hero", btn)) Save();
            ImGui.EndDisabled();
            string line = problem ?? _message;
            if (line.Length > 0)
            {
                Vector2 ts = ImGui.CalcTextSize(line);
                ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
                UiText.Colored(problem != null ? TextDim : _messageIsError ? Bad : Good, line);
            }
        }
    }

    private void Delete(int index)
    {
        try { File.Delete(_saved[index].Path); }
        catch (Exception ex) { _message = "Couldn't delete: " + ex.Message; }
        Reload();
    }

    // ── Editor ────────────────────────────────────────────────────────────────────────────────────────

    private void StartNew()
    {
        _editingPath = null;
        _locked = null;
        _faction = null; _base = null; _class = null;
        _skills.Clear(); _picks.Clear();
        _name = ""; _filter = ""; _message = "";
        _view = EView.Edit;
    }

    private void StartEdit(int index)
    {
        if (index < 0 || index >= _saved.Count) return;
        StartNew();
        var (path, hero) = _saved[index];
        _editingPath = path;
        _name = hero.Name;
        _faction = StarQuestData.Factions.FirstOrDefault(f => f.Uid == hero.FactionUid);
        _base = _faction?.Heroes.FirstOrDefault(h => h.Id == hero.BaseHeroId);
        _class = StarQuestData.ClassById(hero.ClassId);
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
        QuestHero hero = StarQuestData.NewHero(_faction, _base);
        hero.Name = _name.Trim();
        if (_class != null) StarQuestData.ApplyClass(hero, _class);
        StarQuestData.ApplyUpgrades(hero, _faction, _base, ValidPicks());
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
            var (w, r, _) = StarQuestData.Loadout(_base, picks);
            if (!StarQuestData.HasTargets(section, w, r)) continue;
            foreach (int o in chosen.OrderBy(i => i))
                if (o >= 0 && o < section.Opts.Count) picks.Add((section, section.Opts[o]));
        }
        return picks;
    }

    private string? SaveProblem()
    {
        if (_name.Trim().Length == 0) return "Give the hero a name.";
        if (_locked != null) return null;
        if (_base == null) return "Pick a hero from the list on the left.";
        if (_class == null) return "Pick a class.";
        if (_skills.Count < _class.Skills) return $"Pick {_class.Skills - _skills.Count} more starter skill{(_class.Skills - _skills.Count == 1 ? "" : "s")}.";
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
            if (renamed) File.Delete(_editingPath!);
            _editingPath = path;
            Reload();
            _selected = _saved.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            _view = EView.List;
            _message = "";
        }
        catch (Exception ex)
        {
            _message = "Couldn't save: " + ex.Message;
            _messageIsError = true;
        }
    }

    private void DrawEditor(float x, float y, float w, float h)
    {
        float gap = 14f;
        float c1 = w * 0.24f, c2 = w * 0.40f, c3 = w - c1 - c2 - gap * 2f;

        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##pickHero", new Vector2(c1, h), ImGuiChildFlags.Borders);
        DrawHeroPicker();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + c1 + gap, y));
        ImGui.BeginChild("##choices", new Vector2(c2, h), ImGuiChildFlags.Borders);
        DrawChoices();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + c1 + c2 + gap * 2f, y));
        ImGui.BeginChild("##sheet", new Vector2(c3, h), ImGuiChildFlags.Borders);
        DrawSheet(Compose());
        ImGui.EndChild();
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

    private void DrawHeroPicker()
    {
        Header("1. HERO");
        if (_locked != null)
        {
            ImGui.PushTextWrapPos(0f);
            UiText.Colored(TextBright, $"{_locked.BaseHeroName}");
            UiText.Disabled(_locked.Faction);
            ImGui.Spacing();
            UiText.Disabled("This hero has already played, so its build is locked. You can still rename it.");
            ImGui.PopTextWrapPos();
            return;
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##filter", "Search factions or heroes...", ref _filter, 64);
        ImGui.BeginChild("##factionList");
        string f = _filter.Trim();

        // 53 factions in one flat list is a scroll with no landmarks, so they fold into Humanity, Chaos
        // and Xenos first. A search opens everything that matches; otherwise only the group holding the
        // faction already picked is open.
        foreach (string group in StarQuestData.FactionGroups)
        {
            var factions = StarQuestData.Factions
                .Where(fc => StarQuestData.GroupOf(fc) == group)
                .OrderBy(fc => fc.Faction, StringComparer.OrdinalIgnoreCase)
                .Select(fc => (Faction: fc, Heroes: Matching(fc, f)))
                .Where(x => x.Heroes.Count > 0)
                .ToList();
            if (factions.Count == 0) continue;

            ImGuiTreeNodeFlags groupFlags = ImGuiTreeNodeFlags.SpanAvailWidth;
            if (f.Length > 0 || (_faction != null && StarQuestData.GroupOf(_faction) == group))
                groupFlags |= ImGuiTreeNodeFlags.DefaultOpen;
            int heroCount = factions.Sum(x => x.Heroes.Count);
            if (!ImGui.TreeNodeEx($"{group.ToUpperInvariant()}  ({factions.Count} factions, {heroCount} heroes)##grp{group}", groupFlags))
                continue;

            foreach (var (fac, heroes) in factions)
            {
                ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth;
                if (f.Length > 0 || _faction == fac) flags |= ImGuiTreeNodeFlags.DefaultOpen;
                if (!ImGui.TreeNodeEx($"{fac.Faction}##{fac.Uid}", flags)) continue;
                foreach (SqHero hero in heroes)
                {
                    bool sel = _base == hero;
                    if (ImGui.Selectable($"  {hero.Name}##{fac.Uid}{hero.Id}", sel))
                    {
                        if (_base != hero) { _picks.Clear(); }
                        _faction = fac;
                        _base = hero;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted($"{hero.Name}  ({hero.Cost}pts)");
                        foreach (SqWeapon wpn in hero.Weapons) UiText.Disabled(wpn.ToQuest().Label());
                        UiText.Disabled(string.Join(", ", hero.Rules));
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize($"{hero.Cost}pts").X + ImGui.GetCursorPosX() - 4f);
                    UiText.Disabled($"{hero.Cost}pts");
                }
                ImGui.TreePop();
            }
            ImGui.TreePop();
        }
        ImGui.EndChild();
    }

    private void DrawChoices()
    {
        Header("2. NAME");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##heroName", "Hero name", ref _name, 40);

        if (_locked != null)
        {
            DrawNewSkillPicks(_locked);
            return;
        }

        Header("3. CLASS");
        float avail = ImGui.GetContentRegionAvail().X;
        float cardW = (avail - 8f) / 2f;
        float lh = ImGui.GetTextLineHeight();
        int i = 0;
        foreach (SqClass cls in StarQuestData.Classes)
        {
            if (i % 2 == 1) ImGui.SameLine(0f, 8f);
            DrawClassCard(cls, cardW, lh * 3.4f + 12f);
            i++;
        }

        if (_class != null)
        {
            Header($"4. STARTER SKILLS  (pick {_class.Skills})");
            SqSkill? feat = StarQuestData.ClassFeat(_class);
            if (feat != null)
            {
                ImGui.PushTextWrapPos(0f);
                UiText.Colored(Accent, $"{feat.Name} (class feature, always known)");
                UiText.Disabled(feat.Rule);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }
            foreach (SqSkill s in StarQuestData.ClassSkills(_class, 0))
            {
                bool on = _skills.Contains(s.Name);
                bool full = !on && _skills.Count >= _class.Skills;
                ImGui.BeginDisabled(full);
                if (ImGui.Checkbox($"{s.Name} ({StarQuestData.SkillStat(s)})##sk{s.Id}", ref on))
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

        if (_base != null)
        {
            Header("5. GEAR UPGRADES  (optional, adds points)");
            bool any = false;
            var sofar = new List<(SqSection, SqOption)>();
            foreach (var (key, section) in Sections())
            {
                any = true;
                bool multi = StarQuestData.IsMultiPick(section);
                var (lw, lr, _) = StarQuestData.Loadout(_base, sofar);
                if (!StarQuestData.HasTargets(section, lw, lr))
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
    }

    /// <summary>A hero that levelled up with "+1 Skill" picks its new skills here: its class's skills and the
    /// skills every class may learn, up to the tier its level has unlocked.</summary>
    private static void DrawNewSkillPicks(QuestHero hero)
    {
        int free = hero.SkillSlots - hero.Skills.Count;
        SqClass? cls = StarQuestData.ClassById(hero.ClassId);
        if (free <= 0 || cls == null) return;
        int tierCap = QuestProgression.TierUnlocked(hero.Level);
        Header($"NEW SKILLS  (pick {free})");
        UiText.Disabled($"Level {hero.Level} unlocks tier {tierCap} skills.");
        for (int tier = 0; tier <= tierCap; tier++)
        {
            var options = StarQuestData.ClassSkills(cls, tier).Concat(StarQuestData.DefaultSkills(tier))
                .Where(s => !hero.Skills.Contains(s.Name)).ToList();
            if (options.Count == 0) continue;
            UiText.Colored(TextBright, tier == 0 ? "Starter skills" : $"Tier {tier}");
            foreach (SqSkill s in options)
            {
                bool on = false;
                if (ImGui.Checkbox($"{s.Name} ({StarQuestData.SkillStat(s)})##new{s.Id}", ref on) && on && hero.Skills.Count < hero.SkillSlots)
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
        dl.AddText(p0 + new Vector2(8f, 6f), U(TextBright), Fit(title, w - 16f));
        dl.AddText(p0 + new Vector2(8f, 6f + lh + 2f), U(Gold), Fit(ClassBonusText(cls), w - 16f));
        dl.AddText(p0 + new Vector2(8f, 6f + (lh + 2f) * 2), U(TextDim), Fit(cls.Description ?? "", w - 16f));
        if (hovered && !string.IsNullOrEmpty(cls.Description))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextUnformatted(cls.Description);
            SqSkill? feat = StarQuestData.ClassFeat(cls);
            if (feat != null) UiText.Colored(Accent, $"{feat.Name}: {feat.Rule}");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
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

    private void DrawSheet(QuestHero? hero)
    {
        Header("HERO SHEET");
        if (hero == null)
        {
            UiText.Disabled("Pick a hero to see its sheet.");
            return;
        }
        ImGui.PushTextWrapPos(0f);
        UiText.Colored(TextBright, hero.Name.Length > 0 ? hero.Name : "(no name yet)");
        UiText.Colored(Gold, $"Level {hero.Level}  {(hero.Class.Length > 0 ? hero.Class : "(no class)")}");
        UiText.Disabled($"{hero.Faction} - {hero.BaseHeroName}");
        ImGui.PopTextWrapPos();
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
        UiText.Disabled($"{hero.Points} pts   -   {hero.Credits}c credits   -   XP {hero.Xp}");

        Header("SPECIAL RULES");
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(hero.Rules.Count > 0 ? string.Join(", ", hero.Rules) : "-");
        if (hero.Gear.Count > 0) UiText.Disabled("Gear: " + string.Join(", ", hero.Gear));
        ImGui.PopTextWrapPos();

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
                ImGui.TableNextColumn(); ImGui.PushTextWrapPos(0f); ImGui.TextUnformatted(wpn.Rules.Count > 0 ? string.Join(", ", wpn.Rules) : "-"); ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }

        Header("SKILLS");
        ImGui.PushTextWrapPos(0f);
        var feat = hero.ClassFeat.Length > 0 ? StarQuestData.SkillByName(hero.ClassFeat) : null;
        if (feat != null) { UiText.Colored(Accent, $"{feat.Name} (feature)"); UiText.Disabled(feat.Rule); }
        foreach (string name in hero.Skills)
        {
            SqSkill? s = StarQuestData.SkillByName(name);
            UiText.Colored(Accent, s != null ? $"{s.Name} ({StarQuestData.SkillStat(s)})" : name);
            if (s != null) UiText.Disabled(s.Rule);
        }
        if (feat == null && hero.Skills.Count == 0) UiText.Disabled("Pick a class to get skills.");
        ImGui.Spacing();
        UiText.Disabled("Common skills (every hero): Discover, Push, Scurry.");
        if (hero.Caster > 0)
            UiText.Disabled($"Casts spells from the {hero.Faction} spell list.");
        ImGui.PopTextWrapPos();

        if (hero.Items.Count > 0 || hero.Injuries.Count > 0)
        {
            Header("ITEMS & INJURIES");
            ImGui.PushTextWrapPos(0f);
            if (hero.Items.Count > 0) ImGui.TextUnformatted("Items: " + string.Join(", ", hero.Items));
            if (hero.Injuries.Count > 0) UiText.Colored(Bad, "Injuries: " + string.Join(", ", hero.Injuries));
            ImGui.PopTextWrapPos();
        }
    }
}
