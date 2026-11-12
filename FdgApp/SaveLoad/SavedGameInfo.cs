using FDG;
using FDG.Players;
using FDG.SaveLoad;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// What a Load Game screen shows about one saved game, with no drawing: who played, the scores, the game and
/// round, whether it can be resumed, and a top-down picture of the board (terrain, objectives, models). Ported
/// from the Raylib Load Game screen's reader (which keeps its own copy while the Raylib build is frozen).
/// </summary>
public sealed class SavedGameInfo
{
    public sealed record Player(string Name, string Faction, string Army, int Points, int Objectives, int? Vp);
    public sealed record Piece(IZone Shape, ETerrainType Type);
    public sealed record Marker(float X, float Z, int? Owner, int Number);
    public sealed record Figure(float X, float Z, float Radius, int Owner);

    public string Path = "";
    public string Title = "";
    public DateTime Saved;
    public List<Player> Players = new();
    public EGameSystem? GameSystem;           // null when the save has no settings (made during setup)
    public string GameLabel = "";
    public string ModeLabel = "";
    public string RoundLabel = "";
    public int ArmyPoints;
    public bool CanResume = true;             // false for a save made before round 1
    public float TableW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
    public float TableH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
    public List<Piece> Terrain = new();
    public List<Marker> Objectives = new();
    public List<Figure> Models = new();

    /// <summary>Every save in the Saved Games folder (and the recovery saves), newest first, without reading them.</summary>
    public static List<SavedGameInfo> List() => SavePaths.ListSaves().Select(p => new SavedGameInfo
    {
        Path = p, Title = System.IO.Path.GetFileNameWithoutExtension(p), Saved = SavePaths.SafeWriteTime(p),
    }).OrderByDescending(e => e.Saved).ToList();

    /// <summary>Reads the save's contents into this entry (slow: call off the main thread).</summary>
    public void Read()
    {
        var store = GameSaveSerializer.Load(File.ReadAllText(Path));
        var table = new TableState(store);

        // Player order: slot 0 first (Player 1 - the host), then the rest in slot order.
        List<IPlayerSlotInfo> slots = table.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
        var colour = new Dictionary<PlayerID, int>();
        for (int i = 0; i < slots.Count; i++) colour[slots[i].PlayerID] = i;
        Dictionary<PlayerID, ArmyData> armies = table.Armies.Objects.OfType<ArmyData>()
            .GroupBy(a => a.PlayerID).ToDictionary(g => g.Key, g => g.First());
        Dictionary<PlayerID, int> scores = table.Progress.Scores.ToDictionary(x => x.PlayerID, x => x.ObjectiveCount);
        var cards = table.Progress.MissionCards;
        foreach (IPlayerSlotInfo slot in slots)
        {
            armies.TryGetValue(slot.PlayerID, out ArmyData? army);
            string faction = !string.IsNullOrWhiteSpace(army?.Faction) ? army!.Faction
                : !string.IsNullOrWhiteSpace(army?.ArmyName) ? army!.ArmyName : slot.Name;
            Players.Add(new Player(slot.Name, faction, !string.IsNullOrWhiteSpace(army?.ArmyName) ? army!.ArmyName : faction,
                army?.PointsLimit ?? 0, scores.TryGetValue(slot.PlayerID, out int sc) ? sc : 0,
                cards != null ? cards.Vp.TryGetValue(slot.SlotID, out int v) ? v : 0 : null));
        }

        GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
        GameSettings settings = progress?.Settings ?? GameSettings.GetDefault();
        ArmyPoints = progress != null ? settings.ArmyPoints : Players.Select(p => p.Points).DefaultIfEmpty(0).Max();
        if (progress != null)
        {
            GameSystem = settings.GameSystem;
            GameLabel = GameSystemInfo.DisplayName(settings.GameSystem);
            (TableW, TableH) = TableSizing.Resolve(settings);
        }
        EGameMode mode = table.Progress.GameMode;
        ModeLabel = mode == EGameMode.Horde ? "Horde Mode" : GameSystemInfo.ModeLabel(mode, settings.GameSystem);
        RoundLabel = table.Progress.RoundCount is int r
            ? (r <= table.Progress.TotalRounds ? $"Round {r} of {table.Progress.TotalRounds}" : $"Round {r} (extra)") : "Deployment";
        CanResume = progress != null;
        if (!CanResume) RoundLabel = "Saved during setup - can't be resumed";

        foreach (ITerrain t in table.Terrain.Objects) Terrain.Add(new Piece(t.Shape, t.TerrainType));
        int n = 0;
        foreach (IObjective o in table.Objectives.Objects)
        {
            n++;
            int? owner = o.OwnerID is PlayerID pid && colour.TryGetValue(pid, out int ci) ? ci : null;
            Objectives.Add(new Marker(o.Position.x, o.Position.z, owner, o.HordeNumber ?? n));
        }
        foreach (IUnit u in table.Units.Objects)
        {
            if (!u.GetIsOnBattlefield()) continue;
            int ci = colour.TryGetValue(u.PlayerID, out int c) ? c : 0;
            foreach (IModel m in u.Models.Where(m => m.GetIsAlive()))
                Models.Add(new Figure(m.Position.x, m.Position.z, m.BaseRadiusInches, ci));
        }
    }

    /// <summary>Whether a save of <paramref name="system"/> belongs under the Grimdark Future (true) or Age of
    /// Fantasy (false) filter; null when unknown (shown under All only).</summary>
    public bool? IsGrimdark => GameSystem is { } s ? GameSystemInfo.Slug(s).StartsWith("grimdark", StringComparison.Ordinal) : null;
}
