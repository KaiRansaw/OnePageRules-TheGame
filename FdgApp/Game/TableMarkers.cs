using System.Text.RegularExpressions;
using FDG;
using FDG.Players;

namespace FdgRaylib.Game;

/// <summary>
/// The markers the optional rules put on the table, as the general needs to read them (lane 4, 2026-11-27): Random
/// Events markers (Advanced Rulebooks 3.6.0, Battlefield Effects), Living Spells, Army Terrain and the Tactical Phase's
/// forward positions. Each one says what it is, where it stands, the book's text for its hover and the ring the rules
/// measure ("All units that move within 3\" of it", "units within 6\"", "within 12\" of this army terrain"). No
/// drawing here - the Godot table draws them; this decides what is shown.
///
/// <para>Ring radii follow how the engine measures: Random Events markers are points (<c>NearPoint</c>: the distance to
/// the marker's centre), Living Spells and Army Terrain measure from the piece's edge, so their rings are the piece's
/// radius plus the book's distance.</para>
/// </summary>
public static class TableMarkers
{
    public enum EKind { Event, LivingSpell, ArmyTerrain, ForwardPosition, Quest }

    /// <summary>What the ring means, which picks its colour: Dangerous / Difficult / Cover areas, an area whose units
    /// take hits or get a buff, a reach (the Ancient Robot's 12", a gate's 3", a forward position's 3" deploy band).</summary>
    public enum ERing { None, Dangerous, Difficult, Cover, Hits, Buff, Reach }

    public sealed record Marker(
        EKind Kind,
        string Name,
        string Text,
        float X,
        float Z,
        float Radius,
        float Ring,
        ERing RingKind,
        string Look,
        string? OwnerId = null,
        string? OwnerName = null,
        int Number = 0,
        float? X2 = null,
        float? Z2 = null,
        bool Line = false,
        float Fx = 0f,
        float Fz = 1f)
    {
        /// <summary>A long spell (Suffocating Cloud, Arcane Palisade) lies across its
    /// facing: its long axis is (-Fz, Fx), as LivingSpells.DistanceTo measures it.</summary>
    public bool IsLong(EGameSystem system) => Kind == EKind.LivingSpell && LivingSpells.Find(system, Name)?.Size == LivingSpells.ESize.Long;

    /// <summary>The label shown over it: the name, the owner and the forward position's number.</summary>
        public string Label => Kind switch
        {
            // Not a bare number: objectives carry those (the mission cards count them).
            EKind.ForwardPosition => $"Forward {Number}",
            _ => OwnerName is { Length: > 0 } o ? $"{Name} ({o})" : Name,
        };
    }

