using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FdgRaylib.Game;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Hud;
using static FdgRaylib.Game.TableMarkers;

namespace OprGodot.Battle;

/// <summary>
/// The optional rules' markers on the table (lane 4, 2026-11-27) - what <see cref="TableMarkers"/> lists: Random Events
/// markers (Zombie Horde, Swarm, Havoc Spawn, Rebels, Anomaly, Sniper, Robot), Living Spells, Army Terrain and the
/// Tactical Phase's forward positions. Each one is a token on its real footprint, a stand-in model from the bundled
/// stylised packs, the ring its rule measures (coloured by what the area does) and a name readable from the general's
/// view. Living spells and army terrain are also terrain pieces in the engine; those pieces are not dressed as terrain
/// (<see cref="TableMarkers.IsMarkerPiece"/>) - this draws them. The Rules view (V) keeps its flat coloured pieces and
/// adds the rings and names.
/// </summary>
public partial class BattleTable
{
    private Node3D? _markerRoot;
    private string _markerSig = "";
    private List<Marker> _markerList = new();

    /// <summary>The markers on the table now (for hovers).</summary>
    public IReadOnlyList<Marker> Markers => _markerList;

    private void SyncMarkers()
    {
        var list = TableMarkers.For(Session.Table, Session.Lobby.Vm.GameSystem);
        string sig = TableMarkers.Signature(list) + "|" + _rulesView;
        if (sig == _markerSig) return;
        _markerSig = sig;
        _markerList = list;
        _markerRoot?.QueueFree();
        _markerRoot = null;
        if (list.Count == 0) return;
        _markerRoot = new Node3D { Name = "Markers" };
        AddChild(_markerRoot);
        TerrainLooks.ETheme theme = TerrainLooks.ThemeFor(Session.Lobby.Vm.GameSystem);
        foreach (Marker m in list) BuildMarker(_markerRoot, m, theme);
    }

    /// <summary>The marker under the screen point (its token, or its ring for areas a unit can stand in).</summary>
    public Marker? MarkerAt(Vector2 screen)
    {
        if (TableAt(screen) is not { } p) return null;
        float Dist(float x, float z) => new Vector2(p.X - x, p.Y - z).Length();
        return _markerList
            .Select(m => (m, d: Mathf.Min(Dist(m.X, m.Z), m.X2 is { } x2 && m.Z2 is { } z2 ? Dist(x2, z2) : float.MaxValue)))
            .Where(t => t.d <= Mathf.Max(t.m.Radius, 0.8f) + 0.2f)
            .OrderBy(t => t.d).Select(t => t.m).FirstOrDefault();
    }

    // ------------------------------------------------------------------ one marker

    private static readonly Color EventColour = new("#e0b040");

    private Color OwnerColour(Marker m)
    {
        if (m.OwnerId is { } id && Session.Table.Players.Objects.FirstOrDefault(p => p.IsFilled && p.PlayerID.ID.ToString() == id) is { } seat)
            return SeatColour(seat.PlayerID);
        return m.Kind == EKind.Event ? EventColour : new Color(0.85f, 0.85f, 0.9f);
    }

    /// <summary>What a ring means, as a colour: the Rules view's own colours for Dangerous / Difficult / Cover, red for
    /// "takes hits", the owner's colour for a buff, pale blue for a reach.</summary>
    private Color RingColour(Marker m, Color owner) => m.RingKind switch
    {
        ERing.Dangerous => TableLook.RulesColour(ETerrainType.Dangerous),
        ERing.Difficult => TableLook.RulesColour(ETerrainType.Difficult),
        ERing.Cover => TableLook.RulesColour(ETerrainType.Cover),
        ERing.Hits => new Color("#e04848"),
        ERing.Reach => new Color("#8fc8ff"),
        _ => owner,
    };

