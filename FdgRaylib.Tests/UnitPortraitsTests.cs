using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Portraits made from downloaded models (Kai, 2026-11-12): one per model version, so a unit shows the look picked for
/// it - found by the same rule the table uses - saved under UnitModels/_Portraits where the library and the lab don't read it.
/// </summary>
[TestFixture]
public class UnitPortraitsTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-portraits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Touch(params string[] parts)
    {
        string p = Path.Combine(new[] { _dir }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Test]
    public void PathFor_MirrorsBookAndUnitUnderPortraitsFolder()
    {
        string model = Touch("Alien Hives", "Hive Lord", "Swarmlord (102).glb");
        Assert.That(UnitPortraits.PathFor(model, _dir),
            Is.EqualTo(Path.Combine(_dir, "_Portraits", "Alien Hives", "Hive Lord", "Swarmlord (102).png")));
    }

    [Test]
    public void Models_ListsEveryVersion_OrFirstOnlyAsTheLibraryDoes()
    {
        Touch("Alien Hives", "Hive Lord", "Swarmlord (102).glb");
        Touch("Alien Hives", "Hive Lord", "Hive Tyrant (2827).glb");
        Touch("Alien Hives", "Empty Unit", "notes.txt");
        Touch("_cache", "Junk", "part.glb");

        Assert.That(UnitPortraits.Models(root: _dir), Has.Count.EqualTo(2), "a folder with no model, and _ folders, are skipped");
        var first = UnitPortraits.Models(firstOnly: true, root: _dir);
        Assert.That(first.Select(m => Path.GetFileName(m.Model)), Is.EqualTo(new[] { "Hive Tyrant (2827).glb" }));
        var lib = UnitModelLibrary.Scan(_dir);
        Assert.That(UnitPortraits.ModelFor(lib, "Alien Hives", "Hive Lord", null), Is.EqualTo(first[0].Model),
            "no look = the same first version the table stands up");
    }

    [Test]
    public void ModelFor_FollowsThePickedLook_LikeTheTable()
    {
        string tyrant = Touch("Alien Hives", "Hive Lord", "Hive Tyrant (2827).glb");
        string swarm = Touch("Alien Hives", "Hive Lord", "Swarmlord (102).glb");
        string vet = Touch("Alien Hives", "Prime Warrior", "Winged Prime (154).glb");
        var lib = UnitModelLibrary.Scan(_dir);
        Assert.That(UnitPortraits.ModelFor(lib, "Alien Hives", "Hive Lord", "Swarmlord (102).glb"), Is.EqualTo(swarm));
        Assert.That(UnitPortraits.ModelFor(lib, "Alien Hives", "Hive Lord", "not-downloaded.glb"), Is.EqualTo(tyrant),
            "a look this player doesn't have = the first version");
        Assert.That(UnitPortraits.ModelFor(lib, "Alien Hives", "Krrek", "Alien Hives/Prime Warrior/"), Is.EqualTo(vet),
            "a Quest hero's folder look");
        Assert.That(UnitPortraits.LookOf(new string?[] { null, "Swarmlord (102).glb" }), Is.EqualTo("Swarmlord (102).glb"));
        Assert.That(UnitPortraits.LookOf(null), Is.Null);
    }

    [Test]
    public void MadePortraits_AreNotListedAsAssetsOrArmies()
    {
        Touch("Alien Hives", "Hive Lord", "Hive Tyrant (2827).glb");
        Touch("_Portraits", "Alien Hives", "Hive Lord.png");
        Assert.That(UnitModelPaths.Browse(_dir).Any(f => f.Contains("_Portraits")), Is.False);
        Assert.That(UnitModelLibrary.Scan(_dir).FileCount, Is.EqualTo(1));
    }
}
