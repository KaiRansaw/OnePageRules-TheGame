using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The official OPR unit pictures (Kai, 2026-11-14: "start fresh and download the official OPR model pictures"): the
/// file rules the Asset Lab's downloader keeps - an existing army folder is reused, a picture already there is never
/// replaced, every download is git-ignored, and the model-made portraits can be cleared.
/// </summary>
[TestFixture]
public class OfficialPicturesTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_dir = Path.Combine(Path.GetTempPath(), "fdg-pics-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Test]
    public void ArmyFolder_ReusesAFolderSpelledDifferently()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "alien-hives"));
        Assert.That(Path.GetFileName(OfficialPictures.ArmyFolder(_dir, "Alien Hives")), Is.EqualTo("alien-hives"));
        Assert.That(Path.GetFileName(OfficialPictures.ArmyFolder(_dir, "Battle Brothers")), Is.EqualTo("Battle Brothers"));
    }

    [Test]
    public void HasPicture_MatchesAnySpellingAndFormat()
    {
        File.WriteAllText(Path.Combine(_dir, "assault_grunts.JPG"), "x");
        Assert.That(OfficialPictures.HasPicture(_dir, "Assault Grunts"), Is.True);
        Assert.That(OfficialPictures.HasPicture(_dir, "Assault Grunt"), Is.False);
        File.WriteAllText(Path.Combine(_dir, "hive lord.txt"), "x");
        Assert.That(OfficialPictures.HasPicture(_dir, "Hive Lord"), Is.False, "not a picture");
    }

    [Test]
    public void GitIgnore_ListsEachFileOnce_EscapingSpecials()
    {
        OfficialPictures.AddGitIgnore(_dir, "Hive Lord.png");
        OfficialPictures.AddGitIgnore(_dir, "Hive Lord.png");
        OfficialPictures.AddGitIgnore(_dir, "Big [Guns]!.png");
        string[] lines = File.ReadAllLines(Path.Combine(_dir, ".gitignore"));
        Assert.That(lines[0], Does.StartWith("#"));
        Assert.That(lines.Count(l => l == "/Hive Lord.png"), Is.EqualTo(1));
        Assert.That(lines, Does.Contain(@"/Big \[Guns\]\!.png"));
    }

    [Test]
    public void SafeName_DropsCharactersFilesCantHave()
    {
        Assert.That(OfficialPictures.SafeName(" Grunt/Veteran: \"Boss\". "), Is.EqualTo("GruntVeteran Boss"));
    }

    [Test]
    public void DeleteModelPortraits_ClearsTheWholeFolder()
    {
        string made = Path.Combine(_dir, "_Portraits");
        Directory.CreateDirectory(Path.Combine(made, "Alien Hives", "Hive Lord"));
        File.WriteAllText(Path.Combine(made, "Alien Hives", "Hive Lord", "a.png"), "x");
        File.WriteAllText(Path.Combine(made, "Alien Hives", "Grunts.png"), "x");
        Assert.That(OfficialPictures.DeleteModelPortraits(made), Is.EqualTo(2));
        Assert.That(Directory.Exists(made), Is.False);
        Assert.That(OfficialPictures.DeleteModelPortraits(made), Is.EqualTo(0));
    }

    [Test]
    public void Games_CoverAllSixSystems_QuestFetchesHeroesAndEnemies()
    {
        Assert.That(OfficialPictures.Games.SelectMany(g => g.Systems).Select(s => s.Id).OrderBy(i => i), Is.EqualTo(new[] { 2, 3, 4, 5, 7, 8, 9, 10 }));
        Assert.That(OfficialPictures.Games.Single(g => g.Name == "GF: Star Quest").Systems.Select(s => s.Id), Is.EquivalentTo(new[] { 9, 10 }));
    }
}
