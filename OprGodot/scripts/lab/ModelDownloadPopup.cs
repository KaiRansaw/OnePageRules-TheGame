using System.Collections.Concurrent;
using FdgRaylib.Models3D;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab's "Download models" window (Kai, 2026-11-12: "the 40k models renamed to the OPR models and put into
/// the game"; every version of a model, from a button in the game).
///
/// <para>
/// Pick an army book; for each of its units that the wiki's Unit Equivalences list covers, every matching model in the
/// ForceOrg catalogue is fetched, put together and saved under the OPR name:
/// <c>UnitModels/&lt;Book&gt;/&lt;Unit&gt;/&lt;Warhammer model&gt; (&lt;id&gt;).glb</c>, with its lab settings beside it.
/// Fetching runs in the background; putting each model together happens on the main thread, one per frame.
/// </para>
/// </summary>
public partial class ModelDownloadPopup : Control
{
    /// <summary>Raised when files were saved, so the lab can refresh its list.</summary>
    public event Action? Saved;

    private UnitModelMap? _map;
    private ForceOrgCatalog? _catalogue;
    private string _catPath = "";

    private Label _catLabel = null!, _status = null!, _bookInfo = null!;
    private OptionButton _book = null!;
    private CheckBox _skip = null!;
    private Button _start = null!, _stop = null!, _close = null!, _choose = null!;
    private ProgressBar _bar = null!;
    private RichTextLabel _log = null!;

    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<Job> _ready = new();
    private int _total, _done, _savedCount, _failed, _skipped;
    private volatile bool _fetchFinished;

    private sealed record Job(ForceOrgCatalog.Model Model, Dictionary<string, string> Files, string Path, string Notes);

    public static ModelDownloadPopup Open(Control parent, string? catalogue = null, string? map = null)
    {
        var p = new ModelDownloadPopup();
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(p);
        p.LoadData(catalogue, map);
        return p;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text("DOWNLOAD MODELS", 30, HudKit.Fg, HudKit.Exo(800)));
        var intro = HudKit.Text("Warhammer 40k models from the ForceOrg Tabletop Simulator mod, saved under each OPR unit's name, " +
                                "following onepagefan.wiki's Unit Equivalences. Every version of a model is fetched.", 15, HudKit.Dim, HudKit.Semi);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(intro);

        col.AddChild(HudKit.Heading("Catalogue", 15));
        var catRow = new HBoxContainer();
        catRow.AddThemeConstantOverride("separation", 10);
        _catLabel = HudKit.Text("", 14, HudKit.Fg, HudKit.Medium);
        _catLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _catLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        catRow.AddChild(_catLabel);
        _choose = Btn("ChooseCatalogue", "Choose file...", "Pick forceorg-models.json. It is copied into the UnitModels folder, so this only happens once.", ChooseCatalogue);
        catRow.AddChild(_choose);
        col.AddChild(catRow);

