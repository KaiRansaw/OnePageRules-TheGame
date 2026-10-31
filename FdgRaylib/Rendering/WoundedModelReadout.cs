using System.Collections.Generic;
using System.Linq;
using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// #414 - which model in a unit is the damaged one, and how to say so.
///
/// <para>
/// **Why this is a thing at all.** OPR assigns wounds to one model at a time. Tough(X): <i>"You must
/// continue to put wounds on the Tough model with most wounds in the unit until it is killed, before
/// starting to put them on the next Tough model (Heroes must be assigned wounds last, even if already
/// wounded)"</i>, and the engine enforces it rather than suggesting it - <c>AssignWoundsResults</c> refuses
/// to start a fresh model while another is mid-fill (#024). So a unit's damage is not spread thin across
/// its models: at any moment it has some dead models, some untouched models, and normally exactly ONE
/// part-wounded model. That one model is the whole story of "how much more can this unit take right now",
/// and until now nothing on screen named it - the health bar is an aggregate and says so in its own doc
/// comment (<see cref="HealthBarRenderer.CasualtyTicks"/>: <i>"no tick can name a particular model"</i>),
/// and the per-model fraction existed only if you hovered that exact base.
/// </para>
///
/// <para>
/// **Normally one, but not guaranteed one.** <c>AssignWoundsResults.PreAssignToAlreadyWoundedModels</c>
/// tops up every already-wounded model before starting anyone new, and when the incoming packet runs dry
/// part way through, two models can be left part-wounded. That is a known, documented gap in the engine,
/// not an invariant to rely on here, so this returns a LIST and the label pluralises. Ordering is
/// fewest-remaining first: that is the model closest to dying and, by the rule above, the one the next hit
/// goes on, so it is the one the player cares about when the line is truncated.
/// </para>
///
/// <para>
/// Only part-wounded models count. A full model has nothing to report, and a 1-wound trooper can never be
/// in this state at all - it is either whole or a casualty - which is what keeps the table quiet: the
/// counter appears on Tough models under fire and nowhere else. ASCII only (CLAUDE.md).
/// </para>
/// </summary>
public static class WoundedModelReadout
{
    /// <summary>Shared with the health bar so a model does not count as damaged for one and not the other.</summary>
    public const float Epsilon = HealthBarRenderer.Epsilon;

    /// <summary>How many models the card line names before it summarises the rest.</summary>
    private const int MaxNamedOnCard = 3;

    /// <summary>
    /// True when this model is alive AND has taken wounds - the one state worth drawing a counter for.
    /// False for a dead model (its wounds are told by its absence) and for a whole one. Never true for a
    /// 1-wound model, which has no middle state.
    /// </summary>
    public static bool IsPartWounded(IModel model) =>
        model.GetIsAlive() && model.WoundsDealt > Epsilon;

    /// <summary>
    /// The unit's part-wounded models, closest to death first (ties keep roster order, so the list is
    /// stable frame to frame and does not shuffle under the player's eye).
    /// </summary>
    public static IReadOnlyList<IModel> Wounded(IUnit unit) =>
        unit.Models
            .Where(IsPartWounded)
            .OrderBy(m => m.RemainingWounds)
            .ToList();

    /// <summary>
    /// "1/3 wounds left" - the counter drawn over a model's own base, and the atom the card line is built
    /// from. Fractions are real here (#199/#287: the probabilistic roller deals fractional wounds), which
    /// is why this goes through <see cref="WoundFormat"/> rather than being interpolated.
    /// </summary>
    public static string Counter(IModel model) =>
        WoundFormat.Fraction(model.RemainingWounds, model.TotalWounds);

    /// <summary>
    /// The unit card's line, or null when no model is part-wounded (the overwhelmingly common case - the
    /// line must cost nothing when there is nothing to say).
    /// </summary>
    public static string? CardLine(IUnit unit)
    {
        IReadOnlyList<IModel> wounded = Wounded(unit);
        if (wounded.Count == 0) return null;

        string label = wounded.Count == 1 ? "Wounded model" : "Wounded models";
        IEnumerable<string> shown = wounded.Take(MaxNamedOnCard).Select(Counter);
        string body = string.Join(", ", shown);
        // A long roster of part-wounded models is a rules anomaly rather than a readout to scroll; name the
        // nearest-dead few and count the rest.
        if (wounded.Count > MaxNamedOnCard) body += $" (+{wounded.Count - MaxNamedOnCard} more)";

        return $"{label}: {body} wounds left";
    }
}
