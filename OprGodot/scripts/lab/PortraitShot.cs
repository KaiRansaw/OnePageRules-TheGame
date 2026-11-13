using FdgRaylib.Models3D;
using Godot;
using OprGodot.Battle;

namespace OprGodot.Lab;

/// <summary>
/// Portraits from downloaded models (Kai, 2026-11-12: "make sure that the portraits will take the picture from the
/// correct angle"). A model is stood up exactly as on the table (<see cref="ModelPlacement"/>, so its reviewed facing
/// holds - its front is +Z), then photographed by one of the <see cref="Angles"/> into a square picture with a clear
/// background, so a card, the army builder or a hero sheet can put it on any colour.
/// </summary>
public static partial class PortraitShot
{
    /// <summary>
    /// One way to photograph a model. Yaw turns the camera round from straight in front (+ = toward the model's left
    /// shoulder), pitch tilts it down from level (- = from below). The crop is the part of the model's box kept in frame:
    /// the top <c>Height</c> fraction and the front <c>Depth</c> fraction - for a beast or a tank the head and the guns
    /// are at the front, not just at the top.
    /// </summary>
    public sealed record Angle(string Id, string Name, float YawDeg, float PitchDeg, float Height, float Depth, float Fov = 30f, float Margin = 1.08f);

    public static readonly Angle[] Angles =
    {
        new("A", "Full body, three-quarter", 30, 14, 1f, 1f),
        new("B", "Upper body, three-quarter", 28, 10, 0.62f, 0.75f),
        new("C", "Close-up, front", 8, 6, 0.42f, 0.55f),
        new("D", "Full body, straight on", 0, 6, 1f, 1f),
        new("E", "Hero, from below", 30, -12, 1f, 1f, 34f),
    };

    /// <summary>The angle portraits are made at: A, Kai's pick on the Portrait Angle Review page (2026-11-12).</summary>
    public static Angle Portrait => Angles[0];

    public static Angle? ById(string id) => Angles.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A stage in its own world for taking pictures: lights, a camera, a clear background. Add it to the tree, put a model
    /// on it with <see cref="Studio.Show"/>, aim with <see cref="Studio.Aim"/>, wait one drawn frame, then
    /// <see cref="Studio.Grab"/>.
    /// </summary>
    public sealed partial class Studio : SubViewport
    {
        private Node3D _stage = null!;
        private Camera3D _cam = null!;
        private Aabb _box;

        public Studio() : this(1024) { }

        public Studio(int size)
        {
            Size = new Vector2I(size, size);
            OwnWorld3D = true;
            TransparentBg = true;
            Msaa3D = Msaa.Msaa4X;
            RenderTargetUpdateMode = UpdateMode.Always;
        }