        col.AddChild(HudKit.Heading("Army", 15));
        var bookRow = new HBoxContainer();
        bookRow.AddThemeConstantOverride("separation", 10);
        _book = new OptionButton { Name = "Book", CustomMinimumSize = new Vector2(380, 0), TooltipText = "The Grimdark Future army book to fetch models for. Sub-factions (Blood Brothers, Change Disciples...) are their own books." };
        _book.ItemSelected += _ => ShowBook();
        bookRow.AddChild(_book);
        _skip = new CheckBox { Name = "SkipExisting", Text = "Skip models already downloaded", ButtonPressed = true, TooltipText = "Leave models that are already in the folder alone (your saved settings for them are kept)." };
        bookRow.AddChild(_skip);
        col.AddChild(bookRow);
        _bookInfo = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _bookInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_bookInfo);

        _bar = new ProgressBar { CustomMinimumSize = new Vector2(0, 22), MaxValue = 1, ShowPercentage = true };
        col.AddChild(_bar);
        _status = HudKit.Text("", 15, HudKit.Fg, HudKit.Semi);
        col.AddChild(_status);
        _log = new RichTextLabel { CustomMinimumSize = new Vector2(0, 220), ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = false };
        _log.AddThemeFontSizeOverride("normal_font_size", 14);
        col.AddChild(_log);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        _start = Btn("StartDownload", "Download", "Fetch and save every model for the units of this army.", Start);
        _stop = Btn("StopDownload", "Stop", "Stop after the models already on their way. What is saved stays; the next run carries on.", Stop);
        _close = Btn("CloseDownload", "Close", "Close this window.", () => { Stop(); QueueFree(); });
        _stop.Disabled = true;
        buttons.AddChild(_start);
        buttons.AddChild(_stop);
        buttons.AddChild(_close);
        col.AddChild(buttons);

        var panel = HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 22, 18, 22, 18));
        HudKit.Place(this, panel, LayoutPreset.Center);
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(130, 38), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    // ------------------------------------------------------------------ data

    private void LoadData(string? catalogue, string? map)
    {
        try { _map = UnitModelMap.Load(map); }
        catch (Exception e) { _map = null; Log($"The unit list could not be read: {e.Message}"); }
        _book.Clear();
        if (_map != null)
            foreach (string b in _map.Books)
                _book.AddItem($"{b}  ({_map.ForBook(b).Count()} units)");
        _catPath = catalogue ?? ForceOrgCatalog.DefaultPath;
        LoadCatalogue();
        ShowBook();
    }

    private void LoadCatalogue()
    {
        _catalogue = null;
        if (File.Exists(_catPath))
        {
            try { _catalogue = ForceOrgCatalog.Load(_catPath); }
            catch (Exception e) { Log($"The catalogue could not be read: {e.Message}"); }
        }
        _catLabel.Text = _catalogue != null
            ? $"{_catPath}  -  {_catalogue.ById.Count:N0} models"
            : "Not found yet. Choose forceorg-models.json (on your PC it is in C:\\OPR DEV\\3d-models).";
        _catLabel.Modulate = _catalogue != null ? Colors.White : Palette.WoundsRed;
        _start.Disabled = _catalogue == null || _map == null || _cts != null;
    }

    private void ChooseCatalogue()
    {
        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.json ; Model catalogue" },
            Title = "Choose forceorg-models.json", UseNativeDialog = true,
        };
        dlg.FileSelected += path =>
        {
            try
            {
                string dest = ForceOrgCatalog.DefaultPath;
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dest, true);
                _catPath = dest;
            }
            catch (Exception e) { Log($"Could not copy the catalogue: {e.Message}"); _catPath = path; }
            LoadCatalogue();
            ShowBook();
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(900, 600));
    }

    private string? SelectedBook => _map == null || _book.Selected < 0 ? null : _map.Books.ElementAt(_book.Selected);

    private void ShowBook()
    {
        if (SelectedBook is not { } book || _map == null) { _bookInfo.Text = ""; return; }
        var units = _map.ForBook(book).ToList();
        int models = units.Sum(u => u.Models.Count);
        int have = units.Sum(u => u.Models.Count(m => File.Exists(UnitModelMap.FileFor(UnitModelPaths.Root, book, u.Unit, m))));
        string broad = units.Any(u => u.TooBroad) ? $"  {units.Count(u => u.TooBroad)} unit(s) matched more models than kept - worth a look." : "";
        _bookInfo.Text = $"{units.Count} units, {models} model versions ({have} already downloaded). Units in the book with no model on the list are left as they are.{broad}";
    }

    // ------------------------------------------------------------------ the run

    /// <summary>Starts a run for a book by name (also used by the screenshot driver).</summary>
    public void StartFor(string book)
    {
        if (_map == null) return;
        int i = _map.Books.ToList().FindIndex(b => b.Equals(book, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Log($"No units listed for {book}."); return; }
        _book.Select(i);
        ShowBook();
        Start();
    }

    private void Start()
    {
        if (_catalogue == null || _map == null || SelectedBook is not { } book || _cts != null) return;
        string root = UnitModelPaths.Root;
        var jobs = new List<(UnitModelMap.Entry Unit, UnitModelMap.ModelRef Ref, string Path)>();
        foreach (var u in _map.ForBook(book))
            foreach (var m in u.Models)
            {
                string path = UnitModelMap.FileFor(root, book, u.Unit, m);
                if (_skip.ButtonPressed && File.Exists(path)) { _skipped++; continue; }
                jobs.Add((u, m, path));
            }
        _total = jobs.Count; _done = 0; _savedCount = 0; _failed = 0;
        _log.Clear();
        Log($"{book}: {jobs.Count} model(s) to fetch{(_skipped > 0 ? $", {_skipped} already there" : "")}.");
        _skipped = 0;
        if (jobs.Count == 0) { _status.Text = "Nothing to fetch - every model is already downloaded."; return; }

        _cts = new CancellationTokenSource();
        _fetchFinished = false;
        _start.Disabled = true; _stop.Disabled = false; _choose.Disabled = true; _book.Disabled = true;
        CancellationToken ct = _cts.Token;
        ForceOrgCatalog cat = _catalogue;
        _ = Task.Run(async () =>
        {
            using var dl = new ModelDownloader();
            foreach (var (unit, mref, path) in jobs)
            {
                if (ct.IsCancellationRequested) break;
                if (!cat.ById.TryGetValue(mref.Id, out var model))
                {
                    Later(() => FailOne($"{unit.Unit} - {mref.Name}: not in this catalogue (id {mref.Id})"));
                    continue;
                }
                try
                {
                    var (files, failed) = await dl.FetchModelAsync(model, ct);
                    if (failed.Count > 0 && !model.Parts.Any(p => files.ContainsKey(p.Mesh)))
                    {
                        Later(() => FailOne($"{unit.Unit} - {mref.Name}: {string.Join("; ", failed.Distinct())}"));
                        continue;
                    }
                    if (failed.Count > 0) Later(() => Log($"{unit.Unit} - {mref.Name}: some parts missing ({string.Join("; ", failed.Distinct())})"));
                    _ready.Enqueue(new Job(model, files, path, $"ForceOrg #{model.Id} \"{model.Name}\" for {book} - {unit.Unit} (wiki: {unit.Warhammer})"));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Later(() => FailOne($"{unit.Unit} - {mref.Name}: {e.Message}")); }
            }
            _fetchFinished = true;
        }, ct);
    }

    /// <summary>Runs on the main thread next frame (the fetch runs on a background thread).</summary>
    private static void Later(Action a) => Callable.From(a).CallDeferred();

    private void Stop()
    {
        if (_cts == null) return;
        _cts.Cancel();
        _status.Text = "Stopping...";
    }

    private void FailOne(string why)
    {
        _failed++; _done++;
        Log("Failed: " + why);
    }

    private void Log(string line) => _log?.AppendText(line + "\n");

    public override void _Process(double delta)
    {
        if (_cts == null) return;
        // One model put together and saved per frame, so the window stays responsive.
        if (_ready.TryDequeue(out Job? job))
        {
            ForceOrgAssembler.Result r = ForceOrgAssembler.Assemble(job.Model, job.Files);
            string? err = r.Error ?? ForceOrgAssembler.Save(r, job.Path, job.Notes);
            r.Root?.Free();
            _done++;
            if (err != null) { _failed++; Log($"Failed: {Path.GetFileNameWithoutExtension(job.Path)}: {err}"); }
            else
            {
                _savedCount++;
                foreach (string w in r.Warnings.Distinct()) Log($"{Path.GetFileNameWithoutExtension(job.Path)}: {w}");
                Saved?.Invoke();
            }
        }
        _bar.Value = _total == 0 ? 0 : (double)_done / _total;
        _status.Text = $"{_done} of {_total}  -  {_savedCount} saved, {_failed} failed";
        if (_fetchFinished && _ready.IsEmpty)
        {
            bool stopped = _cts.IsCancellationRequested;
            _cts.Dispose();
            _cts = null;
            _status.Text = (stopped ? "Stopped. " : "Done. ") + $"{_savedCount} saved, {_failed} failed" + (stopped ? " - press Download to carry on." : ".");
            Log(_status.Text);
            _start.Disabled = false; _stop.Disabled = true; _choose.Disabled = false; _book.Disabled = false;
            ShowBook();
            Saved?.Invoke();
        }
    }

    public override void _ExitTree() => _cts?.Cancel();
}
