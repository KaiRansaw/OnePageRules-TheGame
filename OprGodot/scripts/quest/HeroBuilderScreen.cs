using FDG;
using FDG.Quest;
using FdgRaylib.Models3D;
using FdgRaylib.Quest;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Quest;

/// <summary>
/// The Hero Builder, one per Quest game (Kai's "Hero Builder Mockup", 2026-11-12, every item kept): the live hero
/// sheet always on the left; on the right My Heroes (portrait cards, Edit / Shop / Delete), New / Edit Hero in four
/// steps (1 Hero &amp; Name, 2 Class, 3 Starter Skills, 4 Gear) with a line saying what's still missing, or the
/// Shop (a tab per hero, for-sale cards, what the hero carries). New for Godot: the hero's 3D look (picked under
/// the sheet in step 1), and Back going to the Quest game's screen. Logic: <see cref="HeroEditor"/>; drawing here is
/// rental.
/// </summary>
public partial class HeroBuilderScreen : Control, IBackGuard
{
    private enum View { List, Edit, Shop }

    public EGameSystem System { get; set; } = EGameSystem.GFStarQuest;

    /// <summary>
    /// Set by Quest Campaigns: open straight in the shop for this campaign's party only (Campaign Builder: heroes buy
    /// and sell before and after a chapter). Back leaves. The campaign's shop hands no money between heroes - the
    /// book's hand-over happens between missions, in the game.
    /// </summary>
    public IReadOnlyList<string>? CampaignParty { get; set; }
    public string CampaignName { get; set; } = "";
    private bool CampaignShop => CampaignParty != null;

    private HeroEditor _e = null!;
    private View _view = View.List;
    private int _step;
    private int _selected;
    private int _shopHero;
    private string _filter = "";
    private SqFaction? _browse;
    private List<string> _lockedOriginalSkills = new();

    private HBoxContainer _top = null!;
    private Control _left = null!, _right = null!;
    private Label _status = null!;
    private LookPreview? _preview;

