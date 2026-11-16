using System.Text.Json.Nodes;
using FDG.ArmyBuilding;
using FdgRaylib.Import;
using FdgRaylib.Lobby;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-15: bots field real lists - Army Forge's Ready-to-Play lists, shipped in armies/&lt;game&gt;/Ready-to-Play/.
/// Every shipped one must load the way the lobby loads it, be for the right game, and still be a legal list.
/// </summary>
[TestFixture]
public class ReadyToPlayListsTests
{
    private static string Root()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (!Directory.Exists(Path.Combine(dir, "armies"))) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, "armies");
    }

    private static IEnumerable<string> Shipped() =>
        Directory.EnumerateFiles(Root(), "*.fdgarmy", SearchOption.AllDirectories)
            .Where(f => f.Replace('\\', '/').Contains("/" + ReadyListHarvester.FolderName + "/"));

    [Test]
    public void TheyShip_ForEveryBattleGame()
    {
        var folders = Shipped().Select(f => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(f))!))).Distinct().ToList();
        Assert.That(folders, Is.SupersetOf(new[] { "GDF", "AoF", "GF Firefight", "AoF Skirmish" }));
    }

    [Test]
    public void EveryOne_LoadsAsTheLobbyLoadsIt_ForItsOwnGame_AndIsStillLegal()
    {
        var problems = new List<string>();
        foreach (string file in Shipped())
        {
            string name = Path.GetRelativePath(Root(), file);
            if (LobbySession.LoadArmyFile(file) is not { } army) { problems.Add($"{name}: does not load"); continue; }
            if (army.Units.Count == 0) problems.Add($"{name}: no units");
            string folder = name.Split(Path.DirectorySeparatorChar)[0];
            string expected = folder switch
            {
                "AoF" => GameSystems.AgeOfFantasy, "GF Firefight" => GameSystems.GFFirefight,
                "AoF Skirmish" => GameSystems.AoFSkirmish, _ => GameSystems.GrimdarkFuture,
            };
            if (!GameSystems.SameSystem(army.GameSystem, expected)) problems.Add($"{name}: game is '{army.GameSystem}'");
            if (army.Book is { } book && army.Selections is { } list)
            {
                var errors = ListValidator.Validate(book, list, ListCompiler.Compile(book, list), forceOrg: false)
                    .Where(i => i.Severity == ListIssueSeverity.Error && !i.Message.Contains("no published points cost")).ToList();
                if (errors.Count > 0) problems.Add($"{name}: {errors[0].Message}");
            }
            else problems.Add($"{name}: no editable list or book riding along");
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(20)));
        Assert.That(Shipped().Count(), Is.GreaterThan(50));
    }

    [Test]
    public void AListPickingAnUpgradeTheCurrentBookNoLongerHas_IsRefused()
    {
        // A real 3.4-era Orc Marauders list (Army Forge, 2025): two of its picks are gone from the 3.6 book.
        var full = JsonNode.Parse("""
            {"list":{"name":"Old Orcs","pointsLimit":1000,"forceOrg":true,"units":[
              {"id":"D69wnil","selectionId":"a","combined":false,"joinToUnit":null,"selectedUpgrades":[{"upgradeId":"gb5ou","optionId":"JnLsq"}]}
            ]},"details":{"listPoints":1000}}
            """);
        BookFile orcs = FdgRaylib.Rendering.BookLibrary.Load().First(b => b.Name == "Orc Marauders" && GameSystems.SameSystem(b.GameSystem, GameSystems.GrimdarkFuture));
        Assert.That(ReadyListHarvester.Rebuild(full, orcs, out string? why), Is.Null);
        Assert.That(why, Is.EqualTo("an upgrade no longer in the current book"));
    }

    [Test]
    public void ABot_TakesARealListOverASampleOne_WhenBothFitThePoints()
    {
        var sample = new ArmyCatalogEntry("/x/armies/GDF/1k - Orcs.fdgarmy", "1k - Orcs", "Orc Marauders", 995, null, 6);
        var real = new ArmyCatalogEntry("/x/armies/GDF/Ready-to-Play/Orc Marauders/Speed Boyz (1000).fdgarmy", "Speed Boyz", "Orc Marauders", 990, null, 7);
        for (int seed = 0; seed < 20; seed++)
        {
            var picker = new BotArmyPicker(new[] { sample, real }, new Random(seed));
            Assert.That(picker.PickNext(Guid.NewGuid(), 1000, FDG.ArmyBuilding.EAllowedGameSystems.GrimdarkFuture, new HashSet<string>())?.Path, Is.EqualTo(real.Path));
        }
    }
}
