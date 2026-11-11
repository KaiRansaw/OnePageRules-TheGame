using Godot;

namespace OprGodot;

/// <summary>
/// A test driver for checking screens without a person at the keyboard. Started by the user arguments after
/// <c>--</c> on the Godot command line, and does nothing otherwise:
/// <list type="bullet">
/// <item><c>--press=NameA,NameB</c> - presses the buttons with those node names, one after another, a few frames apart.</item>
/// <item><c>--shot=path.png</c> - saves a screenshot of the window once the presses are done, then quits.</item>
/// <item><c>--size=1920x1080</c> - window size for the screenshot.</item>
/// </list>
/// Example: <c>godot --path OprGodot -- --press=GrimdarkFuture --shot=/tmp/gf.png</c>.
/// </summary>
public partial class ScriptedRun : Node
{
    private readonly Queue<string> _presses = new();
    private string? _shot;
    private int _wait = 20;

    /// <summary>True when the command line asks for a scripted run (no node made).</summary>
    public static bool IsRequested => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--press=") || a.StartsWith("--shot="));

    public static bool Requested(out ScriptedRun run)
    {
        run = new ScriptedRun();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--press="))
                foreach (string name in arg["--press=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    run._presses.Enqueue(name);
            else if (arg.StartsWith("--shot=")) run._shot = arg["--shot=".Length..];
            else if (arg.StartsWith("--size=") && arg["--size=".Length..].Split('x') is [var w, var h]
                     && int.TryParse(w, out int width) && int.TryParse(h, out int height))
                DisplayServer.WindowSetSize(new Vector2I(width, height));
        }
        return run._presses.Count > 0 || run._shot != null;
    }

    public override void _Process(double delta)
    {
        if (--_wait > 0) return;
        _wait = 15;

        if (_presses.TryDequeue(out string? name))
        {
            // "wait:N" waits N more frames (a lobby filling its seats, a scan finishing).
            if (name.StartsWith("wait:") && int.TryParse(name[5..], out int frames)) { _wait = frames; return; }
            // "tab:Name" opens the tab called Name in whatever TabContainer has one.
            if (name.StartsWith("tab:")) { OpenTab(GetTree().Root, name[4..]); return; }
            // Real mouse clicks through the whole input pipeline, on the battle table:
            // "clickmine" (your first ready unit), "clickenemy" (the enemy nearest it), "clickrel:dx;dz"
            // (the selected unit's middle + dx,dz inches), "hoverrel:dx;dz" (the same, pointer only).
            if (name.StartsWith("click") || name.StartsWith("hover")) { TableMouse(name); return; }
            if (FindButton(GetTree().Root, name) is { } button) button.EmitSignal(BaseButton.SignalName.Pressed);
            else GD.PrintErr($"ScriptedRun: no button named '{name}' on screen.");
            return;
        }

        if (_shot != null)
        {
            Image image = GetViewport().GetTexture().GetImage();
            Error saved = image.SavePng(_shot);
            GD.Print($"ScriptedRun: screenshot {(saved == Error.Ok ? "saved" : "FAILED")} - {_shot}");
        }
        GetTree().Quit();
    }

    private void TableMouse(string step)
    {
        if (Find<Battle.BattleScreen>(GetTree().Root) is not { } battle) { GD.PrintErr("ScriptedRun: no battle on screen."); return; }
        var mine = battle.Session.Table.Units.Objects.Where(u => battle.Session.IsMine(u.PlayerID) && battle.Table.UnitCentre(u) != null).ToList();
        FDG.IUnit? Pick() => battle.SelectedUnit ?? mine.FirstOrDefault();
        Vector3? world = null;
        if (step == "clickmine") world = mine.Select(u => battle.Table.UnitCentre(u)).FirstOrDefault(c => c != null);
        else if (step == "clickenemy" && Pick() is { } me && battle.Table.UnitCentre(me) is { } from)
            world = battle.Session.Table.Units.Objects.Where(u => !battle.Session.IsMine(u.PlayerID))
                .Select(u => battle.Table.UnitCentre(u)).Where(c => c != null).OrderBy(c => c!.Value.DistanceTo(from)).FirstOrDefault();
        else if (step.Contains(':') && Pick() is { } sel && battle.Table.UnitCentre(sel) is { } c)
        {
            string[] xy = step[(step.IndexOf(':') + 1)..].Split(';');
            world = c + new Vector3(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), 0, -float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        if (world is not { } w) { GD.PrintErr($"ScriptedRun: nothing to {step}."); return; }
        Vector2 at = battle.Table.Camera.UnprojectPosition(w);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        if (step.StartsWith("hover")) return;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        GD.Print($"ScriptedRun: {step} at {at}");
    }

    private static T? Find<T>(Node node) where T : Node
    {
        if (node is T t) return t;
        foreach (Node child in node.GetChildren())
            if (Find<T>(child) is { } f) return f;
        return null;
    }

    private static bool OpenTab(Node node, string name)
    {
        if (node is TabContainer tabs)
            for (int i = 0; i < tabs.GetTabCount(); i++)
                if (tabs.GetTabControl(i).Name == name) { tabs.CurrentTab = i; return true; }
        foreach (Node child in node.GetChildren())
            if (OpenTab(child, name)) return true;
        return false;
    }

    private static BaseButton? FindButton(Node node, string name)
    {
        if (node is BaseButton b && b.Name == name && b.IsVisibleInTree()) return b;
        foreach (Node child in node.GetChildren())
            if (FindButton(child, name) is { } found) return found;
        return null;
    }
}
