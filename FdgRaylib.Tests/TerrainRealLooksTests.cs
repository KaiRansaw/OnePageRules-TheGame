using System.IO;
using System.Linq;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-27: "I don't want any AI assets remaining in the game, other than your code". Every terrain kind is drawn
/// from the real model packs (no made-up shapes to fall back on), and flat pieces lie on CC0 photo textures.
/// </summary>
[TestFixture]
public class TerrainRealLooksTests
{
    private static readonly string[] Kinds =
    {
        "Forest", "Field", "Hill", "Mountain", "Rubble", "Ruins", "Building", "Wall", "Barricade",
        "LakeShallow", "LakeDeep", "RiverShallow", "RiverDeep", "Swamp", "Lava", TerrainLooks.Wreck, TerrainLooks.Hazard,
    };

    [Test]
    public void EveryKind_HasARealModelLook_InBothGames(
        [Values(TerrainLooks.ETheme.Grimdark, TerrainLooks.ETheme.Fantasy)] TerrainLooks.ETheme theme)
    {
        foreach (string kind in Kinds)
            Assert.That(TerrainLooks.For(kind, theme), Is.Not.Empty, $"{theme} has no look for {kind}");
    }

    private static string Repo => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));

    [Test]
    public void SurfaceTextures_Ship_WithTheirLicence()
    {
        string dir = Path.Combine(Repo, "OprGodot", "terrain", "surfaces");
        Assert.That(File.Exists(Path.Combine(dir, "License.txt")), "License.txt");
        foreach (string name in new[] { "water_normal", "mud", "mud_normal", "lava_flow", "lava_flow_emission", "lava_crust",
                     "lava_crust_emission", "grass", "grass_normal", "grassy_dirt", "mossy_ground", "dark_grass" })
            Assert.That(File.Exists(Path.Combine(dir, name + ".jpg")), name);
    }

    [Test]
    public void NoAutoMadeDraftPieces_Ship()
    {
        string dir = Path.Combine(Repo, "FdgRaylib", "Assets", "TerrainPieces");
        Assert.That(Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories) : Array.Empty<string>(), Is.Empty,
            "only pieces Kai builds in the Terrain Workshop ship");
    }
}