    private void BuildMarker(Node3D root, Marker m, TerrainLooks.ETheme theme)
    {
        Color owner = OwnerColour(m);
        Color ring = RingColour(m, owner);
        bool isLong = m.IsLong(Session.Lobby.Vm.GameSystem);
        var spots = new List<Vector2> { new(m.X, m.Z) };
        if (m.X2 is { } x2 && m.Z2 is { } z2) spots.Add(new Vector2(x2, z2));
        float top = 0f;
        foreach (Vector2 at in spots)
        {
            var holder = new Node3D { Name = "Marker", Position = World(at.X, at.Y) };
            holder.SetMeta("marker", m.Name);
            root.AddChild(holder);
            // The long axis is (-Fz, Fx) on the table = (-Fz, 0, -Fx) in the world; the holder's local x is (cos, 0, -sin).
            float yaw = Mathf.Atan2(m.Fx, -m.Fz);
            if (isLong) holder.Rotation = new Vector3(0, yaw, 0);
            if (!_rulesView || m.Kind == EKind.Event || m.Kind == EKind.ForwardPosition)
            {
                Token(holder, m, owner, isLong);
                top = Mathf.Max(top, Look(holder, m, theme, owner, isLong));
            }
            if (m.Ring > 0) Ring(holder, m.Ring, ring, area: m.RingKind is ERing.Dangerous or ERing.Difficult or ERing.Cover, isLong ? m : null,
                strong: m.RingKind == ERing.Hits);
        }
        if (m.Line && spots.Count == 2 && spots[0].DistanceTo(spots[1]) > 0.1f)
        {
            // Twin Shock-Spheres / Arcane Pillars: the line between the copies is what hurts.
            Vector3 a = World(spots[0].X, spots[0].Y, 0.15f), b = World(spots[1].X, spots[1].Y, 0.15f);
            var beam = TableLook.Mesh(root, new BoxMesh { Size = new Vector3(0.12f, 0.08f, a.DistanceTo(b)) },
                TableLook.SharedMat(ring with { A = 0.9f }, 0.4f, ring, 1.6f, unshaded: true), (a + b) / 2);
            beam.LookAt(b, Vector3.Up, true);
            beam.Position = (a + b) / 2;
            beam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        // The name, over the first copy, above the model.
        var label = new Label3D
        {
            Text = m.Label.ToUpperInvariant(), Font = HudKit.Bold, FontSize = 40, PixelSize = 0.015f, OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, OutlineModulate = new Color(0, 0, 0, 0.9f),
            Modulate = (m.Kind == EKind.Event ? EventColour : owner).Lerp(Colors.White, 0.35f),
            Position = World(m.X, m.Z, Mathf.Max(top, 0.4f) + 0.7f),
        };
        root.AddChild(label);
    }

    /// <summary>The marker's footprint: a thin disc (or the long piece's bar) in the owner's colour with a dark top.</summary>
    private static void Token(Node3D holder, Marker m, Color owner, bool isLong)
    {
        Mesh rim, face;
        if (isLong)
        {
            rim = new BoxMesh { Size = new Vector3(FDG.LivingSpells.LongLength, 0.1f, FDG.LivingSpells.LongWidth) };
            face = new BoxMesh { Size = new Vector3(FDG.LivingSpells.LongLength - 0.12f, 0.12f, FDG.LivingSpells.LongWidth - 0.12f) };
        }
        else
        {
            float r = m.Radius;
            rim = TableLook.SharedMesh($"marker-rim-{r:0.00}", () => new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.1f, RadialSegments = 32 });
            face = TableLook.SharedMesh($"marker-face-{r:0.00}", () => new CylinderMesh { TopRadius = r - 0.07f, BottomRadius = r - 0.07f, Height = 0.12f, RadialSegments = 32 });
        }
        TableLook.Mesh(holder, rim, TableLook.SharedMat(owner, 0.4f, owner, 1.2f), new Vector3(0, 0.05f, 0));
        TableLook.Mesh(holder, face, TableLook.SharedMat(new Color(0.09f, 0.09f, 0.1f), 0.6f), new Vector3(0, 0.06f, 0));
    }

