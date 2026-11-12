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
    public void Catalogue_LoadsPartsAndIds()
    {
        string path = Path.Combine(_dir, ForceOrgCatalog.FileName);
        File.WriteAllText(path, "{\"source\":\"s\",\"models\":[{\"id\":7,\"name\":\"X\",\"group\":1,\"parts\":[{\"mesh\":\"a\",\"texture\":\"\",\"color\":[1,1,1],\"pos\":[1,2,3],\"rot\":[0,90,0],\"scale\":[1,1,1],\"parent\":-1}]}]}");
        var cat = ForceOrgCatalog.Load(path);
        Assert.That(cat.ById[7].Parts[0].Pos, Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(cat.ById[7].Parts[0].Parent, Is.EqualTo(-1));
    }
}
