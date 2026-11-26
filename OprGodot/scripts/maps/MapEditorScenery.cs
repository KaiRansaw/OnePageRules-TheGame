using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Maps;

/// <summary>
/// The scenery round the table (Kai, 2026-11-26, option C): the Battlefield window sets the set, density and seed
/// (Reroll); "Edit by hand" opens this tool, which starts from the generated pieces and lets you place, move, turn,
/// size and delete single ones. Saved with the map (FdgApp <see cref="MapScenery"/>). Looks only.
/// </summary>
public partial class MapEditorScreen
{
    private string? _sceneryModel;      // the palette pick to place, or null
    private int _scenerySel = -1;       // the selected piece
    private bool _sceneryDragging;
    private MeshInstance3D? _sceneryRing;

    private MapScenery Scenery => MapScenery.Parse(Draft.Scenery);

    private List<MapScenery.Piece> SceneryPieces(MapScenery s) =>
        s.Resolve(MapAtmosphere.HorizonFor(HorizonId).ToString(), W, D);

    /// <summary>The Battlefield window's scenery rows.</summary>
    private void SceneryRows(VBoxContainer col, Action reopen)
    {
        MapScenery s = Scenery;
        col.AddChild(HudKit.Heading("Scenery round the table", 14));
        col.AddChild(Forge.ForgeWidgets.Wrapped(s.Pieces == null
            ? "Trees, bushes and rocks on the land round the table (Settings -> Around the table: Tabletop or Battlefield). Pick a set and how much, Reroll until you like it, then Edit by hand to move single pieces."
            : $"Edited by hand: {s.Pieces.Count} pieces. Edit by hand to change them, or Back to generated to start again from the set.", 13, HudKit.Dim));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var set = new OptionButton { Name = "ScenerySet", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(260, 0), Disabled = s.Pieces != null,
            TooltipText = "Which trees and rocks: Auto follows the horizon style (woods, dead trees...)." };
        foreach (var (_, name) in MapScenery.Sets) set.AddItem("Scenery: " + name);
        set.Selected = Math.Max(0, MapScenery.Sets.ToList().FindIndex(x => x.Id == s.Set));
        set.ItemSelected += i => { Draft.Remember(); s.Set = MapScenery.Sets[(int)i].Id; Draft.Scenery = s.ToText(); RebuildWorld(); reopen(); };
        row.AddChild(set);
        row.AddChild(HudKit.Text("How much", 14, HudKit.Fg, HudKit.Semi));
        var dens = new HSlider { Name = "SceneryDensity", MinValue = 0, MaxValue = 200, Step = 10, Value = s.Density * 100, CustomMinimumSize = new Vector2(220, 24),
            Editable = s.Pieces == null, TooltipText = "0% = nothing round the table, 100% = the usual, 200% = a thick wood." };
        var dval = HudKit.Text($"{s.Density * 100:0}%", 14, HudKit.Fg, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(56, 0));
        dens.DragStarted += () => Draft.Remember();
        dens.ValueChanged += v => { dval.Text = $"{v:0}%"; s.Density = (float)v / 100f; Draft.Scenery = s.ToText(); };
        dens.DragEnded += _ => RebuildWorld();
        row.AddChild(dens);
        row.AddChild(dval);
        row.AddChild(Ui.Button("Reroll", () => { Draft.Remember(); s.Seed++; Draft.Scenery = s.ToText(); RebuildWorld(); reopen(); },
            "Shuffle where the pieces go.").Also(b => { b.Name = "SceneryReroll"; b.Disabled = s.Pieces != null; }));
        row.AddChild(Ui.Grow());
        if (s.Pieces != null)
            row.AddChild(Ui.Button("Back to generated", () => { Draft.Remember(); s.Pieces = null; Draft.Scenery = s.ToText(); RebuildWorld(); reopen(); },
                "Throw away the hand-placed pieces and use the set again.").Also(b => b.Name = "SceneryGenerated"));
        row.AddChild(Ui.Button("Edit by hand", () => { GetNodeOrNull("Battlefield")?.QueueFree(); StartScenery(); },
            "Place, move, turn, size and delete single pieces.").Also(b => b.Name = "SceneryEdit"));
        col.AddChild(row);
    }

    private void StartScenery()
    {
        MapScenery s = Scenery;
        if (s.Pieces == null)
        {
            Draft.Remember();
            s.Pieces = SceneryPieces(s);
            Draft.Scenery = s.ToText();
        }
        _tool = ETool.Scenery;
        _scenerySel = -1;
        _sceneryModel = null;
        _picked.Clear();
        _sel = null;
        // Step back so the land round the table is in view.
        _topView = false;
        _pitch = 35f;
        _distance = Mathf.Max(_distance, 420f * Horizon.DistanceScale);
        ApplyCamera();
        Say("Scenery: click a piece to pick it (drag to move, Q/E turn, +/- size, Del removes), or pick a model on the left and click the land.");
        RebuildWorld();
        Rebuild();
    }

