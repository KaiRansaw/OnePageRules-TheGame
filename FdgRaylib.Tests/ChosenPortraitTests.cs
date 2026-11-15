using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-14: a portrait made on purpose for one model version is the one the game uses for it.</summary>
[TestFixture]
public class ChosenPortraitTests
{
    [Test]
    public void APortraitCountsAsChosen_OnlyOnceMarked_AndOnlyWhileItsFileExists()
    {
        string root = Path.Combine(Path.GetTempPath(), $"portraits-{Guid.NewGuid():N}");
        try
        {
            string model = Path.Combine(root, "Alien Hives", "Hive Lord", "Hive Tyrant (2827).glb");
            Directory.CreateDirectory(Path.GetDirectoryName(model)!);
            File.WriteAllText(model, "x");
            string pic = UnitPortraits.PathFor(model, root);
            Directory.CreateDirectory(Path.GetDirectoryName(pic)!);
            File.WriteAllText(pic, "x");
            Assert.That(UnitPortraits.IsChosen(model, root), Is.False, "made in bulk before 2.20: not chosen");
            UnitPortraits.MarkChosen(model, root);
            Assert.That(UnitPortraits.IsChosen(model, root), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(root, UnitPortraits.FolderName, UnitPortraits.ChosenFile)), Does.Contain("Alien Hives/Hive Lord/Hive Tyrant (2827).png"));
            File.Delete(pic);
            Assert.That(UnitPortraits.IsChosen(model, root), Is.False, "the picture is gone");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); UnitPortraits.Forget(); }
    }
}
