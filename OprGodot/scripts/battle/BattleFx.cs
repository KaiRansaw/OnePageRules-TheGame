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

    /// <summary>When the last projectile in flight lands (msec) - a wounded model flinches then, not before.</summary>
    private ulong _landAtMs;
    /// <summary>Where the last attack came from (world) - a wounded model flinches away from it.</summary>
    private Vector3 _lastFrom;

    /// <summary>
    /// One attack's effect. hits/attacks &lt;= 0: unknown, every shot lands. big: a big weapon (AttackLook.IsBig) - the
    /// large explosion.
    /// </summary>
    public void Attack(IReadOnlyList<Position> from, IReadOnlyList<Position> to, int volleys, bool melee, string? weapon,
        float hits = -1, float attacks = -1, float life = 1.5f, bool big = false)
    {
        if (from.Count == 0 || to.Count == 0) return;
        if (!UserConfig.Current.View.BattleEffects) { Shots(from, to, volleys, melee, life); return; }
        EAttackLook look = AttackLook.For(weapon, melee);
        float landFraction = attacks > 0 ? Math.Clamp(hits / attacks, 0f, 1f) : 1f;
        int waves = Math.Clamp(volleys, 1, 3);
        float slow = Math.Max(1f, life / 1.5f);   // a screenshot run asks for a long life: play it slowly, so it's caught
        _lastFrom = new Vector3(from.Average(p => p.x), 0, from.Average(p => p.z));
        _lastFrom = World(_lastFrom.X, _lastFrom.Z);
        BattleSounds.Play(AttackLook.FireSound(look), Math.Min(from.Count, 3));
        float firstLand = float.MaxValue;
        for (int w = 0; w < waves; w++)
        {
            float delay = w * 0.22f * slow;
            int n = from.Count, landing = (int)MathF.Round(landFraction * n);
            for (int i = 0; i < n; i++)
            {
                if (_liveFx > 200) break;   // a huge unit: enough is enough
                Position f = from[i], t = to[(i + w) % to.Count];
                bool lands = i < landing;
                float stagger = delay + _fxRng.RandfRange(0f, 0.1f) * slow;
                float land = AttackLook.IsMelee(look) ? MeleeBlow(look, f, t, lands, stagger, slow, big)
                                                      : Shot(look, f, t, lands, stagger, slow, big);
                if (lands) firstLand = Math.Min(firstLand, land);
            }
        }
        if (firstLand < float.MaxValue)
        {
            _landAtMs = Math.Max(_landAtMs, Time.GetTicksMsec() + (ulong)(firstLand * 1000));
            if (AttackLook.HitSound(look) is { } hitSound)
                After(firstLand, () => BattleSounds.Play(big && hitSound == "shot_hit" ? "explosion" : hitSound, 2));
        }
    }

    /// <summary>
    /// Where a shot at a model aims: the model on the table nearest that spot, at the middle of its height (a 3D model's
    /// own height, or the token's) - so hits land ON the model, not beside it (Kai, 2026-11-26).
    /// </summary>
    private Vector3 AimAt(Position p)
    {
        Vector3 spot = World(p.x, p.z);
        ModelView? best = null;
        float bestD = 2.5f * 2.5f;
        foreach (ModelView v in _models.Values)
        {
            Vector3 r = v.Root.Position;
            float d = (r.X - spot.X) * (r.X - spot.X) + (r.Z - spot.Z) * (r.Z - spot.Z);
            if (d < bestD) { bestD = d; best = v; }
        }
        Vector3 at = best != null ? best.Root.Position + Vector3.Up * Math.Clamp(best.Height * 0.5f, 0.4f, 4f) : spot + Vector3.Up * 0.8f;
        return at + new Vector3(_fxRng.RandfRange(-0.12f, 0.12f), _fxRng.RandfRange(-0.1f, 0.1f), _fxRng.RandfRange(-0.12f, 0.12f));
    }

    /// <summary>The muzzle: the firing model's own height (a tank's gun is higher than a trooper's).</summary>
    private Vector3 MuzzleOf(Position p)
    {
        Vector3 spot = World(p.x, p.z);
        float h = 0.95f;
        foreach (ModelView v in _models.Values)
        {
            Vector3 r = v.Root.Position;
            if ((r.X - spot.X) * (r.X - spot.X) + (r.Z - spot.Z) * (r.Z - spot.Z) < 0.36f) { h = Math.Clamp(v.Height * 0.6f, 0.6f, 5f); spot.Y = r.Y; break; }
        }
        return spot + Vector3.Up * h;
    }

    /// <summary>A wounded model flinches back and settles (looks only - the model never moves on the table). Played when
    /// the shot in flight lands.</summary>
    public void Flinch(ModelID id)
    {
        if (!UserConfig.Current.View.BattleEffects) return;
        ulong now = Time.GetTicksMsec();
        float wait = _landAtMs > now ? (_landAtMs - now) / 1000f : 0f;
        After(wait + _fxRng.RandfRange(0f, 0.05f), () =>
        {
            if (!_models.TryGetValue(id, out ModelView? v) || !IsInstanceValid(v.Root)) return;
            Vector3 away = (v.Root.Position - _lastFrom) with { Y = 0 };
            away = away.LengthSquared() < 0.01f ? Vector3.Back : away.Normalized();
            // Knocked back and a little up; the table's own per-frame catch-up eases it back onto its spot.
            v.Root.Position += away * Math.Clamp(0.25f + v.Height * 0.12f, 0.3f, 0.9f) + Vector3.Up * 0.12f;
        });
    }

    /// <summary>Returns when the shot lands (seconds from now).</summary>
    private float Shot(EAttackLook look, Position f, Position t, bool lands, float delay, float slow, bool big)
    {
        Vector3 a = MuzzleOf(f);
        Vector3 b = AimAt(t);
        Vector3 dir = (b - a) with { Y = 0 };
        if (dir.LengthSquared() < 0.01f) return delay;
        dir = dir.Normalized();
        if (!lands)   // a miss flies past, a little wide
            b += dir * _fxRng.RandfRange(4f, 7f) + dir.Cross(Vector3.Up) * _fxRng.RandfRange(-1.5f, 1.5f) + Vector3.Down * 0.4f;
        a += dir * 0.35f;
        (Color core, Color glow) = Colours(look);

        if (look is not EAttackLook.Thrown and not EAttackLook.Spell)
        {
            int kind = look switch { EAttackLook.Heavy or EAttackLook.Explosive => 2, EAttackLook.Flame => 3, EAttackLook.Laser or EAttackLook.Plasma => 1, _ => 0 };
            Spawn(string.Format(MuzzleScenes[big ? 2 : kind], _fxRng.RandiRange(1, 6)), a, dir, big ? 1.0f : look == EAttackLook.Heavy ? 0.6f : 0.45f,
                delay, 1.2f * slow, along: true);
        }

        float dist = a.DistanceTo(b);
        if (look == EAttackLook.Flame)
        {
            // A gout: a stream of growing fire puffs reaching the target.
            float puffTime = Math.Clamp(dist / 22f, 0.3f, 0.8f) * slow;
            for (int p = 0; p < 9; p++)
                Projectile(a, b + new Vector3(_fxRng.RandfRange(-0.4f, 0.4f), 0, _fxRng.RandfRange(-0.4f, 0.4f)), core, glow,
                    big ? 0.4f : 0.25f, 1.0f, dist / puffTime, delay + p * 0.035f * slow, puff: true);
            float landFlame = delay + puffTime;
            if (lands) Spawn(string.Format(Impact, _fxRng.RandiRange(1, 2)), b, -dir, big ? 0.9f : 0.5f, landFlame, 2f * slow, tint: glow);
            return landFlame;
        }

        // Slowed so each shot can be followed, but never so slow a long shot drags: a third to three-quarters of a second.
        float travel = Math.Clamp(dist / Math.Max(AttackLook.Speed(look), 1f), 0.3f, 0.75f) * slow;
        float size = look switch { EAttackLook.Explosive => 0.2f, EAttackLook.Plasma or EAttackLook.Spell => 0.17f, EAttackLook.Thrown => 0.06f, _ => 0.08f };
        if (big) size *= 1.6f;
        float trail = look switch { EAttackLook.Laser => 7f, EAttackLook.Explosive => 5f, EAttackLook.Thrown => 1.5f, EAttackLook.Plasma or EAttackLook.Spell => 3.5f, _ => 4.5f };
        Projectile(a, b, core, glow, size, trail, dist / travel, delay, puff: false, arc: look == EAttackLook.Thrown ? 0.12f * dist : 0);
        float land = delay + travel;
        if (lands) Explode(look, b, dir, land, slow, big);
        return land;
    }

    /// <summary>The hit, sized and picked by weapon: sparks for bullets and arrows, an energy burst for lasers, plasma and
    /// spells, a blast for explosives - and the big blast for big weapons (vehicles, monsters, Blast(3)+, heavy guns).</summary>
    private void Explode(EAttackLook look, Vector3 at, Vector3 dir, float delay, float slow, bool big)
    {
        string scene = look switch
        {
            EAttackLook.Explosive => BigImpact,
            EAttackLook.Plasma or EAttackLook.Spell or EAttackLook.Laser => Hit,
            _ => big ? BigImpact : Impact,
        };
        float scale = look switch
        {
            EAttackLook.Thrown => 0.3f,
            EAttackLook.Bullet => 0.4f,
            EAttackLook.Heavy => 0.5f,
            EAttackLook.Explosive => 0.75f,
            _ => 0.5f,
        };
        if (big) scale *= 1.9f;
        Color? tint = look switch { EAttackLook.Plasma => new Color(0.4f, 0.7f, 1f), EAttackLook.Laser => new Color(1f, 0.3f, 0.25f), EAttackLook.Spell => new Color(0.75f, 0.4f, 1f), _ => null };
        Spawn(string.Format(scene, _fxRng.RandiRange(1, 2)), at, -dir, scale, delay, 2.4f * slow, tint: tint);
        if (big)   // a second, ground-level burst so the big ones read as big from any camera angle
            Spawn(string.Format(BigImpact, _fxRng.RandiRange(1, 2)), at with { Y = at.Y * 0.4f }, -dir, scale * 0.7f, delay + 0.06f * slow, 2.4f * slow);
    }

    /// <summary>Returns when the blow lands (seconds from now).</summary>
    private float MeleeBlow(EAttackLook look, Position f, Position t, bool lands, float delay, float slow, bool big)
    {
        Vector3 a = MuzzleOf(f), b = AimAt(t);
        Vector3 dir = (b - a) with { Y = 0 };
        dir = dir.LengthSquared() < 0.01f ? Vector3.Forward : dir.Normalized();
        Vector3 mid = a + dir * Math.Min(1.2f, a.DistanceTo(b) * 0.6f);
        float swing = big ? 0.9f : look == EAttackLook.Blunt ? 0.55f : 0.45f;
        Spawn(look == EAttackLook.Claw ? Claw : Swing, mid, dir, swing, delay, 1.6f * slow,
            tint: look switch { EAttackLook.Blunt => new Color(1f, 0.75f, 0.35f), EAttackLook.Claw => new Color(1f, 0.35f, 0.3f), _ => null });
        float land = delay + 0.15f * slow;
        if (lands) Spawn(string.Format(big ? BigImpact : Hit, _fxRng.RandiRange(1, 2)), b, -dir, big ? 0.8f : 0.4f, land, 2f * slow);
        return land;
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

    /// <summary>
    /// A shot flying from a to b at speed (inches a second): a glowing head and a fading streak behind it, as long as
    /// `trail` inches, so the shot can be traced from model to model (Kai, 2026-11-26). A puff (flame) grows instead.
    /// arc: how high a thrown thing lobs.
    /// </summary>
    private void Projectile(Vector3 a, Vector3 b, Color core, Color glow, float size, float trail, float speed, float delay,
        bool puff, float arc = 0)
    {
        float dist = a.DistanceTo(b);
        if (dist < 0.1f) return;
        float time = dist / Math.Max(speed, 1f);
        // Plain mixed colour, not added light: added light washes every colour out to white on a bright table.
        var headMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = puff ? core with { A = 0.85f } : arc > 0 ? core : core.Lerp(Colors.White, 0.3f),
        };
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = size, Height = size * 2, RadialSegments = 10, Rings = 5 },
            MaterialOverride = headMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(head);
        _liveFx++;
        head.Position = a;

        MeshInstance3D? tail = null;
        StandardMaterial3D? tailMat = null;
        if (!puff)
        {
            // The streak: a thin box from the head back along the path, see-through at its far end.
            tailMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = Colors.White,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            tail = new MeshInstance3D { Mesh = TailMesh(glow, size), MaterialOverride = tailMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(tail);
        }

        OmniLight3D? light = null;
        if (!puff && arc <= 0 && _fxRng.Randf() < 0.35f)
        {
            light = new OmniLight3D { LightColor = glow, LightEnergy = 1.2f, OmniRange = 3.5f, ShadowEnabled = false };
            head.AddChild(light);
        }

        Vector3 At(float k)
        {
            Vector3 p = a.Lerp(b, Math.Clamp(k, 0f, 1f));
            if (arc > 0) p.Y += arc * 4f * k * (1 - k);
            return p;
        }
        void PlaceTail(float kHead, float kTail)
        {
            if (tail == null) return;
            Vector3 h = At(kHead), t = At(kTail);
            float len = h.DistanceTo(t);
            if (len < 0.02f) { tail.Visible = false; return; }
            tail.Visible = true;
            tail.Position = (h + t) / 2;
            Vector3 d = (h - t) / len;
            tail.Basis = Basis.LookingAt(d, Mathf.Abs(d.Y) > 0.99f ? Vector3.Right : Vector3.Up).Scaled(new Vector3(1, 1, len));
        }
        float trailK = trail / dist;   // the streak's length, as a share of the path

        Tween tw = CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.TweenCallback(Callable.From(() => head.Visible = true));
        tw.TweenMethod(Callable.From<float>(k =>
        {
            if (!IsInstanceValid(head)) return;
            head.Position = At(k);
            if (puff) { head.Scale = Vector3.One * (0.4f + 2.2f * k); headMat.AlbedoColor = core.Lerp(glow, Math.Min(1f, k * 1.4f)) with { A = 0.85f * (1f - k * 0.8f) }; }
            else PlaceTail(k, k - trailK);
        }), 0f, 1f, time);
        // Arrived: the head goes, the streak's end catches up with it and fades.
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(head)) head.Visible = false; }));
        if (tail != null)
            tw.TweenMethod(Callable.From<float>(u =>
            {
                if (!IsInstanceValid(tail)) return;
                PlaceTail(1f, 1f - trailK * (1f - u));
                tailMat!.AlbedoColor = Colors.White with { A = 1f - u };
            }), 0f, 1f, Math.Min(0.25f, time * 0.6f));
        else tw.TweenMethod(Callable.From<float>(v => headMat.AlbedoColor = headMat.AlbedoColor with { A = v }), headMat.AlbedoColor.A, 0f, 0.15f);
        tw.TweenCallback(Callable.From(() => { _liveFx--; head.QueueFree(); tail?.QueueFree(); }));
    }

    /// <summary>A unit-long streak along Z (-0.5 tail .. +0.5 head): solid colour at the head, clear at the tail.</summary>
    private static ArrayMesh TailMesh(Color c, float size)
    {
        float r = Math.Max(size * 0.55f, 0.035f);
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        // Two crossed ribbons, so it reads from any angle.
        foreach (Vector3 side in new[] { new Vector3(r, 0, 0), new Vector3(0, r, 0) })
        {
            Vector3 h1 = new Vector3(0, 0, -0.5f) + side, h2 = new Vector3(0, 0, -0.5f) - side;   // LookingAt: -Z is forward = head
            Vector3 t1 = new Vector3(0, 0, 0.5f) + side * 0.3f, t2 = new Vector3(0, 0, 0.5f) - side * 0.3f;
            Color head = c with { A = 0.95f }, tailC = c with { A = 0f };
            void V(Vector3 p, Color col) { st.SetColor(col); st.AddVertex(p); }
            V(h1, head); V(h2, head); V(t1, tailC);
            V(h2, head); V(t2, tailC); V(t1, tailC);
        }
        return st.Commit();
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
