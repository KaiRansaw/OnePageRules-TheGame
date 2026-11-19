using System.Numerics;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Models3D;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-19: downloaded models carry the base they were sculpted on, often a different size from the Army Forge
/// base the game draws. Option A: the game base stays (the rules measure from it), the model's own base is found and
/// hidden, and a Size slider grows or shrinks the model - all saved with the model's settings, so "Send my settings to
/// GitHub" carries them to every player.
/// </summary>
[TestFixture]
public class OwnBaseTests
{
    // ---------------------------------------------------------------- shapes, in inches (1 file unit = 1 inch)

    /// <summary>A flat-topped round slab, <paramref name="diameter"/> across, from y=0 up to <paramref name="top"/>.</summary>
    private static List<OwnBaseFinder.Tri> Disc(float diameter, float top, int sides = 24)
    {
        var t = new List<OwnBaseFinder.Tri>();
        float r = diameter / 2;
        Vector3 P(int i, float y) => new(r * MathF.Cos(i * 2 * MathF.PI / sides), y, r * MathF.Sin(i * 2 * MathF.PI / sides));
        for (int i = 0; i < sides; i++)
        {
            t.Add(new(new Vector3(0, top, 0), P(i, top), P(i + 1, top), 1));     // top, facing up
            t.Add(new(new Vector3(0, 0, 0), P(i + 1, 0), P(i, 0), -1));         // underside, facing down
            t.Add(new(P(i, 0), P(i + 1, 0), P(i + 1, top), 0));                 // rim
            t.Add(new(P(i, 0), P(i + 1, top), P(i, top), 0));
        }
        return t;
    }

