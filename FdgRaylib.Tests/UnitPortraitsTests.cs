using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Portraits made from downloaded models (Kai, 2026-11-12): one per unit, of the unit's first model file (the one the
/// table falls back to), saved under UnitModels/_Portraits where the model library and the Asset Lab don't read it.
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
            Is.EqualTo(Path.Combine(_dir, "_Portraits", "Alien Hives", "Hive Lord.png")));
    }

    [Test]
    public void Units_TakesTheFirstModelAlphabetically_AsTheLibraryDoes()
    {
        Touch("Alien Hives", "Hive Lord", "Swarmlord (102).glb");
        Touch("Alien Hives", "Hive Lord", "Hive Tyrant (2827).glb");
        Touch("Alien Hives", "Empty Unit", "notes.txt");
        Touch("_cache", "Junk", "part.glb");

        var units = UnitPortraits.Units(_dir);
        Assert.That(units, Has.Count.EqualTo(1), "a folder with no model, and _ folders, are skipped");
        Assert.That(Path.GetFileName(units[0].Model), Is.EqualTo("Hive Tyrant (2827).glb"));

        var lib = UnitModelLibrary.Scan(_dir);
        Assert.That(UnitPortraits.SubjectFor(lib, "Alien Hives", "Hive Lord"), Is.EqualTo(units[0].Model),
            "the portrait is of the same file the table falls back to");
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
