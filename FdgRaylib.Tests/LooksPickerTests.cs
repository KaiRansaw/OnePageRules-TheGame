using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FdgRaylib.Forge;
using FdgRaylib.Horde;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Looks screen's picking and giving (<see cref="LooksPicker"/>, Kai 2026-11-27, mockup A "Squad on the table"):
/// pick, add / take away, box, same-look pick, give to the picked, mix evenly, picks that survive a screen rebuild,
/// save and reload, combined units and the Horde Shop's recruits.
/// </summary>
[TestFixture]
public class LooksPickerTests
{
    private static readonly string[] V = { "/m/U/a.glb", "/m/U/b.glb", "/m/U/c.glb" };

    private static (ForgeSession S, int Row) TenModels()
    {
        var s = new ForgeSession(new List<BookFile> { DemoBook.Build() });
        int row = s.Add("warriors");
        RosterUnit roster = s.Roster("warriors")!;
        int n = s.Detail(row).Unit.ModelCount;
        if (n < 10 && roster.Sections.FirstOrDefault(x => x.Variant == UpgradeVariant.AddModels) is { } grow)
            s.Choose(row, grow, grow.Options[0].Id, 10 - n);
        return (s, row);
    }

    [Test]
    public void Click_PicksOne_AndShiftOrCtrlAddsOrTakesAway()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.Click(2, add: false);
        Assert.That(p.Picked, Is.EqualTo(new[] { 2 }));
        p.Click(5, add: true);
        p.Click(7, add: true);
        Assert.That(p.Picked, Is.EqualTo(new[] { 2, 5, 7 }));
        p.Click(5, add: true);
        Assert.That(p.Picked, Is.EqualTo(new[] { 2, 7 }), "a held click on a picked model takes it away");
        p.Click(4, add: false);
        Assert.That(p.Picked, Is.EqualTo(new[] { 4 }), "a plain click picks just that model");
        Assert.That(p.Anchor, Is.EqualTo(4));
        p.Click(99, add: false);
        Assert.That(p.Picked, Is.EqualTo(new[] { 4 }), "a model the unit doesn't have changes nothing");
    }

    [Test]
    public void Box_PicksWhatIsInside_ReplacingOrAdding()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.Click(0, add: false);
        p.Box(new[] { 3, 4, 8 }, add: false);
        Assert.That(p.Picked, Is.EqualTo(new[] { 3, 4, 8 }));
        p.Box(new[] { 1, 42 }, add: true);
        Assert.That(p.Picked, Is.EqualTo(new[] { 1, 3, 4, 8 }));
        p.Box(Array.Empty<int>(), add: false);
        Assert.That(p.Picked, Is.Empty, "an empty box on its own clears the picks");
        p.PickAll();
        Assert.That(p.Picked, Has.Count.EqualTo(s.Slots(row).Count));
        p.PickNone();
        Assert.That(p.Picked, Is.Empty);
    }

    [Test]
    public void Give_GoesToEveryPickedModel_AndOnlyThem()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.PickExactly(new[] { 2, 3, 6, 7 });
        Assert.That(p.Give(1, V), Is.EqualTo(4));
        Assert.That(s.List.Units[row].ModelLooks, Is.EqualTo(new List<string?> { null, null, "b.glb", "b.glb", null, null, "b.glb", "b.glb" }));
        Assert.That(p.Wearing(1, V), Is.EqualTo(4));
        Assert.That(p.Wearing(0, V), Is.EqualTo(s.Slots(row).Count - 4), "unpicked models wear the first version");
        Assert.That(p.PickedVersion(V), Is.EqualTo(1), "the card on every picked model is outlined");
        Assert.That(p.Picked, Is.EqualTo(new[] { 2, 3, 6, 7 }), "giving a look keeps the picks");

        p.Click(0, add: true);
        Assert.That(p.PickedVersion(V), Is.EqualTo(-1), "picked models wearing different versions outline no card");
        p.Give(0, V);
        Assert.That(s.List.Units[row].ModelLooks, Is.Null, "everyone back on the first version stores nothing");

        p.PickNone();
        Assert.That(p.Give(2, V), Is.Zero, "nothing picked, nothing given");
        Assert.That(s.List.Units[row].ModelLooks, Is.Null);
    }

    [Test]
    public void DoubleClick_PicksEveryModelWithTheSameLook()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.PickExactly(new[] { 1, 4, 5 });
        p.Give(2, V);
        p.PickSameLook(4, V);
        Assert.That(p.Picked, Is.EqualTo(new[] { 1, 4, 5 }));
        p.PickSameLook(0, V);
        Assert.That(p.Picked, Does.Not.Contain(1).And.Not.Contain(4).And.Not.Contain(5));
        Assert.That(p.Picked, Has.Count.EqualTo(s.Slots(row).Count - 3), "the first-version models, picked or stored");
    }

    [Test]
    public void MixEvenly_DealsTheVersionsRoundTheSquad_TheSameEveryTime()
    {
        var (s, row) = TenModels();
        int n = s.Slots(row).Count;
        Assert.That(n, Is.GreaterThanOrEqualTo(6), "the demo unit grows to a real squad");
        var p = new LooksPicker(s, row);
        p.MixEvenly(V);
        var looks = s.Slots(row).Select(x => LooksPicker.FileOf(x, V)).ToList();
        for (int i = 0; i < n; i++) Assert.That(looks[i], Is.EqualTo(V[i % 3]), $"model {i + 1}");
        int[] counts = Enumerable.Range(0, 3).Select(v => p.Wearing(v, V)).ToArray();
        Assert.That(counts.Max() - counts.Min(), Is.LessThanOrEqualTo(1), "as even as the squad allows");
        Assert.That(counts.Sum(), Is.EqualTo(n));

        var first = s.Slots(row).Select(x => x.Look).ToList();
        p.PickExactly(new[] { 0 });
        p.Give(2, V);
        p.MixEvenly(V);
        Assert.That(s.Slots(row).Select(x => x.Look), Is.EqualTo(first), "the same squad always gets the same mix");

        p.MixEvenly(new[] { V[0] });
        Assert.That(s.List.Units[row].ModelLooks, Is.Null, "one version: everyone wears it, nothing stored");
    }

    [Test]
    public void Picks_SurviveARebuild_AndFollowTheUnitWhenRowsMove()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.PickExactly(new[] { 1, 2 });
        // The screen is rebuilt (Save does this) - the host keeps the same picker, so the picks are still there.
        Assert.That(p.Picked, Is.EqualTo(new[] { 1, 2 }));
        // A unit added before it moves its row number; the picker follows the unit, not the number.
        int other = s.Add("warriors");
        s.List.Units.Insert(0, s.List.Units[other]);
        s.List.Units.RemoveAt(other + 1);
        Assert.That(p.Row, Is.EqualTo(row + 1));
        p.Give(1, V);
        Assert.That(s.List.Units[row + 1].ModelLooks![1], Is.EqualTo("b.glb"));
        Assert.That(s.List.Units[0].ModelLooks, Is.Null, "the unit that moved in front is untouched");
        s.Remove(p.Row);
        Assert.That(p.Valid, Is.False);
        Assert.That(p.Picked, Is.Empty);
        Assert.That(p.Give(1, V), Is.Zero);
    }

    [Test]
    public void Looks_SaveAndReload_RoundTrip()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        p.MixEvenly(V);
        p.PickExactly(new[] { 0, 1 });
        p.Give(2, V);
        List<string?> want = s.Slots(row).Select(x => x.Look).ToList();

        string path = Path.Combine(Path.GetTempPath(), $"looks-{Guid.NewGuid():N}.fdgarmy");
        try
        {
            BuiltArmyFile compiled = s.Compile();
            File.WriteAllText(path, JsonSerializer.Serialize(compiled, compiled.GetType(), RuleJson.Options));
            ForgeSession.LoadResult read = ForgeSession.Read(path);
            Assert.That(read.Problem, Is.Null);
            Assert.That(read.File!.Units.Single().ModelLooks, Is.EqualTo(want), "the table copy carries every model's look");
            var reopened = new ForgeSession(new List<BookFile> { DemoBook.Build() });
            reopened.Adopt(read.File!);
            Assert.That(reopened.Slots(0).Select(x => x.Look), Is.EqualTo(want), "the editable list carries them too");
            var again = new LooksPicker(reopened, 0);
            Assert.That(again.Wearing(2, V), Is.EqualTo(want.Count(l => l == "c.glb")));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void CombinedUnit_BothHalves_FromEitherHalf_LandOnTheRightTableModels()
    {
        var s = new ForgeSession(new List<BookFile> { DemoBook.Build() });
        int row = s.Add("warriors");
        int n = s.Detail(row).Unit.ModelCount;
        s.SetCombined(row, true);
        int host = s.List.Units.FindIndex(u => u.CombinedWithId == null);
        int spawned = s.PartnerOf(host);

        // Opened from the second half: still both halves, the first copy first.
        var p = new LooksPicker(s, spawned);
        Assert.That(p.Slots(), Has.Count.EqualTo(n * 2));
        p.PickExactly(new[] { 0, n, n * 2 - 1 });
        p.Give(1, V);
        Assert.That(s.List.Units[host].ModelLooks![0], Is.EqualTo("b.glb"));
        Assert.That(s.List.Units[spawned].ModelLooks![0], Is.EqualTo("b.glb"));
        Assert.That(s.List.Units[spawned].ModelLooks![n - 1], Is.EqualTo("b.glb"));

        p.MixEvenly(V);
        List<string?> merged = s.Compile().Units.Single(u => u.ModelCount == n * 2).ModelLooks!;
        for (int i = 0; i < n * 2; i++)
            Assert.That(LooksPicker.FileOf(new ForgeSession.ModelSlot(0, i, merged[i], Array.Empty<string>()), V), Is.EqualTo(V[i % 3]),
                $"table model {i + 1} wears what the screen showed as Model {i + 1}");
    }

    [Test]
    public void HordeShopRecruit_LooksGoWithItIntoTheCartAndTheReply()
    {
        var request = new HordeShopRequest(new PlayerID(Guid.NewGuid()), 1, 300, "Dark Vanguard", "Test",
            new List<HordeShopObjective> { new(1, 18, 16, true) }) { PointsEarned = 300, PointsPerObjective = 40 };
        var shop = new HordeShopSession(request, new[] { DemoBook.Build() });
        ForgeSession draft = shop.Draft("warriors");
        var p = new LooksPicker(draft, 0);
        p.PickExactly(new[] { 1 });
        p.Give(2, V);
        shop.AddRecruit(draft.List.Units[0], 1);
        Assert.That(shop.Cart.List.Units[0].ModelLooks![1], Is.EqualTo("c.glb"));

        // Looks chosen on a recruit already in the cart stick to that recruit.
        var inCart = new LooksPicker(shop.Cart, 0);
        inCart.PickExactly(new[] { 0 });
        inCart.Give(1, V);
        var reply = shop.Reply();
        var army = JsonSerializer.Deserialize<ArmyListFile>(reply.RecruitsJson!, RuleJson.Options)!;
        Assert.That(army.Units[0].ModelLooks![0], Is.EqualTo("b.glb"));
        Assert.That(army.Units[0].ModelLooks![1], Is.EqualTo("c.glb"));
    }

    [Test]
    public void FocusVersion_FollowsTheLastClickedPickedModel()
    {
        var (s, row) = TenModels();
        var p = new LooksPicker(s, row);
        Assert.That(p.FocusVersion(V), Is.EqualTo(-1));
        p.PickExactly(new[] { 3 });
        p.Give(2, V);
        p.Click(1, add: true);
        Assert.That(p.FocusVersion(V), Is.EqualTo(0), "the last-clicked model wears the first version");
        p.Click(1, add: true); // taken away again
        Assert.That(p.FocusVersion(V), Is.EqualTo(2), "back to the first picked model");
        Assert.That(p.PickedLine(), Is.EqualTo("1 model picked"));
    }

    [Test]
    public void Formation_IsATidyBlock_ModelOneBackLeft_NoBasesTouching()
    {
        Assert.That(SquadFormation.Columns(3), Is.EqualTo(3));
        Assert.That(SquadFormation.Columns(10), Is.EqualTo(5));
        Assert.That(SquadFormation.Columns(20), Is.EqualTo(7));
        var spots = SquadFormation.Spots(10, 1f);
        Assert.That(spots, Has.Count.EqualTo(10));
        Assert.That(spots[0].X, Is.LessThan(spots[4].X));
        Assert.That(spots[0].Z, Is.LessThan(spots[5].Z), "model 6 is in the front row");
        Assert.That(spots.Average(p => p.X), Is.EqualTo(0).Within(1e-4));
        Assert.That(spots.Average(p => p.Z), Is.EqualTo(0).Within(1e-4));
        for (int i = 0; i < spots.Count; i++)
            for (int j = i + 1; j < spots.Count; j++)
                Assert.That(Math.Sqrt(Math.Pow(spots[i].X - spots[j].X, 2) + Math.Pow(spots[i].Z - spots[j].Z, 2)), Is.GreaterThan(1.0));
        var odd = SquadFormation.Spots(7, 1f);
        Assert.That(odd[5].X + odd[6].X, Is.EqualTo(0).Within(1e-4), "a short last row is centred");
    }
}
