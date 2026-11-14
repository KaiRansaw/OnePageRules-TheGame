using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// "Match models" (Kai, 2026-11-12, from the mockup he reviewed): give the catalogue models no OPR unit uses yet to a
/// unit by hand, and take wrong automatic matches away.
///
/// <para>
/// Left: a Warhammer 40k faction (<see cref="CatalogueFactions"/>), a search box and that faction's unused models
/// ("Show models already used" lists the rest too, to move one). Middle: the clicked model, fetched and turning on its
/// base beside a 28mm person. Right: an army, its units (no model yet = gold, at the top), and what the picked unit
/// already has, each with Remove. The bar at the bottom says what goes where; Space uses it, W / S step through the
/// models.
/// </para>
///
/// <para>
/// A match is saved to <see cref="ModelMatches"/> straight away and the model is saved into the unit's folder, so it
/// shows on the table at once. Remove takes the model off the unit and moves its file to UnitModels/_Dropped (never
/// deleted). "Save matches for everyone" writes the file and says to tell Claude, who ships it in the game.
/// </para>
/// </summary>
public partial class MatchModelsScreen : Control, IOwnBackdrop
{
    private ForceOrgCatalog? _cat;
    private UnitModelMap _map = null!;
    private ModelMatches _matches = null!;
    private SortedDictionary<string, List<string>> _books = null!;
    private Dictionary<string, SortedDictionary<string, List<string>>> _otherGames = new();
    private SortedDictionary<string, List<ForceOrgCatalog.Model>> _factions = new();
    private string _root = "", _matchesPath = "";
    private string? _startBook;

    private Node3D _world = null!, _stage = null!;
    private Camera3D _camera = null!;
    private OptionButton _faction = null!, _book = null!;
    private LineEdit _search = null!;
    private CheckBox _showUsed = null!;
    private ItemList _models = null!, _units = null!;
    private VBoxContainer _has = null!;
    private Label _count = null!, _title = null!, _info = null!, _loading = null!, _target = null!, _hasTitle = null!, _state = null!;
    private Button _use = null!;

    private readonly List<ForceOrgCatalog.Model> _shown = new();
    private readonly List<string> _unitRows = new();
    private Dictionary<int, List<string>> _usedBy = new();
    private Dictionary<int, string> _savedById = new();
    private readonly Dictionary<int, ForceOrgAssembler.Result> _previews = new();
    private readonly List<int> _previewOrder = new();
    private ModelDownloader? _dl;
    private ForceOrgCatalog.Model? _model;
    private string? _unit;
    private bool _busy;
    private float _spin, _distance = 6, _focusY = 0.8f;

    /// <summary>Where it starts: the army the Asset Lab's filter names, if any.</summary>
    public string? StartBook { set => _startBook = value; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _root = UnitModelPaths.Root;
        _matchesPath = ModelMatches.LocalPath(_root);
        _matches = ModelMatches.Load(_matchesPath);
        _map = UnitModelMap.Load(matchesRoot: _root);
        _books = GfBookUnits.LoadAll();
        _otherGames = GfBookUnits.LoadOtherGames();
        if (_books.Count == 0) foreach (string b in _map.Books) _books[b] = new();
        // Both catalogues as one: ForceOrg's 40k models for Grimdark Future, Stormvault's Age of Sigmar ones for AoF.
        _cat = ForceOrgCatalog.LoadAll();
        if (_cat != null) _factions = CatalogueFactions.ByFaction(_cat);
        _dl = new ModelDownloader();

        _world = new Node3D { Name = "MatchWorld" };
        AddChild(_world);
        TableLook.AddLighting(_world);
        TableLook.AddBoard(_world, 72, 48);
        _stage = new Node3D { Name = "Stage" };
        _world.AddChild(_stage);
        _camera = new Camera3D { Fov = 40, Current = true, Far = 2000, Near = 0.05f };
        _world.AddChild(_camera);
        _camera.MakeCurrent();
        Person();
        IndexSaved();
        BuildHud();
        ApplyCamera();
    }

    public override void _ExitTree()
    {
        _dl?.Dispose();
        foreach (var r in _previews.Values) r.Root?.QueueFree();
    }

    // ================================================================== HUD