    private void StopScenery()
    {
        Horizon.FollowCamera(_world, _distance);
        _tool = ETool.Select;
        _scenerySel = -1;
        _sceneryModel = null;
        _sceneryRing?.QueueFree();
        _sceneryRing = null;
        Say("");
        Rebuild();
    }

    /// <summary>The left panel while the scenery tool is on: the models to place.</summary>
    private void SceneryPanel()
    {
        _left.AddChild(HudKit.Heading("Scenery", 13));
        _left.AddChild(Forge.ForgeWidgets.Wrapped("Pick a model, then click the land to place it. Click a piece to pick it: drag moves it, Q / E turn, + / - size, Del removes. Right-click stops placing.", 12, HudKit.Dim));
        var top = new HBoxContainer();
        top.AddChild(Ui.Button("Done", StopScenery, "Back to the table tools.").Also(b => b.Name = "SceneryDone"));
        top.AddChild(Ui.Button("Back to generated", () =>
        {
            Draft.Remember();
            var s = Scenery;
            s.Pieces = null;
            Draft.Scenery = s.ToText();
            StopScenery();
            RebuildWorld();
        }, "Throw away the hand-placed pieces and use the set again.").Also(b => b.Name = "SceneryGenerated"));
        _left.AddChild(top);
        foreach (var (group, models) in MapScenery.Palette)
        {
            _left.AddChild(HudKit.Heading(group, 12));
            var grid = new GridContainer { Columns = 1 };
            grid.AddThemeConstantOverride("h_separation", 5);
            grid.AddThemeConstantOverride("v_separation", 5);
            foreach (string m in models)
            {
                string model = m;
                var b = new Button { Name = "Scenery_" + m, Text = Label(m), ToggleMode = true, ButtonPressed = _sceneryModel == m,
                    FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(250, 30), Alignment = HorizontalAlignment.Left };
                b.Pressed += () => { _sceneryModel = _sceneryModel == model ? null : model; _scenerySel = -1; Rebuild(); };
                grid.AddChild(b);
            }
            _left.AddChild(grid);
        }
    }

