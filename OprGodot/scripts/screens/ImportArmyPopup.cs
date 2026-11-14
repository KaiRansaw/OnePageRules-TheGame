using FDG;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Import;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// Import an army list from Army Forge (Kai, 2026-11-11: "add the import army list function ... so i can make
/// lists to test these specific things"). Paste a share link, IMPORT, check what came in, SAVE. The list goes in
/// its own game's armies folder, so it shows in that game's lobby picker. Opened from the main menu's Army Forge
/// button and from the lobby's Choose Army popup (which then hands the saved list straight to that seat).
///
/// <para>The fetch and the file writing are shared code (<see cref="ArmyForgeShareService"/>,
/// <see cref="ImportedArmySaver"/>) - the same pipeline as the Raylib Forge's Import Link button.</para>
/// </summary>
public partial class ImportArmyPopup : Control
{
    /// <summary>Called after a save with the written path and the game the list is for.</summary>
    public Action<string, EGameSystem>? Saved { get; set; }

    /// <summary>The lobby's game, when opened from the lobby: a list for another game is saved but not used.</summary>
    public EGameSystem? LobbySystem { get; set; }

    /// <summary>The save button's words when opened from the lobby ("Save and use for Kai").</summary>
    public string SaveLabel { get; set; } = "Save to my armies";

    /// <summary>The Army Forge screen's full report (Raylib's Forge import modal): every unit with its gear, under the notes.</summary>
    public bool Detailed { get; set; }

    private LineEdit _link = null!;
    private Button _import = null!, _save = null!;
    private Label _status = null!, _summary = null!;
    private LineEdit _name = null!;
    private VBoxContainer _result = null!, _notes = null!;
    private Task<ArmyForgeShareService.ImportOutcome>? _task;
    private ArmyForgeShareService.ImportOutcome? _outcome;

