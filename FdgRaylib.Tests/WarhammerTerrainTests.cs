using FdgRaylib.Models3D;
using FdgRaylib.Terrain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The optional Warhammer terrain (Kai, 2026-11-14): the Warhammer Map Builder mod's sets read into a catalogue the
/// model downloader and assembler already understand.
/// </summary>
[TestFixture]
public class WarhammerTerrainTests
{
    private static string Model(string nick, string mesh, string tex = "", float x = 0, float z = 0, string states = "") =>
        "{\"Name\":\"Custom_Model\",\"Nickname\":" + Newtonsoft.Json.JsonConvert.ToString(nick) +
        $",\"Transform\":{{\"posX\":{x},\"posY\":1,\"posZ\":{z},\"rotX\":0,\"rotY\":90,\"rotZ\":0,\"scaleX\":2,\"scaleY\":2,\"scaleZ\":2}}," +
        $"\"ColorDiffuse\":{{\"r\":0.5,\"g\":0.5,\"b\":0.5}},\"Tags\":[\"desert_tiles\"],\"CustomMesh\":{{\"MeshURL\":\"{mesh}\",\"DiffuseURL\":\"{tex}\"}}" +
        (states.Length > 0 ? ",\"States\":{\"2\":" + states + "}" : "") + "}";

    private static string Tile(float x, params string[] models) =>
        "{\"Name\":\"Custom_Tile\",\"Nickname\":\"\",\"Transform\":{\"posX\":" + x + "},\"LuaScript\":" +
        Newtonsoft.Json.JsonConvert.ToString("function place() end\nobjectJSONs = {\n" + string.Join(",\n", models.Select(m => "  [[" + m + "]]")) + "\n}") + "}";

    private static string Mod(params string[] tiles) =>
        "{\"SaveName\":\"Warhammer Map Builder\",\"ObjectStates\":[" + string.Join(",", tiles) + ",{\"Name\":\"Notecard\"}]}";

    [Test]
    public void EachSetTile_BecomesASet_LeftToRight_WithItsPieces()
    {
        string json = Mod(
            Tile(10, Model("Ruin : Area Terrain : Light Cover, Obscuring", "https://x/m2", "https://x/t2", 5, 6)),
            Tile(-10,
                Model("Barricade : Obstacle : Light cover, Defense line", "https://x/m1", "", 1, 2),
                Model("", "https://x/m3", "", 3, 4, states: Model("Crater", "https://x/m4"))));

        WarhammerTerrain.Result r = WarhammerTerrain.Parse(json);

        Assert.That(r.Sets.Values, Is.EqualTo(new[] { "Terrain set 1 (desert)", "Terrain set 2 (desert)" }), "numbered by where the mod lays the tiles");
        var first = r.Pieces.Where(p => p.Set == WarhammerTerrain.GroupBase).ToList();
        Assert.That(first.Select(p => p.Name), Is.EqualTo(new[] { "Barricade", "Piece 1", "Crater" }), "unnamed pieces numbered; an alternate look is a piece too");
        Assert.That(first[0].Keywords, Is.EqualTo("Obstacle: Light cover, Defense line"));
        Assert.That(r.Layout[WarhammerTerrain.GroupBase].Select(p => (p.X, p.Z)), Is.EqualTo(new[] { (1f, 2f), (3f, 4f) }),
            "where the set places its pieces (alternate looks are not placed)");
        Assert.That(r.Models.All(m => m.Id >= WarhammerTerrain.IdBase), Is.True, "ids never meet ForceOrg's or Stormvault's");
    }