    private void BuildHud()
    {
        // ---- left: the models
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back to the Asset Lab (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("MATCH MODELS", 32, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("Give a model to a unit, or take one away", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(t);
        left.AddChild(head);

        _faction = new OptionButton { Name = "Faction", FocusMode = FocusModeEnum.None, TooltipText = "The Warhammer faction whose models to list: 40k factions for Grimdark Future, Age of Sigmar ones for Age of Fantasy." };
        foreach (var (f, list) in _factions) _faction.AddItem($"{f}   ({list.Count})");
        _faction.ItemSelected += _ => FillModels();
        left.AddChild(_faction);
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search this faction (e.g. Hormagaunt)", ClearButtonEnabled = true };
        _search.TextChanged += _ => FillModels();
        left.AddChild(_search);
        _showUsed = new CheckBox { Name = "ShowUsed", Text = "Show models already used", FocusMode = FocusModeEnum.None, TooltipText = "List the models some unit already has too, to give one to another unit as well." };
        _showUsed.Toggled += _ => FillModels();
        left.AddChild(_showUsed);
        _models = new ItemList { Name = "Models", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300), FocusMode = FocusModeEnum.None, AllowSearch = false };
        _models.ItemSelected += i => { if (i >= 0 && i < _shown.Count) Select(_shown[(int)i]); };
        left.AddChild(_models);
        _count = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        left.AddChild(_count);
        if (_cat == null)
        {
            var none = HudKit.Text("No catalogue yet: open Download models in the Asset Lab - choose forceorg-models.json (Grimdark Future) or press Get Age of Fantasy models (Stormvault) first.", 15, HudKit.Gold, HudKit.Semi);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            left.AddChild(none);
        }
        Side(left, true);

        // ---- right: the army and the unit
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        right.AddChild(HudKit.Heading("Army", 15));
        _book = new OptionButton { Name = "Book", FocusMode = FocusModeEnum.None, TooltipText = "The army whose units to match. Firefight and Star Quest use Grimdark Future's models; AoF: Skirmish and AoF: Quest use Age of Fantasy's." };
        foreach (string b in _books.Keys) _book.AddItem(b);
        _book.ItemSelected += _ => { _unit = null; FillUnits(); OpenPairedFaction(); };
        right.AddChild(_book);
        var key = HudKit.Text("Gold = no model yet", 14, HudKit.Gold, HudKit.Semi);
        right.AddChild(key);
        _units = new ItemList { Name = "Units", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 260), FocusMode = FocusModeEnum.None, AllowSearch = false };
        _units.ItemSelected += i => { if (i >= 0 && i < _unitRows.Count) { _unit = _unitRows[(int)i]; FillHas(); UpdateTarget(); } };
        right.AddChild(_units);
        _hasTitle = HudKit.Text("This unit has", 16, HudKit.Teal, HudKit.Bold);
        right.AddChild(_hasTitle);
        _has = new VBoxContainer { Name = "Has" };
        _has.AddThemeConstantOverride("separation", 2);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 220), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _has.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_has);
        right.AddChild(scroll);
        var share = Btn("SaveMatches", "Save matches for everyone", "Saves your matches to one small file (no models in it). Tell Claude when it's saved: it goes into the game, so friends' downloads use the same matches.", SaveForEveryone);
        share.CustomMinimumSize = new Vector2(0, 40);
        right.AddChild(share);
        _state = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _state.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_state);
        Side(right, false);

        // ---- top: what is showing
        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 4);
        _title = HudKit.Text("Pick a model on the left", 24, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
        _info = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.CustomMinimumSize = new Vector2(560, 0);
        info.AddChild(_title);
        info.AddChild(_info);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(info, 16, 10, 16, 10)), LayoutPreset.CenterTop, new Vector2(0, 16));

        // ---- bottom: use it
        var bar = new VBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        _target = HudKit.Text("", 18, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
        _target.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _target.CustomMinimumSize = new Vector2(620, 0);
        bar.AddChild(_target);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        var prev = Btn("Prev", "Previous (S)", "The model above in the list.", () => Step(-1));
        prev.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        prev.CustomMinimumSize = new Vector2(140, 40);
        row.AddChild(prev);
        _use = Btn("UseForUnit", "Use for this unit (Space)", "Give this model to the unit picked on the right, and save it into that unit's folder.", () => _ = UseSelected());
        _use.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _use.CustomMinimumSize = new Vector2(260, 40);
        row.AddChild(_use);
        var next = Btn("Next", "Next (W)", "The model below in the list.", () => Step(1));
        next.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        next.CustomMinimumSize = new Vector2(140, 40);
        row.AddChild(next);
        bar.AddChild(row);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(bar, 18, 12, 18, 12)), LayoutPreset.CenterBottom, new Vector2(0, -16));

        _loading = HudKit.Text("Fetching...", 28, HudKit.Lime, HudKit.Bold, HorizontalAlignment.Center);
        _loading.Visible = false;
        HudKit.Place(this, _loading, LayoutPreset.Center, Vector2.Zero);

        int start = _startBook == null ? -1 : _books.Keys.ToList().FindIndex(b => b.Equals(_startBook, StringComparison.OrdinalIgnoreCase));
        if (start < 0) start = Math.Max(0, _books.Keys.ToList().IndexOf("Alien Hives"));
        if (_book.ItemCount > 0) _book.Select(start);
        FillModels();
        FillUnits();
        UpdateTarget();
    }

    private void Side(Control content, bool left)
    {
        var panel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(content, 16, 14, 16, 14));
        panel.AnchorTop = 0; panel.AnchorBottom = 1;
        panel.AnchorLeft = left ? 0 : 1; panel.AnchorRight = left ? 0 : 1;
        panel.OffsetTop = 16; panel.OffsetBottom = -16;
        panel.OffsetLeft = left ? 16 : -456; panel.OffsetRight = left ? 456 : -16;
        AddChild(panel);
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private string Book => _book.ItemCount == 0 ? "" : _book.GetItemText(_book.Selected);

    private string? Faction => _faction.ItemCount == 0 ? null : _factions.Keys.ElementAt(_faction.Selected);

    // ================================================================== lists

    /// <summary>Which units use each model, after the hand matches ("Alien Hives: Assault Grunts").</summary>
    private void IndexUsed()
    {
        _usedBy = new();
        foreach (var e in _map.Units)
            foreach (var m in e.Models)
            {
                if (!_usedBy.TryGetValue(m.Id, out var list)) _usedBy[m.Id] = list = new();
                list.Add($"{e.Book}: {e.Unit}");
            }
    }

    /// <summary>One saved file per model id (any unit's), to copy from instead of fetching again.</summary>
    private void IndexSaved()
    {
        _savedById = new();
        if (!Directory.Exists(_root)) return;
        foreach (string f in UnitModelPaths.Browse(_root))
        {
            string rel = Path.GetRelativePath(_root, f);
            if (rel.StartsWith('_') || !f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;
            string stem = Path.GetFileNameWithoutExtension(f);
            int open = stem.LastIndexOf(" (", StringComparison.Ordinal);
            if (open >= 0 && stem.EndsWith(')') && int.TryParse(stem[(open + 2)..^1], out int id)) _savedById.TryAdd(id, f);
        }
    }

    private void FillModels()
    {
        IndexUsed();
        _models.Clear();
        _shown.Clear();
        if (Faction is not { } f) { _count.Text = ""; return; }
        string q = _search.Text.Trim();
        int unused = 0;
        foreach (var m in _factions[f])
        {
            bool used = _usedBy.TryGetValue(m.Id, out var by);
            if (!used) unused++;
            if (used && !_showUsed.ButtonPressed) continue;
            if (q.Length > 0 && !m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !m.Id.ToString().Contains(q)) continue;
            string text = $"{m.Name}   #{m.Id}";
            if (used) text += $"   - {by![0]}{(by.Count > 1 ? $" (+{by.Count - 1})" : "")}";
            int i = _models.AddItem(text);
            if (used) _models.SetItemCustomFgColor(i, HudKit.Dim);
            if (used) _models.SetItemTooltip(i, "Used by:\n" + string.Join("\n", by!));
            _shown.Add(m);
        }
        _count.Text = $"{_shown.Count:N0} shown  -  {unused:N0} of {_factions[f].Count:N0} unused";
        int sel = _model == null ? -1 : _shown.IndexOf(_model);
        if (sel >= 0) { _models.Select(sel); _models.EnsureCurrentIsVisible(); }
    }

    private List<UnitModelMap.ModelRef> ModelsOf(string unit) =>
        _map.Units.FirstOrDefault(e => e.Book == Book && e.Unit == unit)?.Models ?? new();

    private void FillUnits()
    {
        _units.Clear();
        _unitRows.Clear();
        var names = new List<string>(_books.TryGetValue(Book, out var list) ? list : new());
        foreach (var e in _map.ForBook(Book)) if (!names.Contains(e.Unit)) names.Add(e.Unit); // a unit only the list has
        // No model yet first, then the rest, each A-Z.
        foreach (string u in names.OrderBy(n => ModelsOf(n).Count > 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            int n = ModelsOf(u).Count;
            int i = _units.AddItem(n == 0 ? u : $"{u}   ({n})");
            if (n == 0) _units.SetItemCustomFgColor(i, HudKit.Gold);
            _unitRows.Add(u);
        }
        int sel = _unit == null ? -1 : _unitRows.IndexOf(_unit);
        if (sel >= 0) { _units.Select(sel); _units.EnsureCurrentIsVisible(); }
        else _unit = null;
        FillHas();
    }

    private void FillHas()
    {
        foreach (Node c in _has.GetChildren()) { _has.RemoveChild(c); c.QueueFree(); }
        if (_unit == null) { _hasTitle.Text = "This unit has"; _has.AddChild(HudKit.Text("Pick a unit above.", 14, HudKit.Dim, HudKit.Semi)); return; }
        var models = ModelsOf(_unit);
        _hasTitle.Text = $"{_unit} has {models.Count}";
        if (models.Count == 0) _has.AddChild(HudKit.Text("No model yet.", 14, HudKit.Gold, HudKit.Semi));
        foreach (var m in models)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            bool have = File.Exists(UnitModelMap.FileFor(_root, Book, _unit, m));
            var name = new Button { Text = $"{m.Name}  #{m.Id}{(have ? "  - downloaded" : "")}", Flat = true, FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, ClipText = true, TooltipText = "Show this model." };
            int id = m.Id;
            name.Pressed += () => { if (_cat != null && _cat.ById.TryGetValue(id, out var cm)) Select(cm); };
            row.AddChild(name);
            var rm = new Button { Name = $"Remove{id}", Text = "Remove", FocusMode = FocusModeEnum.None, TooltipText = "Take this model away from the unit. Its file moves to UnitModels/_Dropped." };
            rm.AddThemeColorOverride("font_color", Palette.WoundsRed);
            rm.Pressed += () => RemoveFromUnit(id);
            row.AddChild(rm);
            _has.AddChild(row);
        }
    }

    private void UpdateTarget()
    {
        string model = _model == null ? "Pick a model on the left" : $"{_model.Name}  #{_model.Id}";
        string unit = _unit == null ? "pick a unit on the right" : $"{Book}: {_unit}";
        bool already = _model != null && _unit != null && ModelsOf(_unit).Any(m => m.Id == _model.Id);
        _target.Text = already ? $"{model}  is already one of  {unit}'s models" : $"{model}   ->   {unit}";
        _use.Disabled = _model == null || _unit == null || already || _busy;
    }

    private void Step(int by)
    {
        if (_shown.Count == 0) return;
        int i = _model == null ? -1 : _shown.IndexOf(_model);
        i = Math.Clamp(i + by, 0, _shown.Count - 1);
        _models.Select(i);
        _models.EnsureCurrentIsVisible();
        Select(_shown[i]);
    }

    // ================================================================== matching

    private string Notes(UnitModelMap.ModelRef m, string unit) => $"ForceOrg #{m.Id} \"{m.Name}\" for {Book} - {unit} (matched by hand)";

    /// <summary>Gives the shown model to the picked unit, saves the match and the model's file, and moves on.</summary>
    public async Task UseSelected()
    {
        if (_model == null || _unit == null || _busy) return;
        var model = _model;
        string unit = _unit, book = Book;
        var mref = new UnitModelMap.ModelRef { Id = model.Id, Name = model.Name };
        if (ModelsOf(unit).Any(m => m.Id == model.Id)) return;
        _busy = true;
        UpdateTarget();

        _matches.Assign(book, unit, mref, GfBookUnits.AliasesFor(book, unit, _otherGames)); // shows in Firefight / Star Quest too
        string? err = SaveMatches();
        _map = UnitModelMap.Load(matchesRoot: _root);

        // Save the model into the unit's folder, so the table shows it now.
        string dest = UnitModelMap.FileFor(_root, book, unit, mref);
        string saved = "";
        if (!File.Exists(dest))
        {
            if (_savedById.TryGetValue(model.Id, out string? from) && File.Exists(from))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    FileLinks.LinkOrCopy(from, dest);
                    var s = UnitModelSettings.Load(from) ?? new UnitModelSettings();
                    s.Notes = Notes(mref, unit);
                    s.Save(dest);
                    saved = "saved (shared with the copy already downloaded)";
                }
                catch (Exception e) { saved = "could not save the file: " + e.Message; }
            }
            else if (await Assembled(model) is { } r)
            {
                saved = ForceOrgAssembler.Save(r, dest, Notes(mref, unit)) is { } e ? "could not save the file: " + e : "downloaded";
            }
            else saved = "not downloaded (could not fetch it) - Download models will get it";
            if (File.Exists(dest)) _savedById.TryAdd(model.Id, dest);
        }

        _busy = false;
        Status(err ?? $"{model.Name} #{model.Id} -> {book}: {unit} - {saved}.", err == null ? HudKit.Lime : Palette.WoundsRed);
        // On to the next unused model (the list closes up over the one just used).
        int at = _shown.IndexOf(model);
        FillModels();
        FillUnits();
        if (_shown.Count > 0 && !_shown.Contains(model)) { _models.Select(Math.Min(Math.Max(at, 0), _shown.Count - 1)); _models.EnsureCurrentIsVisible(); Select(_shown[Math.Min(Math.Max(at, 0), _shown.Count - 1)]); }
        else UpdateTarget();
    }

    /// <summary>Takes a model away from the picked unit; its file (if downloaded) moves to UnitModels/_Dropped.</summary>
    public void RemoveFromUnit(int id)
    {
        if (_unit == null) return;
        var mref = ModelsOf(_unit).FirstOrDefault(m => m.Id == id);
        if (mref == null) return; // not one of this unit's
        _matches.Unassign(Book, _unit, id);
        string? err = SaveMatches();
        _map = UnitModelMap.Load(matchesRoot: _root);

        string moved = "";
        if (mref != null)
        {
            string file = UnitModelMap.FileFor(_root, Book, _unit, mref);
            if (File.Exists(file))
            {
                try
                {
                    string to = Path.Combine(_root, "_Dropped", UnitModelMap.SafeName(Book), UnitModelMap.SafeName(_unit), Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(file, to);
                    string side = file + UnitModelSettings.Suffix;
                    if (File.Exists(side)) { if (File.Exists(to + UnitModelSettings.Suffix)) File.Delete(to + UnitModelSettings.Suffix); File.Move(side, to + UnitModelSettings.Suffix); }
                    if (_savedById.TryGetValue(id, out string? s) && s == file) _savedById.Remove(id);
                    IndexSaved();
                    moved = " - its file moved to UnitModels/_Dropped";
                }
                catch (Exception e) { moved = " - could not move its file: " + e.Message; }
            }
        }
        Status(err ?? $"#{id} {mref?.Name} taken off {Book}: {_unit}{moved}.", err == null ? HudKit.Lime : Palette.WoundsRed);
        FillModels();
        FillUnits();
        UpdateTarget();
    }

    private string? SaveMatches()
    {
        try { _matches.Save(_matchesPath); return null; }
        catch (Exception e) { return "Could not save the match: " + e.Message; }
    }

    public void SaveForEveryone()
    {
        if (SaveMatches() is { } err) { Status(err, Palette.WoundsRed); return; }
        int units = _matches.Units.Count, adds = _matches.Units.Sum(u => u.Add.Count), removes = _matches.Units.Sum(u => u.Remove.Count);
        Status(_matches.Changes == 0
            ? "No matches made yet - nothing to share."
            : $"Saved {adds} added and {removes} removed model(s) across {units} unit(s) to UnitModels/{ModelMatches.FileName}. Tell Claude - it goes into the game for everyone.",
            HudKit.Lime);
    }

    private void Status(string text, Color colour)
    {
        _state.Text = text;
        _state.Modulate = colour;
    }

    // ================================================================== preview

    private void Select(ForceOrgCatalog.Model m)
    {
        _model = m;
        int i = _shown.IndexOf(m);
        if (i >= 0 && !_models.IsSelected(i)) { _models.Select(i); _models.EnsureCurrentIsVisible(); }
        UpdateTarget();
        _ = Preview(m);
    }

    /// <summary>The model put together from its fetched parts (kept for the last few), or null if it can't be.</summary>
    private async Task<ForceOrgAssembler.Result?> Assembled(ForceOrgCatalog.Model model)
    {
        if (_previews.TryGetValue(model.Id, out var have)) return have;
        Dictionary<string, string> files;
        List<string> failed;
        _loading.Visible = true;
        try { (files, failed) = await _dl!.FetchModelAsync(model, CancellationToken.None); }
        catch (Exception e) { _loading.Visible = false; _info.Text += "\nCould not fetch: " + e.Message; return null; }
        _loading.Visible = false;
        var r = ForceOrgAssembler.Assemble(model, files);
        if (r.Root == null) { _info.Text += "\nCould not show it: " + (r.Error ?? string.Join("; ", failed)); return null; }
        _previews[model.Id] = r;
        _previewOrder.Add(model.Id);
        while (_previewOrder.Count > 8)
        {
            int old = _previewOrder[0];
            _previewOrder.RemoveAt(0);
            if (old != _model?.Id && _previews.Remove(old, out var gone)) gone.Root?.QueueFree();
        }
        return r;
    }

    private async Task Preview(ForceOrgCatalog.Model m)
    {
        _title.Text = $"{m.Name}  #{m.Id}";
        string faction = _cat == null ? "" : CatalogueFactions.For(_cat, m.Group);
        _info.Text = _usedBy.TryGetValue(m.Id, out var by)
            ? $"{faction}  -  used by {string.Join(", ", by.Take(3))}{(by.Count > 3 ? $" and {by.Count - 3} more" : "")}"
            : $"{faction}  -  no unit uses it yet";
        Clear();
        if (_savedById.TryGetValue(m.Id, out string? saved) && File.Exists(saved))
        {
            // Downloaded for some unit already: show that file, turned the way it was set.
            LoadedAsset a = AssetLoader.Load(saved);
            if (a.Model != null) Show(a.Model, UnitModelSettings.Load(saved) ?? new UnitModelSettings { HeightMm = 0 }, $"{a.Triangles:N0} triangles - already downloaded");
            return;
        }
        var r = await Assembled(m);
        if (r == null || _model?.Id != m.Id) return; // clicked on to another one meanwhile
        var s = new UnitModelSettings { HeightMm = 0, BaseMm = r.BaseMm ?? 25, YawDegrees = ModelDownloadDefaults.Load().YawDegrees, CentreOnBase = false };
        Show(r.Root!, s, $"{r.Triangles:N0} triangles, {(r.BaseMm ?? 25):0.#} mm base");
    }

    private void Clear()
    {
        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
    }

    private void Show(Node3D source, UnitModelSettings s, string line)
    {
        Clear();
        var holder = new Node3D();
        _stage.AddChild(holder);
        if (s.BaseMm > 0) TableLook.AddBase(holder, s.BaseMm / UnitModelSettings.MmPerInch / 2, HudKit.Lime);
        var pivot = new Node3D();
        holder.AddChild(pivot);
        var body = (Node3D)source.Duplicate();
        pivot.AddChild(body);
        var (_, box) = ModelPlacement.Place(pivot, body, s);
        _info.Text += $"\n{line}  -  stands {box.Size.Y * UnitModelSettings.MmPerInch:0} mm tall";
        _focusY = Mathf.Max(0.5f, box.Size.Y * 0.45f);
        _distance = Mathf.Max(4.5f, Mathf.Max(box.Size.Y, Mathf.Max(box.Size.X, box.Size.Z)) * 2.4f + 2f);
        ApplyCamera();
    }

    /// <summary>A 28mm person (32mm to the top of the head) to judge size against.</summary>
    private void Person()
    {
        float baseR = 25 / UnitModelSettings.MmPerInch / 2, hIn = 32 / UnitModelSettings.MmPerInch;
        var man = new Node3D { Position = new Vector3(-2.2f, 0, 0) };
        _world.AddChild(man);
        TableLook.AddBase(man, baseR, new Color(0.5f, 0.5f, 0.5f));
        var mat = TableLook.Mat(new Color(0.62f, 0.64f, 0.66f), 0.7f);
        float y0 = TableLook.BaseTop, head = hIn * 0.13f, legs = hIn * 0.47f, torso = hIn - head * 2 - legs;
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, mat, new Vector3(-0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.09f, Height = legs }, mat, new Vector3(0.08f, y0 + legs / 2, 0));
        TableLook.Mesh(man, new CapsuleMesh { Radius = 0.17f, Height = torso + 0.1f }, mat, new Vector3(0, y0 + legs + torso / 2, 0));
        TableLook.Mesh(man, new SphereMesh { Radius = head, Height = head * 2 }, mat, new Vector3(0, y0 + hIn - head, 0));
    }

    private void ApplyCamera()
    {
        // Between the two side panels; the person stands to the model's left.
        Vector3 focus = new(-1.1f, _focusY, 0);
        Vector3 dir = new Vector3(0.2f, 0.42f, 1f).Normalized();
        _camera.Position = focus + dir * _distance;
        _camera.LookAt(focus, Vector3.Up);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (GetViewport().GuiGetHoveredControl() != this) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _distance = Mathf.Max(1.5f, _distance * 0.9f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _distance = Mathf.Min(80f, _distance * 1.1f); break;
            default: return;
        }
        ApplyCamera();
        AcceptEvent();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || GetViewport().GuiGetFocusOwner() is LineEdit) return;
        switch (k.Keycode)
        {
            // Kai's facing-review keys: W / S step, Space uses it.
            case Key.W: Step(1); break;
            case Key.S: Step(-1); break;
            case Key.Space or Key.Enter or Key.KpEnter: _ = UseSelected(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _spin += (float)delta * 0.5f;
        _stage.Rotation = new Vector3(0, _spin, 0);
    }

    /// <summary>An Age of Fantasy army opens the models list on the Age of Sigmar faction it stands in for.</summary>
    private void OpenPairedFaction()
    {
        if (_book.Selected < 0 || _book.Selected >= _books.Count) return;
        string book = _books.Keys.ElementAt(_book.Selected);
        if (!_map.AosFactions.TryGetValue(book, out var aos) || aos.Count == 0) return;
        int fi = _factions.Keys.ToList().FindIndex(f => f.Equals(aos[0], StringComparison.OrdinalIgnoreCase));
        if (fi >= 0 && fi != _faction.Selected) { _faction.Select(fi); FillModels(); }
    }

    // ================================================================== screenshot driver

    /// <summary>"fac:Tyranids", "find:Hormagaunt", "used:1", "model:3" (nth in the list), "army:Alien Hives", "unit:Assault Grunts", "use", "remove:45", "share".</summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        switch (verb)
        {
            case "fac":
                int fi = _factions.Keys.ToList().FindIndex(f => f.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (fi >= 0) { _faction.Select(fi); FillModels(); }
                break;
            case "find": _search.Text = arg; FillModels(); break;
            case "used": _showUsed.ButtonPressed = arg == "1"; break;
            case "model" when int.TryParse(arg, out int n) && n < _shown.Count: _models.Select(n); Select(_shown[n]); break;
            case "army":
                int bi = _books.Keys.ToList().FindIndex(b => b.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (bi >= 0) { _book.Select(bi); _unit = null; FillUnits(); OpenPairedFaction(); UpdateTarget(); }
                break;
            case "unit" when _unitRows.Contains(arg): _unit = arg; FillUnits(); UpdateTarget(); break;
            case "use": _ = UseSelected(); break;
            case "remove" when int.TryParse(arg, out int id): RemoveFromUnit(id); break;
            case "share": SaveForEveryone(); break;
        }
    }
}
