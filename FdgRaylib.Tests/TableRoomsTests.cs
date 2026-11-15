using FDG;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-14: a room around the table - "a few of each" (cozy, grimdark, fantasy).</summary>
[TestFixture]
public class TableRoomsTests
{
    [Test]
    public void ThreeOfEachKind_AllDifferent()
    {
        foreach (TableRooms.EKind k in Enum.GetValues<TableRooms.EKind>())
            Assert.That(TableRooms.All.Count(r => r.Kind == k), Is.EqualTo(3), k.ToString());
        Assert.That(TableRooms.All.Select(r => r.Id).Distinct().Count(), Is.EqualTo(TableRooms.All.Count));
        Assert.That(TableRooms.All.All(r => r.Name.All(c => c <= 0x7F)), Is.True, "game text is ASCII");
    }

    [Test]
    public void Auto_PicksADownloadedRoomOfTheGamesKind_NoneAndMissingGiveNoRoom()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"rooms-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.That(TableRooms.Pick(TableRooms.Auto, EGameSystem.GrimdarkFuture, dir), Is.Null, "nothing downloaded");
            var cellar = TableRooms.ById("drachenfels_cellar")!;
            File.WriteAllText(TableRooms.FileFor(cellar, dir), "x");
            Assert.That(TableRooms.Pick(TableRooms.Auto, EGameSystem.AoFSkirmish, dir), Is.EqualTo(cellar));
            Assert.That(TableRooms.Pick(TableRooms.Auto, EGameSystem.GFStarQuest, dir), Is.Null, "no grimdark room downloaded");
            Assert.That(TableRooms.Pick("drachenfels_cellar", EGameSystem.GrimdarkFuture, dir), Is.EqualTo(cellar), "a picked room is used for any game");
            Assert.That(TableRooms.Pick(TableRooms.None, EGameSystem.AgeOfFantasy, dir), Is.Null);
            Assert.That(TableRooms.Pick("workshop", EGameSystem.AgeOfFantasy, dir), Is.Null, "picked but not downloaded");
        }
        finally { Directory.Delete(dir, true); }
    }
}