    /// <summary>Every marker on the table now, in a stable order (events, spells, army terrain, forward positions).</summary>
    public static List<Marker> For(ITableState table, EGameSystem system)
    {
        var list = new List<Marker>();
        if (GameProgressUtilities.TryGetProgress(table.DataStore) is not { } progress) return list;
        string Seat(string playerId) =>
            table.Players.Objects.FirstOrDefault(p => p.IsFilled && p.PlayerID.ID.ToString() == playerId)?.Name ?? "";

        if (progress.Battlefield is { } b)
            foreach (EventMarker m in b.Markers)
            {
                (float ring, ERing kind, string look) = EventRing(m.Code);
                list.Add(new Marker(EKind.Event, BattlefieldEffects.EventName(m.Code, system), BattlefieldEffects.EventText(m.Code, system),
                    m.X, m.Z, EventMarkerRadius, ring, kind, look));
            }

        if (progress.LivingSpells is { } ls)
            foreach (LivingSpellPiece p in ls.OnTable)
            {
                if (LivingSpells.Find(system, p.Name) is not { } d) continue;
                (float n, ERing kind) = SpellRing(d);
                float r = LivingSpells.Radius(d.Size);
                list.Add(new Marker(EKind.LivingSpell, d.Name, d.Text, p.X, p.Z, r, n > 0 ? r + n : 0, kind, SpellLook(d.Name),
                    p.Owner, p.OwnerName, X2: p.X2, Z2: p.Z2, Line: d.Name == LivingSpells.TwinShockSpheres,
                    Fx: p.Fx, Fz: p.Fz));
            }

        if (progress.ArmyTerrain is { } at)
            foreach (ArmyPiece p in at.Pieces)
            {
                (float n, ERing kind) = ArmyRing(p.Name);
                list.Add(new Marker(EKind.ArmyTerrain, p.Name, ArmyText(p.Name, system), p.X, p.Z, FDG.ArmyTerrain.PieceRadius,
                    n > 0 ? FDG.ArmyTerrain.PieceRadius + n : 0, kind, ArmyLook(p.Name), p.Owner, Seat(p.Owner),
                    X2: p.X2, Z2: p.Z2, Line: p.Name == FDG.ArmyTerrain.ArcanePillars));
            }

        if (progress.TacticalPhase is { MarkersRemoved: false } tp)
            foreach (ForwardMarker m in tp.Markers)
            {
                IPlayerSlotInfo? seat = table.Players.Objects.FirstOrDefault(p => p.SlotID == m.Slot);
                list.Add(new Marker(EKind.ForwardPosition, $"Forward position {m.Number}",
                    "Units must be placed fully within 3\" of the forward position markers, or within 12\" of the table corner that is furthest from all enemy forward position markers."
                    + (m.Locked ? "\nLocked: it may not be moved anymore." : ""),
                    m.X, m.Z, TacticalPhase.MarkerRadius, TacticalPhase.MarkerRadius + 3f, ERing.Reach, "forward",
                    seat?.PlayerID.ID.ToString(), seat?.Name, m.Number));
            }
        if (progress.Quest is { } q)
        {
            int primary = 0, secondary = 0;
            foreach (QuestMarker m in q.Markers)
            {
                // A carried objective rides with its hero; silence / poison sit on a unit - neither stands on the table.
                if (m.CarriedBy.Length > 0 || m.OnUnit.Length > 0) continue;
                list.Add(QuestOne(m, m.Kind == "objective" ? (m.ForSecondary ? ++secondary : ++primary) : 0, q));
            }
            foreach (QuestToken t in q.Tokens)
                list.Add(new Marker(EKind.Quest, t.IsTreasure ? $"Treasure {t.Id}" : $"Search {t.Id}",
                    t.IsTreasure ? $"{t.Label}. A hero within 1\" may pick it up." : "Search token: contents unknown until a hero searches it (within 1\").",
                    t.X, t.Z, QuestMarkerRadius, QuestMarkerRadius + 1f, ERing.Reach, t.IsTreasure ? "treasure" : "search", Number: t.Id));
        }
        return list;
    }

    /// <summary>Quest markers are 32mm tokens too (the Campaign Builders give no size).</summary>
    public const float QuestMarkerRadius = 32f / 25.4f / 2f;

