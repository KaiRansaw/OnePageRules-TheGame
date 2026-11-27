using FDG;
using FDG.Data;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Lane 4 (2026-11-27): the optional rules' markers drawn on the table, with the rings their book text measures.</summary>
[TestFixture]
public class TableMarkersTests
{
    private static readonly EGameSystem[] Systems =
        { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish };

    [Test]
    public void EveryEventMarker_RingIsTheDistanceItsBookTextNames()
    {
        foreach (EGameSystem s in Systems)
            foreach (int code in new[] { 21, 31, 32, 33, 34, 35, 36 })
            {
                (float ring, TableMarkers.ERing kind, _) = TableMarkers.EventRing(code);
                string text = BattlefieldEffects.EventText(code, s);
                if (ring == 0)
                {
                    Assert.That(text, Does.Contain("line of sight"), $"{s} {code}: no ring only for the line-of-sight markers");
                    continue;
                }
                Assert.That(text, Does.Contain($"within {ring:0}\""), $"{s} {code}");
                if (kind == TableMarkers.ERing.Dangerous) Assert.That(text, Does.Contain("Dangerous terrain"), $"{s} {code}");
                if (kind == TableMarkers.ERing.Difficult) Assert.That(text, Does.Contain("Difficult terrain"), $"{s} {code}");
            }
        Assert.That(TableMarkers.EventRing(31).Ring, Is.EqualTo(3f), "Zombie Horde: the area within 3\" counts as Dangerous");
        Assert.That(TableMarkers.EventRing(35).Ring, Is.EqualTo(12f), "Ancient Robot: the closest unit within 12\"");
    }

    [Test]
    public void EveryLivingSpell_RingComesFromItsUnitsWithinClause()
    {
        foreach (EGameSystem s in Systems)
            foreach (LivingSpells.Def d in LivingSpells.Table(s))
            {
                (float n, TableMarkers.ERing kind) = TableMarkers.SpellRing(d);
                bool unitsWithin = d.Text.Contains("Units within") || d.Text.Contains("units within") || d.Text.Contains("unit ends its activation within");
                Assert.That(n > 0, Is.EqualTo(unitsWithin), $"{s} {d.Name}: ring only when units within a distance are affected");
                if (n > 0) Assert.That(d.Text, Does.Contain($"within {n:0}\""), $"{s} {d.Name}");
                if (n > 0) Assert.That(kind, Is.Not.EqualTo(TableMarkers.ERing.None), d.Name);
                Assert.That(TableMarkers.SpellLook(d.Name), Is.Not.EqualTo("spell"), $"{d.Name} has its own look");
            }
        var gates = LivingSpells.Find(EGameSystem.GrimdarkFuture, "Linked Jumpgates")!;
        Assert.That(TableMarkers.SpellRing(gates), Is.EqualTo((3f, TableMarkers.ERing.Reach)), "gates: within 3\", not the 24\" placing distance");
        Assert.That(TableMarkers.SpellRing(LivingSpells.Find(EGameSystem.GrimdarkFuture, LivingSpells.ArcanePalisade)!).Inches, Is.EqualTo(0f));
    }

    [Test]
    public void EveryArmyTerrainPiece_RingComesFromItsText()
    {
        foreach (string name in FDG.ArmyTerrain.Built)
        {
            (float n, _) = TableMarkers.ArmyRing(name);
            string text = TableMarkers.ArmyText(name, EGameSystem.GrimdarkFuture);
            if (name == FDG.ArmyTerrain.ArcanePillars) { Assert.That(n, Is.EqualTo(0f), "a line, not a ring"); continue; }
            Assert.That(n, Is.GreaterThan(0f), name);
            Assert.That(text, Does.Contain($"within {n:0}\""), name);
            Assert.That(TableMarkers.ArmyLook(name), Is.Not.EqualTo("army"), $"{name} has its own look");
        }
        Assert.That(TableMarkers.ArmyRing(FDG.ArmyTerrain.FactionRelic).Inches, Is.EqualTo(12f));
        Assert.That(TableMarkers.ArmyRing(FDG.ArmyTerrain.TwinVoidgates).Inches, Is.EqualTo(3f));
    }

