using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Quest;
using FDG.Rules.Serialization;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// A Quest campaign chapter's enemy force "built from an army book": the whole book's roster at base model
/// count, built fresh from the shipped book when the mission is played. Both Quest systems, and the
/// checked-in collections under armies/ must still be what the shared builder produces.
/// </summary>
[TestFixture]
public class QuestEnemyFromBookTests
{
    private string? _outDir;

    [TearDown]
    public void Cleanup()
    {
        try { if (_outDir != null && Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [TestCase(EGameSystem.GFStarQuest, 50)]
    [TestCase(EGameSystem.AoFQuest, 40)]
    public void EachQuestSystemOffersItsOwnEnemyBooks(EGameSystem system, int atLeast)
    {
        var books = QuestEnemyCollections.BooksFor(system);
        Assert.That(books.Count, Is.GreaterThanOrEqualTo(atLeast),
            $"{system}: expected its own enemy books from Assets/Books/{QuestGameTypes.For(system)}.");
        Assert.That(books.All(b => b.Faction.Length > 0), Is.True);
        string prefix = system == EGameSystem.AoFQuest ? "AoFQ" : "GFSQ";
        Assert.That(books.All(b => b.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)), Is.True,
            "a Quest system must never be offered another system's books.");
    }

    [TestCase(EGameSystem.GFStarQuest)]
    [TestCase(EGameSystem.AoFQuest)]
    public void WritingFromABook_GivesTheWholeRoster(EGameSystem system)
    {
        QuestEnemyCollections.BookChoice choice = QuestEnemyCollections.BooksFor(system).First();
        _outDir = Path.Combine(Path.GetTempPath(), $"quest-enemy-{Guid.NewGuid():N}");

        string? path = QuestEnemyCollections.WriteFromBook(system, choice.FileName, _outDir);

        Assert.That(path, Is.Not.Null);
        BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(
            Path.Combine(QuestEnemyCollections.BooksFolderFor(system), choice.FileName)), RuleJson.Options)!;
        BuiltArmyFile army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path!), RuleJson.Options)!;
        Assert.That(army.Units.Count, Is.EqualTo(book.Units.Count), "one of every unit in the book");
        Assert.That(army.Name, Does.EndWith("enemy collection"));
    }

    [Test]
    public void AMissingBook_GivesNoArmy_SoTheLobbyPicks()
    {
        _outDir = Path.Combine(Path.GetTempPath(), $"quest-enemy-{Guid.NewGuid():N}");
        Assert.That(QuestEnemyCollections.WriteFromBook(EGameSystem.GFStarQuest, "no-such-book.fdgbook", _outDir), Is.Null);
    }

    /// <summary>The dev tool and the campaign share one builder; it must still produce the checked-in files.</summary>
    [Test]
    public void TheSharedBuilder_MatchesTheCheckedInCollections()
    {
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null, "needs the repo's armies folder");
        string folder = Path.Combine(ArmyPaths.FolderPath!, QuestGameTypes.StarQuest);
        QuestEnemyCollections.BookChoice choice = QuestEnemyCollections.BooksFor(EGameSystem.GFStarQuest)
            .First(b => File.Exists(Path.Combine(folder, $"{b.Faction} - enemy collection.fdgarmy")));
        _outDir = Path.Combine(Path.GetTempPath(), $"quest-enemy-{Guid.NewGuid():N}");

        string built = File.ReadAllText(QuestEnemyCollections.WriteFromBook(EGameSystem.GFStarQuest, choice.FileName, _outDir)!);
        BuiltArmyFile fresh = JsonSerializer.Deserialize<BuiltArmyFile>(built, RuleJson.Options)!;
        BuiltArmyFile shipped = JsonSerializer.Deserialize<BuiltArmyFile>(
            File.ReadAllText(Path.Combine(folder, $"{choice.Faction} - enemy collection.fdgarmy")), RuleJson.Options)!;
        Assert.That(fresh.Units.Select(u => u.Name), Is.EqualTo(shipped.Units.Select(u => u.Name)));
        Assert.That(fresh.TotalPoints, Is.EqualTo(shipped.TotalPoints));
    }

    [Test]
    public void AChaptersBookSurvivesCloneAndSave()
    {
        var chapter = new CampaignChapter { EnemyBook = "GFSQ-AlienHives.fdgbook", EnemyName = "Alien Hives (whole book)" };
        Assert.That(chapter.Clone().EnemyBook, Is.EqualTo("GFSQ-AlienHives.fdgbook"));

        string json = JsonSerializer.Serialize(chapter);
        Assert.That(JsonSerializer.Deserialize<CampaignChapter>(json)!.EnemyBook, Is.EqualTo("GFSQ-AlienHives.fdgbook"));
    }
}