    public override void _Ready()
    {
        // A full-screen layer, not a pop-up window: a pop-up closes when the game loses focus, and you leave the
        // game to copy the link from your browser.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(820, 560) };
        col.AddThemeConstantOverride("separation", 10);
        var panel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 18, 14, 18, 14));
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        center.AddChild(panel);
        AddChild(center);

        col.AddChild(HudKit.Text("IMPORT FROM ARMY FORGE", 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        col.AddChild(Wrap(HudKit.Text("On army-forge.onepagerules.com open your list, press Share and copy the link. Paste it here.", 15, HudKit.Dim, HudKit.Medium)));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _link = new LineEdit { Name = "ImportLink", PlaceholderText = "https://army-forge.onepagerules.com/share?id=...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _link.TextSubmitted += _ => StartImport();
        row.AddChild(_link);
        var paste = Ui.Button("Paste", () => { _link.Text = DisplayServer.ClipboardGet().Trim(); StartImport(); }, "Paste the link you copied and import it.");
        paste.Name = "ImportPaste";
        row.AddChild(paste);
        _import = Ui.Button("IMPORT", StartImport, "Fetch the list from Army Forge.", "CommitButton");
        _import.Name = "ImportFetch";
        row.AddChild(_import);
        col.AddChild(row);

        _status = Wrap(HudKit.Text("", 15, HudKit.Dim, HudKit.Semi));
        col.AddChild(_status);

        _result = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _result.AddThemeConstantOverride("separation", 8);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.AddChild(HudKit.Text("Name", 15, HudKit.Dim, HudKit.Semi));
        _name = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameRow.AddChild(_name);
        _result.AddChild(nameRow);
        _summary = Wrap(HudKit.Text("", 17, HudKit.Fg, HudKit.Semi));
        _result.AddChild(_summary);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _notes = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _notes.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_notes);
        _result.AddChild(scroll);
        col.AddChild(_result);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Ui.Button("Close", QueueFree, "Close without saving.", "BackButton"));
        _save = Ui.Button(SaveLabel, Save, "Save the list in its game's armies folder.", "CommitButton");
        _save.Name = "ImportSave";
        _save.Disabled = true;
        buttons.AddChild(_save);
        col.AddChild(buttons);

    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        QueueFree();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Opens the popup over <paramref name="parent"/> with the link box focused.</summary>
    public static ImportArmyPopup Open(Node parent, Action<ImportArmyPopup>? setup = null)
    {
        var p = new ImportArmyPopup();
        setup?.Invoke(p);
        p.Name = "ImportArmyPopup";
        parent.AddChild(p);
        p._link.GrabFocus();
        return p;
    }

    /// <summary>For scripted runs: put a link in the box and import it.</summary>
    public void ImportLink(string link) { _link.Text = link; StartImport(); }

    private void StartImport()
    {
        if (_task is { IsCompleted: false }) return;
        string link = _link.Text.Trim();
        if (!ArmyForgeShareService.TryParseShareId(link, out _))
        {
            Status(link.Length == 0 ? "Paste a share link first." : "That doesn't look like an Army Forge share link.", Palette.WoundsRed);
            return;
        }
        _outcome = null;
        _result.Visible = false;
        _save.Disabled = true;
        _import.Disabled = true;
        Status("Fetching the list from Army Forge...", HudKit.Dim);
        _task = Task.Run(() => ArmyForgeShareService.FetchAndImportAsync(link));
    }

    public override void _Process(double delta)
    {
        if (_task is not { IsCompleted: true } t) return;
        _task = null;
        _import.Disabled = false;
        if (t.IsFaulted || t.IsCanceled)
        {
            Exception? e = t.Exception?.GetBaseException();
            Status("Import failed: " + (e?.Message ?? "cancelled."), Palette.WoundsRed);
            return;
        }
        Show(t.Result);
    }

    private void Show(ArmyForgeShareService.ImportOutcome o)
    {
        _outcome = o;
        ArmyListFile army = o.Result.Army;
        EGameSystem system = ImportedArmySaver.SystemOf(army);
        _name.Text = army.Name;
        _summary.Text = $"{army.Faction}  -  {GameSystemInfo.DisplayName(system)}  -  {army.Units.Count} units  -  {army.TotalPoints} pts";

        foreach (Node c in _notes.GetChildren()) c.QueueFree();
        bool otherGame = LobbySystem is { } lobby && lobby != system;
        if (otherGame)
            Note($"This is a {GameSystemInfo.DisplayName(system)} list. It will be saved in the {GameSystemInfo.DisplayName(system)} armies, not used in this {GameSystemInfo.DisplayName(LobbySystem!.Value)} game.", HudKit.Gold);
        foreach (string e in o.Result.ListErrors) Note("Army Forge says: " + e, Palette.WoundsRed);
        foreach (string w in o.Result.Warnings) Note(w, HudKit.Gold);
        if (o.InertRules.Count > 0)
            Note("Rules the game doesn't play yet (they do nothing): " + string.Join(", ", o.InertRules), HudKit.Gold);
        if (o.ForgeSession is { } s)
        {
            foreach ((string name, int pts) in s.ExcludedUnits) Note($"Not in our copy of the book: {name} ({pts} pts) - still in the list as Army Forge sent it.", HudKit.Dim);
            foreach ((string name, int ours, int theirs) in s.UnitPointsDeltas) Note($"{name}: Army Forge says {theirs} pts, our book copy says {ours} pts. Army Forge's number is used.", HudKit.Dim);
            foreach (string w in s.Warnings) Note(w, HudKit.Gold);
        }
        if (_notes.GetChildCount() == 0) Note("Everything came in cleanly.", HudKit.Lime);
        // The points check: our Forge prices the same picks against our copy of the book.
        if (o.ForgeSession is { } check)
        {
            if (check.OurTotalPoints == check.TheirTotalPoints && check.ExcludedUnits.Count == 0)
                Note($"Points check: our Army Forge comes to the same {check.OurTotalPoints} pts.", HudKit.Lime);
            else if (check.UnpricedUpgradeCount > 0)
                Note($"Points check: ours comes to {check.OurTotalPoints} pts, Army Forge's to {check.TheirTotalPoints}. {check.UnpricedUpgradeCount} picked upgrade(s) have no price on Army Forge, so ours counts them as free.", HudKit.Gold);
            else if (check.OurTotalPoints != check.TheirTotalPoints)
                Note($"Points check: ours comes to {check.OurTotalPoints} pts, Army Forge's to {check.TheirTotalPoints}. The list keeps Army Forge's numbers; editing it here uses ours.", Palette.WoundsRed);
        }
        else Note("No army book here matches this faction, so the list can be played but not edited in the Forge.", HudKit.Dim);
        if (Detailed)
        {
            Note("UNITS", HudKit.Dim);
            foreach (UnitFileEntry u in army.Units)
            {
                Note($"{u.Name} [{u.ModelCount}] - Qua {u.Quality}+ Def {u.Defense}+ - {u.PointCost} pts", HudKit.Fg);
                string gear = string.Join(", ", u.Weapons.Select(w => $"{w.Quantity}x {w.Name}"));
                if (gear.Length > 0) Note("    " + gear, HudKit.Dim);
            }
        }

        _result.Visible = true;
        _save.Text = otherGame ? "Save to my armies" : SaveLabel;
        _save.Disabled = false;
        Status("Imported. Check it, then save.", HudKit.Lime);
    }

    private void Save()
    {
        if (_outcome is not { } o) return;
        try
        {
            string path = ImportedArmySaver.Save(o, _name.Text);
            EGameSystem system = ImportedArmySaver.SystemOf(o.Result.Army);
            Status($"Saved to armies\\{ArmyPaths.FolderNameFor(system)}\\{System.IO.Path.GetFileName(path)}", HudKit.Lime);
            _save.Disabled = true;
            Saved?.Invoke(path, system);
        }
        catch (Exception e)
        {
            Status("SAVE FAILED - " + e.Message, Palette.WoundsRed);
        }
    }

    private void Status(string text, Color colour)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);
    }

    private void Note(string text, Color colour) => _notes.AddChild(Wrap(HudKit.Text("- " + text, 14, colour, HudKit.Medium)));

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(10, 0);
        return l;
    }
}
