using FdgRaylib.Models3D;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab's "Official pictures" window (Kai, 2026-11-14: "start fresh and download the official OPR model
/// pictures"). Downloads each unit's picture from Army Forge (<see cref="OfficialPictures"/>) into UnitImages - the same
/// fetch as the old "Download Unit Pictures" tool, without leaving the game - and can delete the portraits that were
/// made from the 3D models, for a clean start. Official pictures always win over model-made ones
/// (<see cref="HudKit.UnitPicture"/>).
/// </summary>
public partial class OfficialPicturesPopup : Control
{
    public event Action? Saved;

    private OptionButton _game = null!;
    private LineEdit _army = null!;
    private CheckBox _standees = null!;
    private Button _start = null!, _stop = null!, _close = null!, _delete = null!;
    private Label _status = null!, _madeInfo = null!;
    private RichTextLabel _log = null!;
    private CancellationTokenSource? _cts;
    private bool _confirmDelete;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public static OfficialPicturesPopup Open(Control parent)
    {
        var p = new OfficialPicturesPopup();
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(p);
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
        col.AddChild(HudKit.Text("OFFICIAL PICTURES", 30, HudKit.Fg, HudKit.Exo(800)));
        var intro = HudKit.Text("Downloads OPR's own picture of every unit from Army Forge and uses it as the unit's portrait - on the army lists, " +
                                "unit cards, Army Forge and Horde Shop. Pictures you already have are kept; delete one to fetch it again. " +
                                "They stay on this computer.", 15, HudKit.Dim, HudKit.Semi);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(intro);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _game = new OptionButton { Name = "PictureGame", CustomMinimumSize = new Vector2(220, 0), TooltipText = "Which game's army books to fetch pictures for." };
        _game.AddItem("All games");
        foreach (var g in OfficialPictures.Games) _game.AddItem(g.Name);
        row.AddChild(_game);
        _army = new LineEdit { Name = "PictureArmy", PlaceholderText = "Army (blank = all armies)", CustomMinimumSize = new Vector2(260, 0), ClearButtonEnabled = true,
            TooltipText = "Only armies whose name contains this, e.g. Alien Hives." };
        row.AddChild(_army);
        _standees = new CheckBox { Name = "PictureStandees", Text = "Big pictures too", TooltipText = "Also fetch each unit's large picture (used for paper standees). Slower and bigger." };
        row.AddChild(_standees);
        col.AddChild(row);

        _status = HudKit.Text("Pick a game and press Download.", 15, HudKit.Fg, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_status);
        _log = new RichTextLabel { Name = "PictureLog", CustomMinimumSize = new Vector2(0, 220), ScrollFollowing = true, SelectionEnabled = true };
        _log.AddThemeFontSizeOverride("normal_font_size", 13);
        col.AddChild(HudKit.Panel(HudKit.PanelBox(8), _log));

        col.AddChild(HudKit.Heading("Start fresh", 15));
        _madeInfo = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _madeInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_madeInfo);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        _delete = Btn("DeleteMadePortraits", "Delete model portraits", "Delete every portrait that was made from the 3D models, so only the official pictures are used.", DeletePressed);
        buttons.AddChild(_delete);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _start = Btn("StartPictures", "Download", "Fetch the pictures now.", () => _ = Run());
        _stop = Btn("StopPictures", "Stop", "Stop downloading. What is saved stays.", () => { _cts?.Cancel(); _status.Text = "Stopping..."; });
        _close = Btn("ClosePictures", "Close", "Close this window.", () => { _cts?.Cancel(); QueueFree(); });
        _stop.Disabled = true;
        buttons.AddChild(_start);
        buttons.AddChild(_stop);
        buttons.AddChild(_close);
        col.AddChild(buttons);

