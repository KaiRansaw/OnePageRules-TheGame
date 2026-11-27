using FDG;
using FdgRaylib.Config;
using FdgRaylib.Game;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// Battle shot and swing effects (Kai, 2026-11-26), replacing the placeholder tracers: a muzzle flash at every firing
/// model, a projectile that flies to the target (hits land with an impact, misses fly past), flame gouts, and for melee
/// a swing or claw at the striker with a hit on whoever is struck. The look follows the weapon (AttackLook). Effects:
/// Binbun VFX (assets/BinbunVFX*, see their License.txt). Sounds: BattleSounds.
/// </summary>
public partial class BattleTable
{
    private const string Fx = "res://assets/";
    private static readonly string[] MuzzleScenes =
    {
        Fx + "BinbunVFX/muzzle_flash/effects/muzzle_flash/muzzle_flash_0{0}.tscn",
        Fx + "BinbunVFX/muzzle_flash/effects/short_flash/short_flash_0{0}.tscn",
        Fx + "BinbunVFX/muzzle_flash/effects/big_flash/big_flash_0{0}.tscn",
        Fx + "BinbunVFX/muzzle_flash/effects/wide_flash/wide_flash_0{0}.tscn",
    };
    private const string Impact = Fx + "BinbunVFX_Vol2/StylizedHitFX/effects/impact/vfx_impact_0{0}.tscn";
    private const string BigImpact = Fx + "BinbunVFX_Vol2/StylizedHitFX/effects/big_impact/vfx_big_impact_0{0}.tscn";
    private const string Hit = Fx + "BinbunVFX_Vol2/StylizedHitFX/effects/hit/vfx_hit_0{0}.tscn";
    private const string Swing = Fx + "BinbunVFX_Vol2/BattleFX/effects/swing/vfx_blank_swing.tscn";
    private const string Claw = Fx + "BinbunVFX_Vol2/BattleFX/effects/claw/vfx_blank_claw.tscn";

    private static readonly Dictionary<string, PackedScene?> _scenes = new();
    private readonly RandomNumberGenerator _fxRng = new();
    private int _liveFx;

    private static PackedScene? Scene(string path) =>
        _scenes.TryGetValue(path, out var s) ? s : _scenes[path] = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;

    /// <summary>One attack's effect. hits/attacks &lt;= 0: unknown, every shot lands.</summary>
    public void Attack(IReadOnlyList<Position> from, IReadOnlyList<Position> to, int volleys, bool melee, string? weapon,
        float hits = -1, float attacks = -1, float life = 1.5f)
    {
        if (from.Count == 0 || to.Count == 0) return;
        if (!UserConfig.Current.View.BattleEffects) { Shots(from, to, volleys, melee, life); return; }
        EAttackLook look = AttackLook.For(weapon, melee);
        float landFraction = attacks > 0 ? Math.Clamp(hits / attacks, 0f, 1f) : 1f;
        int waves = Math.Clamp(volleys, 1, 3);
        float slow = Math.Max(1f, life / 1.5f);   // a screenshot run asks for a long life: play it slowly, so it's caught
        BattleSounds.Play(AttackLook.FireSound(look), Math.Min(from.Count, 3));
        for (int w = 0; w < waves; w++)
        {
            float delay = w * 0.18f * slow;
            int n = from.Count, landing = (int)MathF.Round(landFraction * n);
            for (int i = 0; i < n; i++)
            {
                if (_liveFx > 160) return;   // a huge unit: enough is enough
                Position f = from[i], t = to[(i + w) % to.Count];
                bool lands = i < landing;
                float stagger = delay + _fxRng.RandfRange(0f, 0.12f) * slow;
                if (AttackLook.IsMelee(look)) MeleeBlow(look, f, t, lands, stagger, slow);
                else Shot(look, f, t, lands, stagger, slow);
            }
        }
        if (AttackLook.HitSound(look) is { } hitSound && landFraction > 0)
            After(0.25f * slow, () => BattleSounds.Play(hitSound, 2));
    }