    [Test]
    public void TheTable_ListsWhatTheRulesPutOnIt_WithRingsFromThePiecesEdge()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var table = new TableState(store);
        GameSettings settings = GameSettings.GetDefault();
        var progress = new GameProgressData(EResumeStage.MainPhase, 2, new List<int>(), new List<int>(), 0,
            new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), settings: settings)
        {
            Battlefield = new BattlefieldEffectsState(),
            LivingSpells = new LivingSpellsState(),
            ArmyTerrain = new ArmyTerrainState(),
            TacticalPhase = new TacticalPhaseState(),
        };
        progress.Battlefield.Markers.Add(new EventMarker { Code = 31, X = 18, Z = 12 });
        progress.LivingSpells.OnTable.Add(new LivingSpellPiece { Name = LivingSpells.BurningPyre, OwnerName = "Kai", X = 30, Z = 20 });
        progress.ArmyTerrain.Pieces.Add(new ArmyPiece { Name = FDG.ArmyTerrain.BattleForge, X = 10, Z = 5 });
        progress.TacticalPhase.Markers.Add(new ForwardMarker { Slot = 0, Number = 2, X = 40, Z = 1 });
        store.Create(progress);

        var markers = TableMarkers.For(table, EGameSystem.GrimdarkFuture);

        Assert.That(markers.Select(m => m.Kind), Is.EqualTo(new[]
        {
            TableMarkers.EKind.Event, TableMarkers.EKind.LivingSpell, TableMarkers.EKind.ArmyTerrain, TableMarkers.EKind.ForwardPosition,
        }));
        Assert.That(markers[0].Name, Is.EqualTo("The Walking Dead"));
        Assert.That((markers[0].X, markers[0].Z, markers[0].Ring), Is.EqualTo((18f, 12f, 3f)));
        Assert.That(markers[1].Ring, Is.EqualTo(LivingSpells.SmallRadius + 6f).Within(0.001f), "6\" from the spell's edge");
        Assert.That(markers[1].Label, Is.EqualTo("Burning Pyre (Kai)"));
        Assert.That(markers[2].Ring, Is.EqualTo(FDG.ArmyTerrain.PieceRadius + 6f).Within(0.001f));
        Assert.That(markers[3].Label, Is.EqualTo("Forward 2"), "not a bare number - objectives are numbered");
        Assert.That(markers.All(m => m.Text.All(c => c < 128)), Is.True, "game text is ASCII");

        progress.TacticalPhase.MarkersRemoved = true;
        Assert.That(TableMarkers.For(table, EGameSystem.GrimdarkFuture).Any(m => m.Kind == TableMarkers.EKind.ForwardPosition), Is.False,
            "forward positions leave the table before round 1");
    }

    [Test]
    public void QuestMarkers_AreNumberedPerObjective_AndCarriedOnesLeaveTheTable()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var table = new TableState(store);
        GameSettings settings = GameSettings.GetDefault();
        settings.GameSystem = EGameSystem.GFStarQuest;
        settings.GameMode = EGameMode.StarQuestMission;
        var q = new QuestProgress { Primary = 4, Secondary = 2 };
        q.Markers.Add(new QuestMarker { Id = 1, Kind = "objective", X = 10, Z = 10 });
        q.Markers.Add(new QuestMarker { Id = 2, Kind = "objective", X = 20, Z = 10 });
        q.Markers.Add(new QuestMarker { Id = 3, Kind = "objective", ForSecondary = true, Action = "seize", Activated = true, Tokens = 1, TokensNeeded = 3, X = 30, Z = 10 });
        q.Markers.Add(new QuestMarker { Id = 4, Kind = "objective", Action = "carry", CarriedBy = "someone", X = 40, Z = 10 });
        q.Markers.Add(new QuestMarker { Id = 5, Kind = "smoke", Action = "", RoundsLeft = 2, X = 5, Z = 5 });
        q.Tokens.Add(new QuestToken { Id = 7, X = 12, Z = 30 });
        var progress = new GameProgressData(EResumeStage.MainPhase, 1, new List<int>(), new List<int>(), 0,
            new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), settings: settings) { Quest = q };
        store.Create(progress);

        var markers = TableMarkers.For(table, EGameSystem.GFStarQuest);

        Assert.That(markers.Select(m => m.Name), Is.EqualTo(new[] { "Primary 1", "Primary 2", "Secondary 1 (1/3)", "Smoke", "Search 7" }));
        Assert.That(markers[0].Ring, Is.EqualTo(1f), "the Skill action's 1\" reach");
        Assert.That(markers[2].Ring, Is.EqualTo(3f), "a seized marker counts heroes within 3\"");
        Assert.That(markers[0].Text, Does.StartWith("Place two objective markers"), "primary 4's book text");
        Assert.That(markers[3].RingKind, Is.EqualTo(TableMarkers.ERing.Cover));
        Assert.That(markers[3].Text, Does.Contain("Within 3\" counts as cover").And.Contain("2 rounds left"));
        Assert.That(markers.All(m => m.Text.All(c => c < 128)), Is.True, "game text is ASCII");
    }
}
