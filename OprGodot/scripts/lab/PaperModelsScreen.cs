using FdgRaylib.Paper;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// Asset Lab -> Paper models (lane 5, Kai 2026-11-27: "OPR has free paper models that work great for the free game assets
/// we need"). Import OPR's paper-miniature PDF sheets - several files at once, or a whole folder - and every figure is cut
/// out automatically (<see cref="PaperCutter"/>). The page shows each cut as a box; a bad cut is fixed here with plain
/// tools: draw a box, split one, merge several, delete, swap front and back, turn over, "not a pair". Matching the
/// figures to units is the next screen (<see cref="PaperMatchScreen"/>).
///
/// <para>Left: import, progress, the sheets. Middle: the page with the cut boxes (front = lime, back = teal). Right: the
/// picked figure's front and back, and the fix tools. Every change saves straight away.</para>
/// </summary>
public partial class PaperModelsScreen : Control
{
    private string _root = "";
    private List<PaperSheet> _sheets = new();
    private PaperSheet? _sheet;
    private int _page;
    private RgbaImage? _pageImage;       // the page the fix tools cut from
    private Texture2D? _pageTexture;
    private readonly HashSet<int> _selected = new();
    private Tool _tool = Tool.Pick;
    private bool _busy;

    private enum Tool { Pick, Draw, Split }

    private ItemList _sheetList = null!;
    private Label _progress = null!, _pageLabel = null!, _figInfo = null!, _toolHint = null!, _status = null!;
    private ProgressBar _bar = null!;
    private PageView _view = null!;
    private TextureRect _front = null!, _back = null!;
    private Button _draw = null!, _split = null!;
    private FileDialog? _dialog;

