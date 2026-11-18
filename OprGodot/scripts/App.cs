using FdgRaylib;
using FdgRaylib.Config;
using Godot;

namespace OprGodot;

/// <summary>
/// The one always-loaded node (autoload "App"): start-up wiring and screen navigation.
///
/// <para>
/// <b>Navigation is a stack</b> (Kai, 2026-11-07: "every Back goes to the previous screen"). Opening a
/// screen hides the one under it rather than freeing it, so Back returns to it exactly as it was left -
/// filters, scroll position, selection. <see cref="Back"/> frees the top screen; <see cref="ToMainMenu"/>
/// unwinds to the bottom.
/// </para>
/// </summary>
public partial class App : Node
{
    public static App I { get; private set; } = null!;

    private Control? _host;
    private readonly Stack<Control> _screens = new();
    /// <summary>The screen on display.</summary>
    public Control? TopScreen => _screens.Count > 0 ? _screens.Peek() : null;
    /// <summary>The look each open screen was built in, so Back puts it back (Hud.Looks).</summary>
    private readonly Dictionary<Control, Hud.Looks.ELook> _looks = new();

    public override void _EnterTree() => I = this;

    public override void _Notification(int what)
    {
        // Pending copies of model settings into the shipped file are written a moment after the last save; don't lose them on quit.
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) FdgRaylib.Models3D.SharedModelSettings.Flush();
    }

    public override void _Ready()
    {
        // Books, Quest data and the default saves folder are found through AppAssets. In the editor that is
        // the repo's FdgRaylib folder (it holds Assets/ and sits beside armies/, Maps/ and heroes/); an
        // exported build carries Assets/ beside the executable.
        AppAssets.Root = OS.HasFeature("editor")
            ? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "FdgRaylib"))
            : Path.GetDirectoryName(OS.GetExecutablePath()) ?? AppAssets.Root;

        // Shared with the Raylib build: same file, same player name, same last host address.
        UserConfig.EnsureExists();
        // Kai's model facings and picture framing go into the game's own files (Assets/Models3D) so every download
        // gets them. Running from the repo, copy what is on this PC into those files now, off the main thread.
        if (FdgRaylib.Models3D.SharedModelSettings.IsAuthority)
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    int n = FdgRaylib.Models3D.SharedSettingsSync.Harvest();
                    GD.Print($"Shared model settings: {n} model(s) recorded in {FdgRaylib.Models3D.SharedModelSettings.RelativePath}.");
                }
                catch (System.Exception e) { GD.PrintErr("Shared model settings: " + e.Message); }
            });
        Hud.TextWrap.Attach(GetTree());
        // An update moves the old .exe/.dlls aside while the game runs; they are free to delete now.
        if (!OS.HasFeature("editor")) FdgRaylib.Update.GameUpdater.CleanUpOldFiles(AppAssets.Root);
        bool scripted = ScriptedRun.Requested(out ScriptedRun run);
        if (UserConfig.Current.StartFullscreen && !OS.HasFeature("editor") && !scripted)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        else if (!scripted && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed)
        {
            // Kai, 2026-11-11: the 1920x1080 window ran off the bottom of the screen (taskbar, scaling). A window
            // that doesn't fit the usable screen starts maximized instead; the picture scales to fit.
            Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            Vector2I size = DisplayServer.WindowGetSize();
            if (size.X > usable.Size.X || size.Y > usable.Size.Y)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        }
        if (scripted) AddChild(run);

        // A newly added UI theme goes on once by itself, so a pulled build shows the pack being tried (Hud.Skin).
        string newest = Hud.Skin.All[^1].Id;
        if (UserConfig.Current.View.UiSkinOffered != newest)
        {
            UserConfig.Current.View.UiSkin = newest;
            UserConfig.Current.View.UiSkinOffered = newest;
            UserConfig.Save();
        }
        // The tactical HUD look for every screen (Kai, 2026-11-10).
        GetTree().Root.Theme = Hud.HudTheme.Build();
        // Menu sounds (2026-11-15), and the saved Master volume from the first frame, not only once Settings is opened.
        Hud.UiSound.Install(this);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Max(UserConfig.Current.View.MasterVolume, 0.0001f)));
    }

    /// <summary>Sees every input first, to know whether the player is on the mouse or the keyboard/controller.</summary>
    public override void _Input(InputEvent e) => Hud.UiJuice.NoteInput(e);

    /// <summary>F11 switches fullscreen on and off on every screen (also in an editor run).</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 }) return;
        bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Fullscreen);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The main scene hands over the control screens are shown in.</summary>
    public void Attach(Control host) => _host = host;

    /// <summary>Opens a screen on top of the current one. <paramref name="init"/> runs before it is shown.</summary>
    public T Open<T>(string scenePath, Action<T>? init = null) where T : Control
    {
        if (_host == null) throw new InvalidOperationException("App.Attach was not called by the main scene.");
        var screen = GD.Load<PackedScene>(scenePath).Instantiate<T>();
        init?.Invoke(screen);
        // A screen that belongs to a system is built in its look; the rest keep the look they were opened in
        // (the main menu and the tools are Grimdark). Set before AddChild - screens read colours as they build.
        Hud.Looks.ELook look = screen is Hud.ILooked l ? l.Look
            : screen is MainMenuScreen ? Hud.Looks.ELook.Grimdark : Hud.Looks.Current.Id;
        Hud.Looks.Use(look, GetTree());
        _looks[screen] = look;
        if (screen is not IOwnBackdrop)
        {
            // Every other screen sits on a dark veil over the 3D table, so text reads.
            var veil = new ColorRect { Color = new Color(0.012f, 0.02f, 0.024f, 0.82f), MouseFilter = Control.MouseFilterEnum.Ignore };
            veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            screen.AddChild(veil);
            screen.MoveChild(veil, 0);
        }
        if (_screens.TryPeek(out Control? under)) under.Visible = false;
        _host.AddChild(screen);
        _screens.Push(screen);
        if (!ScriptedRun.IsRequested) Hud.UiJuice.ScreenIn(screen);
        return screen;
    }

    /// <summary>A screen whose system changed while open (the Army Forge's game picker): rebuild colours in that look.</summary>
    public void SetLook(Control screen, Hud.Looks.ELook look)
    {
        _looks[screen] = look;
        Hud.Looks.Use(look, GetTree());
    }

    /// <summary>Opens the placeholder for a screen not ported yet, saying which one and when it comes.</summary>
    public void OpenNotYet(string screen, string detail) =>
        Open<ComingSoonScreen>(Scenes.ComingSoon, s => s.Setup(screen, detail));

    /// <summary>Closes the top screen and shows the one it was opened from.</summary>
    public void Back()
    {
        if (_screens.Count <= 1) return; // the main menu has no Back
        if (_screens.Peek() is IBackGuard guard && !guard.AllowBack()) return;
        Control leaving = _screens.Pop();
        _looks.Remove(leaving);
        Reveal(_screens.Peek());
        if (ScriptedRun.IsRequested) leaving.QueueFree();
        else Hud.UiJuice.ScreenOut(leaving, leaving.QueueFree);
    }

    /// <summary>Shows a screen again that was under the one closing, with the same fade-in a new one gets.</summary>
    private void Reveal(Control screen)
    {
        if (_looks.TryGetValue(screen, out var look)) Hud.Looks.Use(look, GetTree());
        screen.Visible = true;
        if (!ScriptedRun.IsRequested) Hud.UiJuice.ScreenIn(screen, 12f);
    }

    /// <summary>Runs <paramref name="action"/> a moment later - after screens just closed have let go of things
    /// (a closed lobby releases the network port when it leaves the tree, at the end of the frame).</summary>
    public void Later(Action action) => GetTree().CreateTimer(0.35).Timeout += action; // after Back's 0.14 s fade-out too

    /// <summary>A plain message box over whatever is showing.</summary>
    public void Message(string title, string text)
    {
        var d = new AcceptDialog { Title = title, DialogText = text };
        d.Confirmed += () => d.QueueFree();
        d.Canceled += () => d.QueueFree();
        GetTree().Root.AddChild(d);
        d.PopupCentered();
    }

    /// <summary>Closes screens down to the nearest <typeparamref name="T"/> and shows it (Continue to campaign from a
    /// finished game closes the game and its lobby). Falls back to the main menu when there is none.</summary>
    public void BackTo<T>() where T : Control
    {
        if (!_screens.Any(s => s is T)) { ToMainMenu(); return; }
        while (_screens.Count > 1 && _screens.Peek() is not T) { _looks.Remove(_screens.Peek()); _screens.Pop().QueueFree(); }
        Reveal(_screens.Peek());
    }

    public void ToMainMenu()
    {
        while (_screens.Count > 1) { _looks.Remove(_screens.Peek()); _screens.Pop().QueueFree(); }
        if (_screens.TryPeek(out Control? menu)) Reveal(menu);
    }

    /// <summary>Esc means Back on every screen that has one.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel") && _screens.Count > 1)
        {
            Back();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Quit() => GetTree().Quit();
}

