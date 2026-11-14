using FDG;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Terrain looks (Kai, 2026-11-14): free CC0 packs bundled with the game, a style per game (Grimdark Future sci-fi,
/// Age of Fantasy fantasy), area terrain scattered on a base, the game's pick unless the map chose one.
/// </summary>
[TestFixture]
public class TerrainLooksTests
{
    private static string TerrainFolder =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "OprGodot", "terrain");

    [Test]
    public void EveryModelALookNames_IsBundledWithTheGame()
    {
        var missing = TerrainLooks.All.SelectMany(l => l.Props.Concat(l.Extras ?? Array.Empty<TerrainLooks.Prop>()))
            .Select(p => p.File).Distinct()
            .Where(f => !File.Exists(Path.Combine(TerrainFolder, f + ".glb")) && !File.Exists(Path.Combine(TerrainFolder, f + ".gltf")))
            .ToList();
        Assert.That(missing, Is.Empty, "re-run tools/terrain/bundle_terrain.py after adding a model to a look");
    }

    [Test]
    public void EveryPackShipsItsLicence() =>
        Assert.That(Directory.GetDirectories(TerrainFolder).Where(d => !File.Exists(Path.Combine(d, "License.txt"))), Is.Empty);

    [Test]
    public void EveryBookKind_HasALook_InBothStyles()
    {
        foreach (var kind in TerrainBook.Kinds)
            foreach (var theme in new[] { TerrainLooks.ETheme.Grimdark, TerrainLooks.ETheme.Fantasy })
                Assert.That(TerrainLooks.For(kind.Id, theme), Is.Not.Empty, $"{kind.Id} in {theme}");
        Assert.That(TerrainLooks.All.Select(l => l.Id), Is.Unique);
    }

    [TestCase(EGameSystem.GrimdarkFuture, TerrainLooks.ETheme.Grimdark)]
    [TestCase(EGameSystem.GFFirefight, TerrainLooks.ETheme.Grimdark)]
    [TestCase(EGameSystem.GFStarQuest, TerrainLooks.ETheme.Grimdark)]
    [TestCase(EGameSystem.AgeOfFantasy, TerrainLooks.ETheme.Fantasy)]
    [TestCase(EGameSystem.AoFSkirmish, TerrainLooks.ETheme.Fantasy)]
    [TestCase(EGameSystem.AoFQuest, TerrainLooks.ETheme.Fantasy)]
    public void EachGame_HasItsOwnStyle(EGameSystem system, TerrainLooks.ETheme theme) =>
        Assert.That(TerrainLooks.ThemeFor(system), Is.EqualTo(theme));

    [Test]
    public void ThePick_IsTheSameEverywhere_RespectsTheMapsChoice_AndFallsBackAcrossStyles()
    {
        int seed = TerrainLooks.SeedFor(12.5f, 30f, "Forest");
        Assert.That(seed, Is.EqualTo(TerrainLooks.SeedFor(12.5f, 30f, "Forest")), "no per-run hashing - both players must see the same trees");
        Assert.That(TerrainLooks.Pick("Forest", "", TerrainLooks.ETheme.Fantasy, seed)!.Theme, Is.EqualTo(TerrainLooks.ETheme.Fantasy));
        Assert.That(TerrainLooks.Pick("Forest", "pine-wood", TerrainLooks.ETheme.Fantasy, seed)!.Id, Is.EqualTo("pine-wood"));
        Assert.That(TerrainLooks.Pick("Forest", "pine-wood", TerrainLooks.ETheme.Grimdark, seed)!.Theme, Is.EqualTo(TerrainLooks.ETheme.Grimdark),
            "a Fantasy look picked on the map is ignored in a Grimdark game - the piece picks its own");
        Assert.That(TerrainLooks.Pick("Forest", "castle-wall", TerrainLooks.ETheme.Fantasy, seed)!.Kinds, Does.Contain("Forest"),
            "a look for another kind is ignored");
    }

    [TestCase("Copse", ETerrainType.Cover | ETerrainType.Difficult, "Forest")]
    [TestCase("Shipping container", ETerrainType.Impassible | ETerrainType.Blocking, "Building")]
    [TestCase("Sandbag line", ETerrainType.Cover, "Barricade")]
    [TestCase("Crashed hauler", ETerrainType.Impassible | ETerrainType.Blocking, "Wreck")]
    [TestCase("Mine field", ETerrainType.Dangerous, "Hazard")]
    [TestCase("Marsh", ETerrainType.Difficult, "Swamp")]
    [TestCase("Rocky ridge", ETerrainType.Impassible | ETerrainType.Blocking, "Mountain")]
    [TestCase("", ETerrainType.Cover | ETerrainType.Unstable, "Ruins")]
    [TestCase("", ETerrainType.Dangerous, "Lava")]
    [TestCase("Thing", ETerrainType.None, "Rubble")]
    public void APieceWithNoKind_GetsOneFromItsNameAndRules(string name, ETerrainType rules, string kind) =>
        Assert.That(TerrainLooks.GuessKind("", name, rules, 2f, 20f), Is.EqualTo(kind));

    [Test]
    public void AKindFromTheMap_IsKept() =>
        Assert.That(TerrainLooks.GuessKind("Hill", "Rocks", ETerrainType.Cover, 1f, 30f), Is.EqualTo("Hill"));
}
