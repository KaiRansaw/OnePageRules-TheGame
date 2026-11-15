using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Campaign.Map;

namespace FdgRaylib.Quest;

/// <summary>
/// #467 - a map campaign on disk (Kai, 2026-11-14; design: project doc claude/campaign-map-spec.md): the player's
/// campaign army, 1-3 rival campaign armies, and the .fdgmap beside the player's file. Everything a map screen needs
/// that isn't drawing: starting one, the armies' books as the map reads them, giving the turn's order, playing it as far
/// as it goes, applying a finished turn to every army and running the rivals' own between-turn steps, and saving.
///
/// <para>The player's battles are real missions. <see cref="Step"/> stops at each one (<see cref="MapTurnResult.WaitingFor"/>);
/// the screen plays it through Mission Setup and then calls <see cref="RecordBattle"/>. Those missions pay their own
/// rewards the ordinary way; a garrison mission pays none (house rule B24).</para>
/// </summary>
public sealed class MapCampaignSession
{
    public string PlayerPath { get; }
    public string MapPath { get; }
    public CampaignMap Map { get; private set; }
    public EGameSystem System => Map.System;
    public CampaignNumbers Numbers => CampaignNumbers.For(System);

    /// <summary>Each army on the map by faction id, with the path it was loaded from.</summary>
    public IReadOnlyDictionary<string, (string Path, BuiltArmyFile Army)> Armies => _armies;
    private readonly Dictionary<string, (string Path, BuiltArmyFile Army)> _armies;

    private readonly Random _rng;
    public int D6() => _rng.Next(1, 7);

    private MapCampaignSession(string playerPath, string mapPath, CampaignMap map, Dictionary<string, (string, BuiltArmyFile)> armies, Random? rng)
    {
        PlayerPath = playerPath;
        MapPath = mapPath;
        Map = map;
        _armies = armies;
        _rng = rng ?? new Random();
    }

    /// <summary>The map file that goes with a player's campaign army: same folder, same name, .fdgmap.</summary>
    public static string MapPathFor(string playerPath) => Path.ChangeExtension(playerPath, CampaignMap.Extension);

    /// <summary>The colours rivals are drawn in, after the player's (index into the screen's palette).</summary>
    public const int MaxRivals = 3;

    // ------------------------------------------------------------------ starting one

