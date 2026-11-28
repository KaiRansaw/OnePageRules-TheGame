using FDG.ArmyBuilding;
using FdgRaylib.Models3D;

namespace FdgRaylib.Forge;

/// <summary>
/// The Looks screen's picking and giving, with no drawing in it (Kai, 2026-11-27: mockup A "Squad on the table").
/// Every model of a unit - both halves of a combined unit - is a slot in table order (<see cref="ForgeSession.Slots"/>).
/// Models are picked (click, Shift/Ctrl-click to add or take away, a box, a double-click for every model wearing the
/// same look), then a version is given to every picked model at once, or every version is mixed evenly over the squad.
/// <para>Why this lives here and not in the screen: the old panel kept its picks inside widgets it rebuilt on every
/// click, so picks were lost (Save rebuilt the whole screen), a scrolled grid snapped back to the top under the mouse
/// and a held button then picked whichever model slid under it, and "Use for all" gave the version last under the
/// mouse rather than the one chosen. The picks live here now, keyed to the unit itself rather than its row number,
/// so a screen rebuild - or a list whose rows moved - keeps them.</para>
/// </summary>
internal sealed class LooksPicker
{
    private readonly SortedSet<int> _picked = new();

    public LooksPicker(ForgeSession session, int row)
    {
        Session = session;
        Unit = session.List.Units[row];
    }

    public ForgeSession Session { get; }

    /// <summary>The list entry whose looks are being chosen (either half of a combined unit).</summary>
    public BuilderUnit Unit { get; }

    /// <summary>The unit's row in the list now, or -1 when it has been taken out of the list.</summary>
    public int Row => Session.List.Units.IndexOf(Unit);

    /// <summary>True while this picker still points at a unit of its session's list.</summary>
    public bool Valid => Row >= 0;

    /// <summary>Every model of the unit, in table order (model 1 first).</summary>
    public List<ForgeSession.ModelSlot> Slots() => Valid ? Session.Slots(Row) : new List<ForgeSession.ModelSlot>();

    /// <summary>The picked models (0-based slot numbers), lowest first. Models that no longer exist are dropped.</summary>
    public IReadOnlyCollection<int> Picked
    {
        get
        {
            int n = Slots().Count;
            _picked.RemoveWhere(i => i >= n);
            return _picked;
        }
    }

    public bool IsPicked(int i) => Picked.Contains(i);

    /// <summary>The last model clicked - the colour painter and the portrait buttons work on its version.</summary>
    public int Anchor { get; private set; } = -1;

    // ------------------------------------------------------------------ picking

    /// <summary>A click on model <paramref name="i"/>: on its own it picks just that model; with Shift or Ctrl held it
    /// adds the model, or takes it away when it was already picked.</summary>
    public void Click(int i, bool add)
    {
        if (i < 0 || i >= Slots().Count) return;
        if (add)
        {
            if (!_picked.Remove(i)) _picked.Add(i);
        }
        else
        {
            _picked.Clear();
            _picked.Add(i);
        }
        Anchor = i;
    }

    /// <summary>A box dragged across the models: picks the ones inside it (added to the picks with Shift/Ctrl, else
    /// instead of them). An empty box with nothing held clears the picks, as a click on empty table does.</summary>
    public void Box(IEnumerable<int> inside, bool add)
    {
        int n = Slots().Count;
        if (!add) _picked.Clear();
        foreach (int i in inside)
            if (i >= 0 && i < n) _picked.Add(i);
    }

    /// <summary>A double-click on model <paramref name="i"/>: every model wearing the same version as it.</summary>
    public void PickSameLook(int i, IReadOnlyList<string> versions)
    {
        var slots = Slots();
        if (i < 0 || i >= slots.Count) return;
        string? file = FileOf(slots[i], versions);
        string key = file ?? slots[i].Look ?? "";
        _picked.Clear();
        for (int k = 0; k < slots.Count; k++)
            if ((FileOf(slots[k], versions) ?? slots[k].Look ?? "") == key) _picked.Add(k);
        Anchor = i;
    }

    public void PickAll()
    {
        _picked.Clear();
        for (int i = 0; i < Slots().Count; i++) _picked.Add(i);
    }

    public void PickNone() => _picked.Clear();

    /// <summary>Picks exactly these models (scripted runs).</summary>
    public void PickExactly(IEnumerable<int> models)
    {
        _picked.Clear();
        Box(models, add: true);
        if (_picked.Count > 0) Anchor = _picked.Min;
    }

    // ------------------------------------------------------------------ giving looks

    /// <summary>The file a model wears: its look when downloaded here, else the unit's first version.</summary>
    public static string? FileOf(ForgeSession.ModelSlot slot, IReadOnlyList<string> versions) => ModelLooks.Pick(versions, slot.Look);

