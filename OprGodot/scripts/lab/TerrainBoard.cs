using System.Text.Json;
using Godot;

namespace OprGodot.Lab;

/// <summary>
/// Built-in terrain plan, step 1 (2026-11-21): pictures of candidate terrain models standing on the game's own table -
/// ground, grid, lighting - beside a 32mm-based figure for scale, so Kai can compare sources. A tool, driven by ScriptedRun
/// ("terrainboard:&lt;spec.json&gt;|&lt;out folder&gt;"); quits when every picture is saved.
/// <para>Spec: {"scenes":[{"name","w","d","pitch","dist","minis":[[x,z]],"items":[{"file","x","z","ry","k"|"h"}]}]}.
/// Inches throughout; "k" = inches per model unit (0.7 for models made in metres: 28mm figures are about 1:56),
/// "h" = scale the model to this height instead. Every model's size in inches is printed, for laying out pieces.</para>
/// </summary>
public sealed partial class TerrainBoard : Node
{
    private readonly string _spec, _outDir;
    private readonly Dictionary<string, Node3D?> _cache = new();

    public TerrainBoard(string spec, string outDir) { _spec = spec; _outDir = outDir; }

    public event Action? Done;

    private sealed record Item(string file, float x, float z, float ry = 0, float k = 0.7f, float h = 0, float y = 0, string? part = null);
    private sealed record Scene(string name, float w = 12, float d = 9, float pitch = 38, float dist = 0, float yaw = 0,
        List<float[]>? minis = null, List<Item>? items = null, int px = 960, int py = 600);
    private sealed record Spec(List<Scene> scenes);

    public override async void _Ready()
    {
        Spec spec = JsonSerializer.Deserialize<Spec>(File.ReadAllText(_spec))!;
        Directory.CreateDirectory(_outDir);
        foreach (Scene s in spec.scenes)
        {
            try { await Shoot(s); }
            catch (Exception e) { GD.PrintErr($"TerrainBoard: {s.name}: {e.Message}"); }
        }
        Done?.Invoke();
    }

    private Node3D? Template(string file)
    {
        if (_cache.TryGetValue(file, out Node3D? t)) return t;
        if (file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            var doc = new FbxDocument();
            var state = new FbxState();
            Node3D? fbx = doc.AppendFromFile(file, state) == Error.Ok ? doc.GenerateScene(state) as Node3D : null;
            if (fbx == null) GD.PrintErr($"TerrainBoard: {file}: FBX did not load");
            _cache[file] = fbx;
            return fbx;
        }
        LoadedAsset a = AssetLoader.Load(file);
        if (a.Model == null) GD.PrintErr($"TerrainBoard: {file}: {a.Error}");
        else GD.Print($"TerrainBoard: loaded {Path.GetFileName(file)} tris={a.Triangles}");
        _cache[file] = a.Model;
        return a.Model;
    }

    private async Task Shoot(Scene s)
    {
        var vp = new SubViewport { Size = new Vector2I(s.px, s.py), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(vp);
        var world = new Node3D();
        vp.AddChild(world);
        Battle.TableLook.AddLighting(world, null, "Hospitable", s.w, s.d, battlefield: true);
        Battle.TableLook.AddBoard(world, s.w, s.d, horizon: "Hospitable", battlefield: true);

        foreach (Item it in s.items ?? new())
        {
            if (Template(it.file) is not { } tpl) continue;
            var body = (Node3D)tpl.Duplicate();
            // "part": keep one named piece of a modular kit file (the rest of the kit is laid out beside it).
            if (it.part != null)
            {
                Node? keep = body.FindChild(it.part, true, false);
                if (keep is not Node3D k3) { GD.PrintErr($"TerrainBoard: no part {it.part} in {it.file}"); continue; }
                Transform3D t = Rel(body, k3);
                foreach (Node c in body.GetChildren()) { body.RemoveChild(c); if (c != k3 && !c.IsAncestorOf(k3)) c.QueueFree(); }
                k3.GetParent()?.RemoveChild(k3);
                body.AddChild(k3);
                k3.Transform = new Transform3D(t.Basis, Vector3.Zero);
            }
            // holder (place, turn) -> scaler (size, centre) -> the model untouched (an FBX keeps its own unit scale).
            var holder = new Node3D();
            var scaler = new Node3D();
            holder.AddChild(scaler);
            scaler.AddChild(body);
            world.AddChild(holder);
            Aabb? box = AssetLoader.Bounds(body, holder);
            if (box is not { } b) continue;
            float k = it.h > 0 && b.Size.Y > 1e-4f ? it.h / b.Size.Y : it.k;
            scaler.Scale = Vector3.One * k;
            scaler.Position = new Vector3(-(b.Position.X + b.Size.X / 2) * k, -b.Position.Y * k + it.y, -(b.Position.Z + b.Size.Z / 2) * k);
            holder.Position = new Vector3(it.x, 0, it.z);
            holder.RotationDegrees = new Vector3(0, it.ry, 0);
            GD.Print($"TerrainBoard: size {Path.GetFileName(it.file)}{(it.part != null ? ":" + it.part : "")} = {b.Size.X * k:0.0} x {b.Size.Z * k:0.0} x {b.Size.Y * k:0.0} in (k {k:0.###})");
        }
        foreach (float[] m in s.minis ?? new()) Mini(world, m[0], m[1]);

        float span = Mathf.Max(s.w, s.d);
        float dist = s.dist > 0 ? s.dist : span * 1.25f;
        float pitch = Mathf.DegToRad(s.pitch), yaw = Mathf.DegToRad(s.yaw);
        var cam = new Camera3D { Fov = 34, Near = 0.1f, Far = 4000 };
        world.AddChild(cam);
        cam.Position = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch) * dist, Mathf.Sin(pitch) * dist, Mathf.Cos(yaw) * Mathf.Cos(pitch) * dist);
        cam.LookAt(new Vector3(0, 0.8f, 0), Vector3.Up);
        cam.Current = true;
        Battle.Horizon.FollowCamera(world, dist);

        for (int f = 0; f < 8; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_outDir, s.name + ".png");
        vp.GetTexture().GetImage().SavePng(path);
        GD.Print($"TerrainBoard: shot {path}");
        vp.QueueFree();
        // Models are big (photo-scanned rocks run to tens of MB): keep only what the next scene uses.
        var next = new HashSet<string>();
        foreach (Node3D? n in _cache.Values) n?.QueueFree();
        _cache.Clear();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static Transform3D Rel(Node3D root, Node3D n)
    {
        Transform3D t = Transform3D.Identity;
        for (Node? c = n; c != null && c != root; c = c.GetParent()) if (c is Node3D c3) t = c3.Transform * t;
        return t;
    }

    // A stand-in 28mm figure on a 32mm base: what every piece is judged against.
    private static void Mini(Node3D world, float x, float z)
    {
        var root = new Node3D { Position = new Vector3(x, 0, z) };
        world.AddChild(root);
        Battle.TableLook.AddBase(root, 0.63f, new Color(0.85f, 0.75f, 0.2f));
        Battle.TableLook.Mesh(root, new CapsuleMesh { Radius = 0.22f, Height = 1.1f }, Battle.TableLook.Mat(new Color(0.6f, 0.62f, 0.66f)), new Vector3(0, 0.7f, 0));
    }
}