    [Test]
    public void TheCatalogue_IsReadByTheModelDownloadersCatalogueLoader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wh-terrain-{Guid.NewGuid():N}.json");
        try
        {
            WarhammerTerrain.Write(WarhammerTerrain.Parse(Mod(Tile(0, Model("Rock", "https://x/rock", "https://x/rock-tex")))), path);
            var (cat, pieces) = WarhammerTerrain.Load(path)!.Value;
            ForceOrgCatalog.Model rock = cat.ById.Values.Single();
            Assert.That(rock.Name, Is.EqualTo("Rock"));
            Assert.That(rock.Parts.Single().Texture, Is.EqualTo("https://x/rock-tex"));
            Assert.That(rock.Parts.Single().Scale, Is.EqualTo(new[] { 2f, 2f, 2f }), "the mod's scale is kept");
            Assert.That(WarhammerTerrain.ModelPathFor(cat, pieces.Single(), "/root"), Does.StartWith(Path.Combine("/root", "Terrain set 1 (desert)", "Rock [")));
        }
        finally { File.Delete(path); }
    }

    private static string MatTile(string url, float x, float z) =>
        "{\"Name\":\"Custom_Tile\",\"Transform\":{\"posX\":" + x + ",\"posZ\":" + z + "},\"CustomImage\":{\"ImageURL\":\"" + url +
        "\"},\"LuaScript\":\"function applyImage() local board = getObjectFromGUID('7f414f') end\"}";

    /// <summary>Kai, 2026-11-14: the mod's battle mats - tiles that paint the table with their picture - become pickable mats.</summary>
    [Test]
    public void BattleMats_AreRead_RowByRow_AndAMapsMatFindsItsPicture()
    {
        string json = Mod(Tile(0, Model("Rock", "https://x/rock")), MatTile("https://x/b", 5, 10), MatTile("https://x/a", -5, 10),
            MatTile("https://x/c", -5, 0), MatTile("https://x/a", 9, 0));
        WarhammerTerrain.Result r = WarhammerTerrain.Parse(json);
        Assert.That(r.Mats.Select(m => m.Url), Is.EqualTo(new[] { "https://x/a", "https://x/b", "https://x/c" }), "top row first, left to right, no repeats");
        Assert.That(r.Mats.Select(m => m.Name), Is.EqualTo(new[] { "Mat 1", "Mat 2", "Mat 3" }));
        Assert.That(r.Pieces.Single().Name, Is.EqualTo("Rock"), "a mat tile is not a terrain set");

        string dir = Path.Combine(Path.GetTempPath(), $"wh-mats-{Guid.NewGuid():N}");
        string list = Path.Combine(dir, "list.json");
        try
        {
            Directory.CreateDirectory(dir);
            WarhammerTerrain.Write(r, list);
            var mats = WarhammerTerrain.LoadMats(list)!;
            Assert.That(mats.Select(m => m.Id), Is.EqualTo(r.Mats.Select(m => m.Id)));
            string jpg = WarhammerTerrain.MatPathFor(mats[0], new byte[] { 0xFF, 0xD8, 0, 0 }, dir);
            Assert.That(jpg, Does.EndWith(".jpg"));
            Assert.That(WarhammerTerrain.MatPathFor(mats[0], new byte[] { 0x89, (byte)'P', 0, 0 }, dir), Does.EndWith(".png"));
            File.WriteAllText(jpg, "x");
            Assert.That(WarhammerTerrain.MatFile(mats[0].Look, dir), Is.EqualTo(jpg));
            Assert.That(WarhammerTerrain.MatFile(mats[1].Look, dir), Is.Null, "not downloaded");
            Assert.That(WarhammerTerrain.MatFile("", dir), Is.Null, "plain felt");

            File.WriteAllText(list, "{\"pieces\":[]}");
            Assert.That(WarhammerTerrain.LoadMats(list), Is.Null, "a list from before mats were read gets rebuilt");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void AMapsMat_IsSavedWithIt()
    {
        var d = MapDraft.New(72, 48);
        d.Mat = "wh-mat:1234";
        var back = FdgRaylib.Cli.TerrainLoader.TryLoadFromJson(d.Snapshot())!;
        Assert.That(back.Mat, Is.EqualTo("wh-mat:1234"));
        Assert.That(FdgRaylib.Cli.TerrainLoader.TryLoadFromJson("{\"Name\":\"old\"}")!.Mat, Is.EqualTo(""), "older maps: plain felt");
    }

    [TestCase("Barricade : Obstacle : Light cover, Defense line, Defensible", "Barricade", "Obstacle: Light cover, Defense line, Defensible")]
    [TestCase("Tall T'au Structure | [CC99CC]Obstacle[-]                [00BFFF]Obscuring, Breachable[-]", "Tall T'au Structure", "Obstacle: Obscuring, Breachable")]
    [TestCase("Ruin (Area Terrain)", "Ruin", "Area Terrain")]
    [TestCase("Structure 3 : Obstacle : Light Cover scaleable Obscuring \r\n", "Structure 3", "Obstacle: Light Cover scaleable Obscuring")]
    [TestCase("wood 1", "wood 1", "")]
    [TestCase("", "", "")]
    public void PieceNames_SplitIntoNameAndWarhammersKeywords(string raw, string name, string keywords) =>
        Assert.That(WarhammerTerrain.SplitName(raw), Is.EqualTo((name, keywords)));

    [Test]
    public void SpawnBlobs_SkipsAnythingThatIsNotJson() =>
        Assert.That(WarhammerTerrain.SpawnBlobs("x = [[{not json}]]\ny = [[{\"a\":1}]]").Single()["a"]!.Value<int>(), Is.EqualTo(1));

    /// <summary>Kai's own copy of the mod, where it exists (Claude's workspace has it; nowhere else does).</summary>
    [Test]
    public void TheRealMod_GivesSixSetsAndItsTerrain()
    {
        string real = "/mnt/user-data/uploads/Workshop/" + WarhammerTerrain.WorkshopId + ".json";
        Assume.That(File.Exists(real), "the mod's save is not on this machine");
        WarhammerTerrain.Result r = WarhammerTerrain.Parse(File.ReadAllText(real));
        Assert.That(r.Sets, Has.Count.EqualTo(5), "five tiles carry pieces (the sixth is empty)");
        Assert.That(r.Pieces, Has.Count.GreaterThan(200));
        Assert.That(r.Mats, Has.Count.EqualTo(106), "the battle mats (Kai, 2026-11-14)");
        Assert.That(r.Pieces.All(p => p.Name.All(c => c <= 0x7F)), Is.True, "game text is ASCII");
        TestContext.WriteLine(string.Join("\n", r.Sets.Select(s => $"{s.Value}: {r.Pieces.Count(p => p.Set == s.Key)} pieces")));
        TestContext.WriteLine(string.Join("\n", r.Pieces.Take(40).Select(p => $"{p.Name} | {p.Keywords}")));
    }

    /// <summary>Kai, 2026-11-14: terrain never stands on a base - including pieces saved before 2.13 with a 25mm one.</summary>
    [Test]
    public void TerrainModels_NeverGetABase()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fdg-wh-" + Guid.NewGuid().ToString("N"), WarhammerTerrain.ModelsFolderName, "Set 1");
        Directory.CreateDirectory(dir);
        try
        {
            string piece = Path.Combine(dir, "Ruin [00001].glb");
            new UnitModelSettings { BaseMm = 25 }.Save(piece);
            Assert.That(UnitModelSettings.IsTerrain(piece), Is.True);
            Assert.That(UnitModelSettings.Load(piece)!.BaseMm, Is.EqualTo(0f));
            string unit = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dir))!, "Alien Hives", "Grunt.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
            new UnitModelSettings { BaseMm = 25 }.Save(unit);
            Assert.That(UnitModelSettings.IsTerrain(unit), Is.False);
            Assert.That(UnitModelSettings.Load(unit)!.BaseMm, Is.EqualTo(25f));
        }
        finally { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dir))!, true); }
    }
}
