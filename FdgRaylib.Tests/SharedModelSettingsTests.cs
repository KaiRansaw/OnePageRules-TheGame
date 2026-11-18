using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-17: model facings set on his PC never reached a fresh download on his laptop. The settings now ship in
/// Assets/Models3D/model-settings.json: written from the repo, read everywhere else.
/// </summary>
[TestFixture, NonParallelizable]
public class SharedModelSettingsTests
{
    private string _dir = "", _oldRoot = "";

    [SetUp]
    public void SetUp()
    {
        _oldRoot = AppAssets.Root;
        _dir = Path.Combine(Path.GetTempPath(), "fdg-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "game", "Assets"));
        Directory.CreateDirectory(Path.Combine(_dir, "UnitModels"));
        AppAssets.Root = Path.Combine(_dir, "game");
        SharedModelSettings.Forget();
        PictureFraming.Forget();
    }

    [TearDown]
    public void TearDown()
    {
        SharedModelSettings.OverrideSourceTree(null);
        SharedModelSettings.Forget();
        PictureFraming.Forget();
        AppAssets.Root = _oldRoot;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Model(string rel)
    {
        string f = Path.Combine(_dir, "UnitModels", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "glb");
        return f;
    }

    /// <summary>Kai's PC, then his laptop: the turn he gave a model on the PC is what the laptop's download stands.</summary>
    [Test]
    public void FacingSetFromTheRepo_IsUsedByAFreshDownload()
    {
        SharedModelSettings.OverrideSourceTree(true);
        string grunt = Model("Alien Hives/Assault Grunts/Hormagaunt (45).glb");
        new UnitModelSettings { YawDegrees = 0 }.Save(grunt);       // downloaded
        new UnitModelSettings { YawDegrees = 90, FacingChecked = true }.SaveEdit(grunt); // turned in the facing review
        new ModelDownloadDefaults { YawDegrees = 180 }.Save();
        SharedModelSettings.Flush();
        Assert.That(File.ReadAllText(SharedModelSettings.ShippedPath), Does.Contain("Alien Hives/Assault Grunts/Hormagaunt (45).glb"));

        // The laptop: same game files, its own fresh download with the default facing.
        SharedModelSettings.OverrideSourceTree(false);
        SharedModelSettings.Forget();
        File.Delete(ModelDownloadDefaults.PathIn(Path.Combine(_dir, "UnitModels")));
        new UnitModelSettings { YawDegrees = 0, Notes = "laptop download" }.Save(grunt);
        var s = UnitModelSettings.Load(grunt)!;
        Assert.That(s.YawDegrees, Is.EqualTo(90));
        Assert.That(s.FacingChecked, Is.True);
        Assert.That(ModelDownloadDefaults.Load().YawDegrees, Is.EqualTo(180), "new downloads face Kai's way too");

        // Turned by hand on the laptop: that wins there.
        s.YawDegrees = 270;
        s.SaveEdit(grunt);
        Assert.That(UnitModelSettings.Load(grunt)!.YawDegrees, Is.EqualTo(270));
    }

    [Test]
    public void Harvest_CollectsEveryModelOnThePc_AndSkipsTheCache()
    {
        SharedModelSettings.OverrideSourceTree(true);
        string a = Model("Orcs/Boss/Warboss (1).glb");
        string b = Model("Terrain - Warhammer/Ruin (7).glb");
        string c = Model("_cache/123.glb");
        File.WriteAllText(UnitModelSettings.PathFor(a), "{\"YawDegrees\": 45}");
        File.WriteAllText(UnitModelSettings.PathFor(b), "{\"UpAxis\": \"ZUp\"}");
        File.WriteAllText(UnitModelSettings.PathFor(c), "{\"YawDegrees\": 10}");
        Assert.That(SharedModelSettings.Harvest(), Is.EqualTo(2));
        SharedModelSettings.OverrideSourceTree(false);
        SharedModelSettings.Forget();
        Assert.That(SharedModelSettings.For(a)!.YawDegrees, Is.EqualTo(45));
        Assert.That(SharedModelSettings.For(c), Is.Null);
    }

    [Test]
    public void Download_DoesNotWriteTheShippedFile()
    {
        SharedModelSettings.OverrideSourceTree(false);
        string a = Model("Orcs/Boss/Warboss (1).glb");
        new UnitModelSettings { YawDegrees = 45 }.SaveEdit(a);
        SharedModelSettings.Flush();
        Assert.That(File.Exists(SharedModelSettings.ShippedPath), Is.False);
    }

    [Test]
    public void PictureFraming_ShipsFromTheRepo_AndALocalFramingWins()
    {
        SharedModelSettings.OverrideSourceTree(true);
        PictureFraming.Set("Alien Hives", "Hive Lord", new PictureFraming.Frame(0.3f, 0.2f, 2f));
        Assert.That(File.Exists(PictureFraming.ShippedPath));

        // A download elsewhere: no framing.json of its own, Kai's framing shows.
        SharedModelSettings.OverrideSourceTree(false);
        File.Delete(PictureFraming.Path);
        PictureFraming.Forget();
        Assert.That(PictureFraming.For("Alien Hives", "Hive Lord").Zoom, Is.EqualTo(2f));
        PictureFraming.Set("Alien Hives", "Hive Lord", PictureFraming.Frame.Default);
        Assert.That(PictureFraming.For("Alien Hives", "Hive Lord").IsDefault, Is.True, "centred by hand here stays centred");
    }
}
