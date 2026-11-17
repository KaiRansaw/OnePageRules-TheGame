using FDG;
using FdgRaylib.Pictures;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai's menu pictures (2026-11-16, "Menu Art &amp; Themes Mockup"): the picture store, Army Forge's book groups for the
/// army picture grid, and the quick book index behind both.
/// </summary>
[TestFixture]
public class MenuPicturesTests
{
    private string _dir = "";
    private string? _was;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "menu-pictures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _was = null;
        MenuPictures.Folder = Path.Combine(_dir, "Pictures");
    }

    [TearDown]
    public void TearDown()
    {
        MenuPictures.Folder = Path.Combine(AppAssets.AssetsFolder, "Pictures");
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        _ = _was;
    }

    private string Png(string name)
    {
        // Any bytes do for the store - it copies files and never decodes them.
        string p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        return p;
    }

    [Test]
    public void AnArmyHasOnePictureAcrossItsFamilysThreeGames()
    {
        // Kai: "one picture per army, across its three games" - Orcs in AoF, Skirmish and Quest.
        Assert.That(MenuPictures.Army(EGameSystem.AgeOfFantasy, "Orcs"), Is.EqualTo(MenuPictures.Army(EGameSystem.AoFSkirmish, "Orcs")));
        Assert.That(MenuPictures.Army(EGameSystem.AoFQuest, "Orcs").Key, Is.EqualTo("army/aof/Orcs"));
        Assert.That(MenuPictures.Army(EGameSystem.GFFirefight, "Alien Hives").Key, Is.EqualTo("army/gf/Alien Hives"));
        Assert.That(MenuPictures.Army(EGameSystem.GrimdarkFuture, "Orcs"), Is.Not.EqualTo(MenuPictures.Army(EGameSystem.AgeOfFantasy, "Orcs")));
    }

    [Test]
    public void SettingAPictureKeepsAGameCopyAndSurvivesARead()
    {
        var slot = MenuPictures.Army(MenuPictures.EFamily.AgeOfFantasy, "Orcs");
        Assert.That(MenuPictures.Get(slot), Is.Null);
        string original = Png("my orcs.png");
        MenuPictures.SetFromFile(slot, original);
        File.Delete(original); // the game's copy doesn't care
        MenuPictures.SetFraming(slot, 0.2f, 0.9f, 1.5f);

        MenuPictures.Reload();
        var e = MenuPictures.Get(slot);
        Assert.That(e, Is.Not.Null);
        Assert.That(File.Exists(MenuPictures.PathOf(e!)), Is.True);
        Assert.That(e!.File, Does.StartWith("army-aof-orcs-").And.EndWith(".png"));
        Assert.That((e.FocusX, e.FocusY, e.Zoom), Is.EqualTo((0.2f, 0.9f, 1.5f)));
    }

    [Test]
    public void ReplacingAndRemovingDeleteTheOldCopy()
    {
        var slot = MenuPictures.Backdrop(MenuPictures.EFamily.GrimdarkFuture);
        var first = MenuPictures.SetFromFile(slot, Png("a.png"));
        string firstPath = MenuPictures.PathOf(first);
        Thread.Sleep(1100); // a new name each time (to the second)
        MenuPictures.SetFromBytes(slot, new byte[] { 1, 2, 3 });
        Assert.That(File.Exists(firstPath), Is.False, "the replaced copy is deleted");
        string second = MenuPictures.PathOf(MenuPictures.Get(slot)!);
        MenuPictures.Remove(slot);
        Assert.That(MenuPictures.Get(slot), Is.Null);
        Assert.That(File.Exists(second), Is.False);
    }

    [Test]
    public void OnlyPictureFilesAreTaken()
    {
        var slot = MenuPictures.GameTile(EGameSystem.AoFQuest);
        string txt = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(txt, "x");
        Assert.Throws<InvalidOperationException>(() => MenuPictures.SetFromFile(slot, txt));
        Assert.That(MenuPictures.Get(slot), Is.Null);
    }

    [Test]
    public void FramingIsKeptInRange()
    {
        var slot = MenuPictures.GameTile(EGameSystem.GrimdarkFuture);
        MenuPictures.SetFromFile(slot, Png("t.png"));
        MenuPictures.SetFraming(slot, -1, 7, 99);
        var e = MenuPictures.Get(slot)!;
        Assert.That((e.FocusX, e.FocusY, e.Zoom), Is.EqualTo((0f, 1f, 4f)));
    }

    [Test]
    public void ArmyGroupsFollowArmyForge()
    {
        // From Army Forge's own data (race-groups.json): AoF's Orcs and Goblins are Greenskins, Dwarves are Dwarves.
        Assert.That(ArmyGroups.GroupOf(EGameSystem.AgeOfFantasy, "Orcs"), Is.EqualTo("Greenskins"));
        Assert.That(ArmyGroups.GroupOf(EGameSystem.AgeOfFantasy, "Goblins"), Is.EqualTo("Greenskins"));
        Assert.That(ArmyGroups.GroupOf(EGameSystem.AgeOfFantasy, "Not a book"), Is.EqualTo(ArmyGroups.Unknown));
        var groups = ArmyGroups.Arrange(EGameSystem.AgeOfFantasy, new[] { "Orcs", "Dwarves", "Goblins", "Beastmen" });
        Assert.That(groups.Select(g => g.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), "groups in name order, as Army Forge shows them");
        Assert.That(groups.First(g => g.Name == "Greenskins").Books, Is.EqualTo(new[] { "Goblins", "Orcs" }), "Army Forge's own book order inside a group");
    }

    [Test]
    public void EveryBundledBookHasAnArmyForgeGroupEntry()
    {
        // Guards race-groups.json against a book refresh that adds or renames a book (re-run tools/army-groups).
        foreach (EGameSystem g in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest })
        {
            var books = BookIndex.Books(g);
            Assert.That(books, Is.Not.Empty, $"{g} has bundled books");
            var unlisted = books.Where(b => ArmyGroups.Arrange(g, new[] { b.Name }).Single().Name == ArmyGroups.Unknown
                                            && !RaceGroupsListBook(g, b.Name)).Select(b => b.Name).ToList();
            Assert.That(unlisted, Is.Empty, $"{g}: books missing from race-groups.json");
        }
    }

    private static bool RaceGroupsListBook(EGameSystem g, string book)
    {
        string json = File.ReadAllText(Path.Combine(AppAssets.AssetsFolder, "Books", "race-groups.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("systems").GetProperty(GameSystemInfo.Slug(g)).EnumerateArray()
            .Any(b => b.GetProperty("name").GetString() == book);
    }

    [Test]
    public void BookIndexReadsUnitNames()
    {
        var orcs = BookIndex.Books(EGameSystem.AgeOfFantasy).FirstOrDefault(b => b.Name == "Orcs");
        Assert.That(orcs, Is.Not.Null);
        Assert.That(orcs!.Units, Is.Not.Empty);
        Assert.That(BookIndex.ArmiesOf(MenuPictures.EFamily.AgeOfFantasy), Does.Contain("Orcs"));
        Assert.That(BookIndex.ArmiesOf(MenuPictures.EFamily.GrimdarkFuture), Does.Contain("Alien Hives").And.Not.Contain("Orcs"));
    }

    [Test]
    public void SafeNamesAreLettersDigitsAndDashes()
    {
        Assert.That(MenuPictures.SafeName("army/aof/Deep-Sea Elves"), Is.EqualTo("army-aof-deep-sea-elves"));
        Assert.That(MenuPictures.SafeName("game/age-of-fantasy-quest"), Is.EqualTo("game-age-of-fantasy-quest"));
    }
}
