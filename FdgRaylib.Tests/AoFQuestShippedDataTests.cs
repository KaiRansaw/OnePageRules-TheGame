using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #417 - the AoF: Quest books and enemy collections shipped in FdgRaylib/Assets/Books/AoF Quest and
// armies/AoF Quest.
//
// WHAT THIS FIXTURE IS REALLY FOR. Army Forge serves AoF: Quest as TWO game-system ids under ONE index
// slug, and the two payloads are indistinguishable - same faction names, same shape, nothing in the
// response saying which you asked for:
//
//   id 7  the player's HERO roster   (Beastmen: 5 units, Ndoli Beast Lord 45pts Q6 D6)
//   id 8  the ENEMY army books       (Beastmen: 20 units, Ndoli Warrior 35pts Q4 D4)
//
// and the whole system also has a THIRD plausible source that is simply wrong: Age of Fantasy's own
// books (id 4), which is what the game served before this import existed. Every one of the three loads,
// compiles, deploys and plays. The only symptom of picking the wrong one is that the points buy the
// wrong amount of enemy, which nobody can see from inside a game.
//
// So the tests that matter here are not "the files exist" - they are the DISCRIMINATING ones: the shipped
// data must be measurably Quest-scaled and measurably not Age of Fantasy's. A re-import run against the
// wrong id fails here instead of shipping.
[TestFixture]
public class AoFQuestShippedDataTests
{
    private static string? ArmiesFolder =>
        ArmyPaths.FolderPath is { } root
            ? Path.Combine(root, ArmyPaths.FolderNameFor(EGameSystem.AoFQuest))
            : null;

    private static List<string> CollectionPaths()
    {
        string? dir = ArmiesFolder;
        Assert.That(dir, Is.Not.Null, "no armies folder found from the test binary");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir!, "*.fdgarmy").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    private static BuiltArmyFile LoadArmy(string path) =>
        JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;

