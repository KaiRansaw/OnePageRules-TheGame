using FdgRaylib.Models3D;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Lab;

/// <summary>
/// The Asset Lab's "Make portraits" window (Kai, 2026-11-12: an in-game way for players to get unit pictures). Every
/// downloaded model version is photographed at portrait angle A (full body, three-quarter - Kai's pick), so a unit's
/// portrait can show the look picked for it in the army builder (<see cref="UnitPortraits"/>). The army lists, unit cards, Army Forge and Horde Shop then
/// show it in place of the plain icon. Nothing is fetched: it only uses models already downloaded.
/// </summary>
public partial class PortraitMakerPopup : Control
{
    /// <summary>Raised when portraits were saved, so screens can show them.</summary>
    public event Action? Saved;

    private const string AllArmies = "All armies";
    private OptionButton _book = null!;
    private CheckBox _redo = null!, _firstOnly = null!;
    private Button _start = null!, _stop = null!, _close = null!;
    private ProgressBar _bar = null!;
    private Label _status = null!, _info = null!;
    private TextureRect _last = null!;
    private Label _lastName = null!;
    private List<(string Book, string Unit, string Model)> _units = new();
    private bool _running, _stopAsked;

    public static PortraitMakerPopup Open(Control parent)
    {
        var p = new PortraitMakerPopup();
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

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text("MAKE PORTRAITS", 30, HudKit.Fg, HudKit.Exo(800)));
        var intro = HudKit.Text("Takes a picture of each unit's downloaded model and uses it as the unit's portrait - on the army lists, " +
                                "unit cards, Army Forge and Horde Shop. Every version is photographed, so a unit shows the look you picked for it in the Army Forge. " +
                                "Only units with a downloaded model get one.", 15, HudKit.Dim, HudKit.Semi);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(intro);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _book = new OptionButton { Name = "PortraitBook", CustomMinimumSize = new Vector2(360, 0), TooltipText = "The army to make portraits for." };
        _book.ItemSelected += _ => ShowInfo();
        row.AddChild(_book);
        _redo = new CheckBox { Name = "RedoPortraits", Text = "Redo ones already made", TooltipText = "Take the picture again even when the unit already has one (after turning a model in the facing review, say)." };
        _redo.Toggled += _ => ShowInfo();
        row.AddChild(_redo);
        _firstOnly = new CheckBox { Name = "FirstOnly", Text = "First version only", TooltipText = "One picture per unit (its first version) - quicker. Units with another look picked then show the first version's picture." };
        _firstOnly.Toggled += _ => { Reload(); ShowInfo(); };
        row.AddChild(_firstOnly);
        col.AddChild(row);
        _info = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_info);

        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 16);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 8);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(0, 22), MaxValue = 1, ShowPercentage = true };
        left.AddChild(_bar);
        _status = HudKit.Text("", 15, HudKit.Fg, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_status);
        mid.AddChild(left);
        var right = new VBoxContainer();
        _last = new TextureRect { CustomMinimumSize = new Vector2(160, 160), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        right.AddChild(HudKit.Panel(HudKit.PanelBox(8), _last));
        _lastName = HudKit.Text("", 13, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        _lastName.CustomMinimumSize = new Vector2(164, 0);
        _lastName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_lastName);
        mid.AddChild(right);
        col.AddChild(mid);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        _start = Btn("StartPortraits", "Make portraits", "Take the pictures now. About a second each.", () => _ = Run());
        _stop = Btn("StopPortraits", "Stop", "Stop after the picture being taken. What is saved stays.", () => { _stopAsked = true; _status.Text = "Stopping..."; });
        _close = Btn("ClosePortraits", "Close", "Close this window.", () => { _stopAsked = true; QueueFree(); });
        _stop.Disabled = true;
        buttons.AddChild(_start);
        buttons.AddChild(_stop);
        buttons.AddChild(_close);
        col.AddChild(buttons);

        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 22, 18, 22, 18)), LayoutPreset.Center);

        _units = UnitPortraits.Models();
        _books = _units.Select(u => u.Book).Distinct().ToList();
        _book.AddItem(AllArmies);
        foreach (string b in _books) _book.AddItem(b);
        Reload();
        ShowInfo();
    }

    private List<string> _books = new();

    private void Reload() => _units = UnitPortraits.Models(_firstOnly.ButtonPressed);

    private static Button Btn(string name, string text, string tip, Action onPressed)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(130, 38), FocusMode = FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    private string? SelectedBook => _book.Selected <= 0 ? null : _books[_book.Selected - 1];

    /// <summary>The units this run would photograph.</summary>
    private List<(string Book, string Unit, string Model)> Todo() =>
        _units.Where(u => SelectedBook == null || u.Book == SelectedBook)
              .Where(u => _redo.ButtonPressed || !File.Exists(UnitPortraits.PathFor(u.Model)))
              .ToList();

    private void ShowInfo()
    {
        if (_units.Count == 0)
        {
            _info.Text = "No models downloaded yet - use Download models... first.";
            _start.Disabled = true;
            return;
        }
        var mine = _units.Where(u => SelectedBook == null || u.Book == SelectedBook).ToList();
        int units = mine.Select(u => (u.Book, u.Unit)).Distinct().Count();
        int todo = Todo().Count;
        _info.Text = $"{units} unit{(units == 1 ? "" : "s")}, {mine.Count} model version{(mine.Count == 1 ? "" : "s")}; {mine.Count - (_redo.ButtonPressed ? 0 : todo)} already photographed. " +
                     $"{todo} to take (about {Math.Max(1, (int)Math.Ceiling(todo / 60.0))} minute{(todo > 60 ? "s" : "")}).";
        _start.Disabled = _running || todo == 0;
    }

    /// <summary>Starts a run for an army by name, or "*" for every army (the screenshot driver uses this).</summary>
    public void StartFor(string book)
    {
        if (book != "*")
        {
            int i = _books.FindIndex(b => b.Equals(book, StringComparison.OrdinalIgnoreCase));
            if (i < 0) { _status.Text = $"No models for {book}."; return; }
            _book.Select(i + 1);
        }
        ShowInfo();
        _ = Run();
    }

    private async Task Run()
    {
        if (_running) return;
        var todo = Todo();
        if (todo.Count == 0) return;
        _running = true; _stopAsked = false;
        _start.Disabled = true; _book.Disabled = true; _redo.Disabled = true; _firstOnly.Disabled = true; _stop.Disabled = false;
        var studio = new PortraitShot.Studio();
        AddChild(studio);
        PortraitShot.Angle angle = PortraitShot.Portrait;
        int done = 0, saved = 0, failed = 0;
        foreach (var (book, unit, model) in todo)
        {
            if (_stopAsked || !IsInsideTree()) break;
            _status.Text = $"{done} of {todo.Count} - {book}: {unit}";
            if (!studio.Show(model, out string? why)) { failed++; GD.PrintErr($"Portrait: {model}: {why}"); }
            else
            {
                studio.Aim(angle);
                for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!IsInsideTree()) break;
                try
                {
                    string path = UnitPortraits.PathFor(model);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    Image img = studio.Grab();
                    Error e = img.SavePng(path);
                    if (e != Error.Ok) throw new IOException(e.ToString());
                    saved++;
                    _last.Texture = ImageTexture.CreateFromImage(img);
                    _lastName.Text = $"{unit}\n{Path.GetFileNameWithoutExtension(model)}";
                }
                catch (Exception ex) { failed++; GD.PrintErr($"Portrait: {unit}: {ex.Message}"); }
            }
            done++;
            _bar.Value = (double)done / todo.Count;
        }
        studio.QueueFree();
        if (!IsInsideTree()) return;
        _running = false;
        _status.Text = (_stopAsked ? "Stopped. " : "Done. ") + $"{saved} portrait{(saved == 1 ? "" : "s")} saved" + (failed > 0 ? $", {failed} could not be taken (the model would not load)." : ".");
        _book.Disabled = false; _redo.Disabled = false; _firstOnly.Disabled = false; _stop.Disabled = true;
        HudKit.ForgetPictures();
        ShowInfo();
        if (saved > 0) Saved?.Invoke();
    }
}
