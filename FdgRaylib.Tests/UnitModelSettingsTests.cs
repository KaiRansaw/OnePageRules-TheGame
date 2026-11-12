using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Asset Lab's saved "how this model stands" numbers (UnitModelSettings) and the UnitModels folder browse.
/// The sidecar is what the table will read when real models go in, so it has to round-trip exactly.
/// </summary>
[TestFixture]
public class UnitModelSettingsTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-unitmodels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void Settings_RoundTripBesideTheModel()
    {
        string model = Path.Combine(_dir, "Assault Grunt.obj");
        File.WriteAllText(model, "v 0 0 0\n");
        var s = new UnitModelSettings { HeightMm = 41.5f, BaseMm = 32, UpAxis = EUpAxis.ZUp, YawDegrees = -90, CentreOnBase = false, Notes = "TTS mod" };
        s.Save(model);

        Assert.That(File.Exists(model + ".model.json"), Is.True);
        UnitModelSettings? back = UnitModelSettings.Load(model);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.HeightMm, Is.EqualTo(41.5f));
        Assert.That(back.BaseMm, Is.EqualTo(32f));
        Assert.That(back.UpAxis, Is.EqualTo(EUpAxis.ZUp));
        Assert.That(back.YawDegrees, Is.EqualTo(-90f));
        Assert.That(back.CentreOnBase, Is.False);
        Assert.That(back.Notes, Is.EqualTo("TTS mod"));
    }

    [Test]
    public void Load_NoneSaved_OrUnreadable_IsNull()
    {
        string model = Path.Combine(_dir, "Hive Lord.glb");
        Assert.That(UnitModelSettings.Load(model), Is.Null);
        File.WriteAllText(model + UnitModelSettings.Suffix, "{ not json");
        Assert.That(UnitModelSettings.Load(model), Is.Null);
    }

    [Test]
    public void Browse_ListsModelsAndPictures_ButNotAModelsOwnSkin()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Alien Hives", "textures"));
        foreach (string f in new[]
                 {
                     "Alien Hives/Assault Grunt.obj", "Alien Hives/Assault Grunt.png", // the obj's skin - not listed
                     "Alien Hives/Assault Grunt.mtl",                                   // not an asset
                     "Alien Hives/Hive Lord.glb", "Alien Hives/textures/hull.jpg",      // texture folder - not listed
                     "Alien Hives/Banner.png",                                          // a picture on its own - listed
                     "notes.txt",
                 })
            File.WriteAllText(Path.Combine(_dir, f), "x");

        var names = UnitModelPaths.Browse(_dir).Select(f => Path.GetRelativePath(_dir, f).Replace('\\', '/')).ToList();
        Assert.That(names, Is.EqualTo(new[] { "Alien Hives/Assault Grunt.obj", "Alien Hives/Banner.png", "Alien Hives/Hive Lord.glb" }));
    }

    [Test]
    public void GuessHeight_InfantryIsA28mmPerson()
    {
        Assert.That(UnitModelSettings.GuessHeightMm(25), Is.EqualTo(32f));
        Assert.That(UnitModelSettings.GuessHeightMm(60), Is.GreaterThan(UnitModelSettings.GuessHeightMm(25)));
    }
}
