using FdgRaylib.Models3D;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Age of Fantasy's models (Kai, 2026-11-13): Stormvault, the Age of Sigmar Tabletop Simulator collection, read into the
/// same catalogue shape as ForceOrg's, with ids that match the unit list's build script and never meet ForceOrg's.
/// </summary>
[TestFixture]
public class StormvaultCatalogTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-stormvault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "sv", "Destruction"));
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static object Model(string name, string body, float bodyY = 0.06f) => new
    {
        Name = "Custom_Model", Nickname = name,
        Transform = new { posX = 12.0, posY = 1.5, posZ = 7.0, rotX = 0.0, rotY = 30.0, rotZ = 0.0, scaleX = 1.0, scaleY = 1.0, scaleZ = 1.0 },
        ColorDiffuse = new { r = 0.0, g = 0.0, b = 0.0 },
        CustomMesh = new { MeshURL = "http://a/base.obj", DiffuseURL = "http://a/base.png" },
        ChildObjects = new object[]
        {
            new
            {
                Name = "Custom_Model", Nickname = "",
                Transform = new { posX = 0.0, posY = bodyY, posZ = 0.0, rotX = 0.0, rotY = 195.0, rotZ = 0.0, scaleX = 1.0, scaleY = 1.0, scaleZ = 1.0 },
                CustomMesh = new { MeshURL = body, DiffuseURL = "http://a/body.png" },
            },
        },
    };

    private void WriteFaction(string file, params object[] objects) =>
        File.WriteAllText(Path.Combine(_dir, "sv", "Destruction", file), JsonConvert.SerializeObject(new
        {
            SaveName = "x",
            ObjectStates = new object[]
            {
                new { Name = "Custom_Model_Infinite_Bag", Nickname = "Troops", ContainedObjects = objects },
                // A text tool: a mesh with no skin, not a model.
                new { Name = "Custom_Model", Nickname = "Text Tool", CustomMesh = new { MeshURL = "http://a/tool.obj", DiffuseURL = "" } },
            },
        }));

    [Test]
    public void StableId_IsTheBuildScriptsNumber_AndClearOfForceOrgIds()
    {
        // tools/unit-models/build_aof_map.py stable_id(['http://a/base.obj', 'http://a/body.obj']) == 524156569
        Assert.That(StormvaultCatalog.StableId(new[] { "http://a/base.obj", "http://a/body.obj" }), Is.EqualTo(524156569));
        Assert.That(StormvaultCatalog.StableId(new[] { "x" }), Is.GreaterThanOrEqualTo(StormvaultCatalog.IdBase));
    }

    [Test]
    public void Build_ReadsEveryModelAsBaseAndFigure_InForceOrgsShape()
    {
        WriteFaction("Ogor Mawtribes.json", Model("Ogor Glutton", "http://a/glutton1.obj"), Model("Ogor Glutton", "http://a/glutton2.obj"),
            Model("Ogor Glutton", "http://a/glutton1.obj")); // the same model twice is kept once
        string output = Path.Combine(_dir, StormvaultCatalog.FileName);

        int n = StormvaultCatalog.Build(Path.Combine(_dir, "sv"), output);
        var cat = ForceOrgCatalog.Load(output);

        Assert.That(n, Is.EqualTo(2));
        Assert.That(cat.ById.Values.Select(m => m.Name), Is.All.EqualTo("Ogor Glutton"));
        var m = cat.ById[StormvaultCatalog.StableId(new[] { "http://a/base.obj", "http://a/glutton1.obj" })];
        Assert.That(m.Parts, Has.Count.EqualTo(2));
        Assert.That(m.Parts[0].Pos, Is.EqualTo(new float[] { 0, 0, 0 }), "the root sits at the origin, not where it stood in TTS");
        Assert.That(m.Parts[1].Parent, Is.EqualTo(0));
        Assert.That(m.Parts[1].Rot[1], Is.EqualTo(195f));
        Assert.That(CatalogueFactions.For(cat, m.Group), Is.EqualTo("Ogor Mawtribes"));
    }

    [Test]
    public void BuildFromZip_ReadsTheFactionSavesInGitHubsZip()
    {
        WriteFaction("Ogor Mawtribes.json", Model("Ogor Glutton", "http://a/glutton1.obj"));
        File.WriteAllBytes(Path.Combine(_dir, "sv", "Destruction", "Ogor Mawtribes.png"), new byte[] { 1, 2, 3 });
        // GitHub's zip puts everything under "Stormvault-main/".
        string staged = Path.Combine(_dir, "zipme");
        Directory.CreateDirectory(staged);
        Directory.Move(Path.Combine(_dir, "sv"), Path.Combine(staged, "Stormvault-main"));
        string zip = Path.Combine(_dir, "sv.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(staged, zip);

        string output = Path.Combine(_dir, StormvaultCatalog.FileName);
        Assert.That(StormvaultCatalog.BuildFromZip(zip, output), Is.EqualTo(1));
        Assert.That(CatalogueFactions.ByFaction(ForceOrgCatalog.Load(output)).Keys, Is.EqualTo(new[] { "Ogor Mawtribes" }));
    }

    [Test]
    public void LoadAll_MergesBothCatalogues_WhenBothAreThere()
    {
        WriteFaction("Ogor Mawtribes.json", Model("Ogor Glutton", "http://a/glutton1.obj"));
        string sv = Path.Combine(_dir, StormvaultCatalog.FileName);
        StormvaultCatalog.Build(Path.Combine(_dir, "sv"), sv);
        string fo = Path.Combine(_dir, ForceOrgCatalog.FileName);
        File.WriteAllText(fo, JsonConvert.SerializeObject(new
        {
            source = "ForceOrg (Tabletop Simulator workshop 3753014527)",
            groups = new Dictionary<int, string> { [1] = "tyranid" },
            models = new[] { new { id = 7, name = "Hormagaunt", group = 1, parts = new[] { new { mesh = "m", texture = "t" } } } },
        }));

        var all = ForceOrgCatalog.LoadAll(fo, sv)!;

        Assert.That(all.ById, Has.Count.EqualTo(2));
        var factions = CatalogueFactions.ByFaction(all).Keys.ToList();
        Assert.That(factions, Does.Contain("Tyranids").And.Contain("Ogor Mawtribes"));
        Assert.That(ForceOrgCatalog.LoadAll(Path.Combine(_dir, "none.json"), Path.Combine(_dir, "none2.json")), Is.Null);
    }

    [Test]
    public void TheGamesOwnUnitList_HasAgeOfFantasyUnits_AndTheirFactionPairs()
    {
        var map = UnitModelMap.Load();
        var guardians = map.Units.Single(u => u.Book == "Eternal Wardens" && u.Unit == "Guardians");
        Assert.That(guardians.Models.Select(m => m.Name), Does.Contain("Vindictors"));
        Assert.That(guardians.Models.All(m => m.Id >= StormvaultCatalog.IdBase));
        Assert.That(guardians.AlsoIn.Select(a => a.System), Does.Contain("AoF Skirmish"));
        Assert.That(map.AosFactions["Ogres"], Is.EqualTo(new[] { "Ogor Mawtribes" }));
        Assert.That(map.Units.Any(u => u.Book == "Alien Hives"), "Grimdark Future's list is still there");
    }

    /// <summary>Kai, 2026-11-14: every Age of Fantasy army gets its models, not just Eternal Wardens (hand matches,
    /// tools/unit-models/aof-hand-matches.tsv - the wiki can't be read from a cloud session).</summary>
    [Test]
    public void EveryAgeOfFantasyArmyWithAnAosCounterpart_HasUnitsWithModels()
    {
        var map = UnitModelMap.Load();
        foreach (string book in map.AosFactions.Keys.Where(b => b != "Deep-Sea Elves"))
            Assert.That(map.Units.Count(u => u.Book == book && u.Models.Count > 0), Is.GreaterThanOrEqualTo(4), book);
        var ardboys = map.Units.Single(u => u.Book == "Orcs" && u.Unit == "Iron Warriors");
        Assert.That(ardboys.Models.Select(m => m.Name), Has.All.EqualTo("Orruk Ardboy"));
        Assert.That(map.Units.Single(u => u.Book == "Giant Tribes War Disciples" && u.Unit == "War Battle Stomper Mega-Giant")
            .Models.Select(m => m.Name), Has.All.EqualTo("Warstomper Mega-Gargant"), "the Giant Tribes rows cover the four Disciples books");
        // Eight book names (Change Disciples, the four Daemons of X...) are shared with Grimdark Future, and some of their
        // units too - the same daemons, the same Warhammer models - so those folders hold both games' models. The rest
        // are Stormvault's alone.
        var gfBooks = UnitModelMap.Load(Path.Combine(AppAssets.AssetsFolder, UnitModelMap.RelativePath)).Books.ToHashSet();
        Assert.That(map.Units.Where(u => map.AosFactions.ContainsKey(u.Book) && !gfBooks.Contains(u.Book))
            .SelectMany(u => u.Models).All(m => m.Id >= StormvaultCatalog.IdBase));
    }

    [Test]
    public void MatchModels_ListsAgeOfFantasyArmies_AndTheirSkirmishNames()
    {
        Assert.That(GfBookUnits.LoadAll().Keys, Does.Contain("Eternal Wardens").And.Contain("Alien Hives"));
        Assert.That(GfBookUnits.IsAof("Ogres"), Is.True);
        Assert.That(GfBookUnits.IsAof("Alien Hives"), Is.False);
        var aliases = GfBookUnits.AliasesFor("Eternal Wardens", "Guardians", GfBookUnits.LoadOtherGames());
        Assert.That(aliases.Select(a => (a.System, a.Unit)), Does.Contain(("AoF Skirmish", "Guardian")));
    }
}