    private void Shot(EAttackLook look, Position f, Position t, bool lands, float delay, float slow)
    {
        Vector3 a = World(f.x, f.z, 0.95f);
        Vector3 b = World(t.x + _fxRng.RandfRange(-0.35f, 0.35f), t.z + _fxRng.RandfRange(-0.35f, 0.35f), 0.7f);
        Vector3 dir = (b - a) with { Y = 0 };
        if (dir.LengthSquared() < 0.01f) return;
        dir = dir.Normalized();
        if (!lands)   // a miss flies past, a little wide
            b += dir * _fxRng.RandfRange(3f, 6f) + dir.Cross(Vector3.Up) * _fxRng.RandfRange(-1.2f, 1.2f);
        a += dir * 0.35f;
        (Color core, Color glow) = Colours(look);

        if (look is not EAttackLook.Thrown and not EAttackLook.Spell)
        {
            int kind = look switch { EAttackLook.Heavy or EAttackLook.Explosive => 2, EAttackLook.Flame => 3, EAttackLook.Laser or EAttackLook.Plasma => 1, _ => 0 };
            Spawn(string.Format(MuzzleScenes[kind], _fxRng.RandiRange(1, 6)), a, dir, look == EAttackLook.Heavy ? 0.6f : 0.45f, delay, 1.2f * slow, along: true);
        }

        if (look == EAttackLook.Flame)
        {
            // A gout: a stream of growing fire puffs reaching the target.
            for (int p = 0; p < 7; p++)
                Projectile(a, b + new Vector3(_fxRng.RandfRange(-0.5f, 0.5f), 0, _fxRng.RandfRange(-0.5f, 0.5f)), core, glow,
                    0.25f, 1.0f, 20f / slow, delay + p * 0.04f * slow, puff: true);
            return;
        }

        float speed = AttackLook.Speed(look) / slow;
        float travel = a.DistanceTo(b) / Math.Max(speed, 1f);
        float size = look switch { EAttackLook.Explosive => 0.22f, EAttackLook.Plasma or EAttackLook.Spell => 0.2f, EAttackLook.Thrown => 0.07f, _ => 0.09f };
        float length = look switch { EAttackLook.Laser => 4f, EAttackLook.Bullet or EAttackLook.Heavy => 1.6f, EAttackLook.Thrown => 0.9f, _ => 0.5f };
        Projectile(a, b, core, glow, size, length, speed, delay, puff: false, arc: look == EAttackLook.Thrown ? 0.12f * a.DistanceTo(b) : 0);

        if (!lands) return;
        string scene = look switch
        {
            EAttackLook.Explosive => BigImpact,
            EAttackLook.Plasma or EAttackLook.Spell or EAttackLook.Laser => Hit,
            _ => Impact,
        };
        float scale = look == EAttackLook.Explosive ? 0.9f : look == EAttackLook.Thrown ? 0.3f : 0.45f;
        Spawn(string.Format(scene, _fxRng.RandiRange(1, 2)), b, -dir, scale, delay + travel, 2.2f * slow);
    }

    private void MeleeBlow(EAttackLook look, Position f, Position t, bool lands, float delay, float slow)
    {
        Vector3 a = World(f.x, f.z, 0.9f), b = World(t.x, t.z, 0.8f);
        Vector3 dir = (b - a) with { Y = 0 };
        dir = dir.LengthSquared() < 0.01f ? Vector3.Forward : dir.Normalized();
        Vector3 mid = a + dir * Math.Min(1.2f, a.DistanceTo(b) * 0.6f);
        Spawn(look == EAttackLook.Claw ? Claw : Swing, mid, dir, look == EAttackLook.Blunt ? 0.55f : 0.45f, delay, 1.6f * slow,
            tint: look switch { EAttackLook.Blunt => new Color(1f, 0.75f, 0.35f), EAttackLook.Claw => new Color(1f, 0.35f, 0.3f), _ => null });
        if (lands) Spawn(string.Format(Hit, _fxRng.RandiRange(1, 2)), b, -dir, 0.4f, delay + 0.15f * slow, 2f * slow);
    }

