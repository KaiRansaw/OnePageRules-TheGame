using System.Linq;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #221: lobby colour picker palette + pick resolution. Pure logic - the dropdown UI and launch wiring
// consume Options/ResolveIndices; only those are pinned here.
[TestFixture]
public class PlayerColorOptionsTests
{
    [Test]
    public void Options_AreEightDistinctNames_StartingWithTheLegacyDefaults()
    {
        Assert.That(PlayerColorOptions.Count, Is.EqualTo(8));
        Assert.That(PlayerColorOptions.Options.Select(o => o.Name).Distinct().Count(), Is.EqualTo(8));

        // Order is load-bearing: slot defaults consume Options in order, so the first four must keep the
        // pre-picker assignments (P1 orange, P2 purple, P3 green, P4 yellow) for an untouched lobby.
        Assert.That(PlayerColorOptions.Options[0].Name, Is.EqualTo("Orange"));
        Assert.That(PlayerColorOptions.Options[1].Name, Is.EqualTo("Purple"));
        Assert.That(PlayerColorOptions.Options[2].Name, Is.EqualTo("Green"));
        Assert.That(PlayerColorOptions.Options[3].Name, Is.EqualTo("Yellow"));
    }

    [Test]
    public void Resolve_NoPicks_AssignsPaletteInOrder()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, null, null, null });
        Assert.That(r, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Resolve_ExplicitPickAlwaysWins()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { 7, null });
        Assert.That(r[0], Is.EqualTo(7), "picked pink stays pink");
        Assert.That(r[1], Is.EqualTo(1), "unpicked slot keeps its own slot colour (purple)");
    }

    [Test]
    public void Resolve_DefaultsAreSlotAnchored_PickingAFreeColourShiftsNobody()
    {
        // P1 picks Red, freeing Orange. P2 and P3 must KEEP purple/green - a default only moves when its
        // colour is actually taken, never because a lower slot vacated its own.
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { 4, null, null });
        Assert.That(r, Is.EqualTo(new[] { 4, 1, 2 }));
    }

    [Test]
    public void Resolve_PickCollidingWithAnothersDefault_BumpsTheDefaultDeterministically()
    {
        // The dropdown (IsTakenByAnother) refuses picks of anyone's current colour, so this input only
        // arises from stale/raced state - the resolver's fallback keeps it deterministic: the explicit
        // pick holds and the defaulted slot shifts to the next free colour.
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, 0 });
        Assert.That(r[1], Is.EqualTo(0), "explicit pick holds");
        Assert.That(r[0], Is.EqualTo(1), "bumped to the next free (purple)");
    }

    [Test]
    public void IsTakenByAnother_ReservesDefaultsAndPicksAlike()
    {
        // Two untouched slots: effective = [orange, purple]. Row 1 may not take orange (row 0's DEFAULT
        // is reserved - no stealing), may keep its own purple, and may take any free colour.
        int[] effective = PlayerColorOptions.ResolveIndices(new int?[] { null, null });
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 0), Is.True, "another row's default is taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 1), Is.False, "own current colour is not taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 5), Is.False, "a free colour is not taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 0, 1), Is.True, "symmetric for the other row");
    }

    [Test]
    public void Resolve_DefaultsNeverCollideWithPicksOrEachOther()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, 2, null, 0 });
        Assert.That(r, Is.EqualTo(new[] { 1, 2, 3, 0 }));
        Assert.That(r.Distinct().Count(), Is.EqualTo(4), "all four distinct");
    }

    [Test]
    public void Resolve_OutOfRangePickIsTreatedAsUnpicked()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { 99, -1 });
        Assert.That(r, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Resolve_MorePlayersThanColours_WrapsInsteadOfThrowing()
    {
        var chosen = new int?[10];
        int[] r = PlayerColorOptions.ResolveIndices(chosen);
        Assert.That(r.Take(8), Is.EqualTo(Enumerable.Range(0, 8)), "first eight get the palette");
        Assert.That(r[8], Is.EqualTo(0), "ninth wraps to its slot colour");
        Assert.That(r[9], Is.EqualTo(1));
    }

    // ── Choose Color wheel (Kai, 2026-11-05) ──────────────────────────────────────────────────────────

    [Test]
    public void WheelColour_RoundTripsThroughTheOneSyncedInt()
    {
        int code = PlayerColorOptions.EncodeCustom(12, 200, 99);
        Assert.That(PlayerColorOptions.IsValidCode(code), Is.True);
        var c = PlayerColorOptions.ColorFor(code);
        Assert.That((c.R, c.G, c.B), Is.EqualTo(((byte)12, (byte)200, (byte)99)));
        Assert.That(code, Is.GreaterThanOrEqualTo(PlayerColorOptions.Count), "never mistaken for a palette index");
    }

    [Test]
    public void WheelColour_EvenPureBlack_IsNotTheNoPickValue()
    {
        int code = PlayerColorOptions.EncodeCustom(0, 0, 0);
        Assert.That(code, Is.Not.EqualTo(-1));
        Assert.That(code, Is.GreaterThan(0));
    }

    [Test]
    public void OldPaletteIndices_StillMeanWhatTheyMeant()
    {
        for (int i = 0; i < PlayerColorOptions.Count; i++)
            Assert.That(PlayerColorOptions.ColorFor(i), Is.EqualTo(PlayerColorOptions.Options[i].Color));
    }

    [Test]
    public void Resolve_KeepsAWheelPick()
    {
        int code = PlayerColorOptions.EncodeCustom(10, 20, 30);
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { code, null });
        Assert.That(r[0], Is.EqualTo(code));
    }

    [Test]
    public void Resolve_ADefaultNeverLandsOnANearTwinOfAWheelPick()
    {
        // Slot 1 picks almost-purple; slot 0's default is orange and stays; slot 2 would default to
        // purple (index 1) but must skip it now.
        int nearPurple = PlayerColorOptions.EncodeCustom(152, 72, 198);
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, nearPurple, null });
        Assert.That(r[0], Is.EqualTo(0));
        Assert.That(r[2], Is.Not.EqualTo(1));
        Assert.That(PlayerColorOptions.Difference(PlayerColorOptions.ColorFor(r[2]),
            PlayerColorOptions.ColorFor(nearPurple)), Is.GreaterThanOrEqualTo(PlayerColorOptions.MinDifference));
    }

    [Test]
    public void Clash_ANearIdenticalColourToAnotherSeat_IsRefused_AndYourOwnIsNot()
    {
        int[] effective = PlayerColorOptions.ResolveIndices(new int?[] { null, null }); // orange, purple
        var nearOrange = new Raylib_cs.Color(250, 165, 10, 255);
        Assert.That(PlayerColorOptions.ClashingRow(effective, 1, nearOrange), Is.EqualTo(0));
        Assert.That(PlayerColorOptions.ClashingRow(effective, 0, nearOrange), Is.EqualTo(-1),
            "moving your own colour slightly is fine");
        var blue = new Raylib_cs.Color(0, 121, 241, 255);
        Assert.That(PlayerColorOptions.ClashingRow(effective, 1, blue), Is.EqualTo(-1));
    }

    [Test]
    public void ThePalettesOwnColours_AreAllFarEnoughApart()
    {
        var o = PlayerColorOptions.Options;
        for (int i = 0; i < o.Length; i++)
        for (int j = i + 1; j < o.Length; j++)
            Assert.That(PlayerColorOptions.Difference(o[i].Color, o[j].Color),
                Is.GreaterThanOrEqualTo(PlayerColorOptions.MinDifference), $"{o[i].Name} vs {o[j].Name}");
    }

    [Test]
    public void TheLobbyOffersTheWheel_NotTheOldDropdown()
    {
        var dir = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "FdgRaylib", "Rendering", "LobbyScreen.cs"));
        Assert.That(src, Does.Contain("\"Choose Color##colorbtn"));
        Assert.That(src, Does.Contain("ImGuiColorEditFlags.PickerHueWheel"));
        Assert.That(src, Does.Contain("PlayerColorOptions.EncodeCustom"));
    }

    /// <summary>
    /// Kai, 2026-11-05: "no color choice is showing". The team dropdown fills the rest of its line, so a
    /// colour control placed beside it (SameLine) is pushed off the card and clipped. It must start its
    /// own line.
    /// </summary>
    [Test]
    public void TheColourControl_IsNotSqueezedOntoTheTeamLine()
    {
        var dir = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "FdgRaylib", "Rendering", "LobbyScreen.cs"));
        int call = src.IndexOf("DrawColorCell(seat, info, players, effectiveColor);", System.StringComparison.Ordinal);
        int team = src.LastIndexOf("DrawTeamCell(seat, info, players.Count);", call, System.StringComparison.Ordinal);
        Assert.That(call, Is.GreaterThan(0));
        Assert.That(team, Is.GreaterThan(0));
        Assert.That(src.Substring(team, call - team), Does.Not.Contain("SameLine"));
    }
}
