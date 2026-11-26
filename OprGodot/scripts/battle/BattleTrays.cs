using FDG;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// The side trays (Kai, 2026-11-26, mockup A of "Side Tables Mockups"): a tray at each short end of the table - your
/// side's on the left, the other side's on the right - in four bays, Ambush / Scout / Reserves / Destroyed, holding the
/// units off the table as their own models. Which unit goes where: FdgApp <see cref="OffTable"/>. Models on a tray can be
/// hovered and clicked like any other (their card shows). Every model stands up, casualties too (Kai, 2026-11-26).
/// </summary>
public partial class BattleTable
{
    /// <summary>Tray size (inches): across, and the gap from the table's rim.</summary>
    public const float TrayW = 22f, TrayGap = 4f;

    private Node3D? _trayRoot;
    private readonly Dictionary<ModelID, ModelView> _trayModels = new();
    private readonly List<Label3D> _trayLabels = new();
    private string _traySig = "";

    /// <summary>Middle of a tray (world x): left = -1, right = +1.</summary>
    private float TrayX(int side) => side * (W / 2 + 1.6f + TrayGap + TrayW / 2);

    private void BuildTrays()
    {
        _trayRoot = new Node3D { Name = "Trays" };
        AddChild(_trayRoot);
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.085f, 0.08f), Roughness = 0.55f };
        Material edge = Horizon.Planks();
        var felt = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.12f, 0.13f), Roughness = 0.9f };
        var divider = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.19f, 0.16f), Roughness = 0.5f };
        const float Rim = 1.2f, RimH = 1f, Top = 2.5f;
        foreach (int side in new[] { -1, 1 })
        {
            float cx = TrayX(side);
            void Box(Vector3 size, Vector3 at, Material m)
            {
                var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at };
                _trayRoot.AddChild(mi);
            }
            Box(new Vector3(TrayW, Top, D), new Vector3(cx, -Top / 2 - 0.02f, 0), felt);
            Box(new Vector3(TrayW + 2 * Rim, RimH + Top, Rim), new Vector3(cx, (RimH - Top) / 2, -(D / 2 + Rim / 2)), edge);
            Box(new Vector3(TrayW + 2 * Rim, RimH + Top, Rim), new Vector3(cx, (RimH - Top) / 2, D / 2 + Rim / 2), edge);
            Box(new Vector3(Rim, RimH + Top, D), new Vector3(cx - side * (TrayW / 2 + Rim / 2), (RimH - Top) / 2, 0), edge);
            Box(new Vector3(Rim, RimH + Top, D), new Vector3(cx + side * (TrayW / 2 + Rim / 2), (RimH - Top) / 2, 0), edge);
            // Three dividers make the four bays.
            for (int i = 1; i < OffTable.Bays.Count; i++)
                Box(new Vector3(TrayW, RimH * 0.6f, 0.5f), new Vector3(cx, RimH * 0.3f, -D / 2 + D * i / OffTable.Bays.Count), divider);
            if (Horizon.Tabletop)
            {
                // On the tabletop look the trays are small side tables of their own - the same table, made narrow.
                var stand = new Node3D { Position = new Vector3(cx, 0, 0) };
                _trayRoot.AddChild(stand);
                if (!Horizon.TableModel(stand, TrayW + 2 * Rim + 1f, D + 2 * Rim + 1f, -Top - 0.05f))
                {
                    Box(new Vector3(TrayW - 2, 4f, D - 4), new Vector3(cx, -Top - 2f, 0), wood);
                    float legH = Horizon.TableHeight + 4f - Top - 4f;
                    foreach (float sz in new[] { -1f, 1f })
                        Box(new Vector3(2.5f, legH, 2.5f), new Vector3(cx + side * (TrayW / 2 - 3f), -Top - 4f - legH / 2, sz * (D / 2 - 4f)), wood);
                }
            }
            // The bays' names, flat on the felt at the end of each bay nearest the camera's usual side.
            for (int i = 0; i < OffTable.Bays.Count; i++)
            {
                OffTable.Bay bay = OffTable.Bays[i];
                var l = new Label3D
                {
                    Text = OffTable.Title(bay), Font = HudKit.Bold, FontSize = 64, PixelSize = 0.03f, OutlineSize = 0,
                    Modulate = HudKit.Lime with { A = 0.85f }, Position = new Vector3(cx, 0.05f, BayTop(i) + 1.6f),
                    RotationDegrees = new Vector3(-90, 0, 0),
                };
                _trayRoot.AddChild(l);
            }
            var who = new Label3D
            {
                Text = side < 0 ? "YOUR SIDE" : "OPPONENTS", Font = HudKit.Bold, FontSize = 56, PixelSize = 0.022f,
                Modulate = HudKit.Dim, Position = new Vector3(cx, 0.05f, D / 2 + 3.2f), RotationDegrees = new Vector3(-90, 0, 0),
            };
            _trayRoot.AddChild(who);
        }
    }

    /// <summary>World z of the far edge of bay i (bays run from the far end, Ambush, to the near end, Destroyed).</summary>
    private float BayTop(int i) => -D / 2 + D * i / OffTable.Bays.Count;

    private void SyncTrays()
    {
        if (_trayRoot == null) BuildTrays();
        // A unit's side: yours (any local seat) on the left; when watching only bots, the first seat's side is "yours".
        var local = Session.Table.Units.Objects.Where(u => Session.IsMine(u.PlayerID)).Select(u => u.PlayerID).ToHashSet();
        if (local.Count == 0 && Session.Table.Players.Objects.FirstOrDefault(p => p.IsFilled) is { } first)
        {
            int team = first.TeamNumber;
            foreach (var p in Session.Table.Players.Objects.Where(p => p.IsFilled && p.TeamNumber == team)) local.Add(p.PlayerID);
        }
        List<OffTable.Entry> entries = OffTable.Sort(Session.Table.Units.Objects.ToList(), Session.Table.Progress.RoundCount);
        string sig = string.Join(";", entries.Select(e => $"{e.Unit.ID.ID}:{e.Bay}:{e.Models.Count}"));
        if (sig == _traySig) return;
        _traySig = sig;

        var keep = new HashSet<ModelID>();
        foreach (Label3D l in _trayLabels) l.QueueFree();
        _trayLabels.Clear();
        foreach (int side in new[] { -1, 1 })
        {
            float cx = TrayX(side);
            for (int b = 0; b < OffTable.Bays.Count; b++)
            {
                OffTable.Bay bay = OffTable.Bays[b];
                var here = entries.Where(e => e.Bay == bay && (local.Contains(e.Unit.PlayerID) ? -1 : 1) == side).ToList();
                if (here.Count == 0) continue;
                // Lay the units out in rows across the bay, each unit a little block of its models; shrink to fit.
                float areaW = TrayW - 2f, areaD = D / OffTable.Bays.Count - 4.2f;
                float x0 = cx - areaW / 2, z0 = BayTop(b) + 3.4f;
                var blocks = here.Select(e =>
                {
                    float step = 2 * Mathf.Max(0.5f, e.Models.Max(m => m.BaseRadiusInches)) + 0.3f;
                    int perRow = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(e.Models.Count * 1.6f)), 1, 8);
                    int rows = Mathf.CeilToInt(e.Models.Count / (float)perRow);
                    return (e, step, perRow, w: perRow * step, d: rows * step + 1.4f);
                }).ToList();
                float scale = 1f;
                for (int tries = 0; tries < 12; tries++)
                {
                    float x = 0, z = 0, rowD = 0;
                    foreach (var bl in blocks)
                    {
                        if (x > 0 && x + bl.w * scale > areaW) { x = 0; z += rowD; rowD = 0; }
                        x += bl.w * scale + 0.8f;
                        rowD = Mathf.Max(rowD, bl.d * scale);
                    }
                    if (z + rowD <= areaD) break;
                    scale *= 0.85f;
                }
                {
                    float x = 0, z = 0, rowD = 0;
                    foreach (var bl in blocks)
                    {
                        if (x > 0 && x + bl.w * scale > areaW) { x = 0; z += rowD; rowD = 0; }
                        for (int i = 0; i < bl.e.Models.Count; i++)
                        {
                            IModel m = bl.e.Models[i];
                            float mx = x0 + x + (i % bl.perRow + 0.5f) * bl.step * scale;
                            float mz = z0 + z + (i / bl.perRow + 0.5f) * bl.step * scale;
                            keep.Add(m.ID);
                            if (!_trayModels.TryGetValue(m.ID, out ModelView? view))
                            {
                                view = MakeModel(m, bl.e.Unit);
                                _trayModels[m.ID] = view;
                            }
                            view.Root.Position = new Vector3(mx, 0, mz);
                            view.Root.Scale = Vector3.One * scale;
                            // Every model stands up facing the table (Kai, 2026-11-26: casualties too).
                            view.Root.Rotation = new Vector3(0, side < 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2, 0);
                        }
                        var name = new Label3D
                        {
                            Text = bl.e.Unit.Name.ToUpperInvariant() + (bl.e.Models.Count > 1 ? $" x{bl.e.Models.Count}" : ""),
                            Font = HudKit.Bold, FontSize = 40, PixelSize = 0.02f, OutlineSize = 12,
                            OutlineModulate = new Color(0, 0, 0, 0.85f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                            Modulate = SeatColour(bl.e.Unit.PlayerID).Lightened(0.35f),
                            Position = new Vector3(x0 + x + bl.w * scale / 2, 2.6f * scale + 0.6f, z0 + z - 0.2f),
                        };
                        _trayRoot!.AddChild(name);
                        _trayLabels.Add(name);
                        x += bl.w * scale + 0.8f;
                        rowD = Mathf.Max(rowD, bl.d * scale);
                    }
                }
            }
        }
        foreach (ModelID gone in _trayModels.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _trayModels[gone].Root.QueueFree();
            _trayModels.Remove(gone);
        }
    }

    /// <summary>The unit on a tray under the screen point, if any (hover and click, like the table).</summary>
    private IUnit? TrayUnitAt(Vector2 screen)
    {
        IUnit? best = null;
        float bestD = float.MaxValue;
        foreach (ModelView v in _trayModels.Values)
        {
            Vector3 at = v.Root.GlobalPosition;
            if (Camera.IsPositionBehind(at)) continue;
            Vector2 s = Camera.UnprojectPosition(at + new Vector3(0, 0.5f, 0));
            Vector2 e = Camera.UnprojectPosition(at + new Vector3(Mathf.Max(0.5f, v.Model.BaseRadiusInches) * v.Root.Scale.X, 0.5f, 0));
            float r = Mathf.Max(10f, s.DistanceTo(e) * 1.3f);
            float d = s.DistanceTo(screen);
            if (d < r && d < bestD) { bestD = d; best = v.Unit; }
        }
        return best;
    }
}