    /// <summary>A closed box from (x0,y0,z0) to (x1,y1,z1), with proper up/down/side normals.</summary>
    private static List<OwnBaseFinder.Tri> Box(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        var t = new List<OwnBaseFinder.Tri>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float up) { t.Add(new(a, b, c, up)); t.Add(new(a, c, d, up)); }
        Quad(new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), 1);
        Quad(new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0), -1);
        Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), 0);
        Quad(new(x0, y0, z1), new(x0, y1, z1), new(x1, y1, z1), new(x1, y0, z1), 0);
        Quad(new(x0, y0, z0), new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z1), 0);
        Quad(new(x1, y0, z0), new(x1, y0, z1), new(x1, y1, z1), new(x1, y1, z0), 0);
        return t;
    }

    [Test]
    public void AFigureOnItsOwnBase_TheBaseIsFound_AndTheCutIsJustAboveItsTop()
    {
        // A 32mm figure standing on a 50mm base 4mm thick.
        var model = Disc(50 / 25.4f, 4 / 25.4f);
        model.AddRange(Box(-0.15f, 4 / 25.4f, -0.1f, 0.15f, 1.4f, 0.1f));

        OwnBaseFinder.Found? found = OwnBaseFinder.Find(model, 1f);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Value.CutInches * 25.4f, Is.InRange(4f, 5.5f), "cut just above the base's top, below the feet");
        Assert.That(found.Value.WidthMm, Is.EqualTo(50).Within(1.5f));
    }

    [Test]
    public void TheSameModel_SavedAtAnotherScale_IsFoundAtTheSameRealHeight()
    {
        // A ForceOrg model is 1 unit = 1 inch; a printable one is in mm. inchesPerUnit puts both on the table's scale.
        var mm = Disc(50, 4);
        mm.AddRange(Box(-4, 4, -2.5f, 4, 36, 2.5f));

        OwnBaseFinder.Found? found = OwnBaseFinder.Find(mm, 1 / 25.4f);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Value.CutInches * 25.4f, Is.InRange(4f, 5.5f));
    }

    [Test]
    public void AFigureWithNoBase_IsLeftWhole()
    {
        // Two feet a little apart and a body: nothing flat and base-sized low down.
        var model = Box(-0.25f, 0, -0.08f, -0.05f, 0.5f, 0.08f);
        model.AddRange(Box(0.05f, 0, -0.08f, 0.25f, 0.5f, 0.08f));
        model.AddRange(Box(-0.3f, 0.5f, -0.12f, 0.3f, 1.3f, 0.12f));

        Assert.That(OwnBaseFinder.Find(model, 1f), Is.Null);
    }

    [Test]
    public void ATankWithNoBase_IsLeftWhole()
    {
        // A hull with a flat roof well above the bottom band - not a base.
        var model = Box(-1.5f, 0, -1f, 1.5f, 1.4f, 1f);

        Assert.That(OwnBaseFinder.Find(model, 1f), Is.Null);
    }

    [Test]
    public void AFlatToken_IsNotCutAway()
    {
        Assert.That(OwnBaseFinder.Find(Disc(1f, 0.12f), 1f), Is.Null);
    }

    [Test]
    public void FacesWithNoNormals_StillFindTheBase()
    {
        // A file without normals: which side is up is not known, only how flat a face is.
        var model = Disc(40 / 25.4f, 3 / 25.4f).Select(t => t with { UpY = float.NaN }).ToList();
        model.AddRange(Box(-0.15f, 3 / 25.4f, -0.1f, 0.15f, 1.3f, 0.1f).Select(t => t with { UpY = float.NaN }));

        OwnBaseFinder.Found? found = OwnBaseFinder.Find(model, 1f);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Value.CutInches * 25.4f, Is.InRange(3f, 4.5f));
    }

    // ---------------------------------------------------------------- the settings travel with the model

    [Test]
    public void SizeAndOwnBase_RoundTrip_AndOldSettingsDefaultToRealSizeWithTheBaseHidden()
    {
        var s = new UnitModelSettings { SizePercent = 140, OwnBaseMm = 3.5f };
        var back = JsonConvert.DeserializeObject<UnitModelSettings>(JsonConvert.SerializeObject(s))!;
        Assert.That(back.SizePercent, Is.EqualTo(140f));
        Assert.That(back.OwnBaseMm, Is.EqualTo(3.5f));
        Assert.That(back.SizeFactor, Is.EqualTo(1.4f).Within(1e-5f));

        // Every model saved before this change: real size, own base found and hidden.
        var old = JsonConvert.DeserializeObject<UnitModelSettings>("{\"HeightMm\":0,\"BaseMm\":65}")!;
        Assert.That(old.SizePercent, Is.EqualTo(100f));
        Assert.That(old.OwnBaseMm, Is.EqualTo(UnitModelSettings.OwnBaseAuto));
        Assert.That(old.SizeFactor, Is.EqualTo(1f));
    }

    [Test]
    public void TheSharedSettingsFile_CarriesSizeAndOwnBase()
    {
        // "Send my settings to GitHub" ships model-settings.json; the new numbers must be in it.
        string json = JsonConvert.SerializeObject(new UnitModelSettings { SizePercent = 125, OwnBaseMm = 0 });
        Assert.That(json, Does.Contain("\"SizePercent\":125"));
        Assert.That(json, Does.Contain("\"OwnBaseMm\":0"));
    }

    // ---------------------------------------------------------------- the Army Forge base the Asset Lab shows

    private static BookFile Book(string faction, string? system, string unit, float diameterMm) => new()
    {
        Name = faction, Faction = faction, GameSystem = system,
        Units = { new RosterUnit { Name = unit, Base = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = diameterMm / 25.4f } } },
    };

    [Test]
    public void GameBase_IsTheUnitsArmyForgeBase_FromTheFullSizeGamesBook()
    {
        var books = new[]
        {
            Book("Alien Hives", GameSystems.GFFirefight, "Hive Lord", 100),
            Book("Alien Hives", null, "Hive Lord", 120),
        };
        Assert.That(GameBaseLookup.BaseMm(books, "Alien Hives", "hive lord"), Is.EqualTo(120).Within(0.01f));
        Assert.That(GameBaseLookup.BaseMm(books.Take(1), "Alien Hives", "Hive Lord"), Is.EqualTo(100).Within(0.01f), "another mode's book is the fallback");
        Assert.That(GameBaseLookup.BaseMm(books, "Alien Hives", "Nobody"), Is.Null);
    }

    [Test]
    public void GameBase_FactionAndUnit_AreReadFromTheModelsFolder()
    {
        Assert.That(GameBaseLookup.FromKey("Alien Hives/Hive Lord/Hive Tyrant (2827).glb"), Is.EqualTo(("Alien Hives", "Hive Lord")));
        Assert.That(GameBaseLookup.FromKey("loose.glb"), Is.Null);
    }

    [Test]
    public void GameBase_ShippedBooks_GiveTheBaseKaiSaw()
    {
        // The real case Kai reported: a Hive Tyrant sculpted for 65mm, on Army Forge's 120mm Hive Lord base.
        string dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "Books");
        Assume.That(Directory.Exists(dir), "books are copied beside the tests");
        var books = Directory.EnumerateFiles(dir, "*.fdgbook", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) == "AlienHives.fdgbook")
            .Select(f => System.Text.Json.JsonSerializer.Deserialize<BookFile>(File.ReadAllText(f), FDG.Rules.Serialization.RuleJson.Options)!)
            .ToList();
        Assume.That(books, Is.Not.Empty);
        Assert.That(GameBaseLookup.BaseMm(books, "Alien Hives", "Hive Lord"), Is.EqualTo(120).Within(0.5f));
    }
}