    /// <summary>A Quest mission marker: objective markers are numbered per objective ("Primary 1", "Secondary 1") with
    /// the Skill action's 1" reach, or the 3" a seize counts; delivery / extraction markers the 3" a carrier must end
    /// within; item markers (grenades, skills) the 3" their area covers (QuestTreasure.NearMarker).</summary>
    private static Marker QuestOne(QuestMarker m, int number, QuestProgress q)
    {
        float r = QuestMarkerRadius;
        switch (m.Kind)
        {
            case "objective":
            {
                string which = m.ForSecondary ? "Secondary" : "Primary";
                bool seize = m.Action == "seize" && m.Activated;
                string text = QuestObjectiveText.Of(m.ForSecondary ? q.Secondary : q.Primary, m.ForSecondary)
                    + (m.Action == "seize" ? m.Activated ? $"\nSeized: {m.Tokens} of {m.TokensNeeded} tokens." : "\nNot activated yet." : "");
                return new Marker(EKind.Quest, $"{which} {number}" + (m.Action == "seize" && m.Activated ? $" ({m.Tokens}/{m.TokensNeeded})" : ""),
                    text, m.X, m.Z, r, seize ? 3f : 1f, ERing.Reach, "objective", Number: number);
            }
            case "delivery":
            case "extraction":
                return new Marker(EKind.Quest, m.Kind == "delivery" ? "Delivery" : "Extraction",
                    QuestObjectiveText.Of(m.ForSecondary ? q.Secondary : q.Primary, m.ForSecondary), m.X, m.Z, r, 3f, ERing.Reach, m.Kind);
            default:
            {
                string name = QuestItemName(m.Kind);
                ERing ring = m.Kind switch
                {
                    "fire" or "biotrap" => ERing.Dangerous,
                    "grease" or "spikes" => ERing.Difficult,
                    "smoke" => ERing.Cover,
                    "noise" or "phantasm" => ERing.None,
                    _ => ERing.Hits,
                };
                // The words the game says when the marker goes down (QuestTreasure.PlaceItemMarker).
                string what = m.Kind switch
                {
                    "noise" => "Enemies treat it as their AI Goal.",
                    "grease" or "spikes" => "Within 3\" counts as difficult terrain.",
                    "smoke" => "Within 3\" counts as cover.",
                    "fire" or "biotrap" => "Within 3\" counts as dangerous terrain.",
                    "phantasm" => "Enemies treat it as a hero.",
                    _ => "",
                };
                string left = m.RoundsLeft is > 0 and < 99 ? $" {m.RoundsLeft} round{(m.RoundsLeft == 1 ? "" : "s")} left." : "";
                return new Marker(EKind.Quest, name, $"{name} marker. {what}{left}".Replace("  ", " ").Trim(), m.X, m.Z, r,
                    ring == ERing.None ? 0f : 3f, ring, "item-" + m.Kind);
            }
        }
    }

    private static string QuestItemName(string kind)
    {
        string n = FDG.Stages.QuestTreasure.MarkerName(kind);
        return n.Length == 0 ? "Marker" : char.ToUpperInvariant(n[0]) + n[1..];
    }

    /// <summary>A Random Events marker's size on the table (the book gives none): a 32mm round token.</summary>
    public const float EventMarkerRadius = 32f / 25.4f / 2f;

    /// <summary>Each event marker's ring, from its book text (BattlefieldEffects.EventText): Zombie Horde and Havoc Spawn
    /// "the area within 3\" of the marker counts as Dangerous terrain", the Swarm's as Difficult, the Anomaly hits units
    /// that move within 3", the Ancient Robot / Guardian shoots within 12". The Sniper and the Rebel Squad pick the
    /// closest unit in line of sight - no distance, no ring.</summary>
    public static (float Ring, ERing Kind, string Look) EventRing(int code) => code switch
    {
        21 => (3f, ERing.Hits, "anomaly"),
        31 => (3f, ERing.Dangerous, "zombies"),
        32 => (0f, ERing.None, "sniper"),
        33 => (3f, ERing.Difficult, "swarm"),
        34 => (3f, ERing.Dangerous, "havoc"),
        35 => (12f, ERing.Reach, "robot"),
        36 => (0f, ERing.None, "rebels"),
        _ => (0f, ERing.None, "event"),
    };

    private static readonly Regex Within = new(@"(?:[Uu]nits|unit ends its activation) within (\d+)""", RegexOptions.Compiled);