    private static (Color core, Color glow) Colours(EAttackLook look) => look switch
    {
        EAttackLook.Laser => (new Color(1f, 0.55f, 0.5f), new Color(1f, 0.1f, 0.1f)),
        EAttackLook.Plasma => (new Color(0.75f, 0.95f, 1f), new Color(0.2f, 0.55f, 1f)),
        EAttackLook.Flame => (new Color(1f, 0.8f, 0.35f), new Color(1f, 0.35f, 0.05f)),
        EAttackLook.Explosive => (new Color(1f, 0.9f, 0.7f), new Color(1f, 0.5f, 0.15f)),
        EAttackLook.Spell => (new Color(0.95f, 0.8f, 1f), new Color(0.6f, 0.2f, 1f)),
        EAttackLook.Thrown => (new Color(0.55f, 0.42f, 0.3f), new Color(0.3f, 0.22f, 0.15f)),
        _ => (new Color(1f, 0.95f, 0.7f), new Color(1f, 0.7f, 0.25f)),
    };

    /// <summary>A glowing streak flying from a to b (a puff grows as it goes). arc: how high a thrown thing lobs.</summary>
    private void Projectile(Vector3 a, Vector3 b, Color core, Color glow, float size, float length, float speed, float delay,
        bool puff, float arc = 0)
    {
        float dist = a.DistanceTo(b);
        if (dist < 0.1f) return;
        float time = dist / Math.Max(speed, 1f);
        // Plain mixed colour, not added light: added light washes every colour out to white on a bright table.
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = puff ? core with { A = 0.85f } : arc > 0 ? core : glow.Lerp(core, 0.45f),
            NoDepthTest = false,
        };
        Mesh mesh = puff ? new SphereMesh { Radius = size, Height = size * 2, RadialSegments = 8, Rings = 4 }
            : new CapsuleMesh { Radius = size, Height = Math.Max(length, size * 2.2f), RadialSegments = 8, Rings = 2 };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        _liveFx++;
        mi.Position = a;
        Vector3 dir = (b - a).Normalized();
        if (!puff) mi.Basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up) * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0));

        OmniLight3D? light = null;
        if (!puff && arc <= 0 && _fxRng.Randf() < 0.35f)
        {
            light = new OmniLight3D { LightColor = glow, LightEnergy = 1.2f, OmniRange = 3.5f, ShadowEnabled = false };
            mi.AddChild(light);
        }

        Tween tw = CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.TweenCallback(Callable.From(() => mi.Visible = true));
        tw.TweenMethod(Callable.From<float>(k =>
        {
            if (!IsInstanceValid(mi)) return;
            Vector3 p = a.Lerp(b, k);
            if (arc > 0) p.Y += arc * 4f * k * (1 - k);
            mi.Position = p;
            if (puff) { mi.Scale = Vector3.One * (0.4f + 2.2f * k); mat.AlbedoColor = core.Lerp(glow, Math.Min(1f, k * 1.4f)) with { A = 0.85f * (1f - k * 0.8f) }; }
        }), 0f, 1f, time);
        tw.TweenMethod(Callable.From<float>(v => mat.AlbedoColor = mat.AlbedoColor with { A = v }), mat.AlbedoColor.A, 0f, puff ? 0.15f : 0.06f);
        tw.TweenCallback(Callable.From(() => { _liveFx--; mi.QueueFree(); }));
    }

    /// <summary>
    /// Plays one Binbun effect scene at a point, facing dir (along: its +X points down dir - the muzzle flashes; otherwise
    /// it is turned to face dir), and frees it after a while.
    /// </summary>
    private void Spawn(string path, Vector3 at, Vector3 dir, float scale, float delay, float keep, bool along = false, Color? tint = null)
    {
        if (Scene(path) is not { } packed) return;
        _liveFx++;
        After(delay, () =>
        {
            if (packed.Instantiate() is not Node3D fx) { _liveFx--; return; }
            if (fx.Get("one_shot").VariantType == Variant.Type.Bool) fx.Set("one_shot", true);
            if (fx.Get("autoplay").VariantType == Variant.Type.Bool) fx.Set("autoplay", false);
            if (tint is { } c && fx.Get("primary_color").VariantType == Variant.Type.Color) fx.Set("primary_color", c);
            AddChild(fx);
            fx.Position = at;
            Vector3 d = dir.LengthSquared() < 0.0001f ? Vector3.Forward : dir.Normalized();
            Vector3 flat = d with { Y = 0 };
            if (flat.LengthSquared() < 0.0001f) flat = Vector3.Forward;
            fx.Basis = along
                ? new Basis(flat.Normalized(), Vector3.Up, flat.Normalized().Cross(Vector3.Up)).Orthonormalized().Scaled(Vector3.One * scale)
                : Basis.LookingAt(flat.Normalized(), Vector3.Up).Scaled(Vector3.One * scale);
            if (fx.HasMethod("play")) fx.Call("play");
            GetTree().CreateTimer(keep).Timeout += () => { _liveFx--; if (IsInstanceValid(fx)) fx.QueueFree(); };
        });
    }

    private void After(float delay, Action act)
    {
        if (delay <= 0.001f) { act(); return; }
        GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(this)) act(); };
    }
}

