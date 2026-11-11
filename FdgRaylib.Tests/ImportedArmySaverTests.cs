using System.IO;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Import;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The Godot import popup's file side: which folder a list goes in, and never overwriting a list.</summary>
public class ImportedArmySaverTests
{
    [Test]
    public void TheFolder_FollowsTheArmysOwnGame()
    {
        Assert.That(ImportedArmySaver.SystemOf(new ArmyListFile { GameSystem = "grimdark-future-firefight" }), Is.EqualTo(EGameSystem.GFFirefight));
        Assert.That(ImportedArmySaver.SystemOf(new ArmyListFile { GameSystem = "age-of-fantasy" }), Is.EqualTo(EGameSystem.AgeOfFantasy));
        Assert.That(ImportedArmySaver.SystemOf(new ArmyListFile { GameSystem = null }), Is.EqualTo(EGameSystem.GrimdarkFuture));
    }

    [Test]
    public void FileNames_DropIllegalCharacters_AndBlankGetsAName()
    {
        Assert.That(ImportedArmySaver.SafeFileName("Orcs: 2k / test?"), Is.EqualTo("Orcs 2k  test"));
        Assert.That(ImportedArmySaver.SafeFileName("  "), Is.EqualTo("Imported Army"));
    }

    [Test]
    public void ASecondImport_GetsANumber_InsteadOfOverwriting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fdg-import-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            string ext = ArmyListFile.EXTENSION_WITH_PERIOD;
            Assert.That(Path.GetFileName(ImportedArmySaver.FreePath(dir, "Elves")), Is.EqualTo("Elves" + ext));
            File.WriteAllText(Path.Combine(dir, "Elves" + ext), "{}");
            Assert.That(Path.GetFileName(ImportedArmySaver.FreePath(dir, "Elves")), Is.EqualTo("Elves (2)" + ext));
            File.WriteAllText(Path.Combine(dir, "Elves (2)" + ext), "{}");
            Assert.That(Path.GetFileName(ImportedArmySaver.FreePath(dir, "Elves")), Is.EqualTo("Elves (3)" + ext));
        }
        finally { Directory.Delete(dir, true); }
    }
}