    /// <summary>A living spell's ring: the distance its units-within clause names ("Units within 6\" get ...", "a unit
    /// ends its activation within 3\""), none for the ones that act only on what they move through or stand on.</summary>
    public static (float Inches, ERing Kind) SpellRing(LivingSpells.Def d)
    {
        Match m = Within.Match(d.Text);
        if (!m.Success) return (0f, ERing.None);
        float n = float.Parse(m.Groups[1].Value);
        ERing kind = d.Name switch
        {
            LivingSpells.BurningPyre or LivingSpells.DeathlyPendulum or LivingSpells.SoulDrainPlatform => ERing.Hits,
            LivingSpells.MagicShackles => ERing.Difficult,
            LivingSpells.DivineMaelstrom => ERing.Cover,
            _ when d.Text.Contains("may be placed anywhere") => ERing.Reach,
            _ => ERing.Buff,
        };
        return (n, kind);
    }

    /// <summary>An army terrain piece's ring, from its book text ("within 6\" of this army terrain", "The area within
    /// 6\" of this faction terrain counts as ..."). Arcane Pillars is a line between its two copies, not a ring.</summary>
    public static (float Inches, ERing Kind) ArmyRing(string name)
    {
        string text = ArmyText(name, EGameSystem.GrimdarkFuture);
        Match m = Regex.Match(text, @"within (\d+)"" of (?:this|one of the copies of this) (?:army|faction) terrain");
        if (!m.Success) return (0f, ERing.None);
        float n = float.Parse(m.Groups[1].Value);
        ERing kind = name switch
        {
            FDG.ArmyTerrain.DefensiveBarrier => ERing.Difficult,
            FDG.ArmyTerrain.ProtectiveDome => ERing.Cover,
            FDG.ArmyTerrain.WatchTower => ERing.Dangerous,
            FDG.ArmyTerrain.TwinJumpgates or FDG.ArmyTerrain.TwinVoidgates => ERing.Reach,
            _ => ERing.Buff,
        };
        return (n, kind);
    }

    public static string ArmyText(string name, EGameSystem system) =>
        FDG.ArmyTerrain.Battle.Concat(FDG.ArmyTerrain.Relic(system)).FirstOrDefault(b => b.Name == name).Text
        ?? FDG.ArmyTerrain.TextOf(name);

    /// <summary>Which stand-in model a living spell gets (the Godot table maps these to the bundled stylised packs).</summary>
    public static string SpellLook(string name) => name switch
    {
        LivingSpells.BurningPyre => "pyre",
        LivingSpells.SuffocatingCloud => "cloud",
        LivingSpells.CogsOfTime => "cogs",
        LivingSpells.DeathlyPendulum => "pendulum",
        LivingSpells.ShardOfMist => "crystal",
        "Linked Jumpgates" or "Linked Voidgates" => "gate",
        LivingSpells.HungeringJaws => "jaws",
        LivingSpells.MagicShackles => "chains",
        LivingSpells.HealingSwarm => "healing",
        LivingSpells.SoulDrainPlatform or LivingSpells.VortexPlatform => "platform",
        LivingSpells.ScreamingHorror => "horror",
        LivingSpells.ArcanePalisade => "palisade",
        LivingSpells.KillingSun => "sun",
        LivingSpells.DivineMaelstrom => "maelstrom",
        LivingSpells.SwordSwarm => "swords",
        LivingSpells.TwinShockSpheres => "spheres",
        LivingSpells.SpiritSeeker => "spirit",
        _ => "spell",
    };

    /// <summary>Which stand-in model an army terrain piece gets.</summary>
    public static string ArmyLook(string name) => name switch
    {
        FDG.ArmyTerrain.ArmyBanner or FDG.ArmyTerrain.IconOfWar => "banner",
        FDG.ArmyTerrain.BattleForge => "forge",
        FDG.ArmyTerrain.DefensiveBarrier => "barrier",
        FDG.ArmyTerrain.ProtectiveDome => "dome",
        FDG.ArmyTerrain.WatchTower => "tower",
        FDG.ArmyTerrain.ArcanePillars => "pillar",
        FDG.ArmyTerrain.EnchantedShrine => "shrine",
        FDG.ArmyTerrain.FactionRelic => "relic",
        FDG.ArmyTerrain.HealingWell => "well",
        FDG.ArmyTerrain.MagicArmory => "armory",
        FDG.ArmyTerrain.TwinJumpgates or FDG.ArmyTerrain.TwinVoidgates => "gate",
        _ => "army",
    };