    /// <summary>"CommonTree_1" -> "Common tree 1", "Rock_Medium_2" -> "Rock medium 2".</summary>
    private static string Label(string model)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in model.Replace('_', ' '))
        {
            if (char.IsUpper(ch) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            sb.Append(sb.Length > 0 && char.IsUpper(ch) ? char.ToLowerInvariant(ch) : ch);
        }
        return sb.ToString();
    }

    /// <summary>The land under a screen point (world x, z).</summary>
    private Vector2? LandAt(Vector2 screen)
    {
        Vector3 from = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
        if (dir.Y > -1e-4f) return null;
        // Walk the ray down to the ground (the land rolls, so a flat plane would miss).
        float y0 = Horizon.Tabletop ? -Horizon.TableHeight : 0f;
        float t = (y0 - from.Y) / dir.Y;
        Vector3 p = from + dir * t;
        for (int i = 0; i < 6; i++)
        {
            float h = Horizon.LandHeight(p.X, p.Z);
            t = (h - from.Y) / dir.Y;
            if (t < 0) return null;
            p = from + dir * t;
        }
        return new Vector2(p.X, p.Z);
    }

    private int SceneryPieceAt(Vector2 screen, List<MapScenery.Piece> pieces)
    {
        int best = -1;
        float bestD = 46f;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            float y = Horizon.LandHeight(p.X, p.Z);
            foreach (float up in new[] { 0f, p.Height * 0.5f })
            {
                Vector3 at = new(p.X, y + up, p.Z);
                if (_camera.IsPositionBehind(at)) continue;
                float d = _camera.UnprojectPosition(at).DistanceTo(screen);
                if (d < bestD) { bestD = d; best = i; }
            }
        }
        return best;
    }

    private void SceneryDown(Vector2 screen)
    {
        MapScenery s = Scenery;
        s.Pieces ??= SceneryPieces(s);
        if (_sceneryModel == null)
        {
            _scenerySel = SceneryPieceAt(screen, s.Pieces);
            if (_scenerySel >= 0) { Draft.Remember(); _sceneryDragging = true; }
            SyncSceneryRing();
            Clear(_right);
            SceneryRight();
            return;
        }
        if (LandAt(screen) is not { } at) return;
        if (Mathf.Abs(at.X) < W / 2 + 2f && Mathf.Abs(at.Y) < D / 2 + 2f) { Say("That's the table - scenery goes round it."); return; }
        Draft.Remember();
        s.Pieces.Add(new MapScenery.Piece(_sceneryModel, at.X, at.Y, (float)GD.RandRange(0, 360), MapScenery.DefaultHeight(_sceneryModel)));
        _scenerySel = s.Pieces.Count - 1;
        Draft.Scenery = s.ToText();
        RebuildWorld();
        SyncSceneryRing();
        Clear(_right);
        SceneryRight();
    }

    private void SceneryDrag(Vector2 screen)
    {
        if (_scenerySel < 0 || LandAt(screen) is not { } at) return;
        MapScenery s = Scenery;
        if (s.Pieces == null || _scenerySel >= s.Pieces.Count) return;
        s.Pieces[_scenerySel] = s.Pieces[_scenerySel] with { X = at.X, Z = at.Y };
        Draft.Scenery = s.ToText();
        // Move the one model now; the land is rebuilt when the drag ends.
        if (_board.FindChild($"Scenery_{_scenerySel}", true, false) is Node3D n)
            n.Position = new Vector3(at.X, Horizon.LandHeight(at.X, at.Y) - 0.5f, at.Y);
        SyncSceneryRing();
    }

    private void SceneryUp()
    {
        _sceneryDragging = false;
        RebuildWorld();
        SyncSceneryRing();
    }

    /// <summary>Q / E turn, + / - size, Del removes the picked piece. True when the key was used.</summary>
    private static Control Note(string text) => ForgeWidgets.Wrapped(text, 12, HudKit.Dim);

    /// <summary>The picked piece's buttons on the right (the same as the keys).</summary>
    private void SceneryRight()
    {
        MapScenery s = Scenery;
        _right.AddChild(HudKit.Heading("Scenery piece", 12));
        if (s.Pieces == null || _scenerySel < 0 || _scenerySel >= s.Pieces.Count)
        {
            _right.AddChild(Note("Click a piece on the land to pick it, or pick a model on the left and click the land to add one."));
            _right.AddChild(Note($"{s.Pieces?.Count ?? 0} pieces round this table."));
            return;
        }
        var p = s.Pieces[_scenerySel];
        _right.AddChild(Note(Label(p.Model)));
        var turn = new HBoxContainer();
        turn.AddChild(Ui.Button("Turn left", () => SceneryChange(Key.Q, false), "Q").Also(b => b.Name = "SceneryTurnLeft"));
        turn.AddChild(Ui.Button("Turn right", () => SceneryChange(Key.E, false), "E").Also(b => b.Name = "SceneryTurnRight"));
        _right.AddChild(turn);
        var size = new HBoxContainer();
        size.AddChild(Ui.Button("Bigger", () => SceneryChange(Key.Equal, false), "+").Also(b => b.Name = "SceneryBigger"));
        size.AddChild(Ui.Button("Smaller", () => SceneryChange(Key.Minus, false), "-").Also(b => b.Name = "ScenerySmaller"));
        _right.AddChild(size);
        _right.AddChild(Ui.Button("Remove", () => SceneryChange(Key.Delete, false), "Del").Also(b => b.Name = "SceneryRemove"));
    }

    private bool SceneryKey(InputEventKey k)
    {
        if (k.Keycode == Key.Escape) { StopScenery(); return true; }
        return SceneryChange(k.Keycode, k.Echo);
    }

    private bool SceneryChange(Key key, bool echo)
    {
        MapScenery s = Scenery;
        if (_scenerySel < 0 || s.Pieces == null || _scenerySel >= s.Pieces.Count) return false;
        var p = s.Pieces[_scenerySel];
        MapScenery.Piece? next = key switch
        {
            Key.Q => p with { Yaw = p.Yaw + 15 },
            Key.E => p with { Yaw = p.Yaw - 15 },
            Key.Equal or Key.KpAdd or Key.Bracketright => p with { Height = Mathf.Min(400f, p.Height * 1.15f) },
            Key.Minus or Key.KpSubtract or Key.Bracketleft => p with { Height = Mathf.Max(3f, p.Height / 1.15f) },
            _ => null,
        };
        if (key is Key.Delete or Key.Backspace)
        {
            Draft.Remember();
            s.Pieces.RemoveAt(_scenerySel);
            _scenerySel = -1;
        }
        else if (next != null)
        {
            if (!echo) Draft.Remember();
            s.Pieces[_scenerySel] = next;
        }
        else return false;
        Draft.Scenery = s.ToText();
        RebuildWorld();
        SyncSceneryRing();
        Clear(_right);
        SceneryRight();
        return true;
    }

    /// <summary>A ring round the picked piece.</summary>
    private void SyncSceneryRing()
    {
        MapScenery s = Scenery;
        if (_tool != ETool.Scenery || s.Pieces == null || _scenerySel < 0 || _scenerySel >= s.Pieces.Count)
        {
            if (_sceneryRing != null) _sceneryRing.Visible = false;
            return;
        }
        var p = s.Pieces[_scenerySel];
        if (_sceneryRing == null || !IsInstanceValid(_sceneryRing))
        {
            _sceneryRing = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.82f, OuterRadius = 1f, Rings = 48 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = HudKit.Lime, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true },
            };
            _world.AddChild(_sceneryRing);
        }
        float r = Mathf.Max(6f, p.Height * 0.3f);
        _sceneryRing.Scale = new Vector3(r, 2f, r);
        _sceneryRing.Position = new Vector3(p.X, Horizon.LandHeight(p.X, p.Z) + 0.5f, p.Z);
        _sceneryRing.Visible = true;
    }
}