    /// <summary>The ring the rule measures, flat on the mat; an area (Dangerous / Difficult / Cover) is also shaded
    /// inside so it reads as ground, not a range. A long spell's band follows its bar.</summary>
    private static void Ring(Node3D holder, float radius, Color c, bool area, Marker? longOne, bool strong = false)
    {
        // Areas the ground itself has (Dangerous / Difficult / Cover) and "takes hits" stand out; a buff's or a reach's
        // ring is lighter - often 12" across a busy table.
        var mat = TableLook.SharedMat(c with { A = area || strong ? 0.95f : 0.55f }, 0.5f, c, area || strong ? 1.3f : 0.8f, unshaded: true);
        if (longOne != null)
        {
            // A band round the bar: the bar's half length plus the reach on each end.
            float reach = radius - FDG.LivingSpells.LongWidth / 2f, len = FDG.LivingSpells.LongLength + reach * 2, wide = radius * 2;
            foreach ((Vector3 at, Vector3 size) in new[]
            {
                (new Vector3(0, 0.05f, wide / 2), new Vector3(len, 0.03f, 0.12f)), (new Vector3(0, 0.05f, -wide / 2), new Vector3(len, 0.03f, 0.12f)),
                (new Vector3(len / 2, 0.05f, 0), new Vector3(0.12f, 0.03f, wide)), (new Vector3(-len / 2, 0.05f, 0), new Vector3(0.12f, 0.03f, wide)),
            })
                TableLook.Mesh(holder, new BoxMesh { Size = size }, mat, at).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            return;
        }
        float w = area || strong ? (radius > 8 ? 0.16f : 0.12f) : 0.08f;
        var ring = TableLook.Mesh(holder, TableLook.SharedMesh($"marker-ring-{radius:0.00}-{w:0.00}", () =>
            new TorusMesh { InnerRadius = radius - w / 2, OuterRadius = radius + w / 2, Rings = Mathf.Clamp((int)(radius * 24), 48, 256), RingSegments = 6 }),
            mat, new Vector3(0, 0.05f, 0));
        ring.Scale = new Vector3(1, 0.25f, 1);
        ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        if (area)
        {
            var fill = TableLook.Mesh(holder, TableLook.SharedMesh($"marker-area-{radius:0.00}", () =>
                new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 64 }),
                TableLook.SharedMat(c with { A = 0.16f }, 0.9f, unshaded: true), new Vector3(0, 0.035f, 0));
            fill.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    // ------------------------------------------------------------------ the stand-in models

    /// <summary>The marker's model on its token; returns how tall it stands (for the label).</summary>
    private static float Look(Node3D holder, Marker m, TerrainLooks.ETheme theme, Color owner, bool isLong)
    {
        bool grim = theme == TerrainLooks.ETheme.Grimdark;
        float r = Mathf.Max(0.45f, m.Radius - 0.05f), wide = r * 2f;
        float tall = m.Kind == EKind.ArmyTerrain ? FDG.ArmyTerrain.PieceHeight : m.Kind == EKind.Event ? 1.3f : 1.1f;
        const string N = "stylised/nature/models/", P = "stylised/props/models/", S = "stylised/scifi/models/", V = "stylised/village/models/";
        Color bone = new("#d8cfb8"), sick = new("#7f9a6a"), stone = new("#9a958a"), magic = new("#b070ff");
        float h = 0;
        float Put(string file, float w, float t, Vector3 at, float yaw = 0, Color? tint = null, float glow = 0)
        {
            if (Model(holder, file, w, t, at, yaw, tint, glow) is { } top) h = Mathf.Max(h, top);
            return h;
        }
        switch (m.Look)
        {
            // Random Events
            case "anomaly":
                Put("space/rock_crystals.glb", wide, tall, Vector3.Zero, 0, magic, 1.2f);
                Glow(holder, magic, 0.35f, new Vector3(0, tall * 0.6f, 0));
                break;
            case "zombies":
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Tau / 3f;
                    Put(P + "Dummy.gltf", r * 0.8f, tall, new Vector3(Mathf.Cos(a) * r * 0.45f, 0.1f, Mathf.Sin(a) * r * 0.45f), a + 0.6f, sick);
                }
                break;
            case "sniper":
                Put(P + "Dummy.gltf", r, tall, new Vector3(0, 0.1f, 0), 0.4f, new Color("#555a60"));
                break;
            case "swarm":
                for (int i = 0; i < 7; i++)
                {
                    float a = i * Mathf.Tau / 7f, d = i == 0 ? 0 : r * 0.55f;
                    Put(N + $"Pebble_Round_{i % 5 + 1}.gltf", r * 0.5f, 0.25f, new Vector3(Mathf.Cos(a) * d, 0.1f, Mathf.Sin(a) * d), a,
                        grim ? new Color("#6a3a7a") : new Color("#4a3a2c"));
                }
                break;
            case "havoc":
                Put(N + "TwistedTree_1.gltf", wide, tall, new Vector3(0, 0.1f, 0), 0, new Color("#c04a8a"), 0.3f);
                break;
            case "robot":
                if (grim) Put("space/turret_single.glb", wide, tall, new Vector3(0, 0.1f, 0), 0, new Color("#b0b8c0"));
                else Put("nature/statue_block.glb", wide, tall, new Vector3(0, 0.1f, 0), 0, stone);
                break;
            case "rebels":
                Put(P + "Dummy.gltf", r * 0.8f, tall, new Vector3(-r * 0.35f, 0.1f, 0), 0.3f, new Color("#8a6a4a"));
                Put(P + "Dummy.gltf", r * 0.8f, tall, new Vector3(r * 0.35f, 0.1f, 0.1f), -0.4f, new Color("#8a6a4a"));
                Put(P + "Banner_1.gltf", r * 0.6f, tall * 1.3f, new Vector3(0, 0.1f, -r * 0.4f), 0, owner);
                break;

            // Living Spells
            case "pyre":
                Put("survival/tree-log.glb", wide, 0.35f, new Vector3(0, 0.1f, 0), 0.5f);
                Put("survival/tree-log.glb", wide, 0.35f, new Vector3(0, 0.3f, 0), 2.1f);
                Glow(holder, new Color("#ff7a20"), r * 0.55f, new Vector3(0, 0.75f, 0));
                h = Mathf.Max(h, 1.1f);
                break;
            case "cloud":
                for (int i = -2; i <= 2; i++)
                    Glow(holder, new Color(0.55f, 0.75f, 0.4f, 0.45f), 0.65f, new Vector3(i * FDG.LivingSpells.LongLength / 5.5f, 0.55f, 0), false);
                h = 1.2f;
                break;
            case "cogs":
                Put(S + "Prop_Fan_Small.gltf", wide, 0.5f, new Vector3(0, 0.45f, 0), 0, new Color("#d8b050"), 0.4f);
                break;
            case "pendulum":
                Put(P + "Chandelier.gltf", wide, tall, new Vector3(0, 0.1f, 0), 0, new Color("#a0a0b0"));
                break;
            case "crystal":
                Put("space/rock_crystalsLargeA.glb", wide, tall, new Vector3(0, 0.1f, 0), 0, new Color("#c8e6ff"), 0.6f);
                break;
            case "gate":
                if (grim) Put(S + "Door_Frame_A.gltf", wide, tall * (m.Kind == EKind.ArmyTerrain ? 1f : 1.4f), new Vector3(0, 0.1f, 0), 0, null, 0);
                else Put(V + "DoorFrame_Round_Brick.gltf", wide, tall * (m.Kind == EKind.ArmyTerrain ? 1f : 1.4f), new Vector3(0, 0.1f, 0));
                Glow(holder, new Color("#6ad0ff"), r * 0.35f, new Vector3(0, h * 0.5f, 0));
                break;
            case "jaws":
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.Tau / 6f;
                    Put("nature/rock_tallA.glb", r * 0.45f, 0.9f, new Vector3(Mathf.Cos(a) * r * 0.65f, 0.1f, Mathf.Sin(a) * r * 0.65f), a, bone);
                }
                break;
            case "chains":
                Put(P + "Chain_Coil.gltf", wide, 0.5f, new Vector3(0, 0.1f, 0), 0, new Color("#9aa0a8"), 0.2f);
                break;
            case "healing":
                Put(N + "Flower_3_Group.gltf", wide, 0.5f, new Vector3(0, 0.1f, 0), 0, null, 0);
                Glow(holder, new Color("#80ff90"), r * 0.3f, new Vector3(0, 0.8f, 0));
                h = Mathf.Max(h, 1f);
                break;
            case "platform":
                Put(grim ? S + "Platform_CenterPlate.gltf" : V + "Floor_UnevenBrick.gltf", wide * 0.95f, 0.25f, new Vector3(0, 0.1f, 0), 0, magic, 0.2f);
                break;
            case "horror":
                Put(N + "DeadTree_1.gltf", wide, tall * 1.2f, new Vector3(0, 0.1f, 0), 0, new Color("#c8c0d0"), 0.2f);
                break;
            case "palisade":
                for (int i = -2; i <= 2; i++)
                    Put(grim ? S + "ShortWall_MetalPlates_Straight.gltf" : "castle/wall-narrow-wood-fence.glb",
                        FDG.LivingSpells.LongLength / 5f, FDG.LivingSpells.PalisadeHeight, new Vector3(i * FDG.LivingSpells.LongLength / 5f, 0.1f, 0), 0, magic, 0.15f);
                break;
            case "sun":
                Glow(holder, new Color("#ffb030"), r * 0.8f, new Vector3(0, 1.1f, 0));
                h = 1.9f;
                break;
            case "maelstrom":
                foreach (float y in new[] { 0.3f, 0.7f, 1.1f })
                {
                    var t = TableLook.Mesh(holder, new TorusMesh { InnerRadius = r * 0.5f * (1.2f - y * 0.4f), OuterRadius = r * 0.6f * (1.2f - y * 0.4f) },
                        TableLook.SharedMat(new Color(0.6f, 0.85f, 1f, 0.8f), 0.3f, new Color("#9ad8ff"), 1.5f), new Vector3(0, y, 0));
                    t.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                h = 1.3f;
                break;
            case "swords":
                Put(P + "WeaponStand.gltf", wide, tall, new Vector3(0, 0.1f, 0), 0, new Color("#c8ccd4"), 0.3f);
                break;
            case "spheres":
                Glow(holder, new Color("#6ab8ff"), r * 0.6f, new Vector3(0, 0.8f, 0));
                h = 1.3f;
                break;
            case "spirit":
                Glow(holder, new Color(0.75f, 1f, 0.85f, 0.7f), r * 0.5f, new Vector3(0, 0.8f, 0));
                Put(P + "Lantern_Wall.gltf", r, 0.6f, new Vector3(0, 0.1f, 0), 0, null, 0);
                h = Mathf.Max(h, 1.2f);
                break;

            // Army Terrain
            case "banner":
                Put(P + (m.Name == FDG.ArmyTerrain.IconOfWar ? "Banner_2.gltf" : "Banner_1.gltf"), wide, tall, new Vector3(0, 0.1f, 0), 0, owner);
                break;
            case "forge":
                Put(P + "Anvil_Log.gltf", wide * 0.8f, tall * 0.5f, new Vector3(0, 0.1f, 0));
                Glow(holder, new Color("#ff8a30"), 0.3f, new Vector3(r * 0.4f, 0.4f, 0));
                break;
            case "barrier":
                Put(grim ? "survival/fence-fortified.glb" : "town/fence.glb", wide, tall * 0.5f, new Vector3(0, 0.1f, 0));
                break;
            case "dome":
            {
                var dome = TableLook.Mesh(holder, new SphereMesh { Radius = r, Height = r * 2, IsHemisphere = true },
                    TableLook.SharedMat(new Color(0.55f, 0.85f, 1f, 0.28f), 0.2f, new Color("#7ac8ff"), 0.6f), new Vector3(0, 0.1f, 0));
                dome.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                Put(S + "Prop_Light_Floor.gltf", r * 0.5f, 0.5f, new Vector3(0, 0.1f, 0));
                h = r + 0.1f;
                break;
            }
            case "tower":
                Put(grim ? "space/supports_high.glb" : "castle/tower-square-mid.glb", wide, tall, new Vector3(0, 0.1f, 0), 0, grim ? null : stone);
                break;
            case "pillar":
                Put(grim ? S + "Column_Astra.gltf" : "nature/statue_columnDamaged.glb", wide * 0.7f, tall, new Vector3(0, 0.1f, 0), 0, stone, 0.1f);
                break;
            case "shrine":
                Put("nature/statue_block.glb", wide * 0.8f, tall * 0.8f, new Vector3(0, 0.1f, 0), 0, stone);
                Glow(holder, magic, 0.3f, new Vector3(0, h + 0.3f, 0));
                break;
            case "relic":
                Put(P + "Chest_Wood.gltf", wide * 0.8f, tall * 0.4f, new Vector3(0, 0.1f, 0));
                Glow(holder, new Color("#ffd860"), 0.3f, new Vector3(0, h + 0.3f, 0));
                break;
            case "well":
                TableLook.Mesh(holder, new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = r * 0.85f, Height = 0.9f },
                    TableLook.SharedMat(stone, 0.9f), new Vector3(0, 0.55f, 0));
                TableLook.Mesh(holder, new CylinderMesh { TopRadius = r * 0.62f, BottomRadius = r * 0.62f, Height = 0.05f },
                    TableLook.SharedMat(new Color("#3a7a9a"), 0.2f, new Color("#3a9aca"), 0.4f), new Vector3(0, 0.98f, 0));
                Put(P + "Bucket_Wooden_1.gltf", r * 0.4f, 0.45f, new Vector3(r * 0.55f, 1f, 0));
                h = Mathf.Max(h, 1.3f);
                break;
            case "armory":
                Put(P + "WeaponStand.gltf", wide, tall * 0.7f, new Vector3(0, 0.1f, 0));
                break;
            case "forward":
                break;   // the token and its label say it all

            // Quest mission markers
            case "objective":
                Put(grim ? S + "Prop_Crate3.gltf" : P + "Crate_Wooden.gltf", wide * 0.8f, 0.8f, new Vector3(0, 0.1f, 0));
                Glow(holder, new Color("#f0f0ff"), 0.18f, new Vector3(0, h + 0.3f, 0));
                break;
            case "delivery":
                Put(P + "Bag.gltf", wide * 0.8f, 0.7f, new Vector3(0, 0.1f, 0), 0, new Color("#c8d8ff"));
                break;
            case "extraction":
                Put(grim ? "space/satelliteDish.glb" : P + "Banner_2.gltf", wide, 1.2f, new Vector3(0, 0.1f, 0));
                break;
            case "search":
                Put(P + "Bag.gltf", wide * 0.7f, 0.5f, new Vector3(0, 0.1f, 0));
                break;
            case "treasure":
                Put(P + "Chest_Wood.gltf", wide * 0.8f, 0.6f, new Vector3(0, 0.1f, 0));
                Glow(holder, new Color("#ffd860"), 0.2f, new Vector3(0, h + 0.25f, 0));
                break;
            case "item-smoke":
                for (int i = 0; i < 3; i++) Glow(holder, new Color(0.7f, 0.7f, 0.72f, 0.5f), 0.45f, new Vector3((i - 1) * 0.35f, 0.4f + i % 2 * 0.2f, 0), false);
                h = 1f;
                break;
            case "item-fire":
            case "item-biotrap":
                Glow(holder, m.Look == "item-fire" ? new Color("#ff7a20") : new Color("#7aff60"), 0.35f, new Vector3(0, 0.45f, 0));
                h = 0.8f;
                break;
            case "item-spikes":
                for (int i = 0; i < 4; i++)
                    Put("nature/rock_tallA.glb", r * 0.4f, 0.5f, new Vector3(Mathf.Cos(i * 1.57f) * r * 0.4f, 0.1f, Mathf.Sin(i * 1.57f) * r * 0.4f), i, new Color("#8a7a5a"));
                break;
            case "item-phantasm":
                Put(P + "Dummy.gltf", r, 1.2f, new Vector3(0, 0.1f, 0), 0, new Color(0.7f, 0.85f, 1f), 0.8f);
                break;
            case "item-noise":
                Put(S + "Prop_Light_Small.gltf", r, 0.5f, new Vector3(0, 0.1f, 0), 0, null, 0);
                Glow(holder, new Color("#ffe080"), 0.15f, new Vector3(0, h + 0.2f, 0));
                break;
            case "item-grease":
                h = 0.15f;
                break;
            default:
                Put(N + "Rock_Medium_1.gltf", wide, 0.6f, new Vector3(0, 0.1f, 0), 0, owner);
                break;
        }
        return h;
    }

    /// <summary>A soft glowing ball (a magic light, a flame, a sun).</summary>
    private static void Glow(Node3D holder, Color c, float radius, Vector3 at, bool emit = true)
    {
        var mesh = TableLook.SharedMesh($"marker-glow-{radius:0.00}", () => new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 24, Rings = 12 });
        var mi = TableLook.Mesh(holder, mesh, TableLook.SharedMat(c with { A = Mathf.Min(c.A, 0.85f) }, 0.3f, emit ? c : null, emit ? 2f : 1f), at);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    /// <summary>A bundled model scaled evenly to fit <paramref name="wide"/> across and <paramref name="tall"/> high,
    /// its bottom-centre at <paramref name="at"/>; tinted (multiplied) and optionally glowing. Returns the top (holder
    /// space), or null when the model isn't in this build.</summary>
    private static float? Model(Node3D holder, string file, float wide, float tall, Vector3 at, float yaw, Color? tint, float glow)
    {
        if (PieceModels.Instance(file) is not { } inst) return null;
        Aabb box = MarkerBounds(inst, Transform3D.Identity);
        if (box.Size.Y <= 0.0001f && box.Size.X <= 0.0001f) { inst.QueueFree(); return null; }
        float across = Mathf.Max(box.Size.X, box.Size.Z);
        float k = Mathf.Min(across > 0.0001f ? wide / across : float.MaxValue, box.Size.Y > 0.0001f ? tall / box.Size.Y : float.MaxValue);
        var pivot = new Node3D { Position = at, Rotation = new Vector3(0, yaw, 0), Scale = new Vector3(k, k, k) };
        Vector3 c = box.GetCenter();
        inst.Position = new Vector3(-c.X, -box.Position.Y, -c.Z);
        pivot.AddChild(inst);
        holder.AddChild(pivot);
        if (tint != null || glow > 0) Tint(inst, tint ?? Colors.White, glow);
        return at.Y + box.Size.Y * k;
    }

    private static Aabb MarkerBounds(Node node, Transform3D parent)
    {
        Transform3D t = node is Node3D n3 ? parent * n3.Transform : parent;
        Aabb? box = node is MeshInstance3D mi && mi.Mesh != null ? t * mi.Mesh.GetAabb() : null;
        foreach (Node c in node.GetChildren())
        {
            Aabb cb = MarkerBounds(c, t);
            if (cb.Size == Vector3.Zero && cb.Position == Vector3.Zero) continue;
            box = box is { } b ? b.Merge(cb) : cb;
        }
        return box ?? new Aabb();
    }

    // One recoloured copy per (material, tint, glow): markers are few, but the same look repeats every redraw.
    private static readonly Dictionary<(ulong, Color, float), Material> MarkerMats = new();

    private static void Tint(Node node, Color tint, float glow)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.GetActiveMaterial(i) is not BaseMaterial3D mat) continue;
                var key = (mat.GetInstanceId(), tint, glow);
                if (!MarkerMats.TryGetValue(key, out Material? done) || !GodotObject.IsInstanceValid(done))
                {
                    var dup = (BaseMaterial3D)mat.Duplicate();
                    Color a = mat.AlbedoColor;
                    dup.AlbedoColor = new Color(a.R * tint.R, a.G * tint.G, a.B * tint.B, a.A);
                    if (glow > 0)
                    {
                        dup.EmissionEnabled = true;
                        dup.Emission = tint;
                        dup.EmissionEnergyMultiplier = glow;
                    }
                    MarkerMats[key] = done = dup;
                }
                mi.SetSurfaceOverrideMaterial(i, done);
            }
        foreach (Node c in node.GetChildren()) Tint(c, tint, glow);
    }
}
