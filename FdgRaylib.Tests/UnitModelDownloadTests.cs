using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Asset Lab's "Download models": the shipped unit list (which catalogue models stand for each GF unit), file
/// naming, and the downloader's cache. The Godot side (putting parts together, .glb export) is checked by a scripted
/// run against a test catalogue; this covers what has no drawing in it.
/// </summary>
[TestFixture]
public class UnitModelDownloadTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-modeldl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string ShippedMap =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "FdgRaylib", "Assets", UnitModelMap.RelativePath);

    /// <summary>Kai, 2026-11-14: every Stormvault (Age of Fantasy) download failed with 403 - its links use Steam's
    /// retired cloud-3 server. The fetch goes to the current one; the link itself (ids, cache) is untouched.</summary>
    [TestCase("http://cloud-3.steamusercontent.com/ugc/1861688073981353330/27883B7913E0E69143C3AA03493B7E7C4E8C29C0/",
        "https://steamusercontent-a.akamaihd.net/ugc/1861688073981353330/27883B7913E0E69143C3AA03493B7E7C4E8C29C0/")]
    [TestCase("https://cloud-3.steamusercontent.com/ugc/1/AB/", "https://steamusercontent-a.akamaihd.net/ugc/1/AB/")]
    [TestCase("https://steamusercontent-a.akamaihd.net/ugc/1/AB/", "https://steamusercontent-a.akamaihd.net/ugc/1/AB/")]
    [TestCase("https://i.imgur.com/abc.png", "https://i.imgur.com/abc.png")]
    public void OldSteamLinks_AreFetchedFromSteamsCurrentServer(string link, string fetched)
    {
        Assert.That(ModelDownloader.FetchAddress(link), Is.EqualTo(fetched));
    }

    [Test]
    public void ShippedMap_LoadsAndNamesRealGfUnits()
    {
        var map = UnitModelMap.Load(ShippedMap);
        Assert.That(map.Units, Has.Count.GreaterThan(400));
        var grunts = map.Units.Single(u => u.Book == "Alien Hives" && u.Unit == "Assault Grunts");
        Assert.That(grunts.Models.Select(m => m.Name), Has.All.Contains("Hormagaunt"));
        Assert.That(grunts.AlsoIn.Select(a => a.Unit), Does.Contain("Assault Grunt"), "Star Quest's singular name");

        // Every unit named must exist in the current GF book of that name - the map names units the game knows.
        string books = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "FdgRaylib", "Assets", "Books", "Grimdark Future");
        var bookUnits = Directory.GetFiles(books, "*.fdgbook")
            .Select(f => Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(f)))
            .ToDictionary(j => (string)j["name"]!, j => j["units"]!.Select(u => (string)u["name"]!).ToHashSet());
        foreach (var e in map.Units)
        {
            Assert.That(bookUnits.ContainsKey(e.Book), Is.True, $"no GF book named {e.Book}");
            Assert.That(bookUnits[e.Book], Does.Contain(e.Unit), $"{e.Book} has no unit {e.Unit}");
            Assert.That(e.Models, Is.Not.Empty, $"{e.Book} - {e.Unit} lists no model");
        }
    }

    [Test]
    public void ShippedMap_CarriesNoDownloadLinks()
    {
        // The catalogue's links are personal-use community uploads and stay on the player's computer.
        Assert.That(File.ReadAllText(ShippedMap), Does.Not.Contain("http"));
    }

    [Test]
    public void FileFor_IsOprNamedAndStable()
    {
        string p = UnitModelMap.FileFor("R", "Alien Hives", "Assault Grunts", new UnitModelMap.ModelRef { Id = 45, Name = "Hormagaunt" });
        Assert.That(p, Is.EqualTo(Path.Combine("R", "Alien Hives", "Assault Grunts", "Hormagaunt (45).glb")));
        Assert.That(UnitModelMap.SafeName("Watch Captain Brother w/ Spear"), Is.EqualTo("Watch Captain Brother w Spear"));
        Assert.That(UnitModelMap.SafeName("3/3 \"Heavy\" Intercessor?."), Is.EqualTo("3 3 Heavy Intercessor"));
    }

    [Test]
    public async Task Downloader_CachesOncePerLink_AndReportsFailures()
    {
        string src = Path.Combine(_dir, "part.obj");
        File.WriteAllText(src, "v 0 0 0\n");
        using var dl = new ModelDownloader(Path.Combine(_dir, "_cache"));
        var model = new ForceOrgCatalog.Model
        {
            Id = 1, Name = "M",
            Parts =
            {
                new ForceOrgCatalog.Part { Mesh = "file://" + src },
                new ForceOrgCatalog.Part { Mesh = "file://" + src, Texture = "file://" + Path.Combine(_dir, "missing.png"), Parent = 0 },
            },
        };
        var (files, failed) = await dl.FetchModelAsync(model, CancellationToken.None);
        Assert.That(files, Has.Count.EqualTo(1), "the shared mesh is fetched once");
        Assert.That(failed, Has.Count.EqualTo(1).And.Some.StartsWith("skin"));
        Assert.That(File.ReadAllText(files["file://" + src]), Is.EqualTo("v 0 0 0\n"));

        // A second run uses the cache even when the source has gone.
        File.Delete(src);
        var (again, _) = await dl.FetchModelAsync(model, CancellationToken.None);
        Assert.That(again, Has.Count.EqualTo(1));
    }

    [Test]
    public void LinkOrCopy_GivesTheSameFileUnderASecondName()
    {
        string a = Path.Combine(_dir, "Battle Brothers", "Destroyers", "Terminator (9).glb");
        string b = Path.Combine(_dir, "Blood Brothers", "Blood Destroyers", "Terminator (9).glb");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        File.WriteAllText(a, "model");
        FileLinks.LinkOrCopy(a, b);
        Assert.That(File.ReadAllText(b), Is.EqualTo("model"));
        File.Delete(a);
        Assert.That(File.ReadAllText(b), Is.EqualTo("model"), "each name stands on its own");
    }

    [Test]
    public void Library_FindsAUnitsModels_ByArmyNameAndOtherGamesNames()
    {
        string grunts = Path.Combine(_dir, "Alien Hives", "Assault Grunts");
        string blood = Path.Combine(_dir, "Blood Brothers", "Blood Destroyers");
        Directory.CreateDirectory(grunts);
        Directory.CreateDirectory(blood);
        File.WriteAllText(Path.Combine(grunts, "Hormagaunt (46).glb"), "x");
        File.WriteAllText(Path.Combine(grunts, "Hormagaunt (45).glb"), "x");
        File.WriteAllText(Path.Combine(blood, "Terminator (9).glb"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "_cache"));
        var map = new UnitModelMap();
        map.Units.Add(new UnitModelMap.Entry { Book = "Alien Hives", Unit = "Assault Grunts", AlsoIn = { new UnitModelMap.Alias { System = "GF Star Quest", Unit = "Assault Grunt" } } });

        var lib = UnitModelLibrary.Scan(_dir, map);
        Assert.That(lib.FileCount, Is.EqualTo(3));
        Assert.That(lib.FilesFor("Alien Hives", "Assault Grunts").Select(Path.GetFileName), Is.EqualTo(new[] { "Hormagaunt (45).glb", "Hormagaunt (46).glb" }));
        Assert.That(lib.FilesFor("alien hives", "Assault Grunt"), Has.Count.EqualTo(2), "Star Quest's singular name");
        Assert.That(lib.FilesFor("Battle Brothers", "Blood Destroyers"), Has.Count.EqualTo(1), "an allied unit from another book");
        Assert.That(lib.FilesFor("Alien Hives", "Hive Lord"), Is.Empty, "no download = the token");
    }

    [Test]
    public void Catalogue_LoadsPartsAndIds()
    {
        string path = Path.Combine(_dir, ForceOrgCatalog.FileName);
        File.WriteAllText(path, "{\"source\":\"s\",\"models\":[{\"id\":7,\"name\":\"X\",\"group\":1,\"parts\":[{\"mesh\":\"a\",\"texture\":\"\",\"color\":[1,1,1],\"pos\":[1,2,3],\"rot\":[0,90,0],\"scale\":[1,1,1],\"parent\":-1}]}]}");
        var cat = ForceOrgCatalog.Load(path);
        Assert.That(cat.ById[7].Parts[0].Pos, Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(cat.ById[7].Parts[0].Parent, Is.EqualTo(-1));
    }

    [Test]
    public void Matches_GiveAndTakeAway_AndLayOverTheList()
    {
        var map = UnitModelMap.Load(ShippedMap);
        int grunts = map.Units.Single(u => u.Book == "Alien Hives" && u.Unit == "Assault Grunts").Models.Count;

        var m = new ModelMatches();
        m.Assign("Alien Hives", "Hive Lord", new UnitModelMap.ModelRef { Id = 9001, Name = "Swarmlord" });
        m.Unassign("Alien Hives", "Assault Grunts", 45);
        m.Assign("Alien Hives", "Assault Grunts", new UnitModelMap.ModelRef { Id = 9002, Name = "Termagant" });
        string path = Path.Combine(_dir, ModelMatches.FileName);
        m.Save(path);
        Assert.That(ModelMatches.Load(path).Changes, Is.EqualTo(3));

        // A list read from a given path is left as it is; the game's own list gets this computer's matches.
        string mapCopy = Path.Combine(_dir, "map.json");
        File.Copy(ShippedMap, mapCopy);
        Assert.That(UnitModelMap.Load(mapCopy).Units.Any(u => u.Unit == "Hive Lord" && u.Book == "Alien Hives"), Is.EqualTo(map.Units.Any(u => u.Unit == "Hive Lord" && u.Book == "Alien Hives")));

        ModelMatches.Load(path).ApplyTo(map);
        ModelMatches.Load(path).ApplyTo(map); // twice changes nothing
        var lord = map.Units.Single(u => u.Book == "Alien Hives" && u.Unit == "Hive Lord");
        Assert.That(lord.Models.Count(x => x.Id == 9001), Is.EqualTo(1));
        var g = map.Units.Single(u => u.Book == "Alien Hives" && u.Unit == "Assault Grunts");
        Assert.That(g.Models.Select(x => x.Id), Does.Not.Contain(45));
        Assert.That(g.Models.Select(x => x.Id), Does.Contain(9002));
        Assert.That(g.Models, Has.Count.EqualTo(grunts));

        // Giving a removed model back takes it off the remove list; removing an added one takes it off the add list.
        m.Assign("Alien Hives", "Assault Grunts", new UnitModelMap.ModelRef { Id = 45, Name = "Hormagaunt" });
        m.Unassign("Alien Hives", "Hive Lord", 9001);
        var gm = m.Units.Single(u => u.Unit == "Assault Grunts");
        Assert.That(gm.Remove, Is.Empty);
        Assert.That(m.Units.Single(u => u.Unit == "Hive Lord").Add, Is.Empty);
    }

    [Test]
    public void Factions_NameTheModsFolders_AndMergeTwoOfOneFaction()
    {
        string path = Path.Combine(_dir, ForceOrgCatalog.FileName);
        File.WriteAllText(path, "{\"source\":\"ForceOrg (Tabletop Simulator workshop 3753014527)\",\"groups\":{\"1\":\"warrior, tyranid\",\"21\":\"raveners, hive\",\"99\":\"new, stuff\"},"
            + "\"models\":[{\"id\":1,\"name\":\"Warrior\",\"group\":1,\"parts\":[]},{\"id\":2,\"name\":\"Ravener\",\"group\":21,\"parts\":[]},{\"id\":3,\"name\":\"Thing\",\"group\":99,\"parts\":[]}]}");
        var cat = ForceOrgCatalog.Load(path);
        var by = CatalogueFactions.ByFaction(cat);
        Assert.That(by["Tyranids"].Select(m => m.Name), Is.EqualTo(new[] { "Ravener", "Warrior" }));
        Assert.That(by.Keys, Does.Contain("Other: new, stuff"), "an unknown folder is listed, never hidden");
    }

    [Test]
    public void BookUnits_ListEveryGfUnit_IncludingOnesWithNoModel()
    {
        string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "FdgRaylib", "Assets", "Books", "Grimdark Future");
        var books = GfBookUnits.Load(folder);
        Assert.That(books.Count, Is.GreaterThan(40));
        Assert.That(books["Alien Hives"], Does.Contain("Assault Grunts"));
        Assert.That(books["Alien Hives"], Does.Contain("Hive Lord"));
    }

    [Test]
    public void HandMatch_FindsTheUnitsNameInFirefightAndStarQuest()
    {
        string books = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "FdgRaylib", "Assets", "Books");
        var others = GfBookUnits.LoadOtherGames(books);
        var a = GfBookUnits.AliasesFor("Alien Hives", "Assault Grunts", others);
        Assert.That(a.Single(x => x.System == "GF Star Quest").Unit, Is.EqualTo("Assault Grunt"), "Star Quest's singular");
        Assert.That(a.Single(x => x.System == "GF Firefight").Unit, Is.EqualTo("Assault Grunts"));

        // Saved with the match and laid over the list, so the table finds the GF unit's models from a Star Quest army.
        var m = new ModelMatches();
        m.Assign("Alien Hives", "Made-up Unit", new UnitModelMap.ModelRef { Id = 9003, Name = "X" },
            new[] { new UnitModelMap.Alias { System = "GF Star Quest", Unit = "Made-up Thing" } });
        string path = Path.Combine(_dir, ModelMatches.FileName);
        m.Save(path);
        var map = new UnitModelMap();
        ModelMatches.Load(path).ApplyTo(map);
        Assert.That(map.Units.Single().AlsoIn.Single().Unit, Is.EqualTo("Made-up Thing"));

        string unitDir = Path.Combine(_dir, "lib", "Alien Hives", "Made-up Unit");
        Directory.CreateDirectory(unitDir);
        File.WriteAllText(Path.Combine(unitDir, "X (9003).glb"), "x");
        Assert.That(UnitModelLibrary.Scan(Path.Combine(_dir, "lib"), map).FilesFor("Alien Hives", "Made-up Thing"), Has.Count.EqualTo(1));
    }
}
