using FDG;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-14: the downloaded Warhammer terrain matched to terrain kinds, and the models used as map pieces - "the
/// model is the piece": its rules from the kind it is matched to, its size from the model.
/// </summary>
[TestFixture]
public class WarhammerTerrainLibraryTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "wh-lib-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private static string Model(string nick, string mesh) =>
        "{\"Name\":\"Custom_Model\",\"Nickname\":" + Newtonsoft.Json.JsonConvert.ToString(nick) +
        ",\"Transform\":{\"posX\":0,\"posY\":0,\"posZ\":0,\"rotY\":0,\"scaleX\":1,\"scaleY\":1,\"scaleZ\":1},\"CustomMesh\":{\"MeshURL\":\"" + mesh + "\"}}";

    /// <summary>A two-set terrain list with every piece's model file present, as the downloader leaves it.</summary>
    private WarhammerTerrainLibrary Library(bool withFiles = true)
    {
        string Tile(float x, params string[] m) => "{\"Name\":\"Custom_Tile\",\"Transform\":{\"posX\":" + x + "},\"LuaScript\":" +
            Newtonsoft.Json.JsonConvert.ToString("objectJSONs = {" + string.Join(",", m.Select(s => "[[" + s + "]]")) + "}") + "}";
        string mod = "{\"ObjectStates\":[" + Tile(0, Model("Ruin : Area Terrain : Light Cover, Breachable", "a"), Model("Barricade : Obstacle : Light cover", "b"),
            Model("Ruin 2 : Area Terrain : Obscuring", "c"), Model("", "d")) + "," + Tile(10, Model("wood 1", "e")) + "]}";
        string cat = Path.Combine(_dir, "warhammer-terrain.json");
        WarhammerTerrain.Write(WarhammerTerrain.Parse(mod), cat);
        if (withFiles)
        {
            var (c, pieces) = WarhammerTerrain.Load(cat)!.Value;
            foreach (var p in pieces)
            {
                string f = WarhammerTerrain.ModelPathFor(c, p, Path.Combine(_dir, "models"));
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                File.WriteAllText(f, "glb");
            }
        }
        return WarhammerTerrainLibrary.Load(cat, Path.Combine(_dir, "models"))!;
    }

    [Test]
    public void EveryDownloadedPiece_IsListed_WithAGuessFromItsWarhammerKeywords()
    {
        var lib = Library();
        Assert.That(lib.Entries, Has.Count.EqualTo(5));
        Assert.That(lib.Entries.ToDictionary(e => e.Name, e => e.Match.Kind), Is.EquivalentTo(new Dictionary<string, string>
        {
            ["Ruin"] = "Ruins", ["Barricade"] = "Barricade", ["Ruin 2"] = "Ruins", ["Piece 1"] = "", ["wood 1"] = "Forest",
        }), "an unnamed piece gets no guess - Kai picks");
        Assert.That(lib.Sets, Has.Count.EqualTo(2));
    }

    [Test]
    public void APieceNotDownloaded_IsNotListed() => Assert.That(Library(withFiles: false).Entries, Is.Empty);

    [Test]
    public void Matches_AreSavedAndReadBack()
    {
        var lib = Library();
        var e = lib.Entries.First(x => x.Name == "Piece 1");
        e.Match.Kind = "Building"; e.Match.Name = "Bunker"; e.Match.Checked = true; e.Match.W = 6; e.Match.D = 4; e.Match.H = 3;
        lib.Entries.First(x => x.Name == "wood 1").Match.Hidden = true;
        lib.Save();

        var again = WarhammerTerrainLibrary.Load(Path.Combine(_dir, "warhammer-terrain.json"), Path.Combine(_dir, "models"))!;
        var back = again.ById(e.Id)!;
        Assert.That((back.Name, back.Match.Kind, back.Match.Checked, back.Match.W), Is.EqualTo(("Bunker", "Building", true, 6f)));
        Assert.That(again.Placeable.Select(x => x.Name), Does.Not.Contain("wood 1"), "hidden pieces stay out of the Map Maker");
    }

    [Test]
    public void APlacedModel_IsAPieceWithItsKindsRules_TheModelsSize_AndTheModelAsItsLook()
    {
        var e = Library().Entries.First(x => x.Name == "Ruin");
        e.Match.W = 12.04f; e.Match.D = 6f; e.Match.H = 9.87f;

        MapPiece p = WarhammerTerrainLibrary.PieceFor(e, 20, 30);

        Assert.That(p.Rules, Is.EqualTo(TerrainBook.Kind("Ruins")!.Rules));
        Assert.That((p.W, p.D, p.Height), Is.EqualTo((12f, 6f, 9.75f)), "the model's size, to the quarter inch");
        Assert.That(p.Look, Is.EqualTo("wh:" + e.Id));
        Assert.That(WarhammerTerrainLibrary.IdOf(p.Look), Is.EqualTo(e.Id));
        Assert.That((p.X, p.Z, p.Name), Is.EqualTo((20f, 30f, "Ruin")));
    }

    [Test]
    public void Decoration_HasNoRules()
    {
        var e = Library().Entries.First(x => x.Name == "Piece 1");
        e.Match.Kind = WarhammerTerrainLibrary.Decoration;
        Assert.That(WarhammerTerrainLibrary.PieceFor(e, 1, 1).Rules, Is.EqualTo(ETerrainType.None));
    }

    [Test]
    public void ASet_DressesAGeneratedMap_SwappingPiecesOfTheKindsItHas()
    {
        var lib = Library();
        foreach (var e in lib.Entries) { e.Match.W = 5; e.Match.D = 4; e.Match.H = 2; }
        var draft = MapDraft.New(72, 48);
        draft.Pieces.Add(MapPiece.FromKind(TerrainBook.Kind("Ruins")!, 20, 20));
        draft.Pieces.Add(MapPiece.FromKind(TerrainBook.Kind("Lava")!, 40, 20));
        int set = lib.Entries.First(e => e.Name == "Ruin").Set;

        int n = lib.DressFromSet(draft, set, new Random(1));

        Assert.That(n, Is.EqualTo(1));
        Assert.That(WarhammerTerrainLibrary.IdOf(draft.Pieces[0].Look), Is.Not.Null, "the ruin became one of the set's ruins");
        Assert.That(draft.Pieces[0].Rules, Is.EqualTo(TerrainBook.Kind("Ruins")!.Rules), "same rules - it is still a ruin");
        Assert.That(draft.Pieces[1].Look, Is.Empty, "the set has no lava, so the lava stays a plain piece");
    }

    [TestCase("Barricade", "Obstacle: Light cover, Defense line", "Barricade")]
    [TestCase("Large Crater", "", "Rubble")]
    [TestCase("3 Storey Ruin", "", "Ruins")]
    [TestCase("wood 2", "", "Forest")]
    [TestCase("Ork Corpses", "", "Rubble")]
    [TestCase("ork bunker head SIMPLE COLLIDER", "", "")]
    [TestCase("Tall T'au Structure", "Obstacle: Obscuring, Breachable, Light Cover", "Ruins")]
    [TestCase("Structure 1", "Obstacle: Light Cover Unstable position", "Barricade")]
    [TestCase("Wall 2", "Obstacle: Light Cover, scaleable, Defense line", "Barricade")]
    [TestCase("Piece 7", "", "")]
    [TestCase("Shipping Container", "Obstacle: Light cover", "Building")]
    [TestCase("Ammo crate", "", "Barricade")]
    public void Guesses(string name, string keywords, string kind) =>
        Assert.That(WarhammerTerrainLibrary.GuessKind(name, keywords), Is.EqualTo(kind));

    [Test]
    public void ALookThatIsNotAModel_HasNoId()
    {
        Assert.That(WarhammerTerrainLibrary.IdOf(""), Is.Null);
        Assert.That(WarhammerTerrainLibrary.IdOf("pine-wood"), Is.Null);
        Assert.That(WarhammerTerrainLibrary.IdOf("wh:abc"), Is.Null);
    }

    /// <summary>Kai, 2026-11-14: containers placed as barricades (an old guess) should become solid once matched as buildings.</summary>
    [Test]
    public void AMapsPieces_FollowTheirModelsMatch_UnlessTheirRulesWereEdited()
    {
        var lib = Library();
        var ruin = lib.Entries.Single(e => e.Name == "Ruin");
        var draft = MapDraft.New(72, 48);
        var placed = WarhammerTerrainLibrary.PieceFor(ruin, 20, 20);
        var edited = WarhammerTerrainLibrary.PieceFor(ruin, 40, 20);
        edited.Rules |= ETerrainType.Dangerous;
        draft.Pieces.Add(placed);
        draft.Pieces.Add(edited);
        ruin.Match.Kind = "Building";
        ruin.Match.Checked = true;
        Assert.That(lib.Refresh(draft), Is.EqualTo(1));
        Assert.That(placed.Kind, Is.EqualTo("Building"));
        Assert.That(placed.Rules, Is.EqualTo(TerrainBook.Kind("Building")!.Rules));
        Assert.That(edited.Kind, Is.EqualTo("Ruins"), "rules changed by hand are left alone");
    }

    [Test]
    public void AnUncheckedGuess_FollowsTheCurrentGuessing_ACheckedOneStays()
    {
        var lib = Library();
        foreach (var e in lib.Entries) { e.Match.Kind = "Wall"; e.Match.Checked = e.Name == "Ruin"; }
        lib.Save();
        var again = WarhammerTerrainLibrary.Load(Path.Combine(_dir, "warhammer-terrain.json"), Path.Combine(_dir, "models"))!;
        Assert.That(again.Entries.Single(e => e.Name == "Ruin").Match.Kind, Is.EqualTo("Wall"), "checked by Kai");
        Assert.That(again.Entries.Single(e => e.Name == "Ruin 2").Match.Kind, Is.EqualTo("Ruins"), "still a guess");
    }
}
