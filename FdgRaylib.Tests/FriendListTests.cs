using FdgRaylib.Config;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The Join a Game friends list: pasted addresses cleaned up, favourites first, one entry per address.</summary>
[TestFixture]
public class FriendListTests
{
    [TestCase("marcus-pc.tail1234.ts.net", "marcus-pc.tail1234.ts.net")]
    [TestCase("  https://marcus-pc.tail1234.ts.net/  ", "marcus-pc.tail1234.ts.net")]
    [TestCase("http://100.101.102.103:7777/lobby?x=1", "100.101.102.103")]
    [TestCase("\"marcus-pc.tail1234.ts.net.\"", "marcus-pc.tail1234.ts.net")]
    public void PastedAddress_IsCleanedForJoin(string pasted, string expected) =>
        Assert.That(FriendList.CleanAddress(pasted), Is.EqualTo(expected));

    [Test]
    public void Add_SortsFavouritesFirst_AndRenamesASecondEntryForTheSamePc()
    {
        var friends = new List<FriendEntry>();
        Assert.That(FriendList.Add(friends, "Marcus", "https://marcus-pc.tail1.ts.net/"), Is.Null);
        Assert.That(FriendList.Add(friends, "dre", "dre-laptop.tail1.ts.net"), Is.Null);
        Assert.That(FriendList.Add(friends, "", "zane-pc.tail1.ts.net"), Is.Null);
        Assert.That(friends.Single(f => f.Address == "zane-pc.tail1.ts.net").Name, Is.EqualTo("zane-pc"), "no name = the PC name");
        Assert.That(FriendList.Add(friends, "Marc", "MARCUS-PC.tail1.ts.net"), Is.Null);
        Assert.That(friends, Has.Count.EqualTo(3));
        Assert.That(friends.Single(f => f.Address == "marcus-pc.tail1.ts.net").Name, Is.EqualTo("Marc"));

        friends.Single(f => f.Name == "zane-pc").Favorite = true;
        Assert.That(FriendList.Sorted(friends).Select(f => f.Name), Is.EqualTo(new[] { "zane-pc", "dre", "Marc" }));
        Assert.That(FriendList.Add(friends, "x", "   "), Is.Not.Null);
        Assert.That(FriendList.Add(friends, "x", "two words"), Is.Not.Null);
    }

    [TestCase("100.101.102.103:7777")]
    [TestCase("http://100.101.102.103/")]
    [TestCase(" 100.101.102.103 ")]
    public async Task Join_LooksUpAPastedAddress(string pasted)
    {
        var ip = await FdgRaylib.Lobby.LobbySession.ResolveAsync(pasted);
        Assert.That(ip?.ToString(), Is.EqualTo("100.101.102.103"));
    }
}