    /// <summary>What is stored for a version: nothing for the first version (unpicked models wear it anyway), else its file name.</summary>
    public static string? LookFor(IReadOnlyList<string> versions, int index) => index <= 0 ? null : ModelLooks.NameOf(versions[index]);

    /// <summary>Gives version <paramref name="index"/> (0 = the first) to every picked model. Returns how many got it
    /// (0 when nothing is picked or there is no such version).</summary>
    public int Give(int index, IReadOnlyList<string> versions)
    {
        if (index < 0 || index >= versions.Count) return 0;
        var slots = Slots();
        var to = Picked.Where(i => i < slots.Count).Select(i => slots[i]).ToList();
        if (to.Count == 0) return 0;
        Session.SetLook(to, LookFor(versions, index));
        return to.Count;
    }

    /// <summary>
    /// "Mix all versions evenly": the whole squad dealt round the versions in model order - model 1 the first version,
    /// model 2 the second, and so on round again - so every version is worn by as many models as the others, give or
    /// take one, and neighbours in the formation differ. The same squad always gets the same mix.
    /// </summary>
    public void MixEvenly(IReadOnlyList<string> versions)
    {
        if (versions.Count == 0) return;
        var slots = Slots();
        for (int v = 0; v < versions.Count; v++)
        {
            int vi = v;
            Session.SetLook(slots.Where((_, k) => k % versions.Count == vi), LookFor(versions, v));
        }
    }

    /// <summary>How many models of the squad wear version <paramref name="index"/>.</summary>
    public int Wearing(int index, IReadOnlyList<string> versions)
    {
        if (index < 0 || index >= versions.Count) return 0;
        return Slots().Count(s => FileOf(s, versions) == versions[index]);
    }

    /// <summary>The version every picked model wears (the card outlined on the screen), or -1 when nothing is picked
    /// or the picked models wear different versions.</summary>
    public int PickedVersion(IReadOnlyList<string> versions)
    {
        var slots = Slots();
        var files = Picked.Where(i => i < slots.Count).Select(i => FileOf(slots[i], versions)).Distinct().ToList();
        if (files.Count != 1 || files[0] == null) return -1;
        return versions.ToList().IndexOf(files[0]!);
    }

    /// <summary>The version the colour painter and the portrait buttons work on: the last-clicked model's when it is
    /// picked, else the first picked model's, else -1.</summary>
    public int FocusVersion(IReadOnlyList<string> versions)
    {
        var slots = Slots();
        int i = Anchor >= 0 && IsPicked(Anchor) ? Anchor : Picked.Count > 0 ? Picked.First() : -1;
        if (i < 0 || i >= slots.Count || FileOf(slots[i], versions) is not { } file) return -1;
        return versions.ToList().IndexOf(file);
    }

    /// <summary>"4 models picked" / "No models picked".</summary>
    public string PickedLine()
    {
        int n = Picked.Count;
        return n == 0 ? "No models picked" : $"{n} model{(n == 1 ? "" : "s")} picked";
    }
}

/// <summary>
/// Where each model of a squad stands on the Looks screen: a tidy block, model 1 at the back left, in rows of five
/// for a ten-model squad and a little wider for bigger ones, a base and a half apart so the bases never touch.
/// Positions are table inches around (0, 0); +Z is towards the camera (the front row).
/// </summary>
public static class SquadFormation
{
    /// <summary>Models in a row for a squad of <paramref name="count"/>: all of them up to five, then five a row up
    /// to ten, then about twice as wide as deep.</summary>
    public static int Columns(int count) =>
        count <= 5 ? Math.Max(1, count) : count <= 10 ? 5 : (int)Math.Ceiling(Math.Sqrt(count * 2.0));

    /// <summary>Centre-to-centre spacing for bases <paramref name="baseInches"/> wide.</summary>
    public static float Spacing(float baseInches) => Math.Max(0.5f, baseInches) * 1.5f + 0.25f;

    /// <summary>Front-to-back spacing: wider than across, so a model never hides the one behind it from the camera.</summary>
    public static float RowSpacing(float baseInches) => Spacing(baseInches) + 0.75f;

    /// <summary>Each model's spot (x across, z towards the camera), model 1 first.</summary>
    public static List<(float X, float Z)> Spots(int count, float baseInches)
    {
        var spots = new List<(float, float)>();
        if (count <= 0) return spots;
        int cols = Columns(count);
        int rows = (count + cols - 1) / cols;
        float gap = Spacing(baseInches);
        for (int i = 0; i < count; i++)
        {
            int r = i / cols, c = i % cols;
            int inRow = Math.Min(cols, count - r * cols); // a short last row is centred
            float x = (c - (inRow - 1) / 2f) * gap;
            float z = (r - (rows - 1) / 2f) * RowSpacing(baseInches);
            spots.Add((x, z));
        }
        return spots;
    }
}
