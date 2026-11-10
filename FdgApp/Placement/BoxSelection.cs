using FDG;

namespace FdgRaylib.Placement;

/// <summary>
/// The drag-a-box-to-pick-some-models gesture, shared by deployment and movement so the two agree on
/// what a box catches and when a press is a box at all.
/// <para>
/// Kai, 2026-11-05: "the drag and highlight option for movement/deployment doesnt work still."
/// Deployment had it (0.76) but only on a plain drag after the whole unit was down, and a plain click
/// on bare table while models were still to place started a box instead of placing the next one.
/// Movement never had it. The rule now, in both screens:
/// </para>
/// <list type="bullet">
///   <item><b>Ctrl + drag</b> on the table draws a box; every model of the unit being placed or moved
///   whose base centre is inside it is selected. Ctrl is the modifier because the plain drag is already
///   the "aim the unit" gesture in both screens, Shift is "stay within Advance" while moving, and Alt
///   is the ruler.</item>
///   <item><b>Ctrl + click</b> on a model adds it to the selection or takes it out.</item>
///   <item>Deployment keeps the plain drag on bare table once every model is down - it never fought
///   anything there.</item>
///   <item><b>Right-click</b> drops the selection, the app-wide "never mind".</item>
/// </list>
/// </summary>
public static class BoxSelection
{
    /// <summary>
    /// A box smaller than this on both sides is a click, not a box. Half an inch is well under the
    /// smallest base (25mm, ~1") so no real box is mistaken for a click.
    /// </summary>
    public const float MinBoxInches = 0.5f;

    /// <summary>Whether the press-to-release span is big enough to be a box rather than a click.</summary>
    public static bool IsBox(Position a, Position b) =>
        MathF.Abs(a.x - b.x) >= MinBoxInches || MathF.Abs(a.z - b.z) >= MinBoxInches;

    /// <summary>
    /// The indices of <paramref name="centres"/> inside the box spanned by <paramref name="a"/> and
    /// <paramref name="b"/> (either corner order). Edges count as inside.
    /// </summary>
    public static List<int> Within(Position a, Position b, IReadOnlyList<Position> centres)
    {
        float left = MathF.Min(a.x, b.x), right = MathF.Max(a.x, b.x);
        float bottom = MathF.Min(a.z, b.z), top = MathF.Max(a.z, b.z);
        var hits = new List<int>();
        for (int i = 0; i < centres.Count; i++)
        {
            Position p = centres[i];
            if (p.x >= left && p.x <= right && p.z >= bottom && p.z <= top) hits.Add(i);
        }
        return hits;
    }

    /// <summary>
    /// Whether a left press should start a box in DEPLOYMENT. Ctrl always may; a plain press only once
    /// every model is down and only on bare table - while models are still to place, a plain click on
    /// bare table places the next one, and a press on a model picks that model up.
    /// </summary>
    public static bool DeploymentPressStartsBox(bool ctrlHeld, bool allPlaced, bool pressedOnModel) =>
        ctrlHeld || (allPlaced && !pressedOnModel);
}