/// <summary>A screen that draws its own shading over the 3D backdrop (the main menu).</summary>
public interface IOwnBackdrop { }

/// <summary>A screen that may hold Back (Esc) - to close an inner view first, or ask before losing changes. When it
/// says no it handles the moment itself (and calls <see cref="App.Back"/> again once the player agrees).</summary>
public interface IBackGuard
{
    bool AllowBack();
}

/// <summary>Scene paths in one place, so a moved file breaks here and nowhere else.</summary>
public static class Scenes
{
    public const string MainMenu = "res://scenes/screens/MainMenu.tscn";
    public const string GameScreen = "res://scenes/screens/GameScreen.tscn";
    public const string Settings = "res://scenes/screens/Settings.tscn";
    public const string ComingSoon = "res://scenes/screens/ComingSoon.tscn";
    public const string Credits = "res://scenes/screens/Credits.tscn";
    public const string JoinPopup = "res://scenes/JoinPopup.tscn";
    public const string Lobby = "res://scenes/screens/Lobby.tscn";
    public const string Battle = "res://scenes/screens/Battle.tscn";
    public const string LoadGame = "res://scenes/screens/LoadGame.tscn";
    public const string AssetLab = "res://scenes/screens/AssetLab.tscn";
    public const string FacingReview = "res://scenes/screens/FacingReview.tscn";
    public const string ModelPicker = "res://scenes/screens/ModelPicker.tscn";
    public const string MatchModels = "res://scenes/screens/MatchModels.tscn";
    public const string ArmyForge = "res://scenes/screens/ArmyForge.tscn";
    public const string HeroBuilder = "res://scenes/screens/HeroBuilder.tscn";
    public const string QuestCampaigns = "res://scenes/screens/QuestCampaigns.tscn";
    public const string ArmyCampaigns = "res://scenes/screens/ArmyCampaigns.tscn";
    public const string MissionSetup = "res://scenes/screens/MissionSetup.tscn";
    public const string MapCampaign = "res://scenes/screens/MapCampaign.tscn";
    public const string MapMaker = "res://scenes/screens/MapMaker.tscn";
    public const string MapEditor = "res://scenes/screens/MapEditor.tscn";
    public const string MatchTerrain = "res://scenes/screens/MatchTerrain.tscn";
}
