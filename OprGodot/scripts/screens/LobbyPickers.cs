using FDG;
using FDG.ArmyBuilding;
using FDG.Network.Connection.Lobby;
using FdgRaylib.Rendering;
using OprGodot.Forge;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Lobby;
using FdgRaylib.SaveLoad;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// The lobby's two full-screen pickers (Kai's wishlist, "Choose Army and Select Map" mockup): Choose Army - search,
/// faction, sort, a table of Army / Faction / Units / Points with over-limit lists grey and last, and on the right the
/// picked list's points, unit count, a legal / over-limit badge, its unit cards and SELECT ARMY; and Select Map - a
/// grid of map pictures for this table size, Random first, and on the right a big preview with the size, pieces,
/// objectives, what each piece is on hover, and USE THIS MAP. Both sit over the lobby; Esc / Back closes them.
/// </summary>
public partial class LobbyScreen
{
    private Control? _picker;

    private Control PickerShell(string title, out VBoxContainer left, out VBoxContainer right, out HBoxContainer footer)
    {
        ClosePicker();
        var veil = new PanelContainer { Name = "Picker", MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.03f, 0.05f, 0.05f, 1f) });
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 32);
        veil.AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 18);
        var back = Ui.Button("Back", ClosePicker, "Back to the lobby, nothing changed (Esc).", "BackButton");
        back.Name = "PickerBack";
        back.CustomMinimumSize = new Vector2(130, 46);
        head.AddChild(back);
        var t = new Label { Text = title, ThemeTypeVariation = "HeaderLabel", VerticalAlignment = VerticalAlignment.Center };
        Ui.ScreenTitle(t);
        head.AddChild(t);
        col.AddChild(head);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18);
        col.AddChild(body);
        left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
        left.AddThemeConstantOverride("separation", 10);
        right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 10);
        var lp = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(left, 18, 14, 18, 14));
        lp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lp.SizeFlagsStretchRatio = 1.5f;
        body.AddChild(lp);
        var rp = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(right, 18, 14, 18, 14));
        rp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(rp);
        footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 12);
        col.AddChild(footer);
        AddChild(veil);
        _picker = veil;
        return veil;
    }

    private void ClosePicker()
    {
        if (_picker != null && IsInstanceValid(_picker)) _picker.QueueFree();
        _picker = null;
    }

    /// <summary>Esc closes an open picker first.</summary>
    private bool PickerOpen => _picker != null && IsInstanceValid(_picker);

    // ================================================================== Choose Army

    private void OpenArmyPicker(LobbyPlayerInfoSummary info)
    {
        IReadOnlyList<ArmyCatalogEntry> armies = Session.Armies();
        string currentKey = LobbySession.ArmyKey(info.ArmyListSummary);
        int limit = Vm.ArmyPoints;

        PickerShell($"Choose army - {info.PlayerName}", out VBoxContainer left, out VBoxContainer right, out HBoxContainer footer);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 10);
        var search = new LineEdit { Name = "ArmySearch", PlaceholderText = "Search name or faction...", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        filters.AddChild(search);
        var factions = new List<string> { "" };
        factions.AddRange(armies.Select(a => a.Faction).Where(f => f.Length > 0).Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        var faction = new OptionButton { Name = "ArmyFaction", CustomMinimumSize = new Vector2(240, 0), TooltipText = "Only this faction's lists." };
        foreach (string f in factions) faction.AddItem(f.Length == 0 ? "All factions" : f);
        filters.AddChild(faction);
        var sort = new OptionButton { Name = "ArmySort", CustomMinimumSize = new Vector2(170, 0), TooltipText = "Order of the lists. Lists over the points limit always come last." };
        foreach (string s in new[] { "Name", "Points - low first", "Points - high first" }) sort.AddItem(s);
        filters.AddChild(sort);
        left.AddChild(filters);
        left.AddChild(Note($"From armies\\{ArmyPaths.FolderNameFor(Vm.GameSystem)}. Grey = over the {limit} pts limit (you can still pick it)."));

        var table = new Tree { Name = "ArmyTable", Columns = 4, ColumnTitlesVisible = true, HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Row };
        string[] titles = { "Army", "Faction", "Units", "Points" };
        for (int i = 0; i < 4; i++) { table.SetColumnTitle(i, titles[i]); table.SetColumnExpand(i, i < 2); }
        table.SetColumnCustomMinimumWidth(2, 80);
        table.SetColumnCustomMinimumWidth(3, 100);
        left.AddChild(table);

        var heading = HudKit.Text("Pick a list on the left", 22, HudKit.Fg, HudKit.Exo(800));
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(heading);
        var facts = new HBoxContainer();
        facts.AddThemeConstantOverride("separation", 12);
        right.AddChild(facts);
        var units = new VBoxContainer();
        units.AddThemeConstantOverride("separation", 4);
        var unitScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        units.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        unitScroll.AddChild(units);
        right.AddChild(unitScroll);

        var import = Ui.Button("Import from Army Forge...", () => { ClosePicker(); ImportArmyPopup.Open(this, p =>
        {
            p.LobbySystem = Vm.GameSystem;
            p.SaveLabel = $"Save and use for {info.PlayerName}";
            p.Saved = (path, system) =>
            {
                Session.RefreshArmies();
                if (system != Vm.GameSystem || ArmyCatalog.ReadEntry(path) is not { } entry) return;
                Session.PickArmy(info.PlayerID, entry);
                p.QueueFree();
            };
        }); }, "Paste a share link from the Army Forge website.");
        import.Name = "ImportArmy";
        footer.AddChild(import);
        footer.AddChild(HudKit.Grow());
        var select = Ui.Button("SELECT ARMY", () => { }, "Give this seat the picked list.", "CommitButton");
        select.Name = "SelectArmy";
        select.CustomMinimumSize = new Vector2(260, 56);
        select.Disabled = true;
        footer.AddChild(select);

        var shown = new List<ArmyCatalogEntry>();
        ArmyCatalogEntry? picked = null;
        Color team = SeatColour(Session.EffectiveColours()[Math.Max(0, Vm.PlayerInfos.ToList().FindIndex(p => p.PlayerID.Equals(info.PlayerID)))]);

        void Preview(ArmyCatalogEntry e)
        {
            picked = e;
            select.Disabled = false;
            heading.Text = e.FileName;
            foreach (Node n in facts.GetChildren()) { facts.RemoveChild(n); n.QueueFree(); }
            facts.AddChild(HudKit.Chip(e.Faction, HudKit.Fg, true, 15));
            facts.AddChild(HudKit.Chip($"{e.Points} pts", HudKit.Lime, true, 15));
            facts.AddChild(HudKit.Chip($"{e.UnitCount} units", HudKit.Fg, true, 15));
            facts.AddChild(e.Points > limit ? HudKit.Chip($"Over the {limit} pt limit", Palette.WoundsRed, true, 15) : HudKit.Chip("Within the limit", HudKit.Lime, true, 15));
            foreach (Node n in units.GetChildren()) { units.RemoveChild(n); n.QueueFree(); }
            if (LobbySession.LoadArmyFile(e.Path) is { } army)
                foreach (UnitFileEntry u in army.Units) units.AddChild(UnitRow(u, army.Faction, team));
            else units.AddChild(Note("Couldn't read this list."));
        }

        void Fill()
        {
            table.Clear();
            TreeItem root = table.CreateItem();
            shown.Clear();
            string q = search.Text.Trim();
            string fac = factions[Math.Max(0, faction.Selected)];
            IEnumerable<ArmyCatalogEntry> rows = armies.Where(e =>
                (q.Length == 0 || e.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Faction.Contains(q, StringComparison.OrdinalIgnoreCase))
                && (fac.Length == 0 || e.Faction == fac));
            rows = sort.Selected switch
            {
                1 => rows.OrderBy(e => e.Points > limit).ThenBy(e => e.Points),
                2 => rows.OrderBy(e => e.Points > limit).ThenByDescending(e => e.Points),
                _ => rows.OrderBy(e => e.Points > limit).ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase),
            };
            foreach (ArmyCatalogEntry e in rows)
            {
                TreeItem it = table.CreateItem(root);
                it.SetText(0, e.FileName);
                it.SetText(1, e.Faction);
                it.SetText(2, e.UnitCount.ToString());
                it.SetText(3, $"{e.Points} pts");
                if (e.Points > limit) for (int c = 0; c < 4; c++) it.SetCustomColor(c, HudKit.Dim with { A = 0.6f });
                shown.Add(e);
                if (e.Key == currentKey && picked == null) { it.Select(0); Preview(e); }
            }
            if (shown.Count == 0)
            {
                TreeItem none = table.CreateItem(root);
                none.SetText(0, LobbySession.ArmyCatalogReady ? "No armies match." : "Reading the armies folder...");
                none.SetSelectable(0, false);
            }
        }

        table.ItemSelected += () =>
        {
            int i = table.GetSelected()?.GetIndex() ?? -1;
            if (i >= 0 && i < shown.Count) Preview(shown[i]);
        };
        void Take()
        {
            if (picked is not { } e) return;
            Session.PickArmy(info.PlayerID, e);
            ClosePicker();
        }
        select.Pressed += Take;
        table.ItemActivated += Take;
        search.TextChanged += _ => Fill();
        faction.ItemSelected += _ => Fill();
        sort.ItemSelected += _ => Fill();
        Fill();
        search.GrabFocus();
    }

    // ================================================================== Select Map

    private void OpenMapPicker(bool locked)
    {
        (float tw, float th) = Session.TableWithoutMap();
        var maps = MapPaths.List()
            .Select(p => (Path: p, Draft: MapDraft.Load(p, out _)))
            .Where(m => m.Draft != null && MathF.Abs(m.Draft.TableW - tw) < 0.01f && MathF.Abs(m.Draft.TableH - th) < 0.01f)
            .Select(m => (m.Path, Draft: m.Draft!))
            .OrderBy(m => m.Draft.Name, StringComparer.OrdinalIgnoreCase).ToList();
        string? current = Session.HasMap ? Vm.TerrainLayoutPath : null;

        PickerShell($"Select map - {GameSystemInfo.DisplayName(Vm.GameSystem)} - {MapDraft.SizeName(tw, th)}", out VBoxContainer left, out VBoxContainer right, out HBoxContainer footer);
        left.AddChild(Note($"Maps made for this game's {MapDraft.SizeName(tw, th)} table. Make more in Map Maker."));
        var grid = new GridContainer { Name = "MapGrid", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        var gridScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        gridScroll.AddChild(grid);
        left.AddChild(gridScroll);

        var title = HudKit.Text("", 24, HudKit.Fg, HudKit.Exo(800));
        right.AddChild(title);
        var big = new Maps.MapPreview { CustomMinimumSize = new Vector2(0, 360), SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(big);
        var facts = HudKit.Text("", 16, HudKit.Dim, HudKit.Semi);
        facts.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(facts);
        var hover = HudKit.Text("Point at a piece to see what it is.", 15, HudKit.Gold, HudKit.Medium);
        hover.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(hover);

        var use = Ui.Button("USE THIS MAP", () => { }, "Play on the picked map.", "CommitButton");
        use.Name = "UseMap";
        use.CustomMinimumSize = new Vector2(260, 56);
        use.Disabled = locked;
        footer.AddChild(use);

        string? picked = current;
        var tiles = new List<(string? Path, Button Tile)>();
        void Show(string? path)
        {
            picked = path;
            foreach (var (p, b) in tiles) b.ButtonPressed = p == path;
            var d = maps.FirstOrDefault(m => m.Path == path).Draft;
            big.Draft = d;
            big.QueueRedraw();
            if (d == null)
            {
                title.Text = "Random";
                facts.Text = "The game lays out terrain when the game starts.";
                return;
            }
            title.Text = d.Name;
            var kinds = d.Pieces.GroupBy(p => p.Name.Length > 0 ? p.Name : p.Kind).OrderByDescending(g => g.Count()).Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key);
            facts.Text = $"{MapDraft.SizeName(d.TableW, d.TableH)} table - {d.Pieces.Count} pieces, {d.Objectives.Count} objective{(d.Objectives.Count == 1 ? "" : "s")}"
                + (d.Objectives.Count == 0 ? " (rolled at the start)" : "") + (d.GeneratedFrom.Length > 0 ? $" - rolled on the {d.GeneratedFrom} table" : "")
                + ".\n" + string.Join(", ", kinds) + ".\nDeployment comes from the lobby's Deployment setting.";
        }

        Button Tile(string? path, MapDraft? d)
        {
            var b = new Button { ToggleMode = true, CustomMinimumSize = new Vector2(220, 190), FocusMode = FocusModeEnum.None, Name = "Map_" + (d?.Name ?? "Random").Replace(' ', '_') };
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = 8; v.OffsetRight = -8; v.OffsetTop = 8; v.OffsetBottom = -8;
            if (d != null) v.AddChild(new Maps.MapPreview { Draft = d, CustomMinimumSize = new Vector2(0, 130), MouseFilter = MouseFilterEnum.Ignore });
            else
            {
                var r = HudKit.Text("RANDOM", 28, HudKit.Dim, HudKit.Exo(800), HorizontalAlignment.Center);
                r.CustomMinimumSize = new Vector2(0, 130);
                r.VerticalAlignment = VerticalAlignment.Center;
                v.AddChild(r);
            }
            var name = HudKit.Text(d?.Name ?? "Random", 16, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
            name.ClipText = true;
            v.AddChild(name);
            b.AddChild(v);
            b.Pressed += () => Show(path);
            tiles.Add((path, b));
            return b;
        }

        grid.AddChild(Tile(null, null));
        foreach (var (p, d) in maps) grid.AddChild(Tile(p, d));
        Show(current);

        // What a piece is, under the pointer on the big preview.
        big.MouseFilter = MouseFilterEnum.Stop;
        big.GuiInput += ev =>
        {
            if (ev is not InputEventMouseMotion mm || big.Draft is not { } d) return;
            float s = Mathf.Min(big.Size.X / d.TableW, big.Size.Y / d.TableH);
            var off = (big.Size - new Vector2(d.TableW, d.TableH) * s) / 2;
            Vector2 at = (mm.Position - off) / s;
            MapPiece? piece = d.PieceAt(at.X, at.Y);
            hover.Text = piece == null ? "Point at a piece to see what it is."
                : $"{(piece.Name.Length > 0 ? piece.Name : piece.Kind)} - {TerrainBook.Describe(piece.Rules)}";
        };

        use.Pressed += () =>
        {
            if (!locked) Session.SetMap(picked);
            ClosePicker();
        };
    }
}
