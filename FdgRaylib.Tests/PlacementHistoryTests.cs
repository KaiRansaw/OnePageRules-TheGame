using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #343 — deployment undo at ACTION granularity. The placed list is roster-ordered, not chronological,
// so the old "pop the last list entry" Undo deleted one model out of a group drop (stranding the rest
// with no way back to the formation ghost) and, after a drag-edit, deleted a DIFFERENT model than the
// one just touched. These tests pin the inversion rules: each recorded gesture undoes as one step, a
// drag restores the pre-drag entry (position AND facing), a group drop empties the list and hands back
// its ghost rotation, and Restart round-trips the cleared snapshot by copy.
//
// The drawing/gesture wiring is ImGui (hand-verified); what lives here is the arithmetic that fails
// silently — same split as ModelRosterTests / PlacementPanelLayout.
[TestFixture]
public class PlacementHistoryTests
{
    // The history never reads the binding, so tests use entries with a null one (T = int keeps the
    // engine's ModelData out of what is purely list arithmetic).
    private static PlacedObjectEntry<int> Entry(float x, float z, Float2? facing = null) =>
        new(null!, new Position(x, z), facing);

    [Test]
    public void TryUndo_WithEmptyHistory_ReturnsFalseAndTouchesNothing()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.TryUndo(placed, out float? rotation), Is.False);
        Assert.That(rotation, Is.Null);
        Assert.That(placed, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Kai's box-select, 2026-11-02: grab part of a placed unit and move it. Moving five models is ONE
    /// gesture, so it has to undo as one - five separate undos to put back what one drag did would be
    /// its own small punishment for changing your mind.
    /// </summary>
    [Test]
    public void UndoSelectionMove_PutsEveryMovedModelBackInOneStep()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>
        {
            Entry(10f, 10f), Entry(11f, 10f), Entry(12f, 10f), Entry(13f, 10f),
        };

        // Models 1 and 3 are dragged away together.
        var before = new Dictionary<int, Position>
        {
            [1] = placed[1].Position,
            [3] = placed[3].Position,
        };
        history.RecordSelectionMove(before);
        placed[1] = new PlacedObjectEntry<int>(null!, new Position(30f, 25f), placed[1].Facing);
        placed[3] = new PlacedObjectEntry<int>(null!, new Position(31f, 25f), placed[3].Facing);

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True);
        Assert.That(rotation, Is.Null, "a selection move has no formation ghost to hand back.");

        Assert.That(placed[1].Position.x, Is.EqualTo(11f).Within(0.001f));
        Assert.That(placed[3].Position.x, Is.EqualTo(13f).Within(0.001f));
        Assert.That(placed[0].Position.x, Is.EqualTo(10f).Within(0.001f), "untouched models stay put.");
        Assert.That(placed[2].Position.x, Is.EqualTo(12f).Within(0.001f));

        Assert.That(history.CanUndo, Is.False, "it was one gesture, so it was one undo.");
    }

    /// <summary>
    /// The recorded positions are COPIED, not aliased. The resolver reuses its origins dictionary
    /// across drags, so a history holding a reference to it would quietly rewrite its own past every
    /// time the player dragged again.
    /// </summary>
    [Test]
    public void UndoSelectionMove_IsNotAffectedByLaterChangesToTheCallersDictionary()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(10f, 10f), Entry(11f, 10f) };

        var origins = new Dictionary<int, Position> { [1] = new Position(11f, 10f) };
        history.RecordSelectionMove(origins);

        // The caller reuses the dictionary for the next drag.
        origins[1] = new Position(99f, 99f);
        placed[1] = new PlacedObjectEntry<int>(null!, new Position(50f, 50f), null);

        history.TryUndo(placed, out _);

        Assert.That(placed[1].Position.x, Is.EqualTo(11f).Within(0.001f),
            "undo restored the position the caller later overwrote, not the original.");
    }

    [Test]
    public void UndoPlace_RemovesTheLastEntry()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };
        history.RecordPlace();
        placed.Add(Entry(2f, 2f));
        history.RecordPlace();

        Assert.That(history.TryUndo(placed, out _), Is.True);

        Assert.That(placed, Has.Count.EqualTo(1));
        Assert.That(placed[0].Position.x, Is.EqualTo(1f));
    }

    [Test]
    public void UndoDrag_RestoresThePreDragEntryAtItsIndex_NotTheListTail()
    {
        // The old bug shape: place A then B, drag A somewhere else, press Undo — B vanished and A kept
        // its new spot. Action-granular undo restores A instead and leaves B alone.
        var history = new PlacementHistory<int>();
        var before = Entry(1f, 1f, new Float2(0f, 1f));
        var placed = new List<PlacedObjectEntry<int>> { before, Entry(2f, 2f) };
        history.RecordPlace();
        history.RecordPlace();

        history.RecordDrag(0, placed[0]);
        placed[0] = Entry(5f, 5f, new Float2(1f, 0f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True);

        Assert.That(rotation, Is.Null);
        Assert.That(placed, Has.Count.EqualTo(2));
        Assert.That(placed[0], Is.EqualTo(before)); // position AND facing back
        Assert.That(placed[1].Position.x, Is.EqualTo(2f));
    }

    [Test]
    public void UndoGroupDrop_EmptiesTheListAndReturnsTheDropRotation()
    {
        // An emptied list is what re-opens the formation ghost (the resolver gates it on Count == 0);
        // the rotation comes back so the ghost returns oriented as it was dropped.
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();
        history.RecordGroupDrop(0.75f);
        placed.Add(Entry(1f, 1f));
        placed.Add(Entry(2f, 1f));
        placed.Add(Entry(3f, 1f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True);

        Assert.That(placed, Is.Empty);
        Assert.That(rotation, Is.EqualTo(0.75f));
    }

    [Test]
    public void UndoRestart_RestoresTheClearedSnapshotByCopy()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f), Entry(2f, 2f) };

        history.RecordRestart(placed);
        placed.Clear();
        placed.Add(Entry(9f, 9f)); // re-placement after the restart must not corrupt the snapshot

        history.RecordPlace();
        Assert.That(history.TryUndo(placed, out _), Is.True);  // undo the re-placement
        Assert.That(history.TryUndo(placed, out _), Is.True);  // undo the restart

        Assert.That(placed, Has.Count.EqualTo(2));
        Assert.That(placed[0].Position.x, Is.EqualTo(1f));
        Assert.That(placed[1].Position.x, Is.EqualTo(2f));
    }

    [Test]
    public void MixedSequence_UnwindsInReverseGestureOrder()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();

        // drop the unit as a group, drag model 1, drag model 0
        history.RecordGroupDrop(0.5f);
        placed.Add(Entry(1f, 1f));
        placed.Add(Entry(2f, 1f));
        history.RecordDrag(1, placed[1]);
        placed[1] = Entry(2f, 4f);
        history.RecordDrag(0, placed[0]);
        placed[0] = Entry(1f, 4f);

        Assert.That(history.TryUndo(placed, out _), Is.True);          // last drag back
        Assert.That(placed[0].Position.z, Is.EqualTo(1f));
        Assert.That(placed[1].Position.z, Is.EqualTo(4f));

        Assert.That(history.TryUndo(placed, out _), Is.True);          // first drag back
        Assert.That(placed[1].Position.z, Is.EqualTo(1f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True); // the drop lifts
        Assert.That(placed, Is.Empty);
        Assert.That(rotation, Is.EqualTo(0.5f));
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Clear_ForgetsEverything()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };
        history.RecordPlace();

        history.Clear();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.TryUndo(placed, out _), Is.False);
        Assert.That(placed, Has.Count.EqualTo(1));
    }

    [Test]
    public void UndoDrag_WhoseIndexNoLongerExists_IsANoOpForTheList()
    {
        // Defensive: the resolver clears the history with the request, so a stale index should never
        // happen — but an out-of-range inversion must degrade to "nothing" rather than throw mid-frame.
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();
        history.RecordDrag(3, Entry(1f, 1f));

        Assert.That(history.TryUndo(placed, out _), Is.True);
        Assert.That(placed, Is.Empty);
    }
}
