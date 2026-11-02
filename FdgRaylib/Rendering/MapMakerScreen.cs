using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Map Maker, from the main menu (Kai, 2026-11-02: a button between Army Forge and Settings).
///
/// <para>
/// The editor itself already existed - it was reachable only from inside the lobby's Custom Map picker,
/// which is a strange place to keep the tool that MAKES the maps, and which is going away now that the
/// map picker replaces the terrain modes. This is the same <see cref="MapEditor"/>, given a front door:
/// a list of the maps on disk, New, Edit, Delete.
/// </para>
///
/// <para>
/// A map is terrain, objective markers and deployment zones - all three already live in the saved file -
/// so once the picker replaces the lobby's objective and terrain settings, this is where those decisions
/// get made instead.
/// </para>
/// </summary>
public sealed class MapMakerScreen : IAppScreen
{
    public Action? OnBack;

    private readonly MapEditor _editor = new();
    private IReadOnlyList<string> _maps = Array.Empty<string>();
    private string? _selected;
    private string? _pendingDelete;
    private string _message = "";

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);

    public void OnShown() => Reload();

    private void Reload()
    {
        _maps = FdgRaylib.SaveLoad.MapPaths.List();
        if (_selected != null && !_maps.Contains(_selected)) _selected = null;
        _selected ??= _maps.Count > 0 ? _maps[0] : null;
    }

    public void Draw(int screenW, int screenH)
    {
        // The editor takes the whole screen while it is open, exactly as it does over the lobby.
        if (_editor.IsOpen)
        {
            _editor.Draw(screenW, screenH);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##mapmaker",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar);

        float margin = 24f;
        ImGui.SetCursorPos(new Vector2(margin, margin));
        UiText.Colored(Accent, "MAP MAKER");
        ImGui.SameLine();
        UiText.Colored(Dim, "   terrain, objective markers and deployment zones");
        ImGui.Separator();

        float listH = screenH - ImGui.GetCursorPosY() - 110f;
        ImGui.BeginChild("##maplist", new Vector2(screenW * 0.45f, listH), ImGuiChildFlags.Borders);
        if (_maps.Count == 0)
        {
            UiText.Wrapped("No maps yet. Press New Map to draw one - it will show up in the map picker "
                           + "when you start a game.");
        }
        foreach (string path in _maps)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (ImGui.Selectable($"{name}##{path}", path == _selected)) _selected = path;
        }
        ImGui.EndChild();

        Vector2 btn = new(190f, 42f);
        float by = screenH - 78f;

        ImGui.SetCursorPos(new Vector2(margin, by));
        if (UiButton.Confirm("New Map", btn))
        {
            _editor.OnSaved ??= _ => { Reload(); _message = "Map saved."; };
            _editor.OpenNew();
        }

        ImGui.SetCursorPos(new Vector2(margin + btn.X + 12f, by));
        ImGui.BeginDisabled(_selected == null);
        if (UiButton.Navigate("Edit", btn))
        {
            _editor.OnSaved ??= _ => { Reload(); _message = "Map saved."; };
            _editor.OpenExisting(_selected!);
        }
        ImGui.SetCursorPos(new Vector2(margin + (btn.X + 12f) * 2f, by));
        if (UiButton.Back(_pendingDelete == _selected ? "Really delete?" : "Delete", btn))
        {
            if (_pendingDelete == _selected && _selected != null)
            {
                try { File.Delete(_selected); _message = "Map deleted."; }
                catch (Exception ex) { _message = "Couldn't delete it: " + ex.Message; }
                _pendingDelete = null;
                _selected = null;
                Reload();
            }
            else _pendingDelete = _selected;
        }
        ImGui.EndDisabled();

        ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X, by));
        if (UiButton.Back("Back", btn) || ImGui.IsKeyPressed(ImGuiKey.Escape, false)) OnBack?.Invoke();

        if (_message.Length > 0)
        {
            ImGui.SetCursorPos(new Vector2(margin, by - 28f));
            UiText.Colored(Dim, _message);
        }

        ImGui.End();
        ImGui.PopStyleColor();
    }
}