        HudKit.Place(this, HudKit.Panel(HudKit.ModalBox(14), HudKit.Pad(col, 22, 18, 22, 18)), LayoutPreset.Center);
        ShowMade();
    }

    public override void _Process(double delta)
    {
        while (_lines.TryDequeue(out string? line)) _log.AppendText(line + "\n");
    }

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(130, 38), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private static string MadeFolder => Path.Combine(UnitModelPaths.Root, UnitPortraits.FolderName);

    private void ShowMade()
    {
        int n = Directory.Exists(MadeFolder) ? Directory.EnumerateFiles(MadeFolder, "*.png", SearchOption.AllDirectories).Count() : 0;
        _madeInfo.Text = n == 0
            ? "No portraits made from the 3D models on this computer."
            : $"{n} portrait{(n == 1 ? "" : "s")} made from the 3D models are on this computer. Official pictures are shown first; delete these for a clean start.";
        _delete.Disabled = n == 0;
        _delete.Text = "Delete model portraits";
        _confirmDelete = false;
    }

    private void DeletePressed()
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            _delete.Text = "Press again to delete";
            _status.Text = "This deletes the model-made portraits for good (the 3D models themselves stay). Press again to go ahead.";
            return;
        }
        int gone = OfficialPictures.DeleteModelPortraits();
        HudKit.ForgetPictures();
        _status.Text = $"Deleted {gone} model-made portrait file{(gone == 1 ? "" : "s")}.";
        ShowMade();
        Saved?.Invoke();
    }

    /// <summary>Starts a download for one game (0 = all) and army (the screenshot driver's "labpics:").</summary>
    public void StartFor(int game, string army)
    {
        _game.Select(Math.Clamp(game, 0, OfficialPictures.Games.Count));
        _army.Text = army;
        _ = Run();
    }

    private async Task Run()
    {
        var games = _game.Selected <= 0 ? OfficialPictures.Games.ToList() : new List<OfficialPictures.GameChoice> { OfficialPictures.Games[_game.Selected - 1] };
        string army = _army.Text.Trim();
        bool standees = _standees.ButtonPressed;
        _cts = new CancellationTokenSource();
        _start.Disabled = true;
        _stop.Disabled = false;
        _log.Clear();
        _status.Text = "Downloading from Army Forge...";
        CancellationToken ct = _cts.Token;
        try
        {
            OfficialPictures.Tally tally = await Task.Run(() => OfficialPictures.DownloadAsync(games, army, standees, Shrink, _lines.Enqueue, ct), ct);
            foreach (string f in tally.Failures.Take(20)) _lines.Enqueue("  failed: " + f);
            if (!IsInstanceValid(this)) return;
            _status.Text = "Done: " + tally + ".";
        }
        catch (OperationCanceledException)
        {
            if (!IsInstanceValid(this)) return;
            _status.Text = "Stopped. What was saved stays.";
        }
        catch (Exception e)
        {
            if (!IsInstanceValid(this)) return;
            _status.Text = "Something went wrong: " + e.Message;
        }
        if (!IsInstanceValid(this)) return;
        HudKit.ForgetPictures();
        Saved?.Invoke();
        _start.Disabled = false;
        _stop.Disabled = true;
    }

    /// <summary>Reads a downloaded picture (PNG, JPEG or WebP), shrinks it so its longest side is at most
    /// <paramref name="maxSide"/>, keeps its transparency, and returns it as PNG.</summary>
    public static byte[]? Shrink(byte[] raw, int maxSide)
    {
        var img = new Image();
        Error err = raw.Length > 3 && raw[0] == 0x89 && raw[1] == 'P' ? img.LoadPngFromBuffer(raw)
            : raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 ? img.LoadJpgFromBuffer(raw)
            : raw.Length > 12 && raw[8] == 'W' && raw[9] == 'E' ? img.LoadWebpFromBuffer(raw)
            : Error.FileUnrecognized;
        if (err != Error.Ok || img.IsEmpty()) return null;
        int w = img.GetWidth(), h = img.GetHeight(), big = Math.Max(w, h);
        if (big > maxSide)
        {
            float k = maxSide / (float)big;
            img.Resize(Math.Max(1, (int)MathF.Round(w * k)), Math.Max(1, (int)MathF.Round(h * k)), Image.Interpolation.Lanczos);
        }
        return img.SavePngToBuffer();
    }
}