/// <summary>
/// Battle sounds (Kai, 2026-11-26): one sound from a folder of OprGodot/sounds, a random pick each time, a few at most
/// at once, at the "Battle sounds" volume. Sounds: see each folder's License.txt.
/// </summary>
public static class BattleSounds
{
    private static readonly Dictionary<string, AudioStream[]> _banks = new();
    private static readonly List<AudioStreamPlayer> _players = new();
    private static readonly RandomNumberGenerator _rng = new();
    private static ulong _lastMs;

    private static AudioStream[] Bank(string folder)
    {
        if (_banks.TryGetValue(folder, out var b)) return b;
        var list = new List<AudioStream>();
        string dir = "res://sounds/" + folder;
        foreach (string file in DirAccess.GetFilesAt(dir))
        {
            string name = file.EndsWith(".import") ? file[..^7] : file;
            if (!name.EndsWith(".ogg") || list.Any(s => s.ResourcePath.EndsWith("/" + name))) continue;
            if (GD.Load<AudioStream>(dir + "/" + name) is { } s) list.Add(s);
        }
        return _banks[folder] = list.ToArray();
    }

    /// <summary>Plays up to `layers` sounds from the folder, slightly staggered and pitched, so a volley sounds like many.</summary>
    public static void Play(string folder, int layers = 1)
    {
        float vol = UserConfig.Current.View.EffectsVolume;
        if (vol <= 0.001f || Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;
        AudioStream[] bank = Bank(folder);
        if (bank.Length == 0) return;
        ulong now = Time.GetTicksMsec();
        if (now - _lastMs < 40) layers = 1;   // many attacks at once: don't stack a wall of noise
        _lastMs = now;
        for (int i = 0; i < Math.Clamp(layers, 1, 3); i++)
        {
            AudioStreamPlayer p = Free(tree);
            p.Stream = bank[_rng.RandiRange(0, bank.Length - 1)];
            p.VolumeDb = Mathf.LinearToDb(Math.Max(vol, 0.0001f)) - 4f - i * 3f;
            p.PitchScale = _rng.RandfRange(0.92f, 1.08f);
            var q = p;
            if (i == 0 && q.IsInsideTree()) q.Play();
            else if (i == 0) q.CallDeferred(AudioStreamPlayer.MethodName.Play, 0f);   // a new player joins the tree next frame
            else tree.CreateTimer(0.06f * i + _rng.RandfRange(0, 0.05f)).Timeout += () => { if (GodotObject.IsInstanceValid(q) && q.IsInsideTree()) q.Play(); };
        }
    }

    private static AudioStreamPlayer Free(SceneTree tree)
    {
        _players.RemoveAll(p => !GodotObject.IsInstanceValid(p));
        foreach (AudioStreamPlayer p in _players) if (!p.Playing) return p;
        if (_players.Count >= 10) return _players.OrderBy(p => p.GetPlaybackPosition()).Last();   // cut the oldest
        var n = new AudioStreamPlayer { Bus = "Master" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, n);
        _players.Add(n);
        return n;
    }
}
