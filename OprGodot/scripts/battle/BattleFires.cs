using System.Collections.Generic;
using System.Linq;
using FDG;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// City Fighting - Buildings on Fire: flames and smoke over each burning building, with a flickering orange light, so
/// the table shows which buildings burn (the Mission panel lists them too).
/// </summary>
public partial class BattleTable
{
    private Node3D? _fires;
    private string _fireSig = "";

    private void SyncFires()
    {
        CityFightingState? state = CityFighting.State(Session.Table.DataStore);
        List<string> burning = state?.Burning ?? new List<string>();
        string sig = string.Join(";", burning);
        if (sig == _fireSig) return;
        _fireSig = sig;

        _fires?.QueueFree();
        _fires = null;
        if (burning.Count == 0) return;
        _fires = new Node3D { Name = "Fires" };
        AddChild(_fires);
        foreach (ITerrain t in Session.Table.Terrain.Objects.ToList())
        {
            if (!burning.Contains(CityFighting.Key(t)) || t.Shape is not FDG.IBoundedZone b) continue;
            float w = b.Bounds.Right - b.Bounds.Left, d = b.Bounds.Top - b.Bounds.Bottom;
            Vector3 at = World((b.Bounds.Left + b.Bounds.Right) / 2f, (b.Bounds.Bottom + b.Bounds.Top) / 2f, Mathf.Max(0.5f, t.HeightInches));
            _fires.AddChild(Fire(at, w, d));
        }
    }

    private static Node3D Fire(Vector3 at, float w, float d)
    {
        var root = new Node3D { Name = "Fire", Position = at };
        float area = Mathf.Max(4f, w * d);

        var flames = new GpuParticles3D
        {
            Amount = Mathf.Clamp((int)(area * 2.5f), 40, 260), Lifetime = 1.1f, Preprocess = 1.1f, Randomness = 0.5f,
            VisibilityAabb = new Aabb(new Vector3(-w, -2, -d), new Vector3(w * 2, 14, d * 2)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        flames.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w * 0.4f, 0.3f, d * 0.4f),
            Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 2.5f, InitialVelocityMax = 4.5f, Gravity = Vector3.Zero,
            ScaleMin = 0.6f, ScaleMax = 1.2f,
            ColorRamp = new GradientTexture1D { Gradient = Ramp(new Color(1f, 0.78f, 0.2f, 1f), new Color(0.95f, 0.3f, 0.03f, 0.9f), new Color(0.3f, 0.05f, 0.02f, 0f)) },
        };
        flames.DrawPass1 = new QuadMesh { Size = new Vector2(1.6f, 1.6f), Material = Glow("circle_05") };
        root.AddChild(flames);

        var smoke = new GpuParticles3D
        {
            Amount = Mathf.Clamp((int)(area * 0.6f), 12, 60), Lifetime = 4.5f, Preprocess = 4.5f, Randomness = 0.4f,
            VisibilityAabb = new Aabb(new Vector3(-w - 6, -2, -d - 6), new Vector3(w * 2 + 12, 40, d * 2 + 12)),
            Position = new Vector3(0, 2.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        smoke.ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w * 0.35f, 0.5f, d * 0.35f),
            Direction = new Vector3(0.15f, 1, 0.05f), Spread = 15f, InitialVelocityMin = 3f, InitialVelocityMax = 5f, Gravity = Vector3.Zero,
            ScaleMin = 0.8f, ScaleMax = 1.8f, AngleMin = 0, AngleMax = 360,
            ColorRamp = new GradientTexture1D { Gradient = Ramp(new Color(0.25f, 0.22f, 0.2f, 0.0f), new Color(0.22f, 0.2f, 0.19f, 0.55f), new Color(0.3f, 0.3f, 0.3f, 0f)) },
        };
        smoke.DrawPass1 = new QuadMesh { Size = new Vector2(6, 6), Material = Soft("smoke_03") };
        root.AddChild(smoke);

        root.AddChild(new FireLight { Position = new Vector3(0, 3f, 0), OmniRange = Mathf.Max(w, d) * 1.4f + 6f });
        return root;
    }

    private static Gradient Ramp(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, c);
        g.AddPoint(0.45f, b);
        return g;
    }

    private static StandardMaterial3D Glow(string picture) => new()
    {
        AlbedoTexture = GD.Load<Texture2D>($"res://weather/{picture}.png"), VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    private static StandardMaterial3D Soft(string picture) => new()
    {
        AlbedoTexture = GD.Load<Texture2D>($"res://weather/{picture}.png"), VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// <summary>An orange light that flickers like a fire.</summary>
    private sealed partial class FireLight : OmniLight3D
    {
        private double _t = GD.Randf() * 10;

        public FireLight()
        {
            LightColor = new Color(1f, 0.55f, 0.2f);
            LightEnergy = 2f;
            ShadowEnabled = false;
        }

        public override void _Process(double delta)
        {
            _t += delta;
            LightEnergy = 1.6f + 0.5f * Mathf.Sin((float)_t * 9f) * Mathf.Sin((float)_t * 3.7f + 1f);
        }
    }
}
