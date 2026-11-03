using System;
using System.IO;
using System.Linq;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-02: "when editing a map, changing the map's name doesn't show in the Map selection."
///
/// <para>
/// Two separate causes, and each would have hidden the other. The Map Maker's list read the FILENAME
/// rather than the map's own name, and <see cref="MapPaths.Save"/> overwrote the existing file without
/// ever renaming it. Fixing only the list would have left a Maps folder whose filenames disagree with
/// every name in the game - fine until a map is copied or sent to somebody. These cover the save half;
/// the list half is what the screen draws.
/// </para>
/// </summary>
[TestFixture]
public class MapRenameTests
{
    [Test]
    public void AMapKeptUnderTheSameNameIsNotRenamed()
    {
        Assert.That(MapPaths.WantsRenaming(Path.Combine("Maps", "Crossroads.fdgterrain"), "Crossroads"),
            Is.False);
    }

    [Test]
    public void CaseAloneIsNotARename()
    {
        // Windows filenames are case-insensitive, so renaming "crossroads" to "Crossroads" would try to
        // write the new file and then delete it as the old one.
        Assert.That(MapPaths.WantsRenaming(Path.Combine("Maps", "Crossroads.fdgterrain"), "crossroads"),
            Is.False);
    }

    [Test]
    public void ARealRenameIsOne()
    {
        Assert.That(MapPaths.WantsRenaming(Path.Combine("Maps", "New Map.fdgterrain"), "Ruins"),
            Is.True, "this is Kai's exact case - a map still called New Map on disk.");
    }

    [Test]
    public void ANameThatCannotBeAFilenameDoesNotRenameForever()
    {
        // The load-bearing one. "Hill 203: Dawn" sanitises to "Hill 203_ Dawn", so comparing the raw
        // name against the filename would differ on EVERY save - renaming the file, every time, for a
        // map whose name never changed.
        string path = Path.Combine("Maps", "Hill 203_ Dawn.fdgterrain");
        Assert.That(MapPaths.WantsRenaming(path, "Hill 203: Dawn"), Is.False);
    }

    [Test]
    public void AMapNameIsSanitisedTheSameWayOnEveryPlatform()
    {
        // A colon is legal in a Linux filename and illegal in a Windows one, so asking the host what
        // is invalid gives a different answer per machine - and a map saved here would be unopenable
        // on Kai's. The scrub is Windows' rules everywhere, because maps get copied around.
        Assert.That(MapPaths.Sanitize("Hill 203: Dawn"), Is.EqualTo("Hill 203_ Dawn"));
        Assert.That(MapPaths.Sanitize("A/B"), Is.EqualTo("A_B"));
        Assert.That(MapPaths.Sanitize("What?"), Is.EqualTo("What_"));
        Assert.That(MapPaths.Sanitize("Trailing dot."), Is.EqualTo("Trailing dot"));
    }

    [Test]
    public void AReservedDeviceNameIsNotUsedAsAFilename()
    {
        // CON, NUL, COM1 and friends cannot be filenames on Windows whatever extension follows.
        Assert.That(MapPaths.Sanitize("CON"), Is.EqualTo("_CON"));
        Assert.That(MapPaths.Sanitize("nul"), Is.EqualTo("_nul"));
        Assert.That(MapPaths.Sanitize("Console"), Is.EqualTo("Console"), "only the exact names.");
    }

    [Test]
    public void AnEmptyNameLeavesTheFileAlone()
    {
        // The editor refuses to save a nameless map, but Save is public and a blank name must never
        // mean "rename this to nothing".
        Assert.That(MapPaths.WantsRenaming(Path.Combine("Maps", "Crossroads.fdgterrain"), "   "),
            Is.False);
    }

    /// <summary>
    /// End to end on a real folder: save a map, rename it, and the old file must be gone and the new
    /// one there. Run against the real Maps folder the game uses, with a name nothing else would pick,
    /// and cleaned up afterwards - the alternative is testing a copy of the logic rather than the
    /// logic.
    /// </summary>
    [Test]
    public void RenamingAMapMovesItsFile()
    {
        string first = "zz Test Map " + Guid.NewGuid().ToString("N")[..8];
        string second = first + " Renamed";
        string? firstPath = null, secondPath = null;

        try
        {
            firstPath = MapPaths.Save(new TerrainLayoutFile { Name = first }, null);
            Assert.That(File.Exists(firstPath), Is.True);
            Assert.That(Path.GetFileNameWithoutExtension(firstPath), Is.EqualTo(first),
                "a new map's file is named after it.");

            // Same map, new name - what the editor does when you type in the name box and hit Save.
            secondPath = MapPaths.Save(new TerrainLayoutFile { Name = second }, firstPath);

            Assert.That(Path.GetFileNameWithoutExtension(secondPath), Is.EqualTo(second));
            Assert.That(File.Exists(secondPath), Is.True, "the renamed map must exist.");
            Assert.That(File.Exists(firstPath), Is.False, "and the old file must not be left behind.");

            TerrainLayoutFile? reloaded = TerrainLayoutLoader.TryLoadFromFile(secondPath, out string? error);
            Assert.That(reloaded, Is.Not.Null, error);
            Assert.That(reloaded!.Name, Is.EqualTo(second));

            Assert.That(MapPaths.List().Contains(secondPath), Is.True,
                "and it must appear in the list the Map Maker and the picker both read.");
        }
        finally
        {
            foreach (string? path in new[] { firstPath, secondPath })
            {
                try { if (path != null && File.Exists(path)) File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Saving a map under a name another map already has must not overwrite that map.</summary>
    [Test]
    public void RenamingOntoATakenNameNumbersItInsteadOfClobbering()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string taken = $"zz Taken {tag}";
        string? takenPath = null, minePath = null, renamedPath = null;

        try
        {
            takenPath = MapPaths.Save(new TerrainLayoutFile { Name = taken }, null);
            minePath = MapPaths.Save(new TerrainLayoutFile { Name = $"zz Mine {tag}" }, null);

            renamedPath = MapPaths.Save(new TerrainLayoutFile { Name = taken }, minePath);

            Assert.That(renamedPath, Is.Not.EqualTo(takenPath),
                "the other map must still be there - a rename is not a licence to overwrite.");
            Assert.That(File.Exists(takenPath), Is.True);

            TerrainLayoutFile? other = TerrainLayoutLoader.TryLoadFromFile(takenPath, out _);
            Assert.That(other, Is.Not.Null);
        }
        finally
        {
            foreach (string? path in new[] { takenPath, minePath, renamedPath })
            {
                try { if (path != null && File.Exists(path)) File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