    public override void _Ready()
    {
        _e = new HeroEditor(System) { OnlyHeroes = CampaignParty };
        if (CampaignShop) { _e.Reload(); _view = View.Shop; }
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        _top = new HBoxContainer();
        _top.AddThemeConstantOverride("separation", 12);
        root.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_top, 14, 8, 14, 8)));
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        root.AddChild(body);
        _left = new MarginContainer { CustomMinimumSize = new Vector2(430, 0) };
        body.AddChild(_left);
        _right = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(_right);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        root.AddChild(_status);
        Rebuild();
    }

    /// <summary>Back / Esc inside the editor or the shop returns to My Heroes; from My Heroes it leaves.</summary>
    public bool AllowBack()
    {
        if (_view == View.List || CampaignShop) return true;
        _view = View.List;
        _e.Reload();
        Rebuild();
        return false;
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    private void Rebuild()
    {
        foreach (Control holder in new[] { (Control)_top, _left, _right })
            foreach (Node c in holder.GetChildren()) { holder.RemoveChild(c); c.QueueFree(); }
        _preview = null;
        TopBar();
        _left.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_view == View.Shop ? Carries() : Sheet(SheetHero()), 14, 12, 14, 12)));
        Control right = _view switch
        {
            View.Edit => Editor(),
            View.Shop => ShopStock(),
            _ => HeroList(),
        };
        _right.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(right, 16, 12, 16, 12)));
    }

    private QuestHero? SheetHero() => _view switch
    {
        View.Edit => _e.Compose(),
        View.Shop => _e.Saved.Count > 0 ? _e.Saved[Math.Clamp(_shopHero, 0, _e.Saved.Count - 1)].Hero : null,
        _ => _selected >= 0 && _selected < _e.Saved.Count ? _e.Saved[_selected].Hero : null,
    };

    // ================================================================== top bar

    private void TopBar()
    {
        string back = _view == View.Edit ? "Cancel" : "Back";
        var b = Ui.Button(back, App.I.Back, _view == View.List ? "Back to the game screen (Esc)." : "Back to My Heroes, nothing saved (Esc).", "BackButton");
        b.Name = "Back";
        b.CustomMinimumSize = new Vector2(110, 44);
        _top.AddChild(b);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        string title = _view switch { View.Edit => _e.EditingPath == null ? "NEW HERO" : "EDIT HERO", View.Shop => CampaignShop ? "CAMPAIGN SHOP" : "HERO SHOP", _ => "HERO BUILDER" };
        t.AddChild(HudKit.Text(title, 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text(CampaignShop ? $"{CampaignName} - the party, between chapters"
            : $"{GameSystemInfo.DisplayName(System)} - {_e.Saved.Count} hero{(_e.Saved.Count == 1 ? "" : "es")}", 14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(t);

        if (_view == View.Edit)
        {
            var steps = new HBoxContainer();
            steps.AddThemeConstantOverride("separation", 4);
            string[] names = { "1 Hero & Name", _e.Locked != null ? "2 Class" : "2 Class", _e.Locked != null ? "3 New Skills" : "3 Starter Skills", "4 Gear" };
            for (int i = 0; i < names.Length; i++)
            {
                int step = i;
                bool off = _e.Locked != null && (i == 1 || i == 3);
                bool done = StepDone(i);
                var s = new Button { Name = $"Step{i + 1}", Text = (done && i != _step ? "✓ " : "") + names[i], ToggleMode = true, ButtonPressed = i == _step, Disabled = off, FocusMode = FocusModeEnum.None,
                    TooltipText = off ? "This hero has already played, so its build is locked." : names[i] };
                s.AddThemeFontSizeOverride("font_size", 15);
                if (i == _step) s.AddThemeColorOverride("font_color", Palette.Lime);
                s.Pressed += () => { _step = step; Rebuild(); };
                steps.AddChild(s);
            }
            _top.AddChild(steps);
        }
        _top.AddChild(Ui.Grow());
        if (_view == View.List)
        {
            var shop = Ui.Button("Shop", () => { _shopHero = Math.Max(0, _selected); _view = View.Shop; Rebuild(); },
                _e.Saved.Count == 0 ? "Make a hero first." : $"Buy, sell and hand things (and {_e.MoneyWord}) between your heroes.");
            shop.Name = "Shop";
            shop.Disabled = _e.Saved.Count == 0;
            _top.AddChild(shop);
            var add = Ui.Button("+ NEW HERO", () => { _e.StartNew(); _browse = null; _step = 0; _view = View.Edit; Rebuild(); },
                "Build a new hero in four steps: hero and name, class, starter skills, gear.", "CommitButton");
            add.Name = "NewHero";
            add.CustomMinimumSize = new Vector2(160, 44);
            _top.AddChild(add);
        }
        else if (_view == View.Edit)
        {
            bool last = _step == 3 || (_e.Locked != null && _step == 2);
            string? problem = _e.SaveProblem();
            if (last || problem == null)
            {
                var save = Ui.Button("SAVE HERO", Save, problem ?? "Save this hero.", "CommitButton");
                save.Name = "SaveHero";
                save.Disabled = problem != null;
                save.CustomMinimumSize = new Vector2(150, 44);
                _top.AddChild(save);
            }
            if (!last)
            {
                var next = Ui.Button("NEXT", () => { _step++; if (_e.Locked != null && _step == 1) _step = 2; Rebuild(); }, "The next step.", problem == null ? null : "CommitButton");
                next.Name = "Next";
                next.CustomMinimumSize = new Vector2(110, 44);
                _top.AddChild(next);
            }
        }
    }

    private bool StepDone(int i) => i switch
    {
        0 => _e.Base != null && _e.Name.Trim().Length > 0,
        1 => _e.Class != null,
        2 => _e.Class != null && _e.Skills.Count >= _e.Class.Skills,
        _ => false,
    };

    private void Save()
    {
        var (path, error) = _e.Save();
        if (error != null) { Say(error, Palette.WoundsRed); return; }
        _selected = _e.Saved.FindIndex(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        _view = View.List;
        Say($"Saved {_e.Saved[Math.Max(0, _selected)].Hero.Name}.", Palette.Lime);
        Rebuild();
    }

    // ================================================================== hero sheet (left)

    private Control Sheet(QuestHero? hero)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(col);
        if (hero == null)
        {
            col.AddChild(ForgeWidgets.Wrapped(_view == View.Edit ? "Pick a base hero (step 1) to see its sheet." : "Pick a hero to see its sheet.", 15, HudKit.Dim));
            return scroll;
        }
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(HudKit.Portrait(hero.Faction, hero.BaseHeroName, "person", 72, HudKit.Friendly, look: hero.Look ?? FDG.Quest.QuestArmy.QuestHeroLook(hero)));
        var t = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        t.AddThemeConstantOverride("separation", -2);
        t.AddChild(HudKit.Text(hero.Name.Length > 0 ? hero.Name.ToUpperInvariant() : "(NO NAME YET)", 21, HudKit.Fg, HudKit.Bold));
        t.AddChild(ForgeWidgets.Wrapped($"Level {hero.Level} {(hero.Class.Length > 0 ? hero.Class : "- no class yet")} - {hero.Faction}, {hero.BaseHeroName}", 13, HudKit.Dim));
        int next = hero.Level < QuestProgression.MaxLevel ? QuestProgression.XpForLevel[hero.Level + 1] : hero.Xp;
        t.AddChild(HudKit.Text($"XP {hero.Xp} / {next}   -   {hero.Credits}{_e.Money}   -   {hero.Points} pts", 14, Palette.PointsBlue, HudKit.Semi));
        head.AddChild(t);
        col.AddChild(head);

        QuestHero? before = _view == View.Edit && _e.Base != null && _e.Faction != null ? _e.Data.NewHero(_e.Faction, _e.Base) : null;
        var stats = new GridContainer { Columns = hero.Caster > 0 ? 8 : 7 };
        stats.AddThemeConstantOverride("h_separation", 4);
        void Stat(string label, string value, bool changed) => stats.AddChild(ForgeWidgets.StatChip(label, value, changed));
        Stat("QUA", $"{hero.Quality}+", before != null && before.Quality != hero.Quality);
        Stat("DEF", $"{hero.Defense}+", false);
        Stat("TOU", $"{hero.Tough}", false);
        Stat("POW", $"{hero.Power}", before != null && before.Power != hero.Power);
        Stat("STR", $"{hero.Strength}+", before != null && before.Strength != hero.Strength);
        Stat("DEX", $"{hero.Dexterity}+", before != null && before.Dexterity != hero.Dexterity);
        Stat("WIL", $"{hero.Willpower}+", before != null && before.Willpower != hero.Willpower);
        if (hero.Caster > 0) Stat("CASTER", $"{hero.Caster}", true);
        col.AddChild(stats);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        foreach (string h in new[] { "WEAPON", "RNG", "ATK", "SPECIAL" }) grid.AddChild(HudKit.Text(h, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        foreach (QuestWeapon w in hero.Weapons)
        {
            bool isNew = before != null && !before.Weapons.Any(o => o.Name == w.Name);
            Color c = isNew ? Palette.Lime : HudKit.Fg;
            grid.AddChild(HudKit.Text((w.Count > 1 ? $"{w.Count}x " : "") + w.Name + (isNew ? " (new)" : ""), 14, c, HudKit.Semi).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(120, 0); }));
            grid.AddChild(HudKit.Text(w.Range > 0 ? $"{w.Range}\"" : "-", 14, c, HudKit.Semi));
            grid.AddChild(HudKit.Text($"A{w.Attacks}", 14, c, HudKit.Semi));
            var spe = HudKit.Text(w.Rules.Count == 0 ? "-" : string.Join(", ", w.Rules), 13, Palette.RuleGold, HudKit.Semi);
            spe.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            spe.CustomMinimumSize = new Vector2(80, 0);
            if (w.Rules.Count > 0) { spe.MouseFilter = MouseFilterEnum.Stop; spe.TooltipText = string.Join("\n\n", w.Rules.Select(r => RuleTip(hero, r))); }
            grid.AddChild(spe);
        }
        col.AddChild(grid);

        col.AddChild(HudKit.Heading("Rules", 14));
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        foreach (string r in hero.Rules)
        {
            bool isNew = before != null && !before.Rules.Contains(r);
            chips.AddChild(HudKit.Chip(r + (isNew ? " (new)" : ""), isNew ? Palette.Lime : Palette.RuleGold, size: 14).Also(c => c.TooltipText = RuleTip(hero, r)));
        }
        foreach (string g in hero.Gear) chips.AddChild(HudKit.Chip(g, HudKit.Fg, size: 14).Also(c => c.TooltipText = g));
        col.AddChild(chips);

        col.AddChild(HudKit.Heading("Class feature and skills", 14));
        if (hero.ClassFeat.Length > 0 && _e.Data.SkillByName(hero.ClassFeat) is { } feat)
            col.AddChild(SkillLine(feat, "feature", HudKit.Fg));
        foreach (string name in hero.Skills)
        {
            if (_e.Data.SkillByName(name) is { } s) col.AddChild(SkillLine(s, QuestDataSet.SkillStat(s), Palette.Lime));
            else col.AddChild(HudKit.Text(name, 14, HudKit.Fg, HudKit.Semi));
        }
        if (hero.ClassFeat.Length == 0 && hero.Skills.Count == 0) col.AddChild(HudKit.Text("Pick a class to get skills.", 13, HudKit.Dim, HudKit.Semi));
        col.AddChild(ForgeWidgets.Wrapped("Common skills (every hero): Discover, Push, Scurry." + (hero.Caster > 0 ? $" Casts spells from the {hero.Faction} spell list." : ""), 12, HudKit.Dim));

        col.AddChild(HudKit.Heading("Items", 14));
        if (hero.Items.Count == 0) col.AddChild(HudKit.Text("None", 13, HudKit.Dim, HudKit.Semi));
        foreach (var g in hero.Items.GroupBy(i => i))
        {
            var l = HudKit.Text(g.Count() > 1 ? $"{ItemName(g.Key)} x{g.Count()}" : ItemName(g.Key), 14, HudKit.Fg, HudKit.Semi);
            if (QuestItems.Find(g.Key) is { } info) { l.MouseFilter = MouseFilterEnum.Stop; l.TooltipText = info.Description; }
            col.AddChild(l);
        }
        col.AddChild(HudKit.Heading("Injuries", 14));
        if (hero.Injuries.Count == 0) col.AddChild(HudKit.Text("None", 13, HudKit.Dim, HudKit.Semi));
        foreach (string inj in hero.Injuries) col.AddChild(HudKit.Text(inj, 14, Palette.WoundsRed, HudKit.Semi));

        if (_view == View.Edit && _step == 0) col.AddChild(LookBox(hero));
        col.AddChild(ForgeWidgets.Wrapped("Hover any rule, skill or item for the book's wording.", 12, HudKit.Dim));
        return scroll;
    }

    private Control SkillLine(SqSkill s, string tag, Color c)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = s.Rule };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(HudKit.Text(s.Name, 15, c, HudKit.Bold).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
        row.AddChild(HudKit.Text(tag, 12, HudKit.Dim, HudKit.Semi).Also(l => { l.MouseFilter = MouseFilterEnum.Ignore; l.SizeFlagsVertical = SizeFlags.ShrinkCenter; }));
        return row;
    }

    private string RuleTip(QuestHero hero, string rule)
    {
        SqFaction? faction = _e.Data.Factions.FirstOrDefault(f => f.Uid == hero.FactionUid);
        return FdgRaylib.Rendering.QuestRuleText.For(rule, System, faction) is { } text ? $"{rule}\n{text}" : rule;
    }

    private string ItemName(string name) => QuestItemSkin.Name(System, name);

    /// <summary>The hero's 3D look (NEW for Godot): which downloaded version of the base hero stands on the table.</summary>
    private Control LookBox(QuestHero hero)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("Look on the table", 14));
        col.AddChild(head);
        var files = ForgeWidgets.Models()?.FilesFor(hero.Faction, hero.BaseHeroName) ?? Array.Empty<string>();
        if (files.Count == 0)
        {
            col.AddChild(ForgeWidgets.Wrapped($"No 3D models downloaded for {hero.BaseHeroName} yet (Asset Lab > Download models). It shows as a token.", 13, Palette.HitsYellow));
            return col;
        }
        var pick = new OptionButton { Name = "HeroLook", FocusMode = FocusModeEnum.None, TooltipText = "Which downloaded version of the base hero this hero uses on the table." };
        for (int i = 0; i < files.Count; i++) pick.AddItem(ModelLooks.Label(files, files[i]) + (i == 0 ? "  (first version)" : ""));
        var current = ModelLooks.Folder(_e.Look);
        string shown = ModelLooks.Pick(files, current?.File) ?? files[0];
        pick.Selected = Math.Max(0, files.ToList().IndexOf(shown));
        pick.ItemSelected += i =>
        {
            _e.Look = i == 0 ? null : ModelLooks.FolderLook(hero.Faction, hero.BaseHeroName, files[(int)i]);
            _preview?.Show(files[(int)i]);
        };
        col.AddChild(pick);
        _preview = new LookPreview { CustomMinimumSize = new Vector2(380, 240) };
        col.AddChild(_preview);
        string first = shown;
        Callable.From(() => _preview?.Show(first)).CallDeferred();
        col.AddChild(HudKit.Text($"{files.Count} version{(files.Count == 1 ? "" : "s")} downloaded - turns slowly, 28mm person for scale", 12, HudKit.Dim, HudKit.Semi));
        return col;
    }

    // ================================================================== my heroes (right)

    private Control HeroList()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Heading("My heroes - click to view, double-click to edit", 15));
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 10);
        flow.AddThemeConstantOverride("v_separation", 10);
        if (_e.Saved.Count == 0) col.AddChild(ForgeWidgets.Wrapped("No heroes yet. Press + NEW HERO to make your first one.", 16, HudKit.Dim));
        for (int i = 0; i < _e.Saved.Count; i++)
        {
            int idx = i;
            var (path, h) = _e.Saved[i];
            QuestCampaign? camp = _e.CampaignOf(path);
            flow.AddChild(HeroCard(h, camp, i == _selected, () => { _selected = idx; Rebuild(); }, () => Edit(idx)));
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(flow);
        col.AddChild(scroll);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        bool has = _selected >= 0 && _selected < _e.Saved.Count;
        buttons.AddChild(Ui.Button("Edit", () => Edit(_selected), "Change this hero.").Also(b => { b.Name = "Edit"; b.Disabled = !has; }));
        buttons.AddChild(Ui.Button("Shop", () => { _shopHero = _selected; _view = View.Shop; Rebuild(); }, "Shop for this hero.").Also(b => { b.Disabled = !has; }));
        buttons.AddChild(Ui.Button("Delete", AskDelete, "Delete this hero (asks first).", "BackButton").Also(b => { b.Name = "Delete"; b.Disabled = !has; }));
        buttons.AddChild(HudKit.Text("Delete asks first, and warns when the hero is in a campaign.", 13, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        col.AddChild(buttons);
        return col;
    }

    private Control HeroCard(QuestHero h, QuestCampaign? camp, bool selected, Action click, Action doubleClick)
    {
        var b = new Button { Name = ForgeWidgets.SafeName("Hero_" + h.Name), CustomMinimumSize = new Vector2(200, 190), FocusMode = FocusModeEnum.None, TooltipText = h.Name };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 8 };
        if (selected) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 2);
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var pic = HudKit.Portrait(h.Faction, h.BaseHeroName, "person", 80, HudKit.Friendly, look: h.Look ?? FDG.Quest.QuestArmy.QuestHeroLook(h));
        pic.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        col.AddChild(pic);
        foreach (var (text, size, c) in new[] { (h.Name, 17, selected ? HudKit.Lime : HudKit.Fg), ($"Lv {h.Level} {h.Class}", 14, HudKit.Fg), ($"{h.Faction} - {h.BaseHeroName}", 12, HudKit.Dim) })
            col.AddChild(HudKit.Text(text, size, c, HudKit.Semi, HorizontalAlignment.Center).Also(l => { l.MouseFilter = MouseFilterEnum.Ignore; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.CustomMinimumSize = new Vector2(180, 0); }));
        col.AddChild(HudKit.Text(camp != null ? $"in campaign \"{camp.Name}\"" : "free", 12, camp != null ? Palette.HeroGold : HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center)
            .Also(l => { l.MouseFilter = MouseFilterEnum.Ignore; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.CustomMinimumSize = new Vector2(180, 0); }));
        b.AddChild(col);
        b.Pressed += click;
        b.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) doubleClick(); };
        return b;
    }

    private void Edit(int index)
    {
        if (index < 0 || index >= _e.Saved.Count) return;
        _e.StartEdit(index);
        _browse = _e.Faction;
        _lockedOriginalSkills = _e.Locked?.Skills.ToList() ?? new List<string>();
        _step = 0;
        _view = View.Edit;
        Rebuild();
    }

    private void AskDelete()
    {
        if (_selected < 0 || _selected >= _e.Saved.Count) return;
        var (path, hero) = _e.Saved[_selected];
        string text = $"Delete {hero.Name}? This can't be undone.";
        if (_e.CampaignOf(path) is { } camp) text += $"\n\n{hero.Name} is in the campaign \"{camp.Name}\". That campaign will be missing this hero.";
        var d = new ConfirmationDialog { Title = "Delete hero?", DialogText = text, OkButtonText = "Delete", CancelButtonText = "Cancel" };
        int idx = _selected;
        d.Confirmed += () =>
        {
            d.QueueFree();
            if (_e.Delete(idx) is { } err) Say(err, Palette.WoundsRed);
            else { Say($"Deleted {hero.Name}.", Palette.Lime); _selected = Math.Min(_selected, _e.Saved.Count - 1); }
            Rebuild();
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
    }

    // ================================================================== editor steps (right)

    private Control Editor()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        Control step = _step switch
        {
            1 => StepClass(),
            2 => _e.Locked != null ? StepNewSkills() : StepSkills(),
            3 => StepGear(),
            _ => StepHero(),
        };
        step.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(step);
        string? problem = _e.SaveProblem();
        bool last = _step == 3 || (_e.Locked != null && _step == 2);
        string line = problem != null ? "Still to do: " + problem : last ? "Ready - press SAVE HERO." : "Everything needed is done - SAVE HERO now, or go on to the optional steps.";
        var box = new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = problem != null ? Palette.HeroGold : Palette.Lime, Cut = 6, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 };
        col.AddChild(HudKit.Panel(box, HudKit.Text(line, 15, problem != null ? Palette.HeroGold : Palette.Lime, HudKit.Semi)).Also(p => p.Name = "Missing"));
        return col;
    }

    private Control StepHero()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        nameRow.AddChild(HudKit.Text("Name", 16, HudKit.Fg, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        var name = new LineEdit { Name = "HeroName", PlaceholderText = "type a name", Text = _e.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 40 };
        name.TextChanged += t => { _e.Name = t; RefreshLeftAndTop(); };
        nameRow.AddChild(name);
        col.AddChild(nameRow);

        if (_e.Locked != null)
        {
            col.AddChild(HudKit.Heading("Hero", 15));
            col.AddChild(HudKit.Text($"{_e.Locked.BaseHeroName} - {_e.Locked.Faction}", 16, HudKit.Fg, HudKit.Semi));
            col.AddChild(ForgeWidgets.Wrapped("This hero has already played a mission, so its build is locked. Renaming, its look and new skills still work.", 14, HudKit.Dim));
            return col;
        }

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        var facCol = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };
        facCol.AddThemeConstantOverride("separation", 4);
        var search = new LineEdit { Name = "HeroSearch", PlaceholderText = "Search factions or heroes...", Text = _filter };
        search.TextChanged += t => { _filter = t; Callable.From(RebuildKeepSearch).CallDeferred(); };
        facCol.AddChild(search);
        var facList = new VBoxContainer();
        facList.AddThemeConstantOverride("separation", 1);
        string f = _filter.Trim();
        foreach (string group in _e.Data.FactionGroups)
        {
            var factions = _e.Data.Factions.Where(fc => _e.Data.GroupOf(fc) == group && HeroEditor.Matching(fc, f).Count > 0).ToList();
            if (factions.Count == 0) continue;
            facList.AddChild(HudKit.Text(group.ToUpperInvariant(), 12, Palette.Header, HudKit.Tracked(HudKit.Bold, 2)));
            foreach (SqFaction fac in factions)
            {
                var fb = new Button { Name = ForgeWidgets.SafeName("Fac_" + fac.Faction), Text = fac.Faction, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
                if (_browse == fac) fb.AddThemeColorOverride("font_color", Palette.Lime);
                fb.Pressed += () => { _browse = fac; Rebuild(); };
                facList.AddChild(fb);
            }
        }
        var fs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        facList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fs.AddChild(facList);
        facCol.AddChild(fs);
        body.AddChild(facCol);

        var heroes = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heroes.AddThemeConstantOverride("separation", 8);
        _browse ??= _e.Faction ?? _e.Data.Factions.FirstOrDefault(fc => HeroEditor.Matching(fc, f).Count > 0);
        if (_browse == null) heroes.AddChild(HudKit.Text("Pick a faction on the left.", 15, HudKit.Dim, HudKit.Semi));
        else
        {
            SqFaction fac = _browse;
            heroes.AddChild(HudKit.Heading($"{fac.Faction} - base heroes", 15));
            var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 8);
            flow.AddThemeConstantOverride("v_separation", 8);
            foreach (SqHero h in HeroEditor.Matching(fac, f))
            {
                SqHero pick = h;
                Control card = ForgeWidgets.Card(fac.Faction, "Base_" + h.Name, h.Name, h.Cost, "", _e.Base == h, () => { _e.PickBase(fac, pick); Rebuild(); }, null);
                card.TooltipText = $"{h.Name} - {h.Cost} pts\n" + string.Join("\n", h.Weapons.Select(w => w.ToQuest().Label())) + (h.Rules.Count > 0 ? "\n" + string.Join(", ", h.Rules) : "");
                flow.AddChild(card);
            }
            var hs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            hs.AddChild(flow);
            heroes.AddChild(hs);
        }
        body.AddChild(heroes);
        col.AddChild(body);
        return col;
    }

    private void RebuildKeepSearch()
    {
        Rebuild();
        if (FindChild("HeroSearch", true, false) is LineEdit le) { le.GrabFocus(); le.CaretColumn = le.Text.Length; }
    }

    /// <summary>Typing the name updates the sheet and the step ticks without stealing the box's focus.</summary>
    private void RefreshLeftAndTop()
    {
        foreach (Control holder in new[] { (Control)_top, _left })
            foreach (Node c in holder.GetChildren()) { holder.RemoveChild(c); c.QueueFree(); }
        _preview = null;
        TopBar();
        _left.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(Sheet(SheetHero()), 14, 12, 14, 12)));
    }

    private Control StepClass()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(HudKit.Heading("Pick a class - hover for the full text", 15));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (SqClass cls in _e.Data.Classes)
        {
            SqClass pick = cls;
            bool sel = _e.Class == cls;
            var b = new Button { Name = ForgeWidgets.SafeName("Class_" + cls.Name), SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 92), FocusMode = FocusModeEnum.None };
            var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 };
            if (sel) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
            HudKit.StyleButton(b, box, HudKit.Fg, 15);
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);
            v.AddThemeConstantOverride("separation", 0);
            string title = string.IsNullOrEmpty(cls.BaseName) || cls.BaseName == cls.Name ? cls.Name : $"{cls.Name}  ({cls.BaseName})";
            v.AddChild(HudKit.Text(title, 17, sel ? Palette.Lime : HudKit.Fg, HudKit.Bold).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
            v.AddChild(HudKit.Text(HeroEditor.ClassBonusText(cls), 13, Palette.Lime, HudKit.Semi).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
            v.AddChild(HudKit.Text(cls.Description ?? "", 12, HudKit.Dim, HudKit.Semi).Also(l => { l.MouseFilter = MouseFilterEnum.Ignore; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.MaxLinesVisible = 2; }));
            b.AddChild(v);
            SqSkill? feat = _e.Data.ClassFeat(cls);
            b.TooltipText = (cls.Description ?? "") + (feat != null ? $"\n\nFeature - {feat.Name}: {feat.Rule}" : "");
            b.Pressed += () => { _e.PickClass(pick); Rebuild(); };
            grid.AddChild(b);
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(grid);
        col.AddChild(scroll);
        if (_e.Class != null && _e.Data.ClassFeat(_e.Class) is { } f)
            col.AddChild(ForgeWidgets.Wrapped($"Class feature - {f.Name}: {f.Rule}", 14, Palette.RuleGold));
        return col;
    }

    private Control StepSkills()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        if (_e.Class == null)
        {
            col.AddChild(ForgeWidgets.Wrapped("Pick a class first (step 2) - the class decides the skills.", 15, HudKit.Dim));
            return col;
        }
        col.AddChild(HudKit.Heading($"Pick {_e.Class.Skills} starter skill{(_e.Class.Skills == 1 ? "" : "s")} - {_e.Skills.Count} of {_e.Class.Skills} picked", 15));
        foreach (SqSkill s in _e.Data.ClassSkills(_e.Class, 0))
        {
            bool on = _e.Skills.Contains(s.Name);
            bool full = !on && _e.Skills.Count >= _e.Class.Skills;
            col.AddChild(SkillRow(s, on, full, () => { _e.ToggleSkill(s.Name); Rebuild(); }));
        }
        col.AddChild(ForgeWidgets.Wrapped("Full: the others grey out until you untick one. The test is what the hero rolls to use it.", 13, HudKit.Dim));
        return new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Also(sc => { col.SizeFlagsHorizontal = SizeFlags.ExpandFill; sc.AddChild(col); });
    }

    /// <summary>A played hero's level-up skill picks: its class's skills and the common ones, up to its tier.</summary>
    private Control StepNewSkills()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        QuestHero hero = _e.Locked!;
        int free = hero.SkillSlots - hero.Skills.Count;
        SqClass? cls = _e.Data.ClassById(hero.ClassId);
        int newPicks = hero.Skills.Count(s => !_lockedOriginalSkills.Contains(s));
        if ((free <= 0 && newPicks == 0) || cls == null)
        {
            col.AddChild(ForgeWidgets.Wrapped("No new skills to pick. A level-up that gives \"+1 Skill\" opens a pick here.", 15, HudKit.Dim));
            return col;
        }
        int tierCap = QuestProgression.TierUnlocked(hero.Level);
        col.AddChild(HudKit.Heading($"New skills - {free} to pick (level {hero.Level} unlocks tier {tierCap})", 15));
        for (int tier = 0; tier <= tierCap; tier++)
        {
            var options = _e.Data.ClassSkills(cls, tier).Concat(_e.Data.DefaultSkills(tier)).Where(s => !_lockedOriginalSkills.Contains(s.Name)).ToList();
            if (options.Count == 0) continue;
            col.AddChild(HudKit.Text(tier == 0 ? "Starter skills" : $"Tier {tier}", 14, Palette.Header, HudKit.Bold));
            foreach (SqSkill s in options)
            {
                bool on = hero.Skills.Contains(s.Name);
                col.AddChild(SkillRow(s, on, !on && free <= 0, () =>
                {
                    if (on) _e.UnlearnNewSkill(s.Name, _lockedOriginalSkills); else _e.LearnSkill(s.Name);
                    Rebuild();
                }));
            }
        }
        return new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Also(sc => { col.SizeFlagsHorizontal = SizeFlags.ExpandFill; sc.AddChild(col); });
    }

    private Control SkillRow(SqSkill s, bool on, bool full, Action toggle)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var tick = new CheckBox { Name = ForgeWidgets.SafeName("Skill_" + s.Name), ButtonPressed = on, Disabled = full, FocusMode = FocusModeEnum.None, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        tick.Pressed += toggle;
        row.AddChild(tick);
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(HudKit.Text(s.Name, 16, on ? Palette.Lime : HudKit.Fg, HudKit.Bold));
        head.AddChild(HudKit.Chip(QuestDataSet.SkillStat(s), Palette.RuleGold, size: 12));
        v.AddChild(head);
        v.AddChild(ForgeWidgets.Wrapped(s.Rule, 13, HudKit.Dim));
        row.AddChild(v);
        var box = new ChamferBox { Fill = on ? new Color(0.12f, 0.17f, 0.10f, 0.95f) : HudKit.GlassLight, Border = on ? HudKit.Lime : HudKit.Edge, Cut = 6, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 };
        var p = HudKit.Panel(box, row);
        if (full) p.Modulate = new Color(1, 1, 1, 0.55f);
        return p;
    }

    private Control StepGear()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        if (_e.Locked != null) { col.AddChild(ForgeWidgets.Wrapped("This hero has already played, so its gear is locked.", 15, HudKit.Dim)); return col; }
        if (_e.Base == null) { col.AddChild(ForgeWidgets.Wrapped("Pick a base hero first (step 1) - the hero decides the gear.", 15, HudKit.Dim)); return col; }
        col.AddChild(HudKit.Heading("Gear upgrades - optional, adds points", 15));
        var sections = _e.GearSections();
        if (sections.Count == 0) col.AddChild(HudKit.Text("This hero has no gear options.", 15, HudKit.Dim, HudKit.Semi));
        foreach (var (entry, usable, why) in sections)
        {
            var (key, section) = entry;
            bool multi = QuestDataSet.IsMultiPick(section);
            var g = new VBoxContainer();
            g.AddThemeConstantOverride("separation", 2);
            var head = new HBoxContainer();
            head.AddChild(HudKit.Text(section.Label, 16, Palette.RuleGold, HudKit.Bold).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; }));
            head.AddChild(HudKit.Text(multi ? "pick any" : "pick one", 13, HudKit.Dim, HudKit.Semi));
            g.AddChild(head);
            ButtonGroup? radio = multi ? null : new ButtonGroup { AllowUnpress = true };
            for (int o = 0; o < section.Opts.Count; o++)
            {
                int option = o;
                SqOption opt = section.Opts[o];
                var line = new HBoxContainer();
                bool on = _e.IsPicked(key, o);
                var box = new CheckBox { Name = ForgeWidgets.SafeName($"Gear_{section.Label}_{opt.Label}"), Text = opt.Label, ButtonPressed = on, ButtonGroup = radio, Disabled = !usable && !on,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, AutowrapMode = TextServer.AutowrapMode.WordSmart, TooltipText = opt.Label };
                box.AddThemeFontSizeOverride("font_size", 15);
                if (on) box.AddThemeColorOverride("font_color", Palette.Lime);
                box.Pressed += () => { _e.TogglePick(key, section, option); Rebuild(); };
                line.AddChild(box);
                line.AddChild(HudKit.Text($"+{opt.Cost ?? 0}", 15, Palette.PointsBlue, HudKit.Bold).Also(l => { l.CustomMinimumSize = new Vector2(44, 0); l.HorizontalAlignment = HorizontalAlignment.Right; }));
                g.AddChild(line);
            }
            if (!usable && why != null) g.AddChild(HudKit.Text(why, 13, HudKit.Dim, HudKit.Semi));
            col.AddChild(HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(g, 12, 8, 12, 8)));
        }
        return new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Also(sc => { col.SizeFlagsHorizontal = SizeFlags.ExpandFill; sc.AddChild(col); });
    }

    // ================================================================== shop

    private Control Carries()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        if (_e.Saved.Count == 0) return col;
        _shopHero = Math.Clamp(_shopHero, 0, _e.Saved.Count - 1);
        var (path, hero) = _e.Saved[_shopHero];
        col.AddChild(HudKit.Text($"{hero.Name.ToUpperInvariant()} CARRIES", 20, HudKit.Fg, HudKit.Bold));
        col.AddChild(HudKit.Text($"Level {hero.Level} - tier {QuestProgression.TierUnlocked(hero.Level)} unlocked (tiers 1 / 2 / 3 unlock at levels 2 / 5 / 9)", 13, HudKit.Dim, HudKit.Semi));
        var others = _e.Saved.Where(x => x.Path != path).ToList();
        if (hero.Items.Count == 0) col.AddChild(HudKit.Text("Nothing yet.", 14, HudKit.Dim, HudKit.Semi));
        foreach (var group in hero.Items.GroupBy(i => i).ToList())
        {
            string item = group.Key;
            QuestItemInfo? info = QuestItems.Find(item);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            box.AddChild(HudKit.Text(group.Count() > 1 ? $"{ItemName(item)} x{group.Count()}" : ItemName(item), 16, HudKit.Fg, HudKit.Bold).Also(l => { if (info != null) { l.MouseFilter = MouseFilterEnum.Stop; l.TooltipText = info.Description; } }));
            if (info != null) box.AddChild(ForgeWidgets.Wrapped(info.Description, 12, HudKit.Dim));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(Ui.Button(info != null ? $"Sell +{QuestItemSkin.Money(System, info.SellValue)}" : "Sell", () =>
            {
                if (QuestShop.Sell(hero, item)) SaveSheet(path, hero);
                Rebuild();
            }, info == null ? "This item has no price in this build, so it can only be handed over." : "Sell it back.").Also(b => { b.Name = ForgeWidgets.SafeName("Sell_" + item); b.Disabled = info == null; }));
            var give = new MenuButton { Text = "Give to...", Flat = false, Disabled = others.Count == 0, TooltipText = others.Count == 0 ? "There is no other hero to give it to." : "Hand it to another hero." };
            foreach (var o in others) give.GetPopup().AddItem(o.Hero.Name);
            give.GetPopup().IdPressed += id =>
            {
                var (otherPath, other) = others[(int)id];
                if (QuestShop.Give(hero, other, item)) { SaveSheet(path, hero); SaveSheet(otherPath, other); }
                Rebuild();
            };
            row.AddChild(give);
            box.AddChild(row);
            col.AddChild(HudKit.Panel(HudKit.PanelBox(6), HudKit.Pad(box, 10, 6, 10, 6)));
        }
        col.AddChild(HudKit.Heading(_e.MoneyWord, 14));
        var money = new HBoxContainer();
        money.AddThemeConstantOverride("separation", 8);
        money.AddChild(HudKit.Text($"{hero.Credits}{_e.Money}", 26, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(money);
        foreach (var (otherPath, other) in CampaignShop ? new() : others)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(HudKit.Text($"to {other.Name}:", 14, HudKit.Fg, HudKit.Semi).Also(l => { l.SizeFlagsVertical = SizeFlags.ShrinkCenter; l.CustomMinimumSize = new Vector2(120, 0); }));
            foreach (int amount in new[] { 5, 10 })
            {
                int a = amount;
                row.AddChild(Ui.Button($"Give {a}{_e.Money}", () =>
                {
                    if (QuestShop.GiveCredits(hero, other, a) > 0) { SaveSheet(path, hero); SaveSheet(otherPath, other); }
                    Rebuild();
                }, $"Hand {a}{_e.Money} to {other.Name}.").Also(b => b.Disabled = hero.Credits < a));
            }
            col.AddChild(row);
        }
        return new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill }.Also(sc => { col.SizeFlagsHorizontal = SizeFlags.ExpandFill; sc.AddChild(col); });
    }

    private Control ShopStock()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        if (_e.Saved.Count == 0) return col;
        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 4);
        for (int i = 0; i < _e.Saved.Count; i++)
        {
            int idx = i;
            var h = _e.Saved[i].Hero;
            var t = new Button { Name = ForgeWidgets.SafeName("ShopTab_" + h.Name), Text = $"{h.Name} - {h.Credits}{_e.Money}", ToggleMode = true, ButtonPressed = i == _shopHero, FocusMode = FocusModeEnum.None };
            if (i == _shopHero) t.AddThemeColorOverride("font_color", Palette.Lime);
            t.Pressed += () => { _shopHero = idx; Rebuild(); };
            tabs.AddChild(t);
        }
        col.AddChild(tabs);
        var (path, hero) = _e.Saved[_shopHero];
        col.AddChild(HudKit.Heading("For sale", 15));
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach ((QuestItemInfo item, string? whyNot) in QuestShop.Stock(hero, System))
        {
            QuestItemInfo it = item;
            var card = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeConstantOverride("separation", 4);
            var head = new HBoxContainer();
            head.AddChild(HudKit.Text(ItemName(item.Name), 16, whyNot == null ? HudKit.Fg : HudKit.Dim, HudKit.Bold).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; }));
            head.AddChild(HudKit.Text(QuestItemSkin.Money(System, item.Cost), 16, Palette.Lime, HudKit.Bold));
            card.AddChild(head);
            card.AddChild(ForgeWidgets.Wrapped(item.Description, 12, HudKit.Dim).Also(l => l.MaxLinesVisible = 3));
            if (whyNot != null) card.AddChild(ForgeWidgets.Wrapped(whyNot, 12, Palette.HeroGold));
            else card.AddChild(Ui.Button("Buy", () =>
            {
                if (QuestShop.Buy(hero, it, System)) { SaveSheet(path, hero); Say($"{hero.Name} bought {ItemName(it.Name)}.", Palette.Lime); }
                Rebuild();
            }, $"Buy it for {hero.Name}.", "CommitButton").Also(b => b.Name = ForgeWidgets.SafeName("Buy_" + it.Name)));
            var p = HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(card, 10, 8, 10, 8));
            p.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            p.TooltipText = item.Description;
            if (whyNot != null) p.Modulate = new Color(1, 1, 1, 0.6f);
            grid.AddChild(p);
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(grid);
        col.AddChild(scroll);
        col.AddChild(ForgeWidgets.Wrapped("Locked items stay visible so you can plan ahead.", 13, HudKit.Dim));
        return col;
    }

    private void SaveSheet(string path, QuestHero hero)
    {
        if (!_e.SaveSheet(path, hero)) Say($"Could not save {hero.Name} - is the file read-only?", Palette.WoundsRed);
    }

    // ================================================================== scripted runs

    /// <summary>"new", "name:Krrek", "faction:Alien Hives", "base:Grunt Veteran", "class:Berserker", "skill:Rage",
    /// "gear:Section|Option", "step:2", "save", "edit:0", "shop", "buy:Med Kit", "list".</summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        switch (verb)
        {
            case "new": _e.StartNew(); _browse = null; _step = 0; _view = View.Edit; break;
            case "name": _e.Name = arg; break;
            case "faction": _browse = _e.Data.Factions.FirstOrDefault(f => f.Faction.Equals(arg, StringComparison.OrdinalIgnoreCase)); break;
            case "base" when _browse?.Heroes.FirstOrDefault(h => h.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } b: _e.PickBase(_browse, b); break;
            case "class" when _e.Data.Classes.FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } cls: _e.PickClass(cls); _step = 1; break;
            case "skill": _e.ToggleSkill(arg); _step = 2; break;
            case "gear" when arg.Split('|') is { Length: 2 } parts:
                foreach (var (entry, _, _) in _e.GearSections())
                    if (entry.Section.Label.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
                    {
                        int o = entry.Section.Opts.FindIndex(x => x.Label.StartsWith(parts[1], StringComparison.OrdinalIgnoreCase));
                        if (o >= 0) _e.TogglePick(entry.Key, entry.Section, o);
                        break;
                    }
                _step = 3;
                break;
            case "step" when int.TryParse(arg, out int s): _step = Math.Clamp(s - 1, 0, 3); break;
            case "save": Save(); return;
            case "edit" when int.TryParse(arg, out int i): Edit(i); return;
            case "shop": _shopHero = Math.Max(0, _selected); _view = View.Shop; break;
            case "buy":
                if (_e.Saved.Count > 0)
                {
                    var (path, hero) = _e.Saved[_shopHero];
                    var item = QuestShop.Stock(hero, System).FirstOrDefault(x => x.Item.Name.Equals(arg, StringComparison.OrdinalIgnoreCase) && x.WhyNot == null).Item;
                    if (item != null && QuestShop.Buy(hero, item, System)) SaveSheet(path, hero);
                }
                break;
            case "list": _view = View.List; _e.Reload(); break;
        }
        Rebuild();
    }
}
