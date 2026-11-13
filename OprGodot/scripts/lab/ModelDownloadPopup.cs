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

    /// <summary>One file to save: put together from fetched parts, or (<see cref="LinkFrom"/>) the same model already saved for another unit.</summary>
    private sealed record Job(ForceOrgCatalog.Model? Model, Dictionary<string, string> Files, string Path, string Notes, string? LinkFrom = null);
    private Button _all = null!, _allFirst = null!, _clearCache = null!, _pick = null!;
    private CheckBox _onePerUnit = null!;

    /// <summary>The versions of a unit this run takes: the first only, or all.</summary>
    private IEnumerable<UnitModelMap.ModelRef> Versions(UnitModelMap.Entry u) => _onePerUnit.ButtonPressed ? u.Models.Take(1) : u.Models;

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
        // Kai, 2026-11-12: one model per unit - always the first on the list, so every player gets the same one.
        _onePerUnit = new CheckBox { Name = "OnePerUnit", Text = "One model per unit", TooltipText = "Only the first model on each unit's list (about a fifth of the space). Everyone who ticks this gets the same model for a unit. Untick for every version." };
        _onePerUnit.Toggled += _ => ShowBook();
        bookRow.AddChild(_onePerUnit);
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
        _start = Btn("StartDownload", "Download", "Fetch and save every model for the units of this army.", () => Start(false));
        _all = Btn("DownloadAll", "Download all armies...", "Every army on the list, one after another. Models already downloaded are skipped; the same model in several armies is stored once.", () => ConfirmAll(false));
        // Kai, 2026-11-12: "Auto download 1 model of each model for all the units at once" - the first version of every
        // unit in every army, so everyone has something to see on the table instead of the stand-in token.
        _allFirst = Btn("DownloadAllFirst", "Download 1 of each...", "Every army on the list, but only the first model of each unit - about a fifth of the space of Download all. Every unit then shows a real model instead of the stand-in.", () => ConfirmAll(true));
        _clearCache = Btn("ClearCache", "Clear download cache", "Delete the raw downloaded parts (UnitModels/_cache). Your finished models are kept; a later download fetches parts again.", ConfirmClearCache);
        _stop = Btn("StopDownload", "Stop", "Stop after the models already on their way. What is saved stays; the next run carries on.", Stop);
        _close = Btn("CloseDownload", "Close", "Close this window.", () => { Stop(); QueueFree(); });
        _stop.Disabled = true;
        _pick = Btn("ChooseModels", "Choose models...", "Look through each unit's versions - each one you click is fetched and shown in 3D - and tick the ones to download.", OpenPicker);
        buttons.AddChild(_clearCache);
        buttons.AddChild(_pick);
        buttons.AddChild(_allFirst);
        buttons.AddChild(_all);
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

    private string? _mapPath;

    private void LoadData(string? catalogue, string? map)
    {
        _mapPath = map;
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
        int models = units.Sum(u => Versions(u).Count());
        int have = units.Sum(u => Versions(u).Count(m => File.Exists(UnitModelMap.FileFor(UnitModelPaths.Root, book, u.Unit, m))));
        string broad = units.Any(u => u.TooBroad) ? $"  {units.Count(u => u.TooBroad)} unit(s) matched more models than kept - worth a look." : "";
        _bookInfo.Text = $"{units.Count} units, {models} model{(_onePerUnit.ButtonPressed ? "s (one each)" : " versions")} ({have} already downloaded). Units in the book with no model on the list are left as they are.{broad}";
    }

    // ------------------------------------------------------------------ the run

    /// <summary>Starts a run for a book by name (also used by the screenshot driver).</summary>
    public void StartFor(string book)
    {
        if (_map == null) return;
        if (book == "*") { Start(true); return; } // every army
        int i = _map.Books.ToList().FindIndex(b => b.Equals(book, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Log($"No units listed for {book}."); return; }
        _book.Select(i);
        ShowBook();
        Start(false);
    }

    /// <summary>The models still to save for these books: (book, unit, model, where it goes).</summary>
    private List<(string Book, UnitModelMap.Entry Unit, UnitModelMap.ModelRef Ref, string Path)> JobsFor(IEnumerable<string> books)
    {
        string root = UnitModelPaths.Root;
        var jobs = new List<(string, UnitModelMap.Entry, UnitModelMap.ModelRef, string)>();
        _skipped = 0;
        foreach (string b in books)
            foreach (var u in _map!.ForBook(b))
                foreach (var m in Versions(u))
                {
                    string path = UnitModelMap.FileFor(root, b, u.Unit, m);
                    if (_skip.ButtonPressed && File.Exists(path)) { _skipped++; continue; }
                    jobs.Add((b, u, m, path));
                }
        return jobs;
    }

    /// <summary>Every model already saved anywhere in UnitModels, by catalogue id (the "(id)" in its file name).</summary>
    private static Dictionary<int, string> SavedById()
    {
        var found = new Dictionary<int, string>();
        string root = UnitModelPaths.Root;
        if (!Directory.Exists(root)) return found;
        var rx = new System.Text.RegularExpressions.Regex(@"\((\d+)\)\.glb$");
        foreach (string f in Directory.EnumerateFiles(root, "*.glb", SearchOption.AllDirectories))
            if (rx.Match(f) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int id)) found.TryAdd(id, f);
        return found;
    }

    private void ConfirmAll(bool firstOnly)
    {
        if (_catalogue == null || _map == null || _cts != null) return;
        _onePerUnit.ButtonPressed = firstOnly;
        var jobs = JobsFor(_map.Books);
        var saved = SavedById();
        int fresh = jobs.Select(j => j.Ref.Id).Distinct().Count(id => !saved.ContainsKey(id));
        // Alien Hives came to ~21 MB per model file and ~4 MB of raw parts per model (Kai's download, 2026-11-12).
        double gb = fresh * 25 / 1024.0;
        string free = "";
        try { free = $" You have {new DriveInfo(Path.GetPathRoot(Path.GetFullPath(UnitModelPaths.Root))!).AvailableFreeSpace / 1e9:0} GB free on that drive."; }
        catch (Exception) { }
        var dlg = new ConfirmationDialog
        {
            Title = firstOnly ? "Download 1 model of each unit?" : "Download all armies?",
            DialogText = (firstOnly ? "The first model of every unit, in every army.\n" : "") + $"{jobs.Count} model files for {_map.Books.Count()} armies ({_skipped} already there are skipped).\n" +
                         $"{fresh} of them are new models to fetch - roughly {gb:0} GB.{free}\n" +
                         "The same model in several armies is stored once. It can take hours; Stop keeps what is saved and the next run carries on.",
            OkButtonText = firstOnly ? "Download 1 of each" : "Download all",
        };
        dlg.Confirmed += () => { Start(true); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ConfirmClearCache()
    {
        string dir = Path.Combine(UnitModelPaths.Root, ModelDownloader.CacheFolderName);
        long bytes = Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length) : 0;
        var dlg = new ConfirmationDialog
        {
            Title = "Clear the download cache?",
            DialogText = $"Delete {bytes / 1e9:0.0} GB of raw downloaded parts? Your finished models stay. Downloading a model again fetches its parts again.",
            OkButtonText = "Delete them",
        };
        dlg.Confirmed += () =>
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); Log($"Download cache cleared ({bytes / 1e9:0.0} GB)."); }
            catch (Exception e) { Log("Could not clear the cache: " + e.Message); }
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void OpenPicker()
    {
        if (_catalogue == null || _map == null) { Log("Choose the catalogue file first."); return; }
        App.I.Open<ModelPickerScreen>(Scenes.ModelPicker, p => p.Setup(_catalogue, _map, SelectedBook, _catPath, _mapPath));
        QueueFree();
    }

    private void Start(bool all)
    {
        if (_catalogue == null || _map == null || _cts != null) return;
        if (!all && SelectedBook == null) return;
        string[] books = all ? _map.Books.ToArray() : new[] { SelectedBook! };
        RunJobs(JobsFor(books), all ? $"All {books.Length} armies" : books[0]);
    }

    /// <summary>
    /// Fetches, puts together and saves exactly these models (also used by the model picker, which hands over the ticked
    /// versions). A model already saved under another unit is linked rather than fetched.
    /// </summary>
    public void RunJobs(List<(string Book, UnitModelMap.Entry Unit, UnitModelMap.ModelRef Ref, string Path)> jobs, string title)
    {
        if (_catalogue == null || _cts != null) return;
        _total = jobs.Count; _done = 0; _savedCount = 0; _failed = 0;
        _log.Clear();
        Log($"{title}: {jobs.Count} model file(s) to save{(_skipped > 0 ? $", {_skipped} already there" : "")}.");
        _skipped = 0;
        if (jobs.Count == 0) { _status.Text = "Nothing to fetch - every model is already downloaded."; return; }

        _cts = new CancellationTokenSource();
        _fetchFinished = false;
        _start.Disabled = true; _all.Disabled = true; _allFirst.Disabled = true; _clearCache.Disabled = true; _pick.Disabled = true; _stop.Disabled = false; _choose.Disabled = true; _book.Disabled = true;
        CancellationToken ct = _cts.Token;
        ForceOrgCatalog cat = _catalogue;
        var seen = SavedById(); // a model already saved for another unit is linked, not fetched again
        _ = Task.Run(async () =>
        {
            using var dl = new ModelDownloader();
            string lastBook = "";
            foreach (var (book, unit, mref, path) in jobs)
            {
                if (ct.IsCancellationRequested) break;
                if (book != lastBook) { lastBook = book; string b = book; Later(() => Log($"-- {b}")); }
                string notes = $"ForceOrg #{mref.Id} \"{mref.Name}\" for {book} - {unit.Unit} (wiki: {unit.Warhammer})";
                if (seen.TryGetValue(mref.Id, out string? already))
                {
                    _ready.Enqueue(new Job(null, new(), path, notes, already));
                    continue;
                }
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
                    _ready.Enqueue(new Job(model, files, path, notes));
                    seen[mref.Id] = path;
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
        // One model put together and saved per frame, so the window stays responsive. Links (a model already saved for
        // another unit) are cheap, so several of those go in a frame.
        for (int links = 0; links < 25 && _ready.TryPeek(out Job? peek); links++)
        {
            _ready.TryDequeue(out Job? job);
            _done++;
            if (job!.LinkFrom != null)
            {
                string? err = LinkSaved(job);
                if (err != null) { _failed++; Log($"Failed: {Path.GetFileNameWithoutExtension(job.Path)}: {err}"); }
                else _savedCount++;
                continue;
            }
            ForceOrgAssembler.Result r = ForceOrgAssembler.Assemble(job.Model!, job.Files);
            string? e = r.Error ?? ForceOrgAssembler.Save(r, job.Path, job.Notes);
            r.Root?.Free();
            if (e != null) { _failed++; Log($"Failed: {Path.GetFileNameWithoutExtension(job.Path)}: {e}"); }
            else
            {
                _savedCount++;
                foreach (string w in r.Warnings.Distinct()) Log($"{Path.GetFileNameWithoutExtension(job.Path)}: {w}");
            }
            break; // one put-together model per frame
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
            _start.Disabled = false; _all.Disabled = false; _allFirst.Disabled = false; _clearCache.Disabled = false; _pick.Disabled = false; _stop.Disabled = true; _choose.Disabled = false; _book.Disabled = false;
            ShowBook();
            Saved?.Invoke();
        }
    }

    /// <summary>
    /// The same catalogue model, already saved for another unit: the file is linked (no extra space) and its settings
    /// copied - so a facing fixed once holds for every army that uses the model.
    /// </summary>
    private static string? LinkSaved(Job job)
    {
        if (!File.Exists(job.LinkFrom)) return "the model it shares was not saved";
        try
        {
            FileLinks.LinkOrCopy(job.LinkFrom!, job.Path);
            var s = UnitModelSettings.Load(job.LinkFrom!) ?? new UnitModelSettings();
            s.Notes = job.Notes;
            s.Save(job.Path);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public override void _ExitTree() => _cts?.Cancel();
}
