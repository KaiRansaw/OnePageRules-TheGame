using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The two rules behind the seat grid's Active switches (Kai's mock, 2026-11-02). Both exist because
/// the obvious behaviour would look like a broken button rather than a rule, and both are pure so they
/// can be checked without an ImGui frame.
///
/// <para>
/// The engine's roster is a <i>list</i>, not four fixed slots, which is the whole source of the
/// awkwardness: adding appends and removing closes the gap. The grid has to present that as four
/// squares without ever letting a click land somewhere other than the square you clicked.
/// </para>
/// </summary>
[TestFixture]
public class LobbySeatGridTests
{
    [Test]
    public void SeatsOneAndTwoNeverSwitchOff()
    {
        // A game needs two sides. There is no "1v0".
        for (int count = 2; count <= LobbyScreen.SeatGridSize; count++)
        {
            Assert.That(LobbyScreen.SeatCanSwitchOff(0, count), Is.False, $"seat 1 of {count}");
            Assert.That(LobbyScreen.SeatCanSwitchOff(1, count), Is.False, $"seat 2 of {count}");
        }
    }

    [Test]
    public void OnlyTheLastSeatSwitchesOff()
    {
        // The load-bearing one. With four seats, switching off seat 3 would remove it from the list and
        // slide seat 4 up into seat 3's square: the card you clicked would still be full (with somebody
        // else in it) and a card you never touched would empty. So seat 3 waits for seat 4 to go first.
        Assert.That(LobbyScreen.SeatCanSwitchOff(2, 4), Is.False, "seat 3 while seat 4 is on");
        Assert.That(LobbyScreen.SeatCanSwitchOff(3, 4), Is.True, "seat 4 is the last one");

        Assert.That(LobbyScreen.SeatCanSwitchOff(2, 3), Is.True,
            "once seat 4 is off, seat 3 is the last one and may go too - so 1v1 stays reachable.");
    }

    [Test]
    public void SeatsFillFromTheLeft()
    {
        // The mirror image. Switching on seat 4 while 3 is off would append the new player at index 2,
        // filling seat 3 and leaving the switch actually clicked still off.
        Assert.That(LobbyScreen.SeatCanSwitchOn(3, 2), Is.False, "seat 4 while seat 3 is off");
        Assert.That(LobbyScreen.SeatCanSwitchOn(2, 2), Is.True, "seat 3 is next");
        Assert.That(LobbyScreen.SeatCanSwitchOn(3, 3), Is.True, "and then seat 4");
    }

    [Test]
    public void TheGridDoesNotOfferAFifthSeat()
    {
        // Four squares is the design. A fifth seat can still EXIST - a loaded save or a networked lobby
        // may hold more, and DrawPlayerList lists those under the grid - but no switch creates one.
        Assert.That(LobbyScreen.SeatCanSwitchOn(LobbyScreen.SeatGridSize, LobbyScreen.SeatGridSize),
            Is.False);
    }

    /// <summary>
    /// Every count from 2 to 4 is reachable by switching, in both directions, without a click ever
    /// being refused at a point where the grid still shows an enabled switch. Walked rather than
    /// spot-checked: the two rules above are each simple, and it is their interaction that decides
    /// whether somebody can actually get from a 1v1 to a four-player game and back.
    /// </summary>
    [Test]
    public void EverySeatCountIsReachableInBothDirections()
    {
        int count = 2;
        while (count < LobbyScreen.SeatGridSize)
        {
            Assert.That(LobbyScreen.SeatCanSwitchOn(count, count), Is.True,
                $"stuck at {count} seats - nothing could be switched on.");
            count++;
        }
        Assert.That(count, Is.EqualTo(LobbyScreen.SeatGridSize));

        while (count > 2)
        {
            Assert.That(LobbyScreen.SeatCanSwitchOff(count - 1, count), Is.True,
                $"stuck at {count} seats - nothing could be switched off.");
            count--;
        }
        Assert.That(count, Is.EqualTo(2), "and back down to a 1v1.");
    }
}