    /// <summary>True when the terrain piece is one of these markers' own footprint (the table draws the marker's model
    /// instead of a terrain look for it) - or the Defensive Barrier's 6" Difficult area, which the barrier's own ring
    /// shows (dressed as terrain it came out as a 15"-wide field of rubble round the piece).</summary>
    public static bool IsMarkerPiece(ITerrain t) =>
        LivingSpells.IsMarker(t) || TacticalPhase.IsMarker(t) || IsArmyPiece(t)
        || (t.Name ?? "").StartsWith(FDG.ArmyTerrain.BarrierAreaPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Areas a rule makes for a round - not pieces of scenery: the Deceiver politician's Difficult + Dangerous ring round an
    /// objective, Twists &amp; Gambits' Objective Denied (Dangerous within 3"), a campaign event's "the ground counts as ...
    /// terrain until the end of the round". The table draws them as shaded ground, not dressed as rocks or woods.
    /// (The two engine names are private there: "Twist: Objective Denied" in TwistsGambits, "Campaign event: " in
    /// CampaignEventRunner.)
    /// </summary>
    public static bool IsRoundArea(ITerrain t) =>
        t.Name is { } n && (n == FDG.PoliticalIntrigue.DeceiverTerrainName || n == "Twist: Objective Denied"
            || n.StartsWith("Campaign event: ", StringComparison.Ordinal));

    /// <summary>The terrain an area counts as, in words ("Difficult + Dangerous").</summary>
    public static string AreaWords(ETerrainType type)
    {
        var words = new List<string>();
        if (type.HasFlag(ETerrainType.Cover)) words.Add("Cover");
        if (type.HasFlag(ETerrainType.Difficult)) words.Add("Difficult");
        if (type.HasFlag(ETerrainType.Dangerous)) words.Add("Dangerous");
        if (type.HasFlag(ETerrainType.Impassible)) words.Add("Impassable");
        return words.Count == 0 ? "Open ground" : string.Join(" + ", words);
    }

    /// <summary>What a round area is called on the table ("Objective Denied", "Deceiver", the campaign event's name).</summary>
    public static string RoundAreaLabel(ITerrain t)
    {
        string n = t.Name ?? "";
        int colon = n.IndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 ? n[(colon + 2)..] : n;
    }

    /// <summary>Army terrain pieces are named "<c>Name (Owner)</c>"; the Defensive Barrier's 6" area is a separate piece.</summary>
    public static bool IsArmyPiece(ITerrain t) =>
        t.Name is { } n && !n.StartsWith(FDG.ArmyTerrain.BarrierAreaPrefix, StringComparison.Ordinal)
        && FDG.ArmyTerrain.Built.Any(b => n.StartsWith(b + " (", StringComparison.Ordinal));

    /// <summary>The line under a marker's hover that says what its ring is.</summary>
    public static string RingNote(Marker m) => m.RingKind switch
    {
        ERing.Dangerous => "The ring: the Dangerous area.",
        ERing.Difficult => "The ring: the Difficult area.",
        ERing.Cover => "The ring: the Cover area.",
        ERing.Hits or ERing.Buff or ERing.Reach => "The ring shows the distance its text measures.",
        _ => "",
    };

    /// <summary>What changed since the last draw (positions rounded to a tenth of an inch; the text too, so a hover
    /// never shows last round's "2 rounds left").</summary>
    public static string Signature(IReadOnlyList<Marker> markers) =>
        string.Join(";", markers.Select(m => $"{m.Kind}|{m.Name}|{m.OwnerId}|{m.Number}|{m.X:0.0}|{m.Z:0.0}|{m.X2:0.0}|{m.Z2:0.0}|{m.Text}"));
}
