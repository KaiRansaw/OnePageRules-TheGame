using Godot;

namespace OprGodot.Battle;

/// <summary>
/// The map's weather drawn over the table (Kai, 2026-11-18: "map effects, like rain, snow"). Cosmetic: no rules, and
/// Settings -> Weather effects turns it off. Built from GPU particles over the playable area:
/// <list type="bullet">
/// <item>rain - thin falling streaks, set up after TCA Weather System's rain (kS222, MIT), scaled to table inches;</item>
/// <item>snow, ash - soft round flakes (Kenney Particle Pack circle_05, CC0) drifting down;</item>
/// <item>fog, dust - big soft puffs (Kenney smoke_03, CC0) rolling low across the table;</item>
/// <item>storm - heavy rain plus lightning flashes.</item>
/// </list>
/// </summary>
public static partial class Weather
{
    public static bool On => FdgRaylib.Config.UserConfig.Current.View.WeatherEffects;

    /// <summary>Adds <paramref name="id"/>'s weather (a <see cref="FdgRaylib.Terrain.MapAtmosphere"/> id) over a
    /// <paramref name="w"/> x <paramref name="d"/> table centred on the origin. Nothing for clear weather.</summary>
    public static Node3D? Add(Node3D parent, float w, float d, string? id)
    {
        Overcast(parent, id);
        if (!On || string.IsNullOrEmpty(id)) return null;
        var root = new Node3D { Name = "Weather" };
        parent.AddChild(root);
        float area = w * d / (72f * 48f); // more particles on a bigger table, the same look per square foot
        switch (id)
        {
            case "rain": Rain(root, w, d, area, 1f, 0.15f); break;
            case "heavy-rain": Rain(root, w, d, area, 2.6f, 0.3f); break;
            case "storm": Rain(root, w, d, area, 2.6f, 0.45f); root.AddChild(new Lightning()); break;
            case "snow": Flakes(root, w, d, area, 1f, new Color(1f, 1f, 1f, 0.95f), 0.5f, 9f, 3f); break;
            case "blizzard": Flakes(root, w, d, area, 3f, new Color(1f, 1f, 1f, 0.95f), 0.42f, 14f, 30f); Puffs(root, w, d, area, new Color(0.95f, 0.97f, 1f, 0.18f), 10f); break;
            case "ash": Flakes(root, w, d, area, 1.2f, new Color(0.3f, 0.28f, 0.27f, 0.9f), 0.35f, 5f, 4f); break;
            case "fog": Puffs(root, w, d, area, new Color(0.88f, 0.9f, 0.92f, 0.22f), 1.5f); break;
            case "dust": Puffs(root, w, d, area, new Color(0.78f, 0.64f, 0.45f, 0.28f), 22f); Flakes(root, w, d, area, 0.8f, new Color(0.7f, 0.58f, 0.4f, 0.8f), 0.12f, 2f, 28f); break;
            default: root.QueueFree(); return null;
        }
        return root;
    }

    /// <summary>Clouds over the sun: bad weather dims the sunlight and the sky and greys the haze. Finds the scene's
    /// "Environment" and "Sun" by walking up from <paramref name="from"/>; remembers their clear-day values so the
    /// Map Maker can switch weather back and forth.</summary>
    public static void Overcast(Node from, string? id)
    {
        if (!On) id = null;
        (float light, float sky, float grey) = id switch
        {
            "rain" => (0.6f, 0.6f, 0.4f),
            "heavy-rain" => (0.45f, 0.45f, 0.6f),
            "storm" => (0.3f, 0.3f, 0.75f),
            "snow" => (0.7f, 0.75f, 0.5f),
            "blizzard" => (0.55f, 0.6f, 0.75f),
            "fog" => (0.6f, 0.7f, 0.7f),
            "ash" => (0.45f, 0.5f, 0.55f),
            "dust" => (0.7f, 0.75f, 0.35f),
            _ => (1f, 1f, 0f),
        };
        for (Node? n = from; n != null; n = n.GetParent())
        {
            if (n.GetNodeOrNull<WorldEnvironment>("Environment") is not { Environment: { } env }) continue;
            env.BackgroundEnergyMultiplier = Remember(env, "sky", env.BackgroundEnergyMultiplier) * sky;
            Color haze = Remember(env, "haze", env.FogLightColor);
            env.FogLightColor = haze.Lerp(new Color(0.55f, 0.57f, 0.6f), grey);
            env.AmbientLightEnergy = Remember(env, "ambient", env.AmbientLightEnergy) * Mathf.Lerp(1f, light, 0.5f);
            if (n.GetNodeOrNull<DirectionalLight3D>("Sun") is { } sun)
                sun.LightEnergy = Remember(sun, "energy", sun.LightEnergy) * light;
            return;
        }
    }

    private static T Remember<[MustBeVariant] T>(GodotObject o, string key, T now)
    {
        string meta = "clear_" + key;
        if (!o.HasMeta(meta)) o.SetMeta(meta, Variant.From(now));
        return o.GetMeta(meta).As<T>();
    }

