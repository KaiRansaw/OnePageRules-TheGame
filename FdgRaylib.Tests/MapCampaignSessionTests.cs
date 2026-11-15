using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Campaign.Map;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>#467 - a map campaign on disk: starting one, reopening it, playing a turn, and deleting it.</summary>
[TestFixture]
public class MapCampaignSessionTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "map-campaign-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static BookFile Book() => new()
    {
        Name = "Test Legion",
        Faction = "Test Legion",
        Units =
        {
            new RosterUnit
            {
                Id = "trooper", Name = "Troopers", Quality = 4, Defense = 4,
                BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
            },
        },
    };

    private static BuiltArmyFile Army(int squads)
    {
        BookFile book = Book();
        var list = new BuilderList { Name = "Army", BookName = book.Name };
        for (int i = 0; i < squads; i++) list.Units.Add(new BuilderUnit { RosterUnitId = "trooper", ModelCount = 5 });
        return ListCompiler.Compile(book, list);
    }

    private MapCampaignSession StartOne(int rivals = 2)
    {
        var rivalArmies = Enumerable.Range(1, rivals).Select(i => ($"Rival {i}", Army(3))).ToList(); // 300 pts of 750: money to spend
        MapCampaignSession? s = MapCampaignSession.Start(EGameSystem.GrimdarkFuture, "Iron Reavers", Army(6), rivalArmies,
            EMapSize.Medium, 10, noPermadeath: false, seed: 9, out string? error, folder: _dir);
        Assert.That(error, Is.Null);
        return s!;
    }

    [Test]
    public void Start_WritesEveryArmyAndTheMap()
    {
        MapCampaignSession s = StartOne(3);
        Assert.That(File.Exists(s.PlayerPath), Is.True);
        Assert.That(File.Exists(s.MapPath), Is.True);
        Assert.That(s.Map.Factions.Count, Is.EqualTo(4));
        foreach (MapFaction f in s.Map.Factions.Where(f => !f.IsPlayer))
        {
            Assert.That(File.Exists(f.ArmyFile), Is.True);
            Assert.That(s.InfoOf(f.Id).IsBot, Is.True);
        }
        StrikeTeamInfo mine = s.InfoOf(CampaignMap.PlayerId);
        Assert.That(MapCampaignSession.IsMapCampaign(mine), Is.True);
        Assert.That(mine.IsBot, Is.False);
        Assert.That(mine.CampaignType, Is.EqualTo(ECampaignType.Endless), "the map decides when it ends, not mission VP alone");
        Assert.That(s.Map.VpTarget, Is.EqualTo(10));
    }

    [Test]
    public void Start_RefusesNoRivals_OrTooMany()
    {
        Assert.That(MapCampaignSession.Start(EGameSystem.GrimdarkFuture, "A", Army(6), new List<(string, BuiltArmyFile)>(),
            EMapSize.Small, 10, false, 1, out string? none, _dir), Is.Null);
        Assert.That(none, Is.Not.Null);
        var four = Enumerable.Range(1, 4).Select(i => ($"R{i}", Army(6))).ToList();
        Assert.That(MapCampaignSession.Start(EGameSystem.GrimdarkFuture, "A", Army(6), four, EMapSize.Small, 10, false, 1, out _, _dir), Is.Null);
    }

    /// <summary>Rivals built for you keep their bought units and sheets, and every sheet points at the rival's file.</summary>
    [Test]
    public void AutoBuiltRivals_AreSavedWithTheirSheetsPointingAtTheirFiles()
    {
        Assume.That(MapCampaignSession.RivalBooks(EGameSystem.GrimdarkFuture), Is.Not.Empty, "no bundled books found from the test host");
        var rivals = Enumerable.Range(0, 2).Select(i => MapCampaignSession.BuildRival(EGameSystem.GrimdarkFuture, new Random(i), new List<string>())!.Value).ToList();
        var units = rivals.Select(r => r.Army.Units.Select(u => u.Name).ToList()).ToList();
        MapCampaignSession s = MapCampaignSession.Start(EGameSystem.GrimdarkFuture, "Iron Reavers", Army(6),
            rivals.Select((r, i) => ($"Rival {i}", r.Army)).ToList(), EMapSize.Small, 10, false, 3, out string? error, _dir)!;
        Assert.That(error, Is.Null);
        for (int i = 0; i < 2; i++)
        {
            var (path, army) = s.Armies["r" + (i + 1)];
            BuiltArmyFile onDisk = StrikeTeamFiles.Load(path)!;
            Assert.That(onDisk.Units.Select(u => u.Name), Is.EqualTo(units[i]), "nothing renamed or lost");
            Assert.That(onDisk.Units.All(u => CampaignRules.Sheet(u)!.TeamFile == path), Is.True);
            Assert.That(onDisk.StrikeTeam!.Name, Is.EqualTo($"Rival {i}"));
            Assert.That(onDisk.StrikeTeam.IsBot, Is.True);
        }
    }

    [Test]
    public void Open_ReadsBackTheSameCampaign()
    {
        MapCampaignSession s = StartOne();
        MapCampaignSession? again = MapCampaignSession.Open(s.PlayerPath, out string? error);
        Assert.That(error, Is.Null);
        Assert.That(again!.Map.ToJson(), Is.EqualTo(s.Map.ToJson()));
        Assert.That(again.Armies.Keys, Is.EquivalentTo(s.Armies.Keys));
    }

    [Test]
    public void ARestingTurn_PaysTheRestAndTheCapitalsIncome_AndTheRivalsSpend()
    {
        MapCampaignSession s = StartOne();
        int before = s.InfoOf(CampaignMap.PlayerId).BankedPoints;
        var rivalUnits = s.Map.Factions.Where(f => !f.IsPlayer).ToDictionary(f => f.Id, f => s.Armies[f.Id].Army.Units.Count);

        Assert.That(s.BeginTurn(new MapOrder(CampaignMap.PlayerId, EOrderKind.Rest)), Is.Null);
        MapTurnResult r;
        while (!(r = s.Step()).Finished) s.RecordBattle(true);

        // Rest (75) + the capital, a Hive World (75). Battles the player fought pay through their missions, not here.
        Assert.That(s.InfoOf(CampaignMap.PlayerId).BankedPoints, Is.EqualTo(before + 150));
        Assert.That(s.Map.Turn, Is.EqualTo(2));
        Assert.That(s.Map.Pending, Is.Null);
        Assert.That(s.Map.Factions.Where(f => !f.IsPlayer && f.Alive).Any(f => s.Armies[f.Id].Army.Units.Count > rivalUnits[f.Id]), Is.True,
            "rivals ran their own books - at least one recruited");

        // Everything was saved.
        MapCampaignSession? again = MapCampaignSession.Open(s.PlayerPath, out _);
        Assert.That(again!.Map.Turn, Is.EqualTo(2));
        Assert.That(again.InfoOf(CampaignMap.PlayerId).BankedPoints, Is.EqualTo(before + 150));
    }

    [Test]
    public void AHalfPlayedTurn_SurvivesClosingTheGame()
    {
        MapCampaignSession s = StartOne(3);
        MapRegion target = CampaignMapRules.Reachable(s.Map, CampaignMap.PlayerId).First(x => x.Owner == null);
        Assert.That(s.BeginTurn(new MapOrder(CampaignMap.PlayerId, EOrderKind.Garrison, target.Id)), Is.Null);
        MapTurnResult first = s.Step();
        Assert.That(first.WaitingFor, Is.Not.Null);
        s.RecordBattle(true);

        MapCampaignSession reopened = MapCampaignSession.Open(s.PlayerPath, out _)!;
        Assert.That(reopened.Map.Pending!.PlayerResults, Is.EqualTo(new[] { true }));
        MapTurnResult r;
        while (!(r = reopened.Step()).Finished) reopened.RecordBattle(true);
        Assert.That(reopened.Map.Region(target.Id)!.Owner, Is.EqualTo(CampaignMap.PlayerId));
    }

    [Test]
    public void BeginTurn_RefusesWhatTheRulesDont()
    {
        MapCampaignSession s = StartOne();
        MapRegion far = s.Map.Regions.First(r => r.Owner == null && !CampaignMapRules.CanReach(s.Map, CampaignMap.PlayerId, r));
        Assert.That(s.BeginTurn(new MapOrder(CampaignMap.PlayerId, EOrderKind.Garrison, far.Id)), Is.Not.Null);
        Assert.That(s.Map.Pending, Is.Null);
    }

    [TestCase(EGameSystem.GrimdarkFuture)]
    [TestCase(EGameSystem.GFFirefight)]
    [TestCase(EGameSystem.AgeOfFantasy)]
    [TestCase(EGameSystem.AoFSkirmish)]
    public void ARivalBuiltForYou_IsALegalStartingArmyFromThatGamesBooks(EGameSystem system)
    {
        var books = MapCampaignSession.RivalBooks(system);
        Assume.That(books, Is.Not.Empty, "no bundled books found from the test host");
        string slug = FDG.ArmyBuilding.GameSystems.CampaignBookSlug(system)!;
        for (int seed = 0; seed < 5; seed++)
        {
            var built = MapCampaignSession.BuildRival(system, new Random(seed), new List<string>());
            Assert.That(built, Is.Not.Null, $"{system} seed {seed}");
            Assert.That(CampaignRules.CannotStart(built!.Value.Army, CampaignNumbers.For(system)), Is.Null);
            Assert.That(FDG.ArmyBuilding.GameSystems.SameSystem(built.Value.Army.Book!.GameSystem, slug), Is.True, "only this game's own books");
        }
    }

    /// <summary>
    /// A garrison battle end to end on disk: set up (garrison made, no rewards, Easy, the land's underdog points), the
    /// mission recorded the way the game's write-back does it, then collected onto the map.
    /// </summary>
    [Test]
    public void AGarrisonBattle_IsSetUp_PaysNothing_AndLandsOnTheMap()
    {
        Assume.That(MapCampaignSession.RivalBooks(EGameSystem.GrimdarkFuture), Is.Not.Empty, "no bundled books found from the test host");
        MapCampaignSession s = StartOne();
        MapRegion target = CampaignMapRules.Reachable(s.Map, CampaignMap.PlayerId).First(x => x.Owner == null);
        Assert.That(s.BeginTurn(new MapOrder(CampaignMap.PlayerId, EOrderKind.Garrison, target.Id)), Is.Null);
        MapBattle battle = s.Step().WaitingFor!;
        Assert.That(battle.Kind, Is.EqualTo(EMapBattleKind.Garrison));

        Assert.That(s.PrepareBattle(battle), Is.Null);
        StrikeTeamInfo info = s.InfoOf(CampaignMap.PlayerId);
        Assert.That(info.NoMissionReward, Is.True);
        Assert.That(File.Exists(info.EnemyArmyFile), Is.True, "the garrison's army file");
        Assert.That(CampaignPlans.Next(info)!.Difficulty, Is.EqualTo(EMissionDifficulty.Easy));
        Assert.That(CampaignPlans.Next(info)!.Title, Does.Contain(target.Name));
        // Step 4: the region's own battlefield, rolled with its type's theme and opened by Mission Setup.
        CampaignMissionPlan plan = CampaignPlans.Next(info)!;
        Assert.That(plan.TerrainMode, Is.EqualTo(ETerrainPlacementMode.LoadFromFile));
        Assert.That(File.Exists(plan.MapFile), Is.True, "the battlefield file");
        TerrainLayoutFile field = FdgRaylib.Cli.TerrainLoader.TryLoadFromJson(File.ReadAllText(plan.MapFile!))!;
        Assert.That(field.Pieces, Is.Not.Empty);
        Assert.That(field.GeneratedFrom, Is.EqualTo(CampaignMapRules.ThemeFor(target.Type).ToString()));
        Assert.That(Path.GetDirectoryName(plan.MapFile), Is.EqualTo(Path.GetDirectoryName(s.PlayerPath)), "beside the campaign, not in the Maps folder");
        var setup = new MissionSetupSession(s.PlayerPath, Array.Empty<CampaignEntrant>());
        Assert.That(setup.MapPath, Is.EqualTo(plan.MapFile), "Mission Setup opens on it");
        Assert.That(s.CollectBattle(), Is.False, "not played yet");

        // The mission, as the game's write-back records it.
        BuiltArmyFile army = StrikeTeamFiles.Load(s.PlayerPath)!;
        int banked = army.StrikeTeam!.BankedPoints;
        var results = army.Units.Select(u => new CampaignUnitResult(CampaignRules.Sheet(u)!.Id, false, 0, 0)).ToList();
        CampaignRules.AfterGame(army, results, true, CampaignNumbers.GrimdarkFuture, () => 3);
        StrikeTeamFiles.Save(s.PlayerPath, army);

        string garrisonFile = info.EnemyArmyFile!;
        Assert.That(s.CollectBattle(), Is.True);
        Assert.That(File.Exists(garrisonFile), Is.False, "the garrison is tidied away");
        Assert.That(File.Exists(plan.MapFile), Is.False, "and the battlefield");
        Assert.That(s.InfoOf(CampaignMap.PlayerId).BankedPoints, Is.EqualTo(banked), "a garrison pays nothing");
        Assert.That(s.InfoOf(CampaignMap.PlayerId).NoMissionReward, Is.False);

        MapTurnResult r;
        while (!(r = s.Step()).Finished) s.RecordBattle(true);
        Assert.That(s.Map.Region(target.Id)!.Owner, Is.EqualTo(CampaignMap.PlayerId));
    }

    [Test]
    public void ARivalBattle_PointsMissionSetupAtThatRival_WithTheLandsUnderdogPoints()
    {
        MapCampaignSession s = StartOne();
        MapRegion mine = s.Map.Regions.First(r => r.Owner == CampaignMap.PlayerId && !r.Capital);
        mine.Type = ERegionType.Shrine; // +1 underdog point
        s.SaveAll();
        var battle = new MapBattle(mine.Id, "r2", CampaignMap.PlayerId, EMapBattleKind.Attack, false);
        // A turn has to be under way for a battle to be prepared.
        Assert.That(s.BeginTurn(new MapOrder(CampaignMap.PlayerId, EOrderKind.Rest)), Is.Null);
        Assert.That(s.PrepareBattle(battle), Is.Null);
        StrikeTeamInfo info = s.InfoOf(CampaignMap.PlayerId);
        Assert.That(info.EnemyArmyFile, Is.EqualTo(s.Armies["r2"].Path));
        Assert.That(info.NoMissionReward, Is.False);
        Assert.That(info.MapUnderdogBonus, Is.EqualTo(1));
        Assert.That(CampaignPlans.Next(info)!.Title, Does.Contain("attacks you"));
    }

    [Test]
    public void DeletingTheCampaign_TakesTheMapAndEveryRival()
    {
        MapCampaignSession s = StartOne(3);
        IReadOnlyList<string> files = ArmyCampaignSession.CampaignFiles(s.PlayerPath, s.InfoOf(CampaignMap.PlayerId));
        foreach (string f in s.Files()) Assert.That(files, Does.Contain(f));
    }
}
