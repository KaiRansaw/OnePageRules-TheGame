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

    public override void _EnterTree() => I = this;

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

        // The tactical HUD look for every screen (Kai, 2026-11-10).
        GetTree().Root.Theme = Hud.HudTheme.Build();
    }

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
        return screen;
    }

    /// <summary>Opens the placeholder for a screen not ported yet, saying which one and when it comes.</summary>
    public void OpenNotYet(string screen, string detail) =>
        Open<ComingSoonScreen>(Scenes.ComingSoon, s => s.Setup(screen, detail));

    /// <summary>Closes the top screen and shows the one it was opened from.</summary>
    public void Back()
    {
        if (_screens.Count <= 1) return; // the main menu has no Back
        _screens.Pop().QueueFree();
        _screens.Peek().Visible = true;
    }

    /// <summary>Runs <paramref name="action"/> a moment later - after screens just closed have let go of things
    /// (a closed lobby releases the network port when it leaves the tree, at the end of the frame).</summary>
    public void Later(Action action) => GetTree().CreateTimer(0.2).Timeout += action;

    /// <summary>A plain message box over whatever is showing.</summary>
    public void Message(string title, string text)
    {
        var d = new AcceptDialog { Title = title, DialogText = text };
        d.Confirmed += () => d.QueueFree();
        d.Canceled += () => d.QueueFree();
        GetTree().Root.AddChild(d);
        d.PopupCentered();
    }

    public void ToMainMenu()
    {
        while (_screens.Count > 1) _screens.Pop().QueueFree();
        if (_screens.TryPeek(out Control? menu)) menu.Visible = true;
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

/// <summary>Scene paths in one place, so a moved file breaks here and nowhere else.</summary>
public static class Scenes
{
    public const string MainMenu = "res://scenes/screens/MainMenu.tscn";
    public const string GameScreen = "res://scenes/screens/GameScreen.tscn";
    public const string Settings = "res://scenes/screens/Settings.tscn";
    public const string ComingSoon = "res://scenes/screens/ComingSoon.tscn";
    public const string JoinPopup = "res://scenes/JoinPopup.tscn";
    public const string Lobby = "res://scenes/screens/Lobby.tscn";
    public const string Battle = "res://scenes/screens/Battle.tscn";
    public const string LoadGame = "res://scenes/screens/LoadGame.tscn";
    public const string AssetLab = "res://scenes/screens/AssetLab.tscn";
    public const string FacingReview = "res://scenes/screens/FacingReview.tscn";
    public const string ModelPicker = "res://scenes/screens/ModelPicker.tscn";
    public const string MatchModels = "res://scenes/screens/MatchModels.tscn";
}
