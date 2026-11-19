using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-18: friends get the Warhammer Map Builder mod's file from him (it's off Steam) and pick it by
/// hand. Only the real mod save is accepted, and it is copied where the terrain download looks for it.</summary>
[TestFixture]
public class WarhammerModFilePickTests
{
    private string _dir = "";

    [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "whpick-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
    [TearDown] public void TearDown() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Test]
    public void TheModsSave_IsCopiedToTheDestination()
    {
        string src = Path.Combine(_dir, "Downloads", "3372253478.json");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "{\"ObjectStates\":[{\"LuaScript\":\"objectJSONs = { [[{}]] }\"}]}");
        string dest = Path.Combine(_dir, "UnitModels", "_mods", "3372253478.json");

        WarhammerTerrain.UsePickedModFile(src, dest);

        Assert.That(File.ReadAllText(dest), Is.EqualTo(File.ReadAllText(src)));
    }

    [Test]
    public void AnyOtherJson_IsRefused_AndNothingIsCopied()
    {
        string src = Path.Combine(_dir, "forceorg-models.json");
        File.WriteAllText(src, "{\"models\":[]}");
        string dest = Path.Combine(_dir, "UnitModels", "_mods", "3372253478.json");

        Assert.Throws<IOException>(() => WarhammerTerrain.UsePickedModFile(src, dest));
        Assert.That(File.Exists(dest), Is.False);
    }
}