    /// <summary>
    /// Starts a map campaign: saves every army as a campaign army (the rivals as bots), makes the map and saves it beside
    /// the player's file. The campaign type is Endless, so the ordinary campaign screen never ends it on mission VP alone
    /// - the map decides (VP target counting starred regions, or capitals). Returns the session, or null with why.
    /// </summary>
    /// <param name="folder">Where the campaign files go; null = this system's campaign folder (tests pass a temp folder).</param>
    public static MapCampaignSession? Start(EGameSystem system, string name, BuiltArmyFile yours,
        IReadOnlyList<(string Name, BuiltArmyFile Army)> rivals, EMapSize size, int vpTarget, bool noPermadeath, int seed,
        out string? error, string? folder = null)
    {
        error = null;
        if (rivals.Count is < 1 or > MaxRivals) { error = $"A map campaign needs 1 to {MaxRivals} rivals."; return null; }
        CampaignNumbers n = CampaignNumbers.For(system);
        string NewPath(string armyName) => folder == null ? StrikeTeamFiles.NewPath(system, armyName) : FreePath(folder, armyName);

        var written = new List<string>();
        void Undo() { foreach (string p in written) try { File.Delete(p); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

        var factions = new List<MapFaction> { new() { Id = CampaignMap.PlayerId, Name = name.Trim(), IsPlayer = true, Colour = 0 } };
        var armies = new Dictionary<string, (string, BuiltArmyFile)>();
        for (int i = 0; i < rivals.Count; i++)
        {
            var (rivalName, army) = rivals[i];
            string path = NewPath(rivalName.Trim());
            CampaignRules.Start(army, rivalName.Trim(), path, n, system, ECampaignType.Endless, noPermadeath, isBot: true);
            army.Selections = null;
            if (!StrikeTeamFiles.Save(path, army)) { Undo(); error = $"Couldn't save {rivalName}'s army file."; return null; }
            written.Add(path);
            string id = "r" + (i + 1);
            factions.Add(new MapFaction { Id = id, Name = rivalName.Trim(), ArmyFile = path, Colour = i + 1 });
            armies[id] = (path, army);
        }

        string playerPath = NewPath(name.Trim());
        string mapPath = MapPathFor(playerPath);
        factions[0].ArmyFile = playerPath;
        CampaignMap map = CampaignMapGenerator.Generate(system, size, factions, seed);
        map.VpTarget = vpTarget;

        CampaignRules.Start(yours, name.Trim(), playerPath, n, system, ECampaignType.Endless, noPermadeath);
        yours.Selections = null;
        yours.StrikeTeam!.EnemyArmyFile = armies["r1"].Item1;
        yours.StrikeTeam.CampaignMapFile = Path.GetFileName(mapPath);
        armies[CampaignMap.PlayerId] = (playerPath, yours);
        if (!StrikeTeamFiles.Save(playerPath, yours)) { Undo(); error = "Couldn't save your campaign army file."; return null; }
        written.Add(playerPath);
        if (!map.Save(mapPath)) { Undo(); error = "Couldn't save the campaign map."; return null; }
        return new MapCampaignSession(playerPath, mapPath, map, armies, null);
    }

    private static string FreePath(string folder, string armyName)
    {
        Directory.CreateDirectory(folder);
        string safe = string.Concat(armyName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "Campaign Army";
        string path = Path.Combine(folder, $"Campaign - {safe}.fdgarmy");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"Campaign - {safe} {i}.fdgarmy");
        return path;
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Whether a campaign army plays on a map.</summary>
    public static bool IsMapCampaign(StrikeTeamInfo? info) => !string.IsNullOrEmpty(info?.CampaignMapFile);

    /// <summary>Opens the map campaign a player's campaign army belongs to, or null with why.</summary>
    public static MapCampaignSession? Open(string playerPath, out string? error, Random? rng = null)
    {
        error = null;
        if (StrikeTeamFiles.Load(playerPath) is not { StrikeTeam: { } info } player) { error = "Couldn't read the campaign army file."; return null; }
        if (!IsMapCampaign(info)) { error = "That campaign isn't played on a map."; return null; }
        string mapPath = Path.Combine(Path.GetDirectoryName(playerPath) ?? "", info.CampaignMapFile!);
        if (CampaignMap.Load(mapPath) is not { } map) { error = "Couldn't read the campaign map file."; return null; }

        var armies = new Dictionary<string, (string, BuiltArmyFile)> { [CampaignMap.PlayerId] = (playerPath, player) };
        foreach (MapFaction f in map.Factions.Where(f => !f.IsPlayer))
        {
            string path = ResolveArmyPath(f.ArmyFile, playerPath);
            if (StrikeTeamFiles.Load(path) is not { StrikeTeam: not null } army) { error = $"Couldn't read {f.Name}'s army file."; return null; }
            armies[f.Id] = (path, army);
        }
        return new MapCampaignSession(playerPath, mapPath, map, armies, rng);
    }

    /// <summary>A rival's file as recorded, or - if the campaign folder was moved - the same file name beside the player's.</summary>
    private static string ResolveArmyPath(string? recorded, string playerPath)
    {
        if (!string.IsNullOrEmpty(recorded) && File.Exists(recorded)) return recorded;
        string beside = Path.Combine(Path.GetDirectoryName(playerPath) ?? "", Path.GetFileName(recorded ?? ""));
        return beside;
    }

    // ------------------------------------------------------------------ the armies' books as the map reads them

    public StrikeTeamInfo InfoOf(string faction) => _armies[faction].Army.StrikeTeam!;

    public MapArmyState StateOf(string faction)
    {
        BuiltArmyFile army = _armies[faction].Army;
        return new MapArmyState(army.StrikeTeam!.BankedPoints, CampaignRules.ArmyValue(army), army.StrikeTeam.Vp);
    }

    public Dictionary<string, MapArmyState> States() => Map.Factions.ToDictionary(f => f.Id, f => StateOf(f.Id));

    /// <summary>An army's VP on the map: mission VP plus starred regions held.</summary>
    public int Vp(string faction) => CampaignMapRules.Vp(Map, faction, StateOf(faction).MissionVp);

    public EMapOutcome Outcome => CampaignMapRules.Outcome(Map, States());

    /// <summary>Why the player can't give this order, or null.</summary>
    public string? WhyNot(MapOrder order) => CampaignMapRules.WhyNot(Map, order, StateOf(order.Faction), Numbers);

    // ------------------------------------------------------------------ playing a turn

    /// <summary>Gives the player's order and starts the turn (the rivals pick theirs). Null, or why not.</summary>
    public string? BeginTurn(MapOrder order)
    {
        if (Map.Pending != null) return "A turn is already under way.";
        if (Outcome != EMapOutcome.InProgress) return "The campaign is over.";
        if (WhyNot(order) is { } why) return why;
        CampaignMapTurn.Begin(Map, order, States(), Numbers);
        return Map.Save(MapPath) ? null : "Couldn't save the campaign map.";
    }

    /// <summary>
    /// Plays the turn under way as far as it goes. When it finishes, applies it: every army's books, the rivals' own
    /// between-turn steps, the report (rivals' spending only for rivals the player has met), and saves everything.
    /// </summary>
    public MapTurnResult Step()
    {
        MapTurnResult result = CampaignMapTurn.Resolve(Map, Numbers);
        if (!result.Finished) return result;

        CampaignMap after = result.Map;
        foreach (var (id, change) in result.Changes)
            if (_armies.TryGetValue(id, out var a) && a.Army.StrikeTeam is { } info) CampaignMapUpkeep.Apply(info, change);

        var report = after.History[^1].Lines;
        foreach (MapFaction rival in after.Alive.Where(f => !f.IsPlayer))
        {
            BuiltArmyFile army = _armies[rival.Id].Army;
            int biggest = _armies.Where(kv => kv.Key != rival.Id).Select(kv => CampaignRules.ArmyValue(kv.Value.Army)).DefaultIfEmpty(0).Max();
            List<string> did = CampaignMapUpkeep.RivalBetweenTurns(after, rival.Id, army, army.Book, Numbers, biggest, D6);
            if (did.Count > 0 && CampaignMapRules.HasMet(after, CampaignMap.PlayerId, rival.Id))
                report.Add($"{rival.Name}: " + string.Join(" ", did));
        }

        Map = after;
        SaveAll();
        return result;
    }

    /// <summary>Records the result of the player battle the turn is waiting for, and saves.</summary>
    public void RecordBattle(bool playerWon)
    {
        CampaignMapTurn.RecordPlayerResult(Map, playerWon);
        Map.Save(MapPath);
    }

    /// <summary>The underdog points the player's land gives in a battle (Shrines, a defended capital or Fortress).</summary>
    public int PlayerUnderdogBonus(MapBattle battle) =>
        Map.Region(battle.Region) is { } r ? CampaignMapRules.UnderdogBonus(Map, CampaignMap.PlayerId, r) : 0;

    public bool SaveAll()
    {
        bool ok = Map.Save(MapPath);
        foreach (var (path, army) in _armies.Values) ok &= StrikeTeamFiles.Save(path, army);
        return ok;
    }

    /// <summary>The armies' files and the map, for deleting a map campaign.</summary>
    public IEnumerable<string> Files() => _armies.Values.Select(a => a.Path).Append(MapPath);
}