    private const float Top = 45f; // particles start this high over the table, inches

    private static GpuParticles3D Emitter(Node3D root, float w, float d, int amount, float lifetime)
    {
        var p = new GpuParticles3D
        {
            Amount = Mathf.Clamp(amount, 50, 30000), Lifetime = lifetime, Preprocess = lifetime, Randomness = 0.4f,
            VisibilityAabb = new Aabb(new Vector3(-w / 2 - 20, -Top - 5, -d / 2 - 20), new Vector3(w + 40, Top + 20, d + 40)),
            Position = new Vector3(0, Top, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(p);
        return p;
    }

    /// <summary>Rain: thin streaks falling fast, slanted by the wind.</summary>
    private static void Rain(Node3D root, float w, float d, float area, float strength, float slant)
    {
        var p = Emitter(root, w, d, (int)(5000 * area * strength), 0.55f);
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w / 2 + 10, 1, d / 2 + 10),
            Direction = new Vector3(slant, -1, slant * 0.4f), Spread = 2f, InitialVelocityMin = 85f, InitialVelocityMax = 100f, Gravity = Vector3.Zero,
        };
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.84f, 0.92f, 0.42f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        p.DrawPass1 = new BoxMesh { Size = new Vector3(0.025f, 1.3f, 0.025f), Material = mat };
    }

    /// <summary>Snow / ash / dust grains: soft round flakes drifting down, pushed sideways by <paramref name="wind"/>.</summary>
    private static void Flakes(Node3D root, float w, float d, float area, float strength, Color c, float size, float fall, float wind)
    {
        var p = Emitter(root, w, d, (int)(3500 * area * strength), Top / fall * 1.1f);
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w / 2 + 20, 2, d / 2 + 20),
            Direction = new Vector3(wind / Mathf.Max(fall, 0.1f), -1, 0.2f).Normalized(), Spread = 12f,
            InitialVelocityMin = fall * 0.8f, InitialVelocityMax = fall * 1.2f, Gravity = Vector3.Zero,
            ScaleMin = 0.6f, ScaleMax = 1.3f,
        };
        p.DrawPass1 = new QuadMesh { Size = new Vector2(size, size), Material = Billboard("circle_05", c) };
    }

    /// <summary>Fog / dust banks: big soft puffs rolling low over the table.</summary>
    private static void Puffs(Node3D root, float w, float d, float area, Color c, float wind)
    {
        var p = Emitter(root, w, d, (int)(90 * area), 14f);
        p.Position = new Vector3(0, 3f, 0);
        p.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w / 2 + 10, 1.5f, d / 2 + 10),
            Direction = new Vector3(1, 0, 0.3f), Spread = 25f, InitialVelocityMin = wind * 0.6f, InitialVelocityMax = wind, Gravity = Vector3.Zero,
            ScaleMin = 0.8f, ScaleMax = 1.6f, AngleMin = 0, AngleMax = 360,
            AlphaCurve = new CurveTexture { Curve = FadeInOut() },
        };
        p.DrawPass1 = new QuadMesh { Size = new Vector2(26, 26), Material = Billboard("smoke_03", c) };
    }

    private static Curve FadeInOut()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 0));
        curve.AddPoint(new Vector2(0.2f, 1));
        curve.AddPoint(new Vector2(0.8f, 1));
        curve.AddPoint(new Vector2(1, 0));
        return curve;
    }

    private static StandardMaterial3D Billboard(string picture, Color c) => new()
    {
        AlbedoTexture = GD.Load<Texture2D>($"res://weather/{picture}.png"), AlbedoColor = c, VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// <summary>A thunderstorm's lightning: every 6-16 seconds the scene flashes once or twice.</summary>
    private sealed partial class Lightning : Node3D
    {
        private readonly OmniLight3D _flash = new() { OmniRange = 400f, LightEnergy = 0f, LightColor = new Color(0.85f, 0.9f, 1f), Position = new Vector3(0, 120, 0), ShadowEnabled = false };
        private double _next = 4;
        private double _t;

        public override void _Ready() => AddChild(_flash);

        public override void _Process(double delta)
        {
            _t += delta;
            if (_t < _next) return;
            _t = 0;
            _next = 6 + GD.Randf() * 10;
            _flash.Position = new Vector3((GD.Randf() - 0.5f) * 200, 120, (GD.Randf() - 0.5f) * 140);
            var tw = CreateTween();
            tw.TweenProperty(_flash, "light_energy", 6f, 0.05);
            tw.TweenProperty(_flash, "light_energy", 0f, 0.12);
            if (GD.Randf() < 0.5f)
            {
                tw.TweenInterval(0.08);
                tw.TweenProperty(_flash, "light_energy", 4f, 0.04);
                tw.TweenProperty(_flash, "light_energy", 0f, 0.2);
            }
        }
    }
}