    public override void _Ready()
    {
        Name = "PaperModels";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _root = PaperStore.LocalRoot;
        var bg = new ColorRect { Color = Palette.Ink, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        Build();
        Reload();
    }

    // ================================================================== layout

    private void Build()
    {
        var row = new HBoxContainer { Name = "Columns" };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetTop = 16; row.OffsetRight = -16; row.OffsetBottom = -16;
        row.AddThemeConstantOverride("separation", 14);
        AddChild(row);

        // ---- left: import and sheets
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        left.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(100, 40), TooltipText = "Back to the Asset Lab (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("PAPER MODELS", 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text("OPR's paper miniatures, cut out", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(t);
        left.AddChild(head);
        left.AddChild(Btn("ImportPdf", "Import PDF sheets...", "Pick one or more paper-miniature PDFs (OPR's free paper minis). Every page is read and every figure cut out. A file you imported before is skipped.", () => PickFiles(false), main: true));
        left.AddChild(Btn("ImportFolder", "Import a folder...", "Every PDF in a folder (and the folders inside it).", () => PickFiles(true)));
        left.AddChild(Btn("OpenMatch", "Match paper models...", "Give the cut-out figures to units: pick figures, pick the game, army and unit.", OpenMatch, main: true));
        _progress = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _progress.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_progress);
        _bar = new ProgressBar { Name = "Progress", MinValue = 0, MaxValue = 1, Step = 0.001, Visible = false, CustomMinimumSize = new Vector2(0, 14), ShowPercentage = false };
        left.AddChild(_bar);
        left.AddChild(HudKit.Heading("Sheets", 15));
        _sheetList = new ItemList { Name = "Sheets", SizeFlagsVertical = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, AllowSearch = false };
        _sheetList.ItemSelected += i => OpenSheet((int)i);
        left.AddChild(_sheetList);
        left.AddChild(Btn("RecutSheet", "Cut this sheet again", "Cuts every page of this sheet again from scratch (after a game update improves the cutting, or to undo your fixes). Figures found in the same spot keep their matches.", RecutSheet));
        left.AddChild(Btn("DeleteSheet", "Delete this sheet...", "Removes the sheet and every figure cut from it (their matches go too). The PDF on your computer is not touched.", ConfirmDeleteSheet));
        left.AddChild(Btn("OpenPaperFolder", "Open the paper models folder", "UnitModels\\_Paper in Windows.", () => OS.ShellOpen(_root)));
        row.AddChild(Framed(left));

        // ---- middle: the page
        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 6);
        var pages = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pages.AddThemeConstantOverride("separation", 10);
        var prev = Btn("PrevPage", "< Page", "The page before.", () => ShowPage(_page - 1));
        prev.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        prev.CustomMinimumSize = new Vector2(110, 34);
        pages.AddChild(prev);
        _pageLabel = HudKit.Text("No sheet open", 18, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center);
        _pageLabel.CustomMinimumSize = new Vector2(260, 0);
        pages.AddChild(_pageLabel);
        var next = Btn("NextPage", "Page >", "The next page.", () => ShowPage(_page + 1));
        next.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        next.CustomMinimumSize = new Vector2(110, 34);
        pages.AddChild(next);
        mid.AddChild(pages);
        _view = new PageView(this) { Name = "Page", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        mid.AddChild(_view);
        _toolHint = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        _toolHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        mid.AddChild(_toolHint);
        row.AddChild(mid);

        // ---- right: the figure and the fix tools
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        right.AddThemeConstantOverride("separation", 8);
        right.AddChild(HudKit.Heading("Figure", 15));
        var pics = new HBoxContainer { CustomMinimumSize = new Vector2(0, 240) };
        pics.AddThemeConstantOverride("separation", 8);
        _front = Pic("Front", "The front - what the table shows facing you.");
        _back = Pic("BackPic", "The back - shown when the camera is behind the model.");
        pics.AddChild(Labelled("Front", _front));
        pics.AddChild(Labelled("Back", _back));
        right.AddChild(pics);
        _figInfo = HudKit.Text("Click a box on the page.", 14, HudKit.Dim, HudKit.Semi);
        _figInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_figInfo);
        right.AddChild(HudKit.Heading("Fix a bad cut", 15));
        _draw = Btn("DrawBox", "Draw a box", "Drag a box on the page. With a figure picked, its box is redrawn (it keeps its matches); with none picked, a new figure is made from what's inside.", () => SetTool(_tool == Tool.Draw ? Tool.Pick : Tool.Draw));
        _draw.ToggleMode = true;
        right.AddChild(_draw);
        _split = Btn("Split", "Split", "Click across the picked figure to cut it in two with a line straight down. Shift+click: a line straight across.", () => SetTool(_tool == Tool.Split ? Tool.Pick : Tool.Split));
        _split.ToggleMode = true;
        right.AddChild(_split);
        right.AddChild(Btn("Merge", "Merge picked", "Join the picked figures (Shift+click to pick several) into one.", Merge));
        right.AddChild(Btn("SwapSides", "Swap front and back", "The cut guessed the wrong copy as the front.", () => Each(PaperStore.SwapFrontBack)));
        right.AddChild(Btn("TurnOver", "Turn over", "The figure stands on its head: turn its pictures half a turn.", () => Each(PaperStore.TurnOver)));
        right.AddChild(Btn("NotAPair", "Not a pair", "The whole box is one picture (no fold, no back).", () => Each(PaperStore.MakeSingle)));
        right.AddChild(Btn("DeleteFigure", "Delete picked", "Not a figure (a title, a logo, a base strip on its own). Its matches go too. Del key.", DeletePicked));
        _status = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_status);
        row.AddChild(Framed(right));
        SetTool(Tool.Pick);
    }

    private static Control Framed(Control c) => HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(c, 14, 12, 14, 12));

    private static TextureRect Pic(string name, string tip) => new()
    {
        Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TooltipText = tip, CustomMinimumSize = new Vector2(140, 210),
    };

    private static Control Labelled(string label, Control c)
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddChild(HudKit.Text(label, 13, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        v.AddChild(c);
        return v;
    }

    private static Button Btn(string name, string text, string tip, Action onPressed, bool main = false)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        if (main) b.ThemeTypeVariation = "CommitButton";
        b.Pressed += onPressed;
        return b;
    }

    private void SetTool(Tool t)
    {
        _tool = t;
        _draw.SetPressedNoSignal(t == Tool.Draw);
        _split.SetPressedNoSignal(t == Tool.Split);
        _toolHint.Text = t switch
        {
            Tool.Draw => _selected.Count == 1 ? "Drag a new box for the picked figure." : "Drag a box round a figure the cut missed.",
            Tool.Split => "Click where to cut the picked figure (a line straight down; Shift+click: straight across).",
            _ => "Click a box to pick it, Shift+click to pick several. Lime = front, teal = back. Del deletes the picked ones.",
        };
        _view.QueueRedraw();
    }

    // ================================================================== sheets and pages

    private void Reload(string? open = null)
    {
        string? keep = open ?? _sheet?.Name;
        _sheets = PaperStore.LoadSheets(_root);
        _sheetList.Clear();
        foreach (var s in _sheets) _sheetList.AddItem($"{s.Name}   ({s.Figures.Count})");
        int i = keep == null ? -1 : _sheets.FindIndex(s => s.Name == keep);
        if (i < 0 && _sheets.Count > 0) i = 0;
        if (i >= 0) { _sheetList.Select(i); OpenSheet(i, keepPage: keep == _sheets[i].Name); }
        else { _sheet = null; _pageImage = null; _pageTexture = null; _pageLabel.Text = "No sheets yet - press Import PDF sheets..."; ShowFigure(); _view.QueueRedraw(); }
    }

    private void OpenSheet(int i, bool keepPage = false)
    {
        if (i < 0 || i >= _sheets.Count) return;
        bool same = _sheet?.Name == _sheets[i].Name;
        _sheet = _sheets[i];
        _selected.Clear();
        ShowPage(keepPage && same ? _page : 0, force: true);
    }

    private void ShowPage(int page, bool force = false)
    {
        if (_sheet == null) return;
        page = Math.Clamp(page, 0, Math.Max(0, _sheet.Pages.Count - 1));
        if (page == _page && !force && _pageImage != null) return;
        _page = page;
        _selected.Clear();
        string path = PaperStore.PagePath(_root, _sheet.Name, page);
        _pageImage = null;
        _pageTexture = System.IO.File.Exists(path) && Image.LoadFromFile(path) is { } img ? ImageTexture.CreateFromImage(img) : null;
        int count = _sheet.Figures.Count(f => f.Page == page);
        _pageLabel.Text = _sheet.Pages.Count == 0 ? _sheet.Name : $"Page {page + 1} of {_sheet.Pages.Count}  -  {count} figures";
        ShowFigure();
        _view.QueueRedraw();
    }

    /// <summary>The page picture to cut from (read on first use - only the fix tools need it).</summary>
    private RgbaImage? PageImage()
    {
        if (_sheet == null) return null;
        if (_pageImage != null) return _pageImage;
        string path = PaperStore.PagePath(_root, _sheet.Name, _page);
        if (!System.IO.File.Exists(path))
        {
            Say("This sheet's page pictures aren't on this computer (it came with the game). Import the PDF here to fix its cuts.", HudKit.Gold);
            return null;
        }
        return _pageImage = RgbaImage.LoadPng(path);
    }

    internal IEnumerable<PaperFigure> PageFigures => _sheet?.Figures.Where(f => f.Page == _page) ?? Enumerable.Empty<PaperFigure>();
    internal Texture2D? PageTexture => _pageTexture;
    internal Vector2 PageSize => _sheet != null && _page < _sheet.Pages.Count ? new Vector2(_sheet.Pages[_page].W, _sheet.Pages[_page].H) : Vector2.One;
    internal bool IsSelected(int n) => _selected.Contains(n);
    internal bool Drawing => _tool == Tool.Draw;

    // ================================================================== picking and fixing (called by the page view)

    internal void ClickFigure(int? n, bool add)
    {
        if (!add) _selected.Clear();
        if (n is { } k) { if (add && _selected.Contains(k)) _selected.Remove(k); else _selected.Add(k); }
        ShowFigure();
        SetTool(_tool);
    }

    internal void SplitAt(Vector2 pagePoint, bool across)
    {
        if (_sheet == null || _selected.Count != 1 || PageImage() is not { } img) { Say("Pick one figure first, then click where to cut it.", HudKit.Gold); return; }
        int n = _selected.First();
        var made = PaperStore.Split(_root, _sheet, n, across ? (int)pagePoint.Y : (int)pagePoint.X, vertical: !across, img);
        if (made.Count == 0) { Say("Couldn't split there - click inside the figure's box, with ink on both sides of the line.", HudKit.Gold); return; }
        _selected.Clear();
        foreach (var f in made) _selected.Add(f.N);
        Changed($"Split into figures {made[0].N} and {made[1].N}.");
        SetTool(Tool.Pick);
    }

    internal void BoxDrawn(Rect2 pageRect)
    {
        if (_sheet == null || PageImage() is not { } img) return;
        var r = new PixelRect((int)pageRect.Position.X, (int)pageRect.Position.Y, (int)pageRect.Size.X, (int)pageRect.Size.Y);
        if (r.W < 6 || r.H < 6) return;
        if (_selected.Count == 1)
        {
            int n = _selected.First();
            if (!PaperStore.Redraw(_root, _sheet, n, r, img)) { Say("No ink in that box.", HudKit.Gold); return; }
            Changed($"Figure {n} cut again from the new box.");
        }
        else
        {
            if (PaperStore.AddBox(_root, _sheet, _page, r, img) is not { } f) { Say("No ink in that box.", HudKit.Gold); return; }
            _selected.Clear();
            _selected.Add(f.N);
            Changed($"New figure {f.N}.");
        }
        SetTool(Tool.Pick);
    }

    private void Merge()
    {
        if (_sheet == null || _selected.Count < 2) { Say("Shift+click two or more boxes first.", HudKit.Gold); return; }
        if (PageImage() is not { } img) return;
        if (PaperStore.Merge(_root, _sheet, _selected.ToList(), img) is not { } f) { Say("Couldn't merge those.", HudKit.Gold); return; }
        var gone = _selected.Where(n => n != f.N).Select(n => _sheet.Name + "/" + n).ToList();
        DropMatches(gone);
        _selected.Clear();
        _selected.Add(f.N);
        Changed($"Merged into figure {f.N}.");
    }

    private void Each(Action<string, PaperSheet, int, RgbaImage> fix)
    {
        if (_sheet == null || _selected.Count == 0) { Say("Pick a figure first.", HudKit.Gold); return; }
        if (PageImage() is not { } img) return;
        foreach (int n in _selected.ToList()) fix(_root, _sheet, n, img);
        Changed("Done.");
    }

    internal void DeletePicked()
    {
        if (_sheet == null || _selected.Count == 0) return;
        var ids = _selected.Select(n => _sheet.Name + "/" + n).ToList();
        PaperStore.Delete(_root, _sheet, _selected.ToList());
        DropMatches(ids);
        _selected.Clear();
        Changed($"Deleted {ids.Count}.");
    }

    private void DropMatches(List<string> ids)
    {
        var m = PaperMatches.Load(PaperMatches.LocalPath(_root));
        if (m.UnassignEverywhere(ids) > 0) m.Save(PaperMatches.LocalPath(_root));
    }

    private void Changed(string what)
    {
        PaperLibrary.Reload();
        Battle.Standee.ClearCache();
        HudKit.ForgetPictures();
        Say(what, HudKit.Lime);
        int sel = _sheetList.GetSelectedItems() is { Length: > 0 } s ? s[0] : 0;
        if (sel < _sheetList.ItemCount && _sheet != null) _sheetList.SetItemText(sel, $"{_sheet.Name}   ({_sheet.Figures.Count})");
        _pageLabel.Text = _sheet == null ? "" : $"Page {_page + 1} of {_sheet.Pages.Count}  -  {_sheet.Figures.Count(f => f.Page == _page)} figures";
        ShowFigure();
        _view.QueueRedraw();
    }

    private void Say(string text, Color c)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c);
    }

    private void ShowFigure()
    {
        PaperFigure? f = _sheet != null && _selected.Count == 1 ? _sheet.Figure(_selected.First()) : null;
        _front.Texture = f == null ? null : Fresh(PaperStore.FigurePath(_root, _sheet!.Name, f.N));
        _back.Texture = f?.Back == null ? null : Fresh(PaperStore.FigurePath(_root, _sheet!.Name, f.N, back: true));
        if (f == null) { _figInfo.Text = _selected.Count > 1 ? $"{_selected.Count} figures picked." : "Click a box on the page."; return; }
        var m = PaperMatches.Merged(PaperMatches.Load(PaperMatches.ShippedPath), PaperMatches.Load(PaperMatches.LocalPath(_root)));
        var units = m.UnitsOf(_sheet!.IdOf(f)).Select(u => $"{u.Book}: {u.Unit}").ToList();
        _figInfo.Text = $"Figure {f.N}  ({f.Fold switch { PaperFold.TopBottom => "fold pair", PaperFold.LeftRight => "side-by-side pair", _ => "single" }})\n"
                        + (units.Count == 0 ? "Not matched to a unit yet." : "Matched to " + string.Join(", ", units));
    }

    /// <summary>A picture read from disk now (not the cached one - it may have just been cut again).</summary>
    private static Texture2D? Fresh(string path) =>
        System.IO.File.Exists(path) && Image.LoadFromFile(path) is { } img ? ImageTexture.CreateFromImage(img) : null;

    // ================================================================== import

    private void PickFiles(bool folder)
    {
        if (_busy) return;
        _dialog?.QueueFree();
        _dialog = new FileDialog
        {
            FileMode = folder ? FileDialog.FileModeEnum.OpenDir : FileDialog.FileModeEnum.OpenFiles,
            Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = folder ? "Pick a folder of paper-miniature PDFs" : "Pick paper-miniature PDFs",
            Filters = folder ? Array.Empty<string>() : new[] { "*.pdf ; PDF sheets" },
        };
        if (folder) _dialog.DirSelected += dir => Import(PaperStore.PdfsIn(dir).ToList());
        else _dialog.FilesSelected += files => Import(files.ToList());
        AddChild(_dialog);
        _dialog.PopupCentered(new Vector2I(1100, 700));
    }

    /// <summary>Imports PDFs one after another off the main thread, the progress bar following along.</summary>
    public void Import(List<string> files)
    {
        if (_busy || files.Count == 0) { if (files.Count == 0) Say("No PDFs there.", HudKit.Gold); return; }
        _busy = true;
        _bar.Visible = true;
        _bar.Value = 0;
        var results = new List<PaperStore.ImportResult>();
        string root = _root;
        System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < files.Count; i++)
            {
                int k = i;
                PaperStore.ImportResult r;
                try
                {
                    r = PaperStore.Import(files[k], root, (text, share) =>
                        Callable.From(() => { _progress.Text = $"File {k + 1} of {files.Count}: {text}"; _bar.Value = (k + share) / files.Count; }).CallDeferred());
                }
                catch (Exception e) { r = new PaperStore.ImportResult(System.IO.Path.GetFileName(files[k]), 0, false, e.Message); }
                results.Add(r);
            }
        }).ContinueWith(_ => Callable.From(() => ImportDone(results)).CallDeferred());
    }

    private void ImportDone(List<PaperStore.ImportResult> results)
    {
        _busy = false;
        _bar.Visible = false;
        int figures = results.Where(r => !r.AlreadyThere && r.Error == null).Sum(r => r.Figures);
        int fresh = results.Count(r => !r.AlreadyThere && r.Error == null);
        var lines = new List<string> { $"Imported {fresh} sheet{(fresh == 1 ? "" : "s")}, {figures} figures." };
        foreach (var r in results.Where(r => r.AlreadyThere)) lines.Add($"{r.Sheet}: imported before - skipped.");
        foreach (var r in results.Where(r => r.Error != null)) lines.Add($"{r.Sheet}: couldn't read it ({r.Error}).");
        _progress.Text = string.Join("\n", lines);
        PaperLibrary.Reload();
        Reload(results.LastOrDefault(r => r.Error == null)?.Sheet);
    }

    // ================================================================== the rest

    private void ConfirmDeleteSheet()
    {
        if (_sheet == null) return;
        var dlg = new ConfirmationDialog { Title = "Delete sheet", DialogText = $"Delete \"{_sheet.Name}\" and its {_sheet.Figures.Count} figures? Their matches go too. Your PDF is not touched.", OkButtonText = "Delete it" };
        string name = _sheet.Name;
        var ids = _sheet.Figures.Select(f => _sheet.IdOf(f)).ToList();
        dlg.Confirmed += () => { PaperStore.Remove(_root, name); DropMatches(ids); PaperLibrary.Reload(); _sheet = null; Reload(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void RecutSheet()
    {
        if (_sheet == null || _busy) return;
        _busy = true;
        _bar.Visible = true;
        var sheet = _sheet;
        string root = _root;
        System.Threading.Tasks.Task.Run(() => PaperStore.Recut(root, sheet, (text, share) =>
                Callable.From(() => { _progress.Text = text; _bar.Value = share; }).CallDeferred()))
            .ContinueWith(t => Callable.From(() =>
            {
                _busy = false;
                _bar.Visible = false;
                if (t.IsFaulted) { _progress.Text = "Couldn't cut it again: " + t.Exception?.GetBaseException().Message; return; }
                if (t.Result is not { } r) { _progress.Text = "This sheet's page pictures aren't on this computer - import the PDF here first."; return; }
                DropMatches(r.Gone.Select(n => sheet.Name + "/" + n).ToList());
                _progress.Text = $"Cut again: {r.Kept} figures kept their place, {r.New} new, {r.Gone.Count} gone.";
                _selected.Clear();
                Changed("Cut again.");
                ShowPage(_page, force: true);
            }).CallDeferred());
    }

    private void OpenMatch() => App.I.Open<PaperMatchScreen>(Scenes.PaperMatch);

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        if (k.Keycode == Key.Delete) { DeletePicked(); AcceptEvent(); }
        else if (k.Keycode == Key.Pagedown) { ShowPage(_page + 1); AcceptEvent(); }
        else if (k.Keycode == Key.Pageup) { ShowPage(_page - 1); AcceptEvent(); }
    }

    /// <summary>
    /// The test driver (ScriptedRun "paper:&lt;command&gt;"): "import:&lt;pdf&gt;[;&lt;pdf&gt;]", "folder:&lt;dir&gt;",
    /// "sheet:&lt;i&gt;", "page:&lt;i&gt;", "pick:&lt;n&gt;[,&lt;n&gt;]", "split:&lt;x&gt;", "merge", "swap", "turn", "single",
    /// "delete", "box:&lt;x&gt;;&lt;y&gt;;&lt;w&gt;;&lt;h&gt;" (page pixels), "match".
    /// </summary>
    public void Drive(string cmd)
    {
        string arg = cmd.Contains(':') ? cmd[(cmd.IndexOf(':') + 1)..] : "";
        switch (cmd.Split(':')[0])
        {
            case "import": Import(arg.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()); break;
            case "folder": Import(PaperStore.PdfsIn(arg).ToList()); break;
            case "sheet": if (int.TryParse(arg, out int s)) { _sheetList.Select(s); OpenSheet(s); } break;
            case "page": if (int.TryParse(arg, out int p)) ShowPage(p); break;
            case "pick":
                _selected.Clear();
                foreach (string n in arg.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(n, out int k)) _selected.Add(k);
                ShowFigure(); SetTool(Tool.Pick); break;
            case "split": if (int.TryParse(arg, out int x)) SplitAt(new Vector2(x, 0), false); break;
            case "merge": Merge(); break;
            case "swap": Each(PaperStore.SwapFrontBack); break;
            case "turn": Each(PaperStore.TurnOver); break;
            case "single": Each(PaperStore.MakeSingle); break;
            case "delete": DeletePicked(); break;
            case "draw": SetTool(Tool.Draw); break;
            case "box":
                var v = arg.Split(';').Select(float.Parse).ToArray();
                BoxDrawn(new Rect2(v[0], v[1], v[2], v[3])); break;
            case "match": OpenMatch(); break;
            case "recut": RecutSheet(); break;
        }
    }

    /// <summary>The page with its cut boxes: click to pick, drag to draw, click to split.</summary>
    private partial class PageView : Control
    {
        private readonly PaperModelsScreen _s;
        private Vector2? _dragFrom, _dragTo;

        public PageView(PaperModelsScreen s) { _s = s; MouseFilter = MouseFilterEnum.Stop; }

        /// <summary>Where the page is drawn (fitted, centred) and its scale (screen px per page px).</summary>
        private (Rect2 Rect, float K) Fit()
        {
            Vector2 page = _s.PageSize;
            float k = Mathf.Min(Size.X / page.X, Size.Y / page.Y);
            Vector2 drawn = page * k;
            return (new Rect2((Size - drawn) / 2, drawn), k);
        }

        private Vector2 ToPage(Vector2 local) { var (r, k) = Fit(); return (local - r.Position) / k; }
        private Rect2 ToScreen(PixelRect p) { var (r, k) = Fit(); return new Rect2(r.Position + new Vector2(p.X, p.Y) * k, new Vector2(p.W, p.H) * k); }

        public override void _Draw()
        {
            var (rect, _) = Fit();
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.06f, 0.07f, 0.08f));
            if (_s.PageTexture is { } tex) DrawTextureRect(tex, rect, false);
            else if (_s._sheet != null) DrawString(HudKit.Semi, new Vector2(20, 40), "No page picture here (this sheet came with the game).", HorizontalAlignment.Left, -1, 16, HudKit.Gold);
            foreach (PaperFigure f in _s.PageFigures)
            {
                bool sel = _s.IsSelected(f.N);
                Rect2 box = ToScreen(f.Box);
                DrawRect(box, sel ? HudKit.Gold : new Color(1, 1, 1, 0.55f), false, sel ? 3 : 1.5f);
                DrawRect(ToScreen(f.Front.Rect), HudKit.Lime with { A = sel ? 0.9f : 0.6f }, false, 1.5f);
                if (f.Back != null) DrawRect(ToScreen(f.Back.Rect), HudKit.Teal with { A = sel ? 0.9f : 0.6f }, false, 1.5f);
                string label = f.N.ToString();
                DrawRect(new Rect2(box.Position, new Vector2(10 + 9 * label.Length, 18)), sel ? HudKit.Gold : new Color(0, 0, 0, 0.7f));
                DrawString(HudKit.Bold, box.Position + new Vector2(4, 14), label, HorizontalAlignment.Left, -1, 14, sel ? Colors.Black : Colors.White);
            }
            if (_dragFrom is { } a && _dragTo is { } b)
                DrawRect(new Rect2(a, b - a).Abs(), HudKit.Gold, false, 2);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.Pressed)
                {
                    if (_s.Drawing) { _dragFrom = _dragTo = mb.Position; QueueRedraw(); }
                    else if (_s._tool == Tool.Split) _s.SplitAt(ToPage(mb.Position), mb.ShiftPressed);
                    else
                    {
                        Vector2 p = ToPage(mb.Position);
                        var hit = _s.PageFigures.Where(f => f.Box.Contains((int)p.X, (int)p.Y)).OrderBy(f => f.Box.Area).FirstOrDefault();
                        _s.ClickFigure(hit?.N, mb.ShiftPressed || mb.CtrlPressed);
                    }
                }
                else if (_dragFrom is { } from && _dragTo is { } to)
                {
                    _dragFrom = _dragTo = null;
                    Vector2 a = ToPage(from), b = ToPage(to);
                    _s.BoxDrawn(new Rect2(a, b - a).Abs());
                    QueueRedraw();
                }
                AcceptEvent();
            }
            else if (e is InputEventMouseMotion mm && _dragFrom != null)
            {
                _dragTo = mm.Position;
                QueueRedraw();
            }
        }
    }
}
