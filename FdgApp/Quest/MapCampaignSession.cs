using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Campaign.Map;
using FdgRaylib.Rendering;
using FdgRaylib.SaveLoad;
using FDG.SaveLoad;

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
            if (army.StrikeTeam != null) MapRivalArmy.Adopt(army, rivalName.Trim(), path, noPermadeath); // built for you
            else CampaignRules.Start(army, rivalName.Trim(), path, n, system, ECampaignType.Endless, noPermadeath, isBot: true);
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

    // ------------------------------------------------------------------ rivals built for you

    /// <summary>This game's army books (a rival may share a faction with you or another rival - Kai, 2026-11-15).</summary>
    public static IReadOnlyList<FDG.ArmyBuilding.BookFile> RivalBooks(EGameSystem system)
    {
        string? slug = FDG.ArmyBuilding.GameSystems.CampaignBookSlug(system);
        return BookLibrary.Load().Where(b => FDG.ArmyBuilding.GameSystems.SameSystem(b.GameSystem, slug)).ToList();
    }

    /// <summary>A rival army built for you: a random book of the game, a name not already taken, a legal starting army.</summary>
    public static (string Name, BuiltArmyFile Army)? BuildRival(EGameSystem system, Random rng, IReadOnlyCollection<string> namesTaken) =>
        MapRivalArmy.Build(system, RivalBooks(system), rng, namesTaken);

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

    /// <summary>
    /// Re-reads every army from disk. The map screen calls it whenever it opens: a mission's write-back, the Camp's
    /// recruits and the trait picks all change the army files behind the session's back, and a stale copy saved over them
    /// would undo that.
    /// </summary>
    public void ReloadArmies()
    {
        foreach (string id in _armies.Keys.ToList())
            if (StrikeTeamFiles.Load(_armies[id].Path) is { StrikeTeam: not null } fresh) _armies[id] = (_armies[id].Path, fresh);
        if (CampaignMap.Load(MapPath) is { } map) Map = map;
    }

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
        ReloadArmies();
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
        ReloadArmies();
        MapTurnResult result = CampaignMapTurn.Resolve(Map, Numbers);
        if (!result.Finished) return result;

        CampaignMap after = result.Map;
        foreach (var (id, change) in result.Changes)
            if (_armies.TryGetValue(id, out var a) && a.Army.StrikeTeam is { } info) CampaignMapUpkeep.Apply(info, change);

        var report = after.History[^1].Lines;

        // Battles settled off the table cost the armies in them casualties and earn them XP, by the book's own
        // after-match steps (no treasury or VP - the map paid those). Knocked-out armies are still updated: their files
        // stay readable in the journal.
        foreach (var (battle, attackerWon) in result.OffScreen)
            foreach (var (side, won) in new[] { (battle.Attacker, attackerWon), (battle.Defender, !attackerWon) })
                if (side != null && side != CampaignMap.PlayerId && _armies.TryGetValue(side, out var sa) && sa.Army.StrikeTeam is { } si)
                    si.History.AddRange(CampaignMapUpkeep.OffScreenBattle(sa.Army, won, Numbers, D6));

        // The land's perks: Forge World (free 5-XP surcharge), Medical Station (faster recovery).
        foreach (MapFaction f in after.Alive)
        {
            BuiltArmyFile army = _armies[f.Id].Army;
            List<string> perks = CampaignMapUpkeep.EndOfTurnPerks(after, f.Id, army);
            army.StrikeTeam!.History.AddRange(perks);
            if (f.IsPlayer) report.AddRange(perks);
        }

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

    // ------------------------------------------------------------------ the player's battles

    /// <summary>A battle has been set up and its mission not yet recorded.</summary>
    public bool BattleUnderWay => Map.Pending?.BattleAtMatch != null;

    /// <summary>The army file the player fights in a battle: a rival's, or (for a garrison) null until it is made.</summary>
    public string? OpponentPath(MapBattle b) => b.OpponentOf(CampaignMap.PlayerId) is { } id && _armies.TryGetValue(id, out var a) ? a.Path : null;

    /// <summary>
    /// Readies the player's campaign army for the battle the turn is waiting on, so Mission Setup opens on it: the
    /// opponent (a rival's campaign army, or a garrison made now), the land's underdog points, and for a garrison no
    /// rewards and Easy difficulty (its force comes out at about half yours - house rule B24). Null, or why not.
    /// </summary>
    public string? PrepareBattle(MapBattle battle)
    {
        ReloadArmies();
        if (Map.Pending == null) return "No turn is under way.";
        if (BattleUnderWay) return null; // already set up (the screen was left and reopened)
        MapRegion region = Map.Region(battle.Region)!;
        BuiltArmyFile player = _armies[CampaignMap.PlayerId].Army;
        StrikeTeamInfo info = player.StrikeTeam!;

        string? enemyPath = OpponentPath(battle);
        bool garrison = battle.OpponentOf(CampaignMap.PlayerId) == null;
        if (garrison)
        {
            int budget = MissionSetupSession.Fieldable(player).Sum(x => x.Unit.PointCost);
            if (MapRivalArmy.Garrison(System, RivalBooks(System), _rng, region.Name, budget) is not { } g) return "Couldn't raise a garrison - no army book for this game.";
            enemyPath = Path.Combine(Path.GetDirectoryName(PlayerPath) ?? "", $"Garrison - {SafeName(region.Name)}.fdgarmy");
            MapRivalArmy.Adopt(g, g.Name, enemyPath, info.NoPermadeath);
            g.Selections = null;
            if (!StrikeTeamFiles.Save(enemyPath, g)) return "Couldn't save the garrison's army file.";
            Map.Pending.GarrisonFile = enemyPath;
        }

        info.EnemyArmyFile = enemyPath;
        info.NoMissionReward = garrison;
        info.MapUnderdogBonus = PlayerUnderdogBonus(battle);
        CampaignMissionPlan plan = CampaignPlans.Next(info) ?? CampaignMissionGenerator.Generate(info.Matches + 1, System, D6);
        plan.Title = BattleTitle(battle);
        if (garrison) plan.Difficulty = EMissionDifficulty.Easy;
        // #467 step 4 - the region's battlefield: the Advanced Rulebook generator rolled with its type's theme, for this
        // battle only (Mission Setup opens on it; another map can still be picked there).
        if (MakeBattlefield(region) is { } field)
        {
            Map.Pending.BattlefieldFile = field;
            plan.TerrainMode = ETerrainPlacementMode.LoadFromFile;
            plan.MapFile = field;
            plan.MapName = Path.GetFileNameWithoutExtension(field);
        }
        CampaignPlans.Set(info, plan);
        Map.Pending.BattleAtMatch = info.Matches;
        return StrikeTeamFiles.Save(PlayerPath, player) && Map.Save(MapPath) ? null : "Couldn't save the campaign.";
    }

    /// <summary>
    /// After the mission: if the battle set up by <see cref="PrepareBattle"/> has been played (the campaign army's match
    /// count moved on), records who won on the map, tidies the garrison away and saves. Returns whether a result was taken.
    /// </summary>
    public bool CollectBattle()
    {
        ReloadArmies();
        if (Map.Pending is not { BattleAtMatch: { } at } pending) return false;
        StrikeTeamInfo info = InfoOf(CampaignMap.PlayerId);
        if (info.Matches <= at) return false;
        bool won = info.History.Any(h => h.StartsWith($"Match {info.Matches}: won", StringComparison.Ordinal));
        pending.BattleAtMatch = null;
        if (pending.BattlefieldFile is { } bf)
        {
            try { if (File.Exists(bf)) File.Delete(bf); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            pending.BattlefieldFile = null;
        }
        if (pending.GarrisonFile is { } g)
        {
            foreach (string f in new[] { g, Path.ChangeExtension(g, ".fdgmission") })
                try { if (File.Exists(f)) File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            pending.GarrisonFile = null;
        }
        // The map's one-mission settings, in case the write-back didn't run (a quit game).
        info.NoMissionReward = false;
        info.MapUnderdogBonus = 0;
        info.EnemyArmyFile = Map.Factions.FirstOrDefault(f => !f.IsPlayer && f.Alive)?.ArmyFile ?? info.EnemyArmyFile;
        CampaignMapTurn.RecordPlayerResult(Map, won);
        StrikeTeamFiles.Save(PlayerPath, _armies[CampaignMap.PlayerId].Army);
        Map.Save(MapPath);
        return true;
    }

    /// <summary>"Bastion IX: you attack the Hollow Choir" - the mission's title. ASCII (game text).</summary>
    public string BattleTitle(MapBattle b)
    {
        string region = Map.Region(b.Region)?.Name ?? b.Region;
        string? them = b.OpponentOf(CampaignMap.PlayerId) is { } id ? Map.Faction(id)?.Name : null;
        return b.Kind switch
        {
            EMapBattleKind.Garrison => $"{region}: the local garrison",
            EMapBattleKind.Clash => $"{region}: you meet {them} there",
            _ when b.Attacker == CampaignMap.PlayerId => $"{region}: you attack {them}",
            _ => $"{region}: {them} attacks you",
        };
    }

    /// <summary>
    /// Rolls a region's battlefield and saves it beside the campaign: the book's generator for its type's theme on this
    /// game's table. The file's path, or null if the game's book has no generator.
    /// </summary>
    public string? MakeBattlefield(MapRegion region)
    {
        if (!TerrainBook.HasGenerator(System)) return null;
        var (w, h) = GameWideConstants.TableSizeFor(System);
        var draft = FdgRaylib.Terrain.MapDraft.New(w, h);
        draft.Generate(System, CampaignMapRules.ThemeFor(region.Type), _rng);
        string name = $"Battlefield - {SafeName(region.Name)}";
        draft.Name = name;
        string path = Path.Combine(Path.GetDirectoryName(PlayerPath) ?? "", name + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
        try
        {
            TerrainLayoutFile layout = draft.ToLayout();
            layout.Name = name;
            File.WriteAllText(path, FdgRaylib.Cli.TerrainLoader.SerializeToJson(layout));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string SafeName(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();

    /// <summary>The player's battles still to come this turn after the one it is waiting on (for the battle card).</summary>
    public IReadOnlyList<MapBattle> LaterPlayerBattles(MapBattle current)
    {
        // Replay the turn with the current battle answered both ways would change what follows; a plain forecast is
        // enough for the card: the regions other armies ordered against the player's land, not yet reached.
        if (Map.Pending is not { } p) return Array.Empty<MapBattle>();
        int at = Map.Regions.FindIndex(r => r.Id == current.Region);
        return p.Orders.Where(o => !Map.Faction(o.Faction)!.IsPlayer && o.Kind == EOrderKind.Attack
                                   && Map.Region(o.Region)?.Owner == CampaignMap.PlayerId
                                   && Map.Regions.FindIndex(r => r.Id == o.Region) > at)
            .Select(o => new MapBattle(o.Region!, o.Faction, CampaignMap.PlayerId, EMapBattleKind.Attack, Map.Region(o.Region)!.Capital))
            .ToList();
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
