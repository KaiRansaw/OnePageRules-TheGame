using FDG;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// "What can this unit see" (F): the ground the selected unit could shoot a normal-sized model on, tinted in the colour of
/// the shortest weapon range that reaches it (the same colours and order as the named range outlines), and the ground
/// in reach but out of sight shaded dark. Worked out by <see cref="SightFan"/> with the engine's own sight test, on a
/// worker thread; it follows the selected unit and redraws when anything on the table moves.
/// </summary>
public partial class BattleTable
{
    private bool _sightView;

    /// <summary>F toggles it; it stays on (following the selection) until F again.</summary>
    public bool SightView
    {
        get => _sightView;
        set
        {
            if (_sightView == value) return;
            _sightView = value;
            _sightSig = "";
            if (!value) ClearSight();
        }
    }

    /// <summary>What the view last showed (null = nothing yet) - the HUD's legend reads it.</summary>
    public SightFan.Result? SightShown { get; private set; }

    private string _sightSig = "";
    private CancellationTokenSource? _sightCancel;
    private SightFan.Result? _sightReady;
    private readonly object _sightLock = new();
    private MeshInstance3D? _sightMesh;

    private static readonly Color HiddenShade = new(0.03f, 0.03f, 0.06f, 0.55f);

    private void SyncSight()
    {
        SightFan.Result? ready;
        lock (_sightLock) { ready = _sightReady; _sightReady = null; }
        if (ready != null) DrawSight(ready);

        if (!_sightView) return;
        IUnit? sel = Selected is { } id ? Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(id)) : null;
        if (sel == null || !sel.GetIsOnBattlefield())
        {
            if (_sightSig != "-") { _sightSig = "-"; ClearSight(); }
            return;
        }
        // Anything that moves can open or close a line: the whole table's living models go into the signature.
        float sum = 0f;
        int count = 0;
        foreach (IModel m in Session.Table.Models.Objects)
        {
            if (!m.GetIsAlive()) continue;
            sum += m.Position.x * 1.37f + m.Position.z * 0.71f;
            count++;
        }
        string sig = $"{sel.ID.ID}|{count}|{sum:0.00}|{Session.Table.Terrain.Objects.Count()}";
        if (sig == _sightSig) return;
        _sightSig = sig;

        _sightCancel?.Cancel();
        var cancel = _sightCancel = new CancellationTokenSource();
        ITableState table = Session.Table;
        bool seeThrough = Session.Lobby.Vm.SeeThroughFriendlyUnits;
        IUnit unit = sel;
        Task.Run(() =>
        {
            try
            {
                SightFan.Result r = SightFan.Compute(table, unit, seeThrough, cancel: cancel.Token);
                if (!cancel.IsCancellationRequested) lock (_sightLock) _sightReady = r;
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { _sightSig = ""; } // the table changed mid-read: try again next frame
        });
    }

    private void ClearSight()
    {
        _sightCancel?.Cancel();
        lock (_sightLock) _sightReady = null;
        _sightMesh?.QueueFree();
        _sightMesh = null;
        SightShown = null;
    }

    private void DrawSight(SightFan.Result r)
    {
        _sightMesh?.QueueFree();
        _sightMesh = null;
        SightShown = _sightView ? r : null;
        if (!_sightView || r.Cells.Count == 0) return;

        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        float h = r.CellInches / 2f;
        foreach (SightFan.Cell c in r.Cells)
        {
            Color col = c.Band < 0 ? HiddenShade : CoolRanges[c.Band % CoolRanges.Length] with { A = 0.30f };
            st.SetColor(col);
            Vector3 a = World(c.X - h, c.Z - h, 0.06f), b = World(c.X + h, c.Z - h, 0.06f);
            Vector3 cc = World(c.X + h, c.Z + h, 0.06f), d = World(c.X - h, c.Z + h, 0.06f);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(cc);
            st.AddVertex(a); st.AddVertex(cc); st.AddVertex(d);
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, SightMat);
        _sightMesh = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "SightView" };
        AddChild(_sightMesh);
    }

    private static StandardMaterial3D? _sightMat;
    private static StandardMaterial3D SightMat => _sightMat is { } m && IsInstanceValid(m) ? m : _sightMat = new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        RenderPriority = -1,
    };
}
