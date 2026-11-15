using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Another player's model picks (Kai, 2026-11-14): sent as paths inside UnitModels, used only when this
/// computer has the file, and never followed outside the folder.</summary>
[TestFixture]
public class SharedModelsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "shared-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Guard", "Riflemen"));
        Directory.CreateDirectory(Path.Combine(_root, "Other", "Shooters"));
        File.WriteAllText(Path.Combine(_root, "Guard", "Riflemen", "v1.glb"), "x");
        File.WriteAllText(Path.Combine(_root, "Other", "Shooters", "moved.glb"), "x");
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Test]
    public void Relative_IsSlashSeparated_AndNullOutsideTheFolder()
    {
        Assert.That(SharedModels.Relative(Path.Combine(_root, "Guard", "Riflemen", "v1.glb"), _root), Is.EqualTo("Guard/Riflemen/v1.glb"));
        Assert.That(SharedModels.Relative(Path.Combine(Path.GetTempPath(), "elsewhere.glb"), _root), Is.Null);
    }

    [Test]
    public void Local_FindsTheSameFile_OrOneByTheSameName()
    {
        var lib = UnitModelLibrary.Scan(_root);
        Assert.That(SharedModels.Local("Guard/Riflemen/v1.glb", lib, _root), Is.EqualTo(Path.Combine(_root, "Guard", "Riflemen", "v1.glb")));
        Assert.That(SharedModels.Local("TheirName/Shooters/moved.glb", lib, _root), Is.EqualTo(Path.Combine(_root, "Other", "Shooters", "moved.glb")),
            "a different folder name here: found by file name");
        Assert.That(SharedModels.Local("Guard/Riflemen/missing.glb", lib, _root), Is.Null, "not on this computer: their pick isn't used");
    }

    [TestCase("../secret.glb")]
    [TestCase("Guard/../../x.glb")]
    [TestCase("C:/Windows/x.glb")]
    [TestCase("Guard/Riflemen/notes.txt")]
    [TestCase("")]
    public void Local_NeverLeavesTheFolder_OrOpensNonModels(string rel)
    {
        Assert.That(SharedModels.Local(rel, UnitModelLibrary.Scan(_root), _root), Is.Null);
    }
}