        public override void _Ready()
        {
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.6f, 0.65f), AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            };
            AddChild(new WorldEnvironment { Environment = env });
            // Key from the camera's side and above, a cool rim from behind - the table's sun and rim, turned to the camera.
            var key = new DirectionalLight3D { LightEnergy = 1.25f, LightColor = new Color(1f, 0.95f, 0.88f), ShadowEnabled = true };
            key.RotationDegrees = new Vector3(-40, 25, 0);
            AddChild(key);
            var rim = new DirectionalLight3D { LightEnergy = 0.55f, LightColor = new Color(0.55f, 0.8f, 0.9f) };
            rim.RotationDegrees = new Vector3(-20, 200, 0);
            AddChild(rim);
            _stage = new Node3D();
            AddChild(_stage);
            _cam = new Camera3D { Near = 0.01f, Far = 1000, Current = true };
            AddChild(_cam);
        }

        /// <summary>Stands a loaded model on the stage with its saved settings; false (with the reason) when it can't.</summary>
        public bool Show(string file, out string? why)
        {
            why = null;
            foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
            LoadedAsset a = AssetLoader.Load(file);
            if (a.Model == null) { why = a.Error ?? "unreadable"; return false; }
            UnitModelSettings s = UnitModelSettings.Load(file)?.Copy() ?? new UnitModelSettings();
            s.BaseMm = 0; // no base in a portrait: feet on the floor
            var holder = new Node3D();
            _stage.AddChild(holder);
            var pivot = new Node3D();
            holder.AddChild(pivot);
            var body = (Node3D)a.Model.Duplicate();
            pivot.AddChild(body);
            (_, _box) = ModelPlacement.Place(pivot, body, s);
            return true;
        }

        /// <summary>Points the camera for an angle so the cropped part of the model fills the square.</summary>
        public void Aim(Angle angle)
        {
            Aabb crop = Crop(_box, angle);
            Vector3 focus = crop.GetCenter();
            float yaw = Mathf.DegToRad(angle.YawDeg), pitch = Mathf.DegToRad(angle.PitchDeg);
            // The model's front is +Z, so "straight in front" is the camera out along +Z looking back at it.
            Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)).Normalized();
            _cam.Fov = angle.Fov;
            float dist = crop.Size.Length() / (2f * Mathf.Tan(Mathf.DegToRad(angle.Fov) / 2f));
            // Two passes: measure where the crop's corners land and pull in or back so the larger side just fits.
            for (int pass = 0; pass < 3; pass++)
            {
                _cam.Position = focus + dir * dist;
                _cam.LookAt(focus, Vector3.Up);
                float extent = 0;
                foreach (Vector3 corner in Corners(crop))
                {
                    Vector3 local = _cam.GlobalTransform.AffineInverse() * corner;
                    float depth = Mathf.Max(-local.Z, 1e-3f);
                    float tan = Mathf.Tan(Mathf.DegToRad(angle.Fov) / 2f);
                    extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(local.X), Mathf.Abs(local.Y)) / (depth * tan));
                }
                if (extent <= 0) break;
                dist *= Mathf.Lerp(1f, extent * angle.Margin, 0.9f);
            }
            _cam.Position = focus + dir * dist;
            _cam.LookAt(focus, Vector3.Up);
        }

        /// <summary>The picture as drawn last frame (call after a frame has been drawn since <see cref="Aim"/>).</summary>
        /// <remarks>
        /// The camera is aimed from the model's box, which is always bigger than the model itself (a raised claw, an
        /// empty corner), so the picture is then trimmed to what was actually drawn: a square round the drawn pixels,
        /// a little margin, scaled to <paramref name="size"/>.
        /// </remarks>
        public Image Grab(int size = 512, float margin = 0.06f)
        {
            Image img = GetTexture().GetImage();
            Rect2I used = img.GetUsedRect();
            if (used.Size.X <= 0 || used.Size.Y <= 0) { img.Resize(size, size); return img; }
            int side = (int)(Mathf.Max(used.Size.X, used.Size.Y) * (1 + 2 * margin));
            Vector2I c = used.Position + used.Size / 2;
            var square = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
            img.Convert(Image.Format.Rgba8);
            // The square can reach past the picture's edge (a crop that cuts the model off at the bottom): only the part
            // inside is copied, at its own place, and the rest stays clear.
            var src = new Rect2I(c.X - side / 2, c.Y - side / 2, side, side);
            Rect2I clip = src.Intersection(new Rect2I(Vector2I.Zero, img.GetSize()));
            square.Fill(new Color(0, 0, 0, 0));
            square.BlitRect(img, clip, clip.Position - src.Position);
            square.Resize(size, size, Image.Interpolation.Lanczos);
            return square;
        }
    }

    /// <summary>The part of the model's box an angle keeps: the top Height and the front Depth of it.</summary>
    public static Aabb Crop(Aabb box, Angle a)
    {
        float h = box.Size.Y * a.Height, d = box.Size.Z * a.Depth;
        var pos = new Vector3(box.Position.X, box.End.Y - h, box.End.Z - d);
        return new Aabb(pos, new Vector3(box.Size.X, h, d));
    }

    private static IEnumerable<Vector3> Corners(Aabb b)
    {
        for (int i = 0; i < 8; i++)
            yield return b.Position + new Vector3((i & 1) != 0 ? b.Size.X : 0, (i & 2) != 0 ? b.Size.Y : 0, (i & 4) != 0 ? b.Size.Z : 0);
    }

    /// <summary>
    /// Photographs every model file under a folder at every angle, saving "&lt;file&gt;__&lt;angle&gt;.png" into
    /// <paramref name="outDir"/> (the angle review, and later the portrait maker's batch). Raises Done when finished.
    /// </summary>
    public sealed partial class Batch : Node
    {
        public event Action? Done;
        private readonly List<string> _files;
        private readonly string _out;
        private readonly Angle[] _angles;

        public Batch() : this("", "") { }

        public Batch(string folder, string outDir, Angle[]? angles = null)
        {
            _files = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList()
                : new List<string>();
            _out = outDir;
            _angles = angles ?? Angles;
        }

        public override async void _Ready()
        {
            Directory.CreateDirectory(_out);
            var studio = new Studio();
            AddChild(studio);
            foreach (string file in _files)
            {
                if (!studio.Show(file, out string? why)) { GD.PrintErr($"Portrait: {file}: {why}"); continue; }
                foreach (Angle a in _angles)
                {
                    studio.Aim(a);
                    for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string name = Path.GetFileNameWithoutExtension(file) + "__" + a.Id + ".png";
                    studio.Grab().SavePng(Path.Combine(_out, name));
                }
                GD.Print($"Portrait: {Path.GetFileName(file)} done");
            }
            Done?.Invoke();
        }
    }
}