    private static BookFile LoadBook(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    /// <summary>Every bundled book, read once per fixture run rather than once per test - the corpus is
    /// ~290 books and several tests walk all of them.</summary>
    private static readonly Lazy<List<BookFile>> AllBooks = new(() =>
        ShippedBooks.AllPaths().Select(LoadBook).ToList());

    private static List<BookFile> QuestBooks() =>
        AllBooks.Value.Where(b => GameSystems.SameSystem(b.GameSystem, GameSystems.AoFQuest)).ToList();

    private static List<BookFile> FantasyBooks() =>
        AllBooks.Value.Where(b => GameSystems.SameSystem(b.GameSystem, GameSystems.AgeOfFantasy)).ToList();

    // ── The books ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheQuestBooksAreShipped_AndStampedAsQuestBooks()
    {
        // The stamp is load-bearing twice over: the lobby's launch gate matches an army's system field
        // against the game type's ArmySource, and the Army Forge screen's system filter groups by it. A
        // book baked into this folder but stamped "age-of-fantasy" would show up as a Fantasy book and
        // be refused by an AoF: Quest lobby.
        List<BookFile> books = QuestBooks();

        Assert.That(books, Is.Not.Empty,
            "no AoF: Quest books shipped - re-run --rebuild-books with the age-of-fantasy-quest cache");
        Assert.That(books.Count, Is.GreaterThanOrEqualTo(40),
            $"only {books.Count} AoF: Quest books - the official index lists 48, two of which are empty "
            + "placeholders with no units, so a healthy bake is 46");
    }

    [Test]
    public void EveryQuestBook_HasUnitsThatCostPoints()
    {
        var broken = new List<string>();

        foreach (BookFile book in QuestBooks())
        {
            if (book.Units.Count == 0) broken.Add($"{book.Name}: no units");
            else if (book.Units.Any(u => u.BasePointCost <= 0))
                broken.Add($"{book.Name}: {book.Units.Count(u => u.BasePointCost <= 0)} free unit(s)");
        }

        Assert.That(broken, Is.Empty);
    }

    [Test]
    public void TheQuestBooksAreQuestScaled_NotAgeOfFantasysPoints()
    {
        // THE test this fixture exists for. OPR's Quest scale is roughly 3x its parent system's for the
        // same model, because a Quest game buys individual models against a party of heroes rather than
        // squads against an army. Measured on the shipped data, 2026-10-31:
        //
        //   Beastmen  Hakemba          AoF  40pts  ->  AoFQ  110pts
        //   Beastmen  Heavy Chariot    AoF 135pts  ->  AoFQ  395pts
        //   Dwarves   Berserker Lord   AoF  55pts  ->  AoFQ  140pts
        //   Orcs      Brute Orc Boss   AoF  95pts  ->  AoFQ  255pts
        //
        // If a re-import is ever pointed at Age of Fantasy's id (4) the two sides become identical and
        // this fails. A median is used rather than any one unit so that OPR re-pricing a single model
        // does not break the build.
        //
        // Checked PER BOOK as well as across the corpus, and that is not belt-and-braces: a
        // corpus-wide median shrugged off one wrong book when this was tried (31 of 46 wrong fails it
        // at 1.00x; one of 46 passes), and one wrong book is a realistic accident - a single re-fetch
        // of a faction OPR had just edited.
        var ratios = new List<double>();
        var identical = new List<string>();
        var wrongScale = new List<string>();

        foreach (BookFile quest in QuestBooks())
        {
            BookFile? fantasy = FantasyBooks().FirstOrDefault(b =>
                string.Equals(b.Name, quest.Name, StringComparison.OrdinalIgnoreCase));
            if (fantasy == null) continue;

            var bookRatios = new List<double>();
            foreach (RosterUnit qu in quest.Units)
            {
                RosterUnit? fu = fantasy.Units.FirstOrDefault(u =>
                    string.Equals(u.Name, qu.Name, StringComparison.OrdinalIgnoreCase));
                if (fu == null || fu.BasePointCost <= 0 || qu.BasePointCost <= 0) continue;
                // Compared per MODEL: Quest buys singles where its parent buys squads, so a unit that
                // went from 3 models at 165pts to 1 model at 160pts has not got cheaper.
                double perModelQuest = qu.BasePointCost / (double)Math.Max(1, qu.BaseModelCount);
                double perModelFantasy = fu.BasePointCost / (double)Math.Max(1, fu.BaseModelCount);
                ratios.Add(perModelQuest / perModelFantasy);
                bookRatios.Add(perModelQuest / perModelFantasy);
                if (Math.Abs(perModelQuest - perModelFantasy) < 0.01) identical.Add($"{quest.Name}/{qu.Name}");
            }

            if (bookRatios.Count < 3) continue;
            bookRatios.Sort();
            double bookMedian = bookRatios[bookRatios.Count / 2];
            if (bookMedian <= 1.5) wrongScale.Add($"{quest.Name} ({bookMedian:0.00}x)");
        }

        Assert.That(ratios, Is.Not.Empty,
            "no unit names shared between the AoF: Quest and Age of Fantasy books, so the scale cannot "
            + "be checked - that is itself suspicious, since the two systems publish the same factions");

        ratios.Sort();
        double median = ratios[ratios.Count / 2];

        Assert.That(median, Is.GreaterThan(1.5),
            $"the shipped AoF: Quest points are only {median:0.00}x Age of Fantasy's per model. The Quest "
            + "scale is about 3x, so this looks like Age of Fantasy's book (Army Forge id 4) baked into "
            + "the AoF Quest folder - check --book-system-id and re-run the import against id 8");

        Assert.That(wrongScale, Is.Empty,
            "these AoF: Quest books are priced at Age of Fantasy's scale, so they were fetched under the "
            + "wrong game-system id (4 instead of 8) - re-run the import for them: "
            + string.Join(", ", wrongScale));

        Assert.That(identical.Count, Is.LessThan(ratios.Count / 4),
            $"{identical.Count} of {ratios.Count} shared units cost exactly the same per model in both "
            + "systems, which is what a wrong-id import looks like: " + string.Join(", ", identical.Take(5)));
    }

    [Test]
    public void TheQuestBooks_AreTheEnemyRosters_NotTheHeroRoster()
    {
        // The other half of the two-id trap, and the easier one to ship by accident: id 7 returns the
        // SAME factions with only their playable heroes - 5 units for Beastmen against 20 - at hero
        // prices. A hero-roster import would leave a Quest game with four fifths of its enemies missing
        // and no way to tell from inside the game.
        List<BookFile> books = QuestBooks();
        if (books.Count == 0) Assert.Pass("no books shipped; TheQuestBooksAreShipped is the test for that");

        double averageUnits = books.Average(b => b.Units.Count);

        Assert.That(averageUnits, Is.GreaterThan(10),
            $"the AoF: Quest books average {averageUnits:0.0} units each. The enemy books (Army Forge id "
            + "8) run 6-35 units; the HERO roster (id 7) is about 5. This looks like the hero roster - "
            + "re-run the import against id 8");
    }

    // ── The collections ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void AoFQuestHasItsOwnArmiesFolder_NotAgeOfFantasys()
    {
        Assert.That(ArmyPaths.FolderNameFor(EGameSystem.AoFQuest), Is.EqualTo("AoF Quest"));
        Assert.That(ArmyPaths.FolderNameFor(EGameSystem.AoFQuest),
            Is.Not.EqualTo(ArmyPaths.FolderNameFor(EGameSystem.AgeOfFantasy)));
    }

    [Test]
    public void TheCollectionFolderIsNotEmpty()
    {
        Assert.That(CollectionPaths(), Is.Not.Empty,
            "armies/AoF Quest is empty - re-run --make-collections against the bundled books");
    }

    [Test]
    public void EveryCollection_IsAnAoFQuestArmy_AndPassesAnAoFQuestLobbysFilter()
    {
        // Same seam as the books, from the army side: a collection stamped with the wrong system is
        // refused by the launch gate, and the failure reads as "your army is not valid" with no hint of
        // why.
        var wrong = new List<string>();

        foreach (string path in CollectionPaths())
        {
            BuiltArmyFile army = LoadArmy(path);
            if (army.GameSystem != GameSystems.AoFQuest
                || !GameSystems.IsAllowed(GameSystemInfo.ArmySource(EGameSystem.AoFQuest), army.GameSystem))
            {
                wrong.Add($"{Path.GetFileName(path)}: '{army.GameSystem ?? "(none)"}'");
            }
        }

        Assert.That(wrong, Is.Empty,
            "every list in armies/AoF Quest must be an AoF: Quest army, or the lobby refuses it");
    }

    [Test]
    public void EveryCollection_HasUnitsThatCostPoints_AndBringsItsOwnRuleDefinitions()
    {
        // The enemy side of a Quest game is drawn by POINTS from a pool, so a free unit could fill a
        // whole wave on its own; and the collections deliberately carry no embedded book snapshot, so
        // their own RuleDefinitions are the only thing the engine can resolve enemy special rules
        // against. Dropping those would leave every enemy rule silently doing nothing.
        var broken = new List<string>();

        foreach (string path in CollectionPaths())
        {
            BuiltArmyFile army = LoadArmy(path);
            string file = Path.GetFileName(path);

            if (army.Units.Count == 0) { broken.Add($"{file}: no units"); continue; }
            if (army.Units.Any(u => u.PointCost <= 0))
                broken.Add($"{file}: {army.Units.Count(u => u.PointCost <= 0)} free unit(s)");
            if (army.Units.Any(u => u.SpecialRules.Count > 0)
                && (army.RuleDefinitions == null || army.RuleDefinitions.Count == 0))
                broken.Add($"{file}: special rules with no definitions");
            if (army.Book != null || army.Selections != null)
                broken.Add($"{file}: embeds a book snapshot (that is the 2MB/17MB difference)");
        }

        Assert.That(broken, Is.Empty);
    }

    [Test]
    public void MostCollections_AreBigEnoughForALateWave()
    {
        // The Quest wave budget is wave * partyPoints / 4, doubled for a boss fight - about 1500pts
        // against a 300pt party at wave 10. A collection below that does not fail loudly: the wave logs
        // "the enemy collection is empty, nobody arrives" and simply does not happen. A floor on the
        // corpus rather than on every file, because a few books genuinely have small rosters.
        List<BuiltArmyFile> all = CollectionPaths().Select(LoadArmy).ToList();
        if (all.Count == 0) Assert.Pass("no collections shipped; TheCollectionFolderIsNotEmpty covers that");

        int deep = all.Count(a => a.Units.Sum(u => u.PointCost) >= 1500);

        Assert.That(deep, Is.GreaterThan(all.Count / 2),
            $"only {deep} of {all.Count} AoF: Quest collections hold 1500+ pts, so most would run dry in "
            + "the late waves - the bake should be the whole roster at base model count");
    }

    [Test]
    public void EveryBundledBook_HasACollection_AndEveryCollectionABook()
    {
        // The two bakes are run separately (--rebuild-books then --make-collections) and nothing checks
        // that the second one was run after the first. A faction with a book and no collection cannot be
        // picked as the enemy; a collection with no book is a list nothing can re-open in the Forge.
        var bookNames = QuestBooks().Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectionFactions = CollectionPaths().Select(p => LoadArmy(p).Faction ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (bookNames.Count == 0 || collectionFactions.Count == 0)
            Assert.Pass("one of the two bakes is missing; the folder tests above cover that");

        Assert.That(bookNames.Except(collectionFactions, StringComparer.OrdinalIgnoreCase), Is.Empty,
            "these AoF: Quest books have no enemy collection - re-run --make-collections");
        Assert.That(collectionFactions.Except(bookNames, StringComparer.OrdinalIgnoreCase), Is.Empty,
            "these AoF: Quest collections name a faction with no bundled book");
    }
}
