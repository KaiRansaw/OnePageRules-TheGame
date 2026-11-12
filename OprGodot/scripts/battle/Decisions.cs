using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace OprGodot.Battle;

/// <summary>One button of the 2x2 action grid.</summary>
/// <param name="Dim">Greyed out but still pickable (the tooltip says why it does little).</param>
public sealed record ActionSlot(string Name, string Icon, string Label, string Key, bool Enabled, string Tooltip, Action Choose, bool Dim = false);

/// <summary>
/// One row of a choice list, docked in the actions area. <paramref name="Pick"/> null = an information row
/// (not clickable). <paramref name="Secondary"/> is a companion button on the same row (#321).
/// </summary>
public sealed record ChoiceRow(string Label, string? Detail, bool Enabled, string? Reason, Action? Pick,
    string? SecondaryLabel = null, Action? Secondary = null, Action<bool>? Hover = null, string? Icon = null,
    bool Selected = false, IUnit? Portrait = null, IReadOnlyList<int>? Pips = null, string? Sub = null);

/// <summary>A tick box in the confirm box.</summary>
public sealed record ConfirmCheck(string Label, string Detail, bool On, Action<bool> Set);

/// <summary>How a unit in the left-hand list takes part in the open decision.</summary>
public readonly record struct UnitMark(bool Pickable, string? Reason);

/// <summary>The book's four actions (Kai, 2026-11-10).</summary>
public enum BookAction { Hold, Advance, Rush, Charge }

/// <summary>
/// An open decision as the battle screen shows it: the prompt line, the actions area (the 2x2 grid and / or a
/// list of choices docked under it), how units in the left-hand list take part, a pending choice waiting for a
/// confirm, and what table clicks, right-click and Esc do. Built on the engine thread, used on the main thread.
/// </summary>
public abstract class Decision
{
    public abstract string Prompt { get; }
    public virtual string Hint => "";
    public virtual IReadOnlyList<ActionSlot>? Actions => null;
    public virtual IReadOnlyList<ChoiceRow>? Rows => null;
    public virtual string? RowsTitle => null;
    /// <summary>The picture on this decision's choice buttons when the choices are drawn as buttons (a row can set its own).</summary>
    public virtual string RowIcon => "scroll-unfurled";
    /// <summary>Draw the choices as square buttons beside the grid (Kai picked style A, 2026-11-11) instead of a list.</summary>
    public virtual bool ButtonChoices => false;
    /// <summary>The way out (Back / Done shooting), or null when the decision is mandatory.</summary>
    public virtual string? CancelLabel => null;
    public virtual void Cancel() { }

    /// <summary>
    /// A choice made but not yet sent (Kai, 2026-11-10: "click where to move, then Space / the popup to
    /// confirm - prevents misclicks"). Non-null shows the confirm box.
    /// </summary>
    public virtual string? ConfirmText => null;
    /// <summary>Tick boxes in the confirm box (which weapons fire at the target). Null = none.</summary>
    public virtual IReadOnlyList<ConfirmCheck>? ConfirmChecks => null;
    /// <summary>Space still confirms, but the decision draws its own confirm button (the mission cards).</summary>
    public virtual bool HideConfirmBox => false;
    /// <summary>The test driver's name for this screen (<c>--hold=</c>, snapshot file name).</summary>
    public virtual string SnapName => GetType().Name;
    public virtual void Confirm() { }
    public virtual void ClearPending() { }

    /// <summary>Right-click: drop the pending choice, else step back. True if it did something.</summary>
    public virtual bool RightClick(BattleScreen s)
    {
        if (ConfirmText != null) { ClearPending(); return true; }
        if (CancelLabel != null) { Cancel(); return true; }
        return false;
    }

    /// <summary>Whether a unit in the left-hand list is one of this decision's choices (null = not involved).</summary>
    public virtual UnitMark? ListMark(IUnit unit) => null;
    /// <summary>True when the choices are the other side's units (the list switches to the ENEMY tab).</summary>
    public virtual bool WantsEnemyList => false;

    /// <summary>Lines added to the top of an enemy's hover popup while this decision is open (the shooting odds), or null.</summary>
    public virtual IReadOnlyList<string>? HoverOdds(IUnit enemy) => null;
    public virtual bool TableClick(BattleScreen s, Vector2 at) => false;
    public virtual void TableHover(BattleScreen s, Vector2 at) { }
    public virtual void UnitPicked(BattleScreen s, IUnit unit) { }
    public virtual void Shown(BattleScreen s) { }
    public virtual void Closed(BattleScreen s) { }
    public abstract bool Done { get; }
    /// <summary>
    /// The scripted test driver's answer (<c>--autoplay</c>): a plain legal choice made through the same
    /// reply path a click uses, so a whole game can be run through the Godot resolvers unattended.
    /// </summary>
    public abstract void AutoPlay(BattleScreen s);
    /// <summary>Bumped when the rows, grid or pending choice change while the decision stays open.</summary>
    public int Version { get; protected set; }
}

public abstract class Decision<T> : Decision
{
    // Continuations run off the main thread: the engine carries on without waiting for the next frame.
    private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<T> Task => _tcs.Task;
    public override bool Done => _tcs.Task.IsCompleted;
    protected void Complete(T reply) => _tcs.TrySetResult(reply);
}

internal static class Book
{
    public static readonly (BookAction Action, string Icon, string Key)[] Grid =
    {
        (BookAction.Hold, "shield", "1"), (BookAction.Advance, "walk", "2"),
        (BookAction.Rush, "run", "3"), (BookAction.Charge, "sword-clash", "4"),
    };

    public static string Tip(BookAction a) => a switch
    {
        BookAction.Hold => "Stay put and shoot. With nothing to shoot at, the unit's activation ends.",
        BookAction.Advance => "Move up to the Advance distance, then you may shoot.",
        BookAction.Rush => "Move up to the Rush distance. No shooting after.",
        _ => "Move into contact with an enemy unit and fight it.",
    };

    /// <summary>Hold asks first (Kai, 2026-11-10): it can end the unit's activation, which can't be undone.</summary>
    public static void ConfirmHold(BattleScreen s, IUnit? unit, Action go) =>
        s.Confirm("Hold?", $"{unit?.Name ?? "This unit"} stays where it is and shoots if it can. If it has nothing to shoot at, its activation ends.",
            "Hold", go);

    /// <summary>
    /// Why Hold would shoot nothing (no ranged weapon, or no enemy within any weapon's range), else null. Kai,
    /// 2026-11-11: Hold stays pickable but greys out, with this on hover. Range only - line of sight is checked
    /// by the engine when the shot is chosen.
    /// </summary>
    public static string? NothingToShoot(BattleScreen s, IUnit unit)
    {
        var shooters = unit.Models.Where(m => m.GetIsAlive() && m.Weapons.Any(w => w.RangeInches > 0)).ToList();
        if (shooters.Count == 0) return "This unit has no shooting weapons - holding ends its activation.";
        foreach (IUnit u in s.Session.Table.Units.Objects)
        {
            if (s.Session.IsMine(u.PlayerID) || !u.GetIsOnBattlefield()) continue;
            foreach (IModel e in u.Models.Where(m => m.GetIsAlive()))
                foreach (IModel m in shooters)
                {
                    float d = Mathf.Sqrt(Mathf.Pow(e.Position.x - m.Position.x, 2) + Mathf.Pow(e.Position.z - m.Position.z, 2)) - e.BaseRadiusInches - m.BaseRadiusInches;
                    if (m.Weapons.Any(w => w.RangeInches > 0 && d <= w.RangeInches + 0.01f)) return null;
                }
        }
        return "No enemy within range of its weapons - holding ends its activation.";
    }
}

// ====================================================================== which unit to activate

/// <summary>
/// Your turn. Clicking a unit (in the left-hand list or on the table) only SELECTS it - pressing one of the four
/// actions is what activates it (Kai, 2026-11-10: picking a different unit must always be possible, and
/// right-click drops the pick). The action pressed is carried into the engine's action menu when it comes.
/// </summary>
public sealed class ActivateDecision : Decision<DataBinding<UnitData>>
{
    private readonly ChooseUnitToActivateRequest _r;
    private IUnit? _pick;
    public ActivateDecision(ChooseUnitToActivateRequest r) => _r = r;
    /// <summary>The engine's request (the test driver borrows its unit bindings to stage example screens).</summary>
    public ChooseUnitToActivateRequest Request => _r;

    public override string Prompt => _pick == null ? "Your turn" : $"Activate {_pick.Name}";
    public override string Hint => _pick == null
        ? "Pick a unit - click it in your army list or on the table (Tab cycles)."
        : "Choose its action (keys 1-4). Pick another unit to switch, right-click to drop the pick.";

    private DataBinding<UnitData>? Option(IUnit unit) =>
        _r.ValidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID))?.Option;

    public override UnitMark? ListMark(IUnit unit)
    {
        if (Option(unit) != null) return new UnitMark(true, null);
        var bad = _r.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID));
        return bad != null ? new UnitMark(false, bad.Reason) : null;
    }

    public override IReadOnlyList<ActionSlot> Actions
    {
        get
        {
            string? noShot = _pick != null && _screen != null ? Book.NothingToShoot(_screen, _pick) : null;
            return Book.Grid.Select(g => new ActionSlot(
                g.Action.ToString(), g.Icon, g.Action.ToString(), g.Key, _pick != null,
                _pick == null ? "Pick a unit first." : g.Action == BookAction.Hold && noShot != null ? noShot : Book.Tip(g.Action),
                () => Act(g.Action), Dim: g.Action == BookAction.Hold && noShot != null)).ToList();
        }
    }

    private void Act(BookAction action)
    {
        if (_pick is not { } unit || Option(unit) is not { } binding) return;
        void Go()
        {
            BattleResolvers.QueuedAction = (unit.ID, action);
            Complete(binding);
        }
        if (action == BookAction.Hold && _screen != null) Book.ConfirmHold(_screen, unit, Go);
        else Go();
    }

    private BattleScreen? _screen;

    public override void Shown(BattleScreen s)
    {
        _screen = s;
        if (BattleResolvers.PendingActivation is { } next)
        {
            BattleResolvers.PendingActivation = null;
            if (Option(next) != null) Pick(s, next);
        }
    }

    private void Pick(BattleScreen s, IUnit unit)
    {
        _pick = unit;
        s.SelectUnit(unit);
        Version++;
    }

    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (Option(unit) != null) Pick(s, unit);
        else if (_r.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID)) is { } bad)
            s.Toast($"{unit.Name}: {bad.Reason}");
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit || !s.Session.IsMine(unit.PlayerID)) return false;
        s.SelectUnit(unit);
        UnitPicked(s, unit);
        return true;
    }

    public override bool RightClick(BattleScreen s)
    {
        if (_pick == null) return false;
        _pick = null;
        s.ClearSelection();
        Version++;
        return true;
    }
}

// ====================================================================== the action menu

/// <summary>
/// Choose Action. Usually answered straight away from the action pressed on the activation grid. When it is
/// shown, the 2x2 grid is the book's four actions (Hold, Advance, Rush, Charge) translated onto the engine's
/// Move / Shoot / Charge / Pass: Advance and Rush open the move tool capped at that distance, Charge moves into
/// contact (or is the engine's Charge when already in reach), Hold is Shoot (or ends the activation when there
/// is nothing to shoot). After a move the grid becomes what is left - Shoot, Charge, End. Anything else (Cast,
/// special actions) is a list docked under the grid.
/// </summary>
public sealed class ActionDecision : Decision<string>
{
    private readonly ChooseActionRequest _r;
    private BattleScreen? _s;
    public ActionDecision(ChooseActionRequest r) => _r = r;

    private const string MoveOpt = FDG.Stages.ChooseActionStage.MOVEMENT_CHOICE_NAME;
    private const string ShootOpt = FDG.Stages.ChooseActionStage.SHOOT_CHOICE_NAME;
    private const string ChargeOpt = FDG.Stages.ChooseActionStage.CHARGE_CHOICE_NAME;
    private const string PassOpt = FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME;
    private static readonly string[] Handled = { MoveOpt, ShootOpt, ChargeOpt, PassOpt };

    private IUnit? Unit => _s?.Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(_r.ActivatingUnitID));

    private bool Can(string o) => _r.ValidOptions.Contains(o);
    private bool Fresh => Can(MoveOpt);   // nothing done yet this activation

    public override string Prompt => Fresh ? $"{Unit?.Name ?? "Your unit"} - choose an action" : $"{Unit?.Name ?? "Your unit"} - then...";
    public override string Hint => "Keys 1-4" + (_r.AllowCancel ? ". Right-click to pick a different unit." : ".");

    private string Describe(string option) =>
        _r.OptionDescriptions != null && _r.OptionDescriptions.TryGetValue(option, out string? d) ? d : "";

    private string? Why(string option) => _r.InvalidOptions.FirstOrDefault(i => i.Option == option)?.Reason;

    public override IReadOnlyList<ActionSlot> Actions
    {
        get
        {
            if (!Fresh)
                return new[]
                {
                    Slot("Shoot", "crosshair", "Shoot", "1", Can(ShootOpt), Can(ShootOpt) ? "Shoot now." : Why(ShootOpt), () => Complete(ShootOpt)),
                    Slot("Charge", "sword-clash", "Charge", "2", Can(ChargeOpt), Can(ChargeOpt) ? "Fight the enemy in reach." : Why(ChargeOpt), () => Complete(ChargeOpt)),
                    Slot("End", "stop-sign", "End", "3", Can(PassOpt), Can(PassOpt) ? "This unit is done for the round." : Why(PassOpt), () => ConfirmEnd()),
                };
            // Hold with nothing to shoot still ends the activation, so it stays pickable but greyed (Kai, 2026-11-11).
            bool holdDim = !Can(ShootOpt) && Can(PassOpt);
            return Book.Grid.Select(g => Slot(g.Action.ToString(), g.Icon, g.Action.ToString(), g.Key, Allowed(g.Action),
                !Allowed(g.Action) ? NotAllowed(g.Action)
                : g.Action == BookAction.Hold && holdDim ? (Why(ShootOpt) ?? "Nothing to shoot at") + " - holding ends this unit's activation."
                : Book.Tip(g.Action), () => Do(g.Action, confirm: true), g.Action == BookAction.Hold && holdDim)).ToList();
        }
    }

    private bool Allowed(BookAction a) => a switch
    {
        BookAction.Hold => Can(ShootOpt) || Can(PassOpt),
        BookAction.Charge => Can(ChargeOpt) || Can(MoveOpt) && ChargeReach().InReach,
        _ => Can(MoveOpt),
    };

    /// <summary>
    /// Kai, 2026-11-11: grey out what can't do anything. A Charge needs at least one model within charge distance of
    /// the target (GF Advanced Rules 3.6.0, Charging) - measured base to base from the unit's own charge allowance.
    /// </summary>
    private (bool InReach, float Distance) ChargeReach()
    {
        if (_s == null || Unit is not { } me) return (true, 12);
        float reach = FDG.Rules.Dispatch.MovementRuleQueries.EffectiveChargeDistance(me,
            FDG.Rules.Dispatch.ReadOnlyRuleEvaluators.For(_s.Session.Table), _s.Session.Table.Terrain.Objects.ToList());
        var mine = me.Models.Where(m => m.GetIsAlive()).ToList();
        foreach (IUnit u in _s.Session.Table.Units.Objects)
        {
            if (_s.Session.IsMine(u.PlayerID) || !u.GetIsOnBattlefield()) continue;
            foreach (IModel e in u.Models.Where(m => m.GetIsAlive()))
                foreach (IModel m in mine)
                {
                    float d = Mathf.Sqrt(Mathf.Pow(e.Position.x - m.Position.x, 2) + Mathf.Pow(e.Position.z - m.Position.z, 2)) - e.BaseRadiusInches - m.BaseRadiusInches;
                    if (d <= reach + 0.01f) return (true, reach);
                }
        }
        return (false, reach);
    }

    private string NotAllowed(BookAction a) => a switch
    {
        BookAction.Hold => Why(PassOpt) ?? Why(ShootOpt) ?? "Not available.",
        BookAction.Charge => Why(ChargeOpt) ?? Why(MoveOpt) ?? (Can(MoveOpt) ? $"No enemy within {ChargeReach().Distance:0}\" - nothing to charge." : "Not available."),
        _ => Why(MoveOpt) ?? "Not available.",
    };

    private static ActionSlot Slot(string name, string icon, string label, string key, bool ok, string? tip, Action go, bool dim = false) =>
        new(name, icon, label, key, ok, tip ?? "Not available.", go, dim);

    /// <summary>Carries out a book action; false (with a note) when the engine won't allow it now.</summary>
    private bool Do(BookAction action, bool confirm)
    {
        if (!Allowed(action)) { _s?.Toast($"Can't {action}: {NotAllowed(action)}"); return false; }
        switch (action)
        {
            case BookAction.Hold:
                void Hold() => Complete(Can(ShootOpt) ? ShootOpt : PassOpt);
                if (confirm && _s != null) Book.ConfirmHold(_s, Unit, Hold); else Hold();
                break;
            case BookAction.Advance: BattleResolvers.NextMove = MoveKind.Advance; Complete(MoveOpt); break;
            case BookAction.Rush: BattleResolvers.NextMove = MoveKind.Rush; Complete(MoveOpt); break;
            case BookAction.Charge:
                if (Can(ChargeOpt)) Complete(ChargeOpt);
                else { BattleResolvers.NextMove = MoveKind.Charge; Complete(MoveOpt); }
                break;
        }
        return true;
    }

    private void ConfirmEnd()
    {
        // #311: ending an activation can't be taken back, so it asks first.
        if (_s == null) { Complete(PassOpt); return; }
        _s.Confirm("End this activation?", Describe(PassOpt).Length > 0 ? Describe(PassOpt) : "The unit does nothing more this round.",
            "End activation", () => Complete(PassOpt));
    }

    public override IReadOnlyList<ChoiceRow>? Rows
    {
        get
        {
            var extra = _r.ValidOptions.Where(o => !Handled.Contains(o))
                .Select(o => new ChoiceRow(o, Describe(o), true, null, () => Complete(o), Icon: ActionIcon(o)))
                .Concat(_r.InvalidOptions.Where(i => !Handled.Contains(i.Option))
                    .Select(i => new ChoiceRow(i.Option, null, false, i.Reason, null, Icon: ActionIcon(i.Option))))
                .ToList();
            return extra.Count > 0 ? extra : null;
        }
    }

    public override string? RowsTitle => "Other actions";
    // Kai, 2026-11-11: other actions are square buttons extending right of the four, not a list under them.
    public override bool ButtonChoices => true;

    private static string ActionIcon(string option)
    {
        string o = option.ToLowerInvariant();
        if (o.Contains("cast") || o.Contains("spell")) return "magic-swirl";
        if (o.Contains("embark") || o.Contains("transport")) return "stack";
        if (o.Contains("shoot") || o.Contains("fire")) return "crosshair";
        if (o.Contains("pass") || o.Contains("end")) return "hourglass";
        if (o.Contains("objective") || o.Contains("seize")) return "flag-objective";
        if (o.Contains("move") || o.Contains("walk")) return "walk";
        if (o.Contains("fight") || o.Contains("melee") || o.Contains("charge")) return "crossed-swords";
        return "bolt-shield";
    }

    public override void AutoPlay(BattleScreen s)
    {
        // Casting first, so a test run goes through the spell screens whenever the army has a caster.
        if (_r.ValidOptions.FirstOrDefault(o => o.StartsWith("Cast", StringComparison.OrdinalIgnoreCase)) is { } cast) { Complete(cast); return; }
        foreach (string o in new[] { ShootOpt, ChargeOpt, MoveOpt, PassOpt })
            if (_r.ValidOptions.Contains(o)) { Complete(o); return; }
        Complete(_r.ValidOptions[0]);
    }

    public override void Shown(BattleScreen s)
    {
        _s = s;
        if (Unit is { } u) s.SelectUnit(u);
        // The action pressed on the activation grid: no second click.
        if (BattleResolvers.QueuedAction is { } q)
        {
            BattleResolvers.QueuedAction = null;
            if (q.Unit.Equals(_r.ActivatingUnitID) && Fresh) Do(q.Action, confirm: false);
        }
    }

    public override string? CancelLabel => _r.AllowCancel ? "Pick another unit" : null;

    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (!_r.AllowCancel || unit.ID.Equals(_r.ActivatingUnitID) || !s.Session.IsMine(unit.PlayerID)) return;
        BattleResolvers.PendingActivation = unit;
        Complete(null!);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit || !s.Session.IsMine(unit.PlayerID) || unit.ID.Equals(_r.ActivatingUnitID)) return false;
        UnitPicked(s, unit);
        return _r.AllowCancel;
    }
}

// ====================================================================== moving

/// <summary>
/// Move a unit: point at the table and the unit's ghost follows (green = Advance, amber = Rush, red = can't go
/// there, with why). Each click drops a TURN POINT and places the move there (Kai, 2026-11-11: "a click should mark
/// a point so a unit could go around corners"); keep clicking to bend the path, Space or the confirm box sends it
/// (Kai, 2026-11-10). Right-click takes the last point back, and with none left goes back. Clicking an enemy ends
/// the path in contact with it (a charge).
/// </summary>
public sealed class MoveDecision : Decision<CancellableResult<List<ModelMoveEntry>>>
{
    private readonly DefineMovementPathRequest _r;
    private MovePlanner.Plan? _pending;
    private readonly List<(float X, float Z)> _via = new();   // the unit's middle at each turn point
    private bool _final;                                       // the path ends in contact - no more points
    private Vector2 _lastHover = new(-9999, -9999);
    private string _note = "";
    private readonly MoveKind? _kind;
    private BattleScreen? _s;

    public MoveDecision(DefineMovementPathRequest r)
    {
        _r = r;
        // Only the main activation move comes from the Hold/Advance/Rush/Charge grid.
        if (r.MainActivationMove) { _kind = BattleResolvers.NextMove; }
        BattleResolvers.NextMove = null;
    }

    private IUnit Unit => _r.UnitDataBinding.GetValue();

    /// <summary>How far the picked action allows (the engine's own budgets).</summary>
    private float? Cap => _kind switch
    {
        MoveKind.Advance => _r.MaxAdvanceDistance,
        MoveKind.Rush => _r.MaxRushDistance,
        _ => null,
    };

    public override string Prompt => _kind switch
    {
        MoveKind.Advance => $"Advance {Unit.Name}",
        MoveKind.Rush => $"Rush {Unit.Name}",
        MoveKind.Charge => $"Charge with {Unit.Name}",
        _ => $"Move {Unit.Name}",
    };

    public override string Hint => _pending != null
        ? (_final ? "Space to confirm - right-click to take the last point back."
                  : "Click again to add a turn, Space to confirm, right-click to take the last point back.")
        : _note.Length > 0 ? _note : _kind switch
        {
            MoveKind.Advance => $"Click where to go - up to {_r.MaxAdvanceDistance:0.#}\". Each click is a turn point. Right-click = back.",
            MoveKind.Rush => $"Click where to go - up to {_r.MaxRushDistance:0.#}\". Each click is a turn point. Right-click = back.",
            MoveKind.Charge => $"Click the enemy unit to charge - up to {_r.MaxDistanceInches:0.#}\". Click the table first to go round something.",
            _ => $"Click where to go - click an enemy to move into contact. Advance {_r.MaxAdvanceDistance:0.#}\", Rush {_r.MaxRushDistance:0.#}\""
                 + (_r.MaxDistanceInches > _r.MaxRushDistance + 0.01f ? $", Charge {_r.MaxDistanceInches:0.#}\"" : "") + ".",
        };

    public override string? CancelLabel => _r.AllowCancel ? "Back" : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(new Cancelled<List<ModelMoveEntry>>()); }

    public override IReadOnlyList<ActionSlot>? Actions => _kind != null ? null : new[]
    {
        new ActionSlot("StayPut", "shield", "Stay put", "1", true, "Don't move this unit.", () => Complete(new Selected<List<ModelMoveEntry>>(MovePlanner.StayInPlace(_r)))),
    };

    private string KindName(MovePlanner.Plan p) => _kind?.ToString()
        ?? (p.Distance <= _r.MaxAdvanceDistance + 0.01f ? "Advance" : p.Distance <= _r.MaxRushDistance + 0.01f ? "Rush" : "Charge");

    public override string? ConfirmText => _pending is { } p
        ? $"{KindName(p)} {p.Distance:0.0}\" to here?" + (_via.Count > (_final ? 0 : 1) ? $" ({_via.Count - (_final ? 0 : 1)} turn{(_via.Count - (_final ? 0 : 1) == 1 ? "" : "s")})" : "")
        : null;

    public override void Confirm()
    {
        if (_pending is not { } p) return;
        _s?.Table.ClearGhosts("move");
        _s?.Table.ClearGhosts("moverange");
        Complete(new Selected<List<ModelMoveEntry>>(p.Moves));
    }

    /// <summary>Right-click: takes the last turn point back (the move then ends at the one before).</summary>
    public override void ClearPending()
    {
        if (_final) _final = false;
        else if (_via.Count > 0) _via.RemoveAt(_via.Count - 1);
        _pending = _s != null && _via.Count > 0 ? MovePlanner.Towards(_r, _s.Session.Table, _via.Take(_via.Count - 1).ToList(), _via[^1].X, _via[^1].Z, Cap) : null;
        if (_pending is { Valid: false }) { _pending = null; _via.Clear(); }
        _lastHover = new(-9999, -9999);
        _s?.Table.ClearGhosts("move");
        _s?.Table.ClearGhosts("moverange");
        if (_pending != null && _s != null) Draw(_s, _pending, placed: true, null);
        Version++;
    }

    public override void Shown(BattleScreen s) { _s = s; s.SelectUnit(Unit); s.Table.HideSelectedRanges = true; }
    public override void Closed(BattleScreen s) { s.Table.ClearGhosts("move"); s.Table.ClearGhosts("moverange"); s.Table.HideSelectedRanges = false; }

    public override void AutoPlay(BattleScreen s)
    {
        IUnit me = Unit;
        var mine = me.Models.Where(m => m.GetIsAlive()).ToList();
        IUnit? nearest = mine.Count == 0 ? null : s.Session.Table.Units.Objects
            .Where(u => !s.Session.IsMine(u.PlayerID) && u.GetIsOnBattlefield() && u.Models.Any(m => m.GetIsAlive()))
            .OrderBy(u => u.Models.Where(m => m.GetIsAlive()).Min(m => Mathf.Pow(m.Position.x - mine[0].Position.x, 2) + Mathf.Pow(m.Position.z - mine[0].Position.z, 2)))
            .FirstOrDefault();
        MovePlanner.Plan? plan = nearest == null ? null
            : _kind == MoveKind.Charge || _kind == null && _r.MaxDistanceInches > _r.MaxRushDistance + 0.01f ? MovePlanner.IntoContact(_r, s.Session.Table, nearest)
            : MovePlanner.Towards(_r, s.Session.Table, nearest.Models.Where(m => m.GetIsAlive()).Average(m => m.Position.x), nearest.Models.Where(m => m.GetIsAlive()).Average(m => m.Position.z), Cap);
        Complete(new Selected<List<ModelMoveEntry>>(plan is { Valid: true } ? plan.Moves : MovePlanner.StayInPlace(_r)));
    }

    /// <summary>The move through the turn points so far, then to the pointer (or into contact with an enemy there).</summary>
    private (MovePlanner.Plan? Plan, bool Contact) PlanFor(BattleScreen s, Vector2 at)
    {
        IUnit? enemy = s.Table.UnitAt(at) is { } target && !s.Session.IsMine(target.PlayerID) && !target.ID.Equals(Unit.ID) ? target : null;
        // A charge goes into contact with the enemy pointed at; the table can be clicked first to go round something.
        if (enemy != null && _kind is MoveKind.Charge or null) return (MovePlanner.IntoContact(_r, s.Session.Table, _via, enemy, Cap), true);
        // Advance and Rush may not end in contact (that is a charge).
        return (s.Table.TableAt(at) is { } p ? MovePlanner.Towards(_r, s.Session.Table, _via, p.X, p.Y, Cap) : null, false);
    }

    private static Vector2 EndCentre(MovePlanner.Plan plan) =>
        new(plan.Ends.Average(e => e.X), plan.Ends.Average(e => e.Z));

    private void Draw(BattleScreen s, MovePlanner.Plan plan, bool placed, MovePlanner.Plan? preview)
    {
        Color Of(MovePlanner.Plan p) => !p.Valid ? Palette.WoundsRed : p.Distance <= _r.MaxAdvanceDistance + 0.01f ? HudKit.Lime
            : p.Distance <= _r.MaxRushDistance + 0.01f ? HudKit.Gold : HudKit.Enemy;
        Color c = Of(plan);
        if (placed) c = c.Lightened(0.25f);
        var ghosts = plan.Ends.Select(e => new BattleTable.Ghost(e.X, e.Z, Mathf.Max(0.4f, e.Radius), c with { A = placed ? 1.6f : 1f })).ToList();
        var lines = new List<BattleTable.GhostLine>();
        var models = Unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (models.Count > 0 && plan.Ends.Count > 0)
        {
            // The path through each turn point, with a small ring on each.
            var from = new Vector2(models.Average(m => m.Position.x), models.Average(m => m.Position.z));
            var points = _via.Select(v => new Vector2(v.X, v.Z)).ToList();
            if (points.Count == 0 || points[^1].DistanceTo(EndCentre(plan)) > 0.05f) points.Add(EndCentre(plan));
            foreach (Vector2 to in points) { lines.Add(new BattleTable.GhostLine(from, to, c with { A = 0.85f })); from = to; }
            foreach (var v in _via) ghosts.Add(new BattleTable.Ghost(v.X, v.Z, 0.45f, c with { A = 0.9f }, Ring: true));
            // The next leg being pointed at, faint.
            if (preview != null && preview.Ends.Count > 0)
            {
                Vector2 to = EndCentre(preview);
                lines.Add(new BattleTable.GhostLine(from, to, Of(preview) with { A = 0.4f }));
                ghosts.AddRange(preview.Ends.Select(e => new BattleTable.Ghost(e.X, e.Z, Mathf.Max(0.4f, e.Radius), Of(preview) with { A = 0.35f })));
            }
        }
        s.Table.SetGhosts("move", ghosts, lines);
        // Kai, 2026-11-11: the weapon ranges travel with the ghost, so you see what you'd reach from there.
        MovePlanner.Plan rangeFrom = preview ?? plan;
        var byId = Unit.Models.Where(m => m.GetIsAlive()).ToDictionary(m => m.ID);
        var (rl, rt) = BattleTable.RangeShapes(rangeFrom.Ends.Where(e => byId.ContainsKey(e.Id))
            .Select(e => (byId[e.Id], new Vector2(e.X, e.Z))), BattleTable.CoolRanges);
        s.Table.SetGhosts("moverange", Array.Empty<BattleTable.Ghost>(), rl, rt);
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (Done || _final) return;   // a path ending in contact takes no more points
        if (at.DistanceTo(_lastHover) < 2) return;
        _lastHover = at;
        var (plan, _) = PlanFor(s, at);
        bool nothing = plan == null || plan.Valid && plan.Distance - (_pending?.Distance ?? 0) < 0.05f;
        if (_pending != null) { Draw(s, _pending, placed: true, nothing ? null : plan); }
        else if (nothing) { s.Table.ClearGhosts("move"); s.Table.ClearGhosts("moverange"); _note = ""; Version++; return; }
        else Draw(s, plan!, placed: false, null);
        string next = _pending != null ? "add a turn there" : "place the move";
        _note = nothing ? "" : plan!.Valid ? $"{KindName(plan)}: {plan.Distance:0.0}\" - click to {next}." : $"Can't go there: {plan.Problem}.";
        Version++;
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (_final) { s.Toast("The move ends in contact - Space to confirm, or right-click to change it."); return true; }
        var (plan, contact) = PlanFor(s, at);
        if (plan == null)
        {
            if (_kind == MoveKind.Charge) s.Toast("Click the enemy unit you want to charge.");
            return true;
        }
        if (!plan.Valid || plan.Distance - (_pending?.Distance ?? 0) < 0.05f)
        {
            s.Toast(plan.Valid ? "That is where the unit already is." : $"Can't move there: {plan.Problem}.");
            return true;
        }
        if (_kind == MoveKind.Charge && !contact)
        {
            // A charge must end in contact: a table click is only a turn point on the way.
            _via.Add((EndCentre(plan).X, EndCentre(plan).Y));
            _pending = null;
            Draw(s, plan, placed: true, null);
            Version++;
            return true;
        }
        _pending = plan;
        if (contact) _final = true;
        else _via.Add((EndCentre(plan).X, EndCentre(plan).Y));
        Draw(s, plan, placed: true, null);
        Version++;
        return true;
    }
}

// ====================================================================== shooting

/// <summary>
/// Shoot (Kai, 2026-11-10/11): the shooter's weapon ranges show while it is selected; you click the enemy unit to
/// shoot and a confirm box lists every weapon that can reach it, ticked, with the engine's forecast. Untick a
/// weapon to keep it for a second target - the books' split fire: "each [weapon] group may be fired at a
/// different target, however you may fire only at up to two different targets" (GF/AoF Advanced 3.6.0 p8; the
/// engine enforces the cap). Confirming fires the ticked weapons (the engine asks weapon by weapon; the rest of the
/// volley is answered the same way). The actions area lists the weapons with Hold fire, and Done shooting / Back.
/// </summary>
public sealed class ShootDecision : Decision<CancellableResult<RangedAttackChoice>>
{
    private readonly ChooseRangedAttackRequest _r;
    private IUnit? _target;
    private BattleScreen? _s;
    private readonly HashSet<string> _unticked = new();
    public ShootDecision(ChooseRangedAttackRequest r) => _r = r;

    /// <summary>A weapon's identity across the engine's weapon-by-weapon requests.</summary>
    public static string Key(IWeapon w) => $"{w.Name}|{w.RangeInches}|{w.Attacks}|{w.ArmorPenetration}";

    private IUnit Shooter => _r.AttackingUnit.GetValue();
    /// <summary>Some enemy a weapon can fire at now (the test driver hovers it for the odds photo).</summary>
    public IUnit ShooterUnit => Shooter;
    public IUnit? AnyTarget => _r.WeaponOptions.SelectMany(w => w.WeaponTargetStats).FirstOrDefault(CanFire)?.TargetUnit.GetValue();

    public override string Prompt => _r.DeclareFirst ? $"{Shooter.Name} - declare shots" : $"{Shooter.Name} - shoot";
    public override string Hint => _target != null ? "Space to confirm - click another enemy to change - right-click to clear."
        : "Click the enemy unit to shoot. The rings show each weapon's range.";
    public override string? RowsTitle => "Weapons";

    public override string? CancelLabel => _r.AllowCancel ? "Back" : _r.AllowStopShooting ? "Done shooting" : null;
    public override void Cancel()
    {
        if (_r.AllowCancel || _r.AllowStopShooting) Complete(new Cancelled<RangedAttackChoice>());
    }

    private static bool CanFire(WeaponTargetStats t) => t.modelsThatCanShoot.Count > 0 && t.UnselectableReason == null;

    private IEnumerable<(WeaponOption W, WeaponTargetStats T)> At(UnitID target) =>
        _r.WeaponOptions.SelectMany(w => w.WeaponTargetStats.Where(t => t.TargetUnit.GetValue().ID.Equals(target) && CanFire(t)).Select(t => (w, t)));

    private void Fire(WeaponOption w, WeaponTargetStats t) =>
        Complete(new Selected<RangedAttackChoice>(new RangedAttackChoice(w.Weapon, t.TargetUnit)));

    private static string Forecast(WeaponOption w, WeaponTargetStats t) => t.Forecast is { } f
        ? $"{(f.AttacksFiring > 0 ? f.AttacksFiring : w.Weapon.Attacks)} attacks, hit on {f.HitRollNeeded}+, they save on {f.SaveRollNeeded}+"
          + (t.HasCover && !w.IgnoresCover ? " (cover)" : "")
        : "";

    public override string? ConfirmText
    {
        get
        {
            if (_target is not { } t) return null;
            var lit = At(t.ID).Where(x => !_unticked.Contains(Key(x.W.Weapon))).Select(x => x.W.Weapon.Name).Distinct().ToList();
            return lit.Count == 0 ? $"No weapon lit for {t.Name} - click a weapon to fire it."
                : $"Shoot {t.Name} with {string.Join(", ", lit)}?";
        }
    }

    public override void Confirm()
    {
        if (_target is not { } t) return;
        var ticked = At(t.ID).Where(x => !_unticked.Contains(Key(x.W.Weapon))).ToList();
        if (ticked.Count == 0) { _s?.Toast("Tick at least one weapon - or right-click to choose another target."); return; }
        BattleResolvers.ShootAt = (Shooter.ID, t.ID, ticked.Select(x => Key(x.W.Weapon)).ToHashSet());
        Fire(ticked[0].W, ticked[0].T);
    }

    public override void ClearPending() { _target = null; _unticked.Clear(); _s?.Table.ClearGhosts("target"); DrawReach(); Version++; }

    /// <summary>
    /// Kai, 2026-11-11: the weapons are square buttons like the rest. Lit = it fires at the target you clicked; click a
    /// lit weapon to keep it for another target (split fire). Hover a weapon to see which enemies it can reach (they
    /// are outlined on the table, not listed). A weapon that can reach nothing has a Hold fire button under it.
    /// </summary>
    public override bool ButtonChoices => true;
    public override string RowIcon => "crosshair";

    private int _hoverWeapon = -1;

    public override IReadOnlyList<ChoiceRow> Rows => _r.WeaponOptions.Select((w, i) =>
    {
        var targets = w.WeaponTargetStats.Where(CanFire).ToList();
        string limited = w.LimitedRule != null ? (w.LimitedAlreadyFired ? $" ({w.LimitedRule}: spent)" : $" ({w.LimitedRule})") : "";
        string range = w.Weapon.RangeInches > 0 ? $" {w.Weapon.RangeInches:0}\"" : "";
        bool atTarget = _target is { } t && targets.Any(x => x.TargetUnit.GetValue().ID.Equals(t.ID));
        bool lit = atTarget && !_unticked.Contains(Key(w.Weapon));
        string detail = _target is { } tt
            ? atTarget ? (lit ? $"Fires at {tt.Name}: " : $"Kept back from {tt.Name} - click to fire. ") + Forecast(w, targets.First(x => x.TargetUnit.GetValue().ID.Equals(tt.ID)))
                       : $"Can't reach {tt.Name}."
            : targets.Count > 0 ? "Can hit: " + string.Join(", ", targets.Select(x => x.TargetUnit.GetValue().Name)) : "";
        bool usable = targets.Count > 0;
        return new ChoiceRow($"{w.Weapon.Name}{range}{limited}", detail, usable, usable ? null : "Nothing in range or sight.",
            usable ? () => ToggleWeapon(w) : null,
            usable ? null : "Hold fire", usable ? null : () => Complete(new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(w.Weapon))),
            Hover: on => { _hoverWeapon = on ? i : -1; DrawReach(); }, Selected: lit);
    }).ToList();

    /// <summary>
    /// Kai, 2026-11-11 ("where should the odds show? - on hover"): the average result of shooting this enemy, from the
    /// engine's own to-hit and save numbers. Before a target is picked every weapon that reaches counts; for the picked
    /// target only the lit ones. Average dice only - weapon rules that change the dice (Blast, Rending...) are not priced.
    /// </summary>
    public override IReadOnlyList<string>? HoverOdds(IUnit enemy)
    {
        var firing = At(enemy.ID).Where(x => _target?.ID.Equals(enemy.ID) != true || !_unticked.Contains(Key(x.W.Weapon))).ToList();
        if (firing.Count == 0) return new[] { "Can't shoot this unit now." };
        double hits = 0, wounds = 0;
        foreach (var (w, t) in firing)
        {
            int dice = t.Forecast is { AttacksFiring: > 0 } f0 ? f0.AttacksFiring : w.Weapon.Attacks * Math.Max(1, t.modelsThatCanShoot.Count);
            int hitOn = Math.Clamp(t.Forecast?.HitRollNeeded ?? 4, 2, 6), saveOn = Math.Clamp(t.Forecast?.SaveRollNeeded ?? 4, 2, 7);
            double h = dice * (7 - hitOn) / 6.0;
            hits += h;
            wounds += h * (saveOn - 1) / 6.0;
        }
        var alive = enemy.Models.Where(m => m.GetIsAlive()).ToList();
        double perModel = alive.Count == 0 ? 1 : Math.Max(1, alive.Average(m => (double)m.TotalWounds));
        int dead = (int)Math.Min(alive.Count, Math.Floor(wounds / perModel + 0.25));
        string weapons = string.Join(", ", firing.Select(x => x.W.Weapon.Name).Distinct());
        return new[]
        {
            "IF YOU SHOOT (average dice)",
            $"{weapons}",
            $"About {hits:0.#} hits, {wounds:0.#} wounds after saves",
            dead == 0 ? "Likely kills: none (maybe 1 on good dice)" : $"Likely kills: about {dead} of {alive.Count} model{(alive.Count == 1 ? "" : "s")}",
            "Blast, Rending and similar rules not counted.",
        };
    }

    private void ToggleWeapon(WeaponOption w)
    {
        if (_target == null) { _s?.Toast("Click an enemy on the table first - the lit weapons fire at it."); return; }
        string k = Key(w.Weapon);
        if (!_unticked.Remove(k)) _unticked.Add(k);
        Version++;
    }

    /// <summary>The enemies in reach outlined: every weapon's (gold), or the hovered weapon's only (its range colour).</summary>
    private void DrawReach()
    {
        if (_s == null || Done) return;   // a hover-off after the shot is sent must not redraw
        var lines = new List<BattleTable.GhostLine>();
        for (int i = 0; i < _r.WeaponOptions.Count; i++)
        {
            if (_hoverWeapon >= 0 && i != _hoverWeapon) continue;
            Color c = _hoverWeapon >= 0 ? BattleTable.CoolRanges[i % BattleTable.CoolRanges.Length] : HudKit.Gold;
            foreach (WeaponTargetStats t in _r.WeaponOptions[i].WeaponTargetStats.Where(CanFire))
                if (_target == null || !t.TargetUnit.GetValue().ID.Equals(_target.ID))
                    lines.AddRange(BattleTable.Outline(t.TargetUnit.GetValue(), 0.45f, c with { A = 0.9f }, 0.14f));
        }
        _s.Table.SetGhosts("targets", Array.Empty<BattleTable.Ghost>(), lines);
    }

    public override void Shown(BattleScreen s)
    {
        _s = s;
        s.SelectUnit(Shooter);
        DrawReach();
        // The rest of a confirmed volley: every other ticked weapon fires at the same target.
        if (BattleResolvers.ShootAt is { } again && again.Shooter.Equals(Shooter.ID))
        {
            if (At(again.Target).FirstOrDefault(x => again.Weapons.Contains(Key(x.W.Weapon))) is { W: not null } next) { Fire(next.W, next.T); return; }
            BattleResolvers.ShootAt = null;
        }
    }

    public override void Closed(BattleScreen s) { s.Table.ClearGhosts("target"); s.Table.ClearGhosts("targets"); }

    public override void AutoPlay(BattleScreen s)
    {
        foreach (WeaponOption w in _r.WeaponOptions)
            foreach (WeaponTargetStats t in w.WeaponTargetStats)
                if (CanFire(t)) { Fire(w, t); return; }
        if (_r.AllowCancel || _r.AllowStopShooting) Cancel();
        else Complete(new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(_r.WeaponOptions[0].Weapon)));
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit || s.Session.IsMine(unit.PlayerID)) return false;
        if (!At(unit.ID).Any())
        {
            string why = _r.WeaponOptions.SelectMany(w => w.WeaponTargetStats)
                .Where(t => t.TargetUnit.GetValue().ID.Equals(unit.ID)).Select(t => t.UnselectableReason).FirstOrDefault(r => r != null)
                ?? "out of range or out of sight";
            s.Toast($"Can't shoot {unit.Name}: {why}");
            return true;
        }
        if (_target?.ID.Equals(unit.ID) != true) _unticked.Clear();
        _target = unit;
        // The shooter keeps its outline; the target gets a red one.
        s.Table.SetGhosts("target", Array.Empty<BattleTable.Ghost>(), BattleTable.Outline(unit, 0.45f, Palette.WoundsRed, 0.22f));
        DrawReach();
        Version++;
        return true;
    }
}

// ====================================================================== generic lists (docked in the actions area)

public sealed class StringListDecision : Decision<string>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "flying-flag";
    private readonly StringSelectionRequest _r;
    public StringListDecision(StringSelectionRequest r) => _r = r;

    public override string Prompt => _r.Instructions;
    public override string? RowsTitle => "Choose";
    public override string? CancelLabel => _r.AllowCancel ? "Back" : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }
    public override void AutoPlay(BattleScreen s) => Rows.First(r => r.Enabled && r.Pick != null).Pick!();

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            // #321: a companion ("hold this weapon back") sits on its owner's row, never as a row of its own.
            var companions = _r.SecondaryActions ?? new Dictionary<string, StringSelectionRequest.SecondaryAction>();
            var companionOptions = companions.Values.Select(c => c.Option).ToHashSet();
            string? Desc(string o) => _r.OptionDescriptions != null && _r.OptionDescriptions.TryGetValue(o, out string? d) ? d : null;
            var rows = new List<ChoiceRow>();
            foreach (string o in _r.ValidOptions.Where(o => !companionOptions.Contains(o)))
            {
                companions.TryGetValue(o, out var c);
                rows.Add(new ChoiceRow(o, Desc(o), true, null, () => Complete(o),
                    c?.ShortLabel, c == null ? null : () => Complete(c.Option)));
            }
            foreach (var i in _r.InvalidOptions.Where(i => !companionOptions.Contains(i.Option)))
                rows.Add(new ChoiceRow(i.Option, Desc(i.Option), false, i.Reason, null));
            return rows;
        }
    }
}

public sealed class YesNoDecision : Decision<bool>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "checked-shield";
    private readonly YesNoRequest _r;
    public YesNoDecision(YesNoRequest r) => _r = r;
    public override string Prompt => _r.QuestionText;
    public override string? RowsTitle => "Choose";
    public override void AutoPlay(BattleScreen s) => Complete(_r.DefaultAnswer);
    public override IReadOnlyList<ChoiceRow> Rows => new[]
    {
        new ChoiceRow("Yes", null, true, null, () => Complete(true), Icon: "checked-shield"),
        new ChoiceRow("No", null, true, null, () => Complete(false), Icon: "stop-sign"),
    };
}

/// <summary>
/// Pick a unit (which to deploy, a spell target, a transport...). Kai, 2026-11-10: no pop-up list - the
/// left-hand list marks the choices (and why the others can't be picked), and the table is clickable.
/// </summary>
/// <summary>A unit pick of any reply shape - the battle screen follows its side of the list (the ENEMY tab).</summary>
public interface IUnitPick { }

/// <summary>
/// Pick a unit: in the left-hand list or on the table, the choices outlined in gold. One class for every unit pick
/// the engine asks (a plain selection, a cancellable one, ...); <paramref name="pick"/> turns the chosen unit into
/// the reply and <paramref name="cancel"/>, when there is a way out, makes the "no thanks" reply.
/// </summary>
public class UnitPickDecision<T> : Decision<T>, IUnitPick
{
    public sealed record Option(DataBinding<UnitData> Unit, string Name, string? Reason = null);

    private readonly string _instructions;
    private readonly IReadOnlyList<Option> _valid, _invalid;
    private readonly string? _cancelLabel;
    private readonly Func<DataBinding<UnitData>, T> _pick;
    private readonly Func<T>? _cancel;
    private BattleScreen? _s;

    public UnitPickDecision(string instructions, IReadOnlyList<Option> valid, IReadOnlyList<Option> invalid,
        Func<DataBinding<UnitData>, T> pick, string? cancelLabel = null, Func<T>? cancel = null)
    {
        _instructions = instructions;
        _valid = valid;
        _invalid = invalid;
        _pick = pick;
        _cancelLabel = cancel != null ? cancelLabel ?? "Back" : null;
        _cancel = cancel;
    }

    public override string Prompt => _instructions;
    public override string Hint => "Click it in the list on the left, or on the table.";
    public override void AutoPlay(BattleScreen s)
    {
        if (_valid.Count > 0) Complete(_pick(_valid[0].Unit));
        else if (_cancel != null) Complete(_cancel());
    }
    public override string? CancelLabel => _cancelLabel;
    public override void Cancel() { if (_cancel != null) Complete(_cancel()); }

    public override UnitMark? ListMark(IUnit unit)
    {
        if (_valid.Any(o => o.Unit.GetValue().ID.Equals(unit.ID))) return new UnitMark(true, null);
        var bad = _invalid.FirstOrDefault(o => o.Unit.GetValue().ID.Equals(unit.ID));
        return bad != null ? new UnitMark(false, bad.Reason) : null;
    }

    public override bool WantsEnemyList => _s != null && _valid.Count > 0
        && _valid.All(o => !_s.Session.IsMine(o.Unit.GetValue().PlayerID));

    /// <summary>
    /// The choices are also listed in the actions area when the list on the left can't show them all on one
    /// tab (targets on both sides - a spell that can hit friend or foe), or when the engine's labels say more
    /// than the unit's name ("Hive Lord (via Synaptic Relay, +1)"). A plain deploy / activate pick has no list.
    /// </summary>
    private bool Listed => _s != null && _valid.Count > 0 && (AlwaysListed
        || _valid.Select(o => _s.Session.IsMine(o.Unit.GetValue().PlayerID)).Distinct().Count() > 1);   // Kai, 2026-11-11: else the left list only

    /// <summary>Show the units as buttons even when the left list could (Kai, 2026-11-11: Skip picks show them too).</summary>
    protected virtual bool AlwaysListed => false;

    public override string? RowsTitle => "Choose";
    public override bool ButtonChoices => true;
    public override string RowIcon => "person";

    public override IReadOnlyList<ChoiceRow>? Rows => !Listed ? null
        : _valid.Select(o => new ChoiceRow(o.Name, null, true, null, () => Complete(_pick(o.Unit)), Portrait: o.Unit.GetValue()))
            .Concat(_invalid.Select(i => new ChoiceRow(i.Name, null, false, i.Reason, null, Portrait: i.Unit.GetValue()))).ToList();

    public override void Shown(BattleScreen s)
    {
        _s = s;
        // Every unit that can be picked gets a gold outline on the table (spell targets, transports...).
        s.Table.SetGhosts("targets", Array.Empty<BattleTable.Ghost>(), _valid
            .SelectMany(o => BattleTable.Outline(o.Unit.GetValue(), 0.45f, HudKit.Gold, 0.16f)));
        Version++;
    }

    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("targets");

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit) return false;
        var o = _valid.FirstOrDefault(v => v.Unit.GetValue().ID.Equals(unit.ID));
        if (o == null) return false;
        Complete(_pick(o.Unit));
        return true;
    }

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (_valid.FirstOrDefault(v => v.Unit.GetValue().ID.Equals(unit.ID)) is { } o) Complete(_pick(o.Unit));
        else if (_invalid.FirstOrDefault(v => v.Unit.GetValue().ID.Equals(unit.ID)) is { } bad) s.Toast($"{unit.Name}: {bad.Reason}");
    }
}

/// <summary>The engine's plain unit selection (activate, deploy, spell targets...).</summary>
public sealed class UnitPickDecision : UnitPickDecision<DataBinding<UnitData>>
{
    public UnitPickDecision(SelectionRequest<UnitData> r) : base(r.Instructions,
        r.ValidOptions.Select(o => new Option(o.Option, o.Name)).ToList(),
        r.InvalidOptions.Select(o => new Option(o.Option, o.Name, o.Reason)).ToList(),
        u => u, r.AllowCancel ? r.CancelLabel : null, r.AllowCancel ? () => null! : null) { }
}

/// <summary>
/// A pick with a "no thanks": Storm and multi-target abilities, Retreating Strike, a unit to activate next,
/// moving through an enemy... (24 places in the engine; the bot answered them all until build 1.54).
/// </summary>
public sealed class CancellableUnitPickDecision : UnitPickDecision<CancellableResult<DataBinding<UnitData>>>
{
    public CancellableUnitPickDecision(CancellableSelectionRequest<UnitData> r) : base(r.Instructions,
        r.ValidOptions.Select(o => new Option(o.Option, o.Name)).ToList(),
        r.InvalidOptions.Select(o => new Option(o.Option, o.Name, o.Reason)).ToList(),
        u => new Selected<DataBinding<UnitData>>(u), "Skip", () => new Cancelled<DataBinding<UnitData>>()) { }

    protected override bool AlwaysListed => true;
}

public sealed class ModelPickDecision : Decision<DataBinding<ModelData>>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "targeting";
    private readonly SelectionRequest<ModelData> _r;
    private BattleScreen? _s;
    private int _hovered = -1;
    public ModelPickDecision(SelectionRequest<ModelData> r) => _r = r;
    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);

    /// <summary>The rule the pick is for ("Takedown: choose the target model" -> Takedown), for its book text.</summary>
    private string? RuleName => _r.Instructions.Contains(':') ? _r.Instructions[.._r.Instructions.IndexOf(':')].Trim() : null;

    public override string Prompt => _r.Instructions;
    public override string Hint => "The numbers on the table match the list - click a model there or in the list.";
    public override string? RowsTitle => RuleName ?? "Choose";
    public override string? CancelLabel => _r.AllowCancel ? _r.CancelLabel : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }

    private static string Describe(IModel m)
    {
        string weapons = string.Join(", ", m.Weapons.Select(w => w.Name).Distinct());
        string wounds = m.TotalWounds > 1 ? $" - {MathF.Ceiling(m.RemainingWounds):0}/{m.TotalWounds:0} wounds" : "";
        return weapons + wounds;
    }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            // Kai, 2026-11-11: "nothing in game explains what Takedown is" - the rule's book wording comes first.
            if (RuleName is { } rule && _s?.RuleText(rule) is { } text)
                rows.Add(new ChoiceRow(rule, text, true, null, null));
            for (int i = 0; i < _r.ValidOptions.Count; i++)
            {
                var o = _r.ValidOptions[i];
                int n = i;
                rows.Add(new ChoiceRow(o.Name, Describe(o.Option.GetValue()), true, null, () => Complete(o.Option),
                    Hover: on => { _hovered = on ? n : -1; Draw(); }));
            }
            rows.AddRange(_r.InvalidOptions.Select(i => new ChoiceRow(i.Name, null, false, i.Reason, null)));
            return rows;
        }
    }

    public override void Shown(BattleScreen s) { _s = s; Draw(); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("pick");

    /// <summary>Each choosable model gets its list number over it; the hovered one gets a bright ring.</summary>
    private void Draw()
    {
        if (_s == null || Done) return;
        // Kai, 2026-11-11: one outline round the unit, a number on each model, a ring only on the hovered one.
        var rings = new List<BattleTable.Ghost>();
        var texts = new List<BattleTable.GhostText>();
        var lines = new List<BattleTable.GhostLine>();
        var units = _s.Session.Table.Units.Objects
            .Where(u => u.Models.Any(m => _r.ValidOptions.Any(o => o.Option.GetValue().ID.Equals(m.ID)))).ToList();
        foreach (IUnit u in units) lines.AddRange(BattleTable.Outline(u, 0.35f, HudKit.Gold, 0.18f));
        for (int i = 0; i < _r.ValidOptions.Count; i++)
        {
            ModelData m = _r.ValidOptions[i].Option.GetValue();
            bool hot = i == _hovered;
            if (hot) rings.Add(new BattleTable.Ghost(m.Position.x, m.Position.z, m.BaseRadiusInches + 0.3f, HudKit.Lime, Ring: true));
            texts.Add(new BattleTable.GhostText(m.Position.x, m.Position.z, (i + 1).ToString(), hot ? HudKit.Lime : HudKit.Gold, 0.02f));
        }
        _s.Table.SetGhosts("pick", rings, lines, texts);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.ModelAt(at) is not { } model) return false;
        var o = _r.ValidOptions.FirstOrDefault(v => v.Option.GetValue().ID.Equals(model.ID));
        if (o == null) return false;
        Complete(o.Option);
        return true;
    }
}

// ====================================================================== placing models (deployment, Scout, Ambush...)

/// <summary>
/// Place a unit (Kai, 2026-11-11 - deployment was the bot's job until now): the zone is outlined, the unit's ghost
/// follows the pointer as the block the engine would actually place there (nearest legal spot: in the zone,
/// clear of other units, enemies and impassable ground), a click places it, Space confirms. Right-click clears,
/// then goes back to the unit list when the stage allows it.
/// </summary>
public sealed class PlaceDecision : Decision<CancellableResult<List<PlacedObjectEntry<ModelData>>>>
{
    private readonly PlaceObjectsRequest<ModelData> _r;
    private BattleScreen? _s;
    private AimedPlacement? _placer;
    private List<PlacedObjectEntry<ModelData>>? _pending;
    private Vector2 _lastHover = new(-9999, -9999);

    public PlaceDecision(PlaceObjectsRequest<ModelData> r) => _r = r;

    private string UnitName => _s?.Session.Table.Units.Objects
        .FirstOrDefault(u => u.Models.Any(m => _r.ModelsToPlace.Count > 0 && m.ID.Equals(_r.ModelsToPlace[0].GetValue().ID)))?.Name ?? "the unit";

    public override string Prompt => string.IsNullOrWhiteSpace(_r.DisplayName) ? $"Place {UnitName}" : _r.DisplayName;
    public override string Hint => AimedPlacement.IsReposition(_r)
        ? "This one can only stay where it is for now."
        : _pending != null ? "Space to confirm - click elsewhere to change it - right-click to clear."
        : _near.Length > 0 ? _near
        : "Click inside the outlined zone - the unit is set up there in formation.";

    public override string? CancelLabel => _r.AllowCancel ? "Pick another unit" : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(new Cancelled<List<PlacedObjectEntry<ModelData>>>()); }

    public override IReadOnlyList<ActionSlot>? Actions => AimedPlacement.IsReposition(_r)
        ? new[] { new ActionSlot("StayPut", "shield", "Stay put", "1", true, "Leave every model where it is.", () => Complete(new Selected<List<PlacedObjectEntry<ModelData>>>(AimedPlacement.StayPut(_r)))) }
        : null;

    public override string? ConfirmText => _pending != null ? $"Deploy {UnitName} here?" : null;
    public override void Confirm()
    {
        if (_pending is not { } p) return;
        Complete(new Selected<List<PlacedObjectEntry<ModelData>>>(p));
    }
    public override void ClearPending()
    {
        _pending = null;
        _lastHover = new(-9999, -9999);
        _s?.Table.ClearGhosts("move");
        Version++;
    }

    public override void Shown(BattleScreen s)
    {
        _s = s;
        _placer = new AimedPlacement(s.Session.Table);
        // The zone, and any keep-out circles round enemies.
        var lines = new List<BattleTable.GhostLine>();
        foreach (var poly in FdgRaylib.Rendering.ZoneOutlines.Polygons(_r.DeploymentZone))
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                lines.Add(new BattleTable.GhostLine(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), HudKit.Lime with { A = 0.9f }, 0.2f));
            }
        // Keep-out areas (Scout / Ambush "over 9 inches from enemies"): ONE outline per enemy unit, not a circle per
        // model (Kai, 2026-11-11). Each disc belongs to the unit whose model it is centred on.
        var enemyModels = s.Session.Table.Units.Objects.Where(u => u.GetIsOnBattlefield())
            .SelectMany(u => u.Models.Where(m => m.GetIsAlive()).Select(m => (u.ID, m))).ToList();
        foreach (var group in _r.EnemyKeepOutDiscs.GroupBy(d => enemyModels.Count == 0 ? default
                     : enemyModels.OrderBy(x => Mathf.Pow(x.m.Position.x - d.Center.x, 2) + Mathf.Pow(x.m.Position.z - d.Center.z, 2)).First().ID))
        {
            Vector2[] hull = BattleTable.HullAt(group.Select(d => (new Vector2(d.Center.x, d.Center.z), d.RadiusInches)).ToList());
            for (int k = 0; k + 1 < hull.Length; k++) lines.Add(new BattleTable.GhostLine(hull[k], hull[k + 1], Palette.WoundsRed with { A = 0.8f }, 0.16f));
        }
        s.Table.SetGhosts("zone", Array.Empty<BattleTable.Ghost>(), lines);
        Version++;
    }

    public override void Closed(BattleScreen s) { s.Table.ClearGhosts("zone"); s.Table.ClearGhosts("move"); }

    public override void AutoPlay(BattleScreen s) =>
        Complete(new FDG.Ai.Resolvers.AiPlaceObjectsResolver<ModelData>(s.Session.Table).Resolve(_r).GetAwaiter().GetResult());

    private List<PlacedObjectEntry<ModelData>>? PlanAt(BattleScreen s, Vector2 at) =>
        AimedPlacement.IsReposition(_r) || _placer == null || s.Table.TableAt(at) is not { } p ? null : _placer.At(_r, p.X, p.Y);

    private string _near = "";

    /// <summary>
    /// The block, and - when the engine had to shift it away from the pointer to find a legal spot - a line from
    /// the pointer to where it will really go, so the gap between mouse and unit is never a surprise.
    /// </summary>
    private void Draw(BattleScreen s, List<PlacedObjectEntry<ModelData>> plan, bool placed, Vector2? aim)
    {
        Color c = placed ? HudKit.Lime.Lightened(0.25f) : HudKit.Lime;
        var mid = new Vector2(plan.Average(e => e.Position.x), plan.Average(e => e.Position.z));
        var lines = new List<BattleTable.GhostLine>();
        _near = "";
        if (aim is { } a && a.DistanceTo(mid) > 1.5f)
        {
            lines.Add(new BattleTable.GhostLine(a, mid, HudKit.Gold with { A = 0.9f }, 0.1f));
            _near = $"Can't stand right there - the nearest legal spot is {a.DistanceTo(mid):0}\" away (gold line).";
        }
        s.Table.SetGhosts("move", plan.Select(e => new BattleTable.Ghost(e.Position.x, e.Position.z,
            Mathf.Max(0.4f, e.Binding.GetValue().BaseRadiusInches), c with { A = placed ? 1.6f : 1f })), lines);
        Version++;
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null || at.DistanceTo(_lastHover) < 2) return;
        _lastHover = at;
        if (PlanAt(s, at) is { } plan) Draw(s, plan, placed: false, s.Table.TableAt(at));
        else { s.Table.ClearGhosts("move"); _near = ""; }
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (PlanAt(s, at) is not { } plan) return AimedPlacement.IsReposition(_r);
        _pending = plan;
        Draw(s, plan, placed: true, s.Table.TableAt(at));
        return true;
    }
}

// ====================================================================== smaller choices

/// <summary>Who to fight (Charge into several units, or a fight with several in reach): click the enemy, or pick it from the list.</summary>
public sealed class MeleeTargetDecision : Decision<CancellableResult<DataBinding<UnitData>>>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "crossed-swords";
    private readonly ChooseMeleeDefenderRequest _r;
    public MeleeTargetDecision(ChooseMeleeDefenderRequest r) => _r = r;
    public override string Prompt => "Choose who to fight";
    public override string Hint => string.IsNullOrWhiteSpace(_r.Instructions) ? "Click the enemy unit, or pick it from the list." : _r.Instructions;
    public override string? CancelLabel => "Back";
    public override void Cancel() => Complete(new Cancelled<DataBinding<UnitData>>());
    public override bool WantsEnemyList => true;
    public override void AutoPlay(BattleScreen s) => Complete(new Selected<DataBinding<UnitData>>(_r.ValidOptions[0].Option));

    private CancellableSelectionRequest<UnitData>.ValidOption? Option(IUnit u) => _r.ValidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(u.ID));

    public override UnitMark? ListMark(IUnit unit)
    {
        if (Option(unit) != null) return new UnitMark(true, null);
        var bad = _r.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID));
        return bad != null ? new UnitMark(false, bad.Reason) : null;
    }

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (Option(unit) is { } o) Complete(new Selected<DataBinding<UnitData>>(o.Option));
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit || Option(unit) is not { } o) return false;
        Complete(new Selected<DataBinding<UnitData>>(o.Option));
        return true;
    }

    // Kai, 2026-11-11: only the enemies you can fight are buttons (the left list still says why the others can't be).
    public override IReadOnlyList<ChoiceRow> Rows =>
        _r.ValidOptions.Select(o => new ChoiceRow(o.Name, null, true, null, () => Complete(new Selected<DataBinding<UnitData>>(o.Option)), Portrait: o.Option.GetValue())).ToList();
    public override string? RowsTitle => "Fight";
}

/// <summary>"Pick one effect" rules at deployment / activation (Versatile..., army rules like Vinci Tech): a list with the book text.</summary>
public sealed class AbilityEffectDecision : Decision<int>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "scroll-unfurled";
    private readonly ChooseAbilityEffectRequest _r;
    public AbilityEffectDecision(ChooseAbilityEffectRequest r) => _r = r;
    public override string Prompt => $"{_r.BearerName}: {_r.RuleName}";
    public override string Hint => _r.Instructions;
    public override string? RowsTitle => _r.RuleName;
    public override void AutoPlay(BattleScreen s) => Complete(0);
    public override IReadOnlyList<ChoiceRow> Rows =>
        _r.Options.Select((o, i) => new ChoiceRow(o.Label, o.Description, true, null, () => Complete(i))).ToList();
}

/// <summary>The once-a-round "do you want to concede?" check.</summary>
public sealed class ConcedeDecision : Decision<bool>
{
    /// <summary>Kai's own wording for the surrender popup (2026-11-11, review notes) - keep it word for word.</summary>
    public const string SurrenderTitle = "Confirm your Surrender? Like a BITCH?";
    public const string SurrenderYes = "Yes, i am a pussy bitch";
    public const string SurrenderNo = "No, Ill take my punishment like a good boy";

    private void Ask(BattleScreen s) => s.Confirm(SurrenderTitle, "The game ends now and counts as a loss.", SurrenderYes, () => Complete(true), SurrenderNo);

    public override bool ButtonChoices => true;
    public override string RowIcon => "stop-sign";
    private readonly ConcedeCheckRequest _r;
    private BattleScreen? _s;
    public ConcedeDecision(ConcedeCheckRequest r) => _r = r;
    public override string Prompt => $"Round {_r.Round} - keep fighting?";
    public override string? RowsTitle => "Concede";
    public override void Shown(BattleScreen s) => _s = s;
    public override void AutoPlay(BattleScreen s) => Complete(false);
    public override IReadOnlyList<ChoiceRow> Rows => new[]
    {
        new ChoiceRow("Keep fighting", null, true, null, () => Complete(false), Icon: "crossed-swords"),
        new ChoiceRow("Concede the game", "Ends the game as a loss.", true, null,
            () => { if (_s != null) Ask(_s); else Complete(true); }),
    };
}

// ====================================================================== spells

/// <summary>
/// Cast a spell (Kai, 2026-11-11): the caster's spells as a list in the actions area - name and cost, what it does,
/// greyed with the reason when it can't be cast. Picking one then asks how hard to push the roll: each extra spell
/// token (and, for a Star Quest hero, each Power) is +1 to the cast, shown as the number needed. Targets are picked
/// next, on the table (gold outlines) or from the list.
/// </summary>
public sealed class SpellDecision : Decision<ChooseSpellReply>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "magic-swirl";
    private readonly ChooseSpellRequest _r;
    private int _spell = -1;
    public SpellDecision(ChooseSpellRequest r) => _r = r;

    private IUnit Caster => _r.CastingUnit.GetValue();
    private static int Needed(int threshold, int boost) => Math.Max(global::DiceUtilities.MINIMUM_SUCCESS_ROLL, threshold - boost);

    public override string Prompt => _spell < 0 ? $"{Caster.Name} - cast a spell" : $"{Caster.Name} - {_r.Spells[_spell].Label}";
    public override string Hint => _spell < 0
        ? $"{_r.AvailableTokens} spell token{(_r.AvailableTokens == 1 ? "" : "s")}" + (_r.AvailablePower > 0 ? $", {_r.AvailablePower} Power" : "")
          + $". Casts on {_r.BaseThreshold}+." + (_r.RelaysInRange.Count > 0 ? $" Relays in range: {string.Join(", ", _r.RelaysInRange.Select(x => $"{x.UnitName} +{x.RollBonus}"))}." : "")
        : "Spend extra to push the roll - each one is +1."
          + (_r.HinderTokensInRange > 0 ? $" Enemy casters nearby hold {_r.HinderTokensInRange} token(s) to hinder you." : "");
    public override string? RowsTitle => _spell < 0 ? "Spells" : "Push the roll";

    public override string? CancelLabel => "Back";
    public override void Cancel()
    {
        if (_spell >= 0) { _spell = -1; Version++; }
        else Complete(ChooseSpellReply.Cancel);
    }

    public override void Shown(BattleScreen s) => s.SelectUnit(Caster);

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            if (_spell < 0)
                return _r.Spells.Select((sp, i) => new ChoiceRow(sp.Label, sp.Description, sp.Castable, sp.UnavailableReason,
                    sp.Castable ? () => { _spell = i; Version++; } : null)).ToList();
            var spell = _r.Spells[_spell];
            int affordable = Math.Max(0, _r.AvailableTokens - spell.Cost);
            int useful = _r.MaxUsefulBoost;
            var rows = new List<ChoiceRow>();
            for (int t = 0; t <= Math.Min(affordable, useful); t++)
            {
                int tokens = t;
                rows.Add(new ChoiceRow(t == 0 ? $"Cast - no extra (needs {Needed(_r.BaseThreshold, 0)}+)" : $"+{t} token{(t == 1 ? "" : "s")} (needs {Needed(_r.BaseThreshold, t)}+)",
                    t == 0 ? $"Costs {spell.Cost} token{(spell.Cost == 1 ? "" : "s")}." : $"Costs {spell.Cost + t} tokens in all.",
                    true, null, () => Complete(new ChooseSpellReply(_spell, tokens, 0))));
            }
            for (int p = 1; p <= Math.Min(_r.AvailablePower, useful); p++)
            {
                int power = p;
                rows.Add(new ChoiceRow($"+{p} Power (needs {Needed(_r.BaseThreshold, p)}+)", "Power is the hero's whole-mission budget.",
                    true, null, () => Complete(new ChooseSpellReply(_spell, 0, power))));
            }
            return rows;
        }
    }

    public override void AutoPlay(BattleScreen s)
    {
        int i = _r.Spells.ToList().FindIndex(x => x.Castable);
        Complete(i < 0 ? ChooseSpellReply.Cancel : new ChooseSpellReply(i, 0, 0));
    }
}

/// <summary>Another caster is casting: spend tokens to help a friend (+1 each) or hinder an enemy (-1 each)?</summary>
public sealed class CastAssistDecision : Decision<CastAssistReply>
{
    public override bool ButtonChoices => true;
    public override string RowIcon => "lightning-helix";
    private readonly CastAssistRequest _r;
    public CastAssistDecision(CastAssistRequest r) => _r = r;

    private string Caster => _r.CastingUnit.GetValue().Name;
    private string Helper => _r.AssistingUnit.GetValue().Name;

    public override string Prompt => _r.IsFriendly ? $"Help {Caster} cast {_r.SpellName}?" : $"Hinder {Caster} casting {_r.SpellName}?";
    public override string Hint => $"{Helper} has {_r.AvailableTokens} token(s)" + (_r.AvailablePower > 0 ? $" and {_r.AvailablePower} Power" : "")
        + (_r.IsFriendly ? " - each spent is +1 to the cast." : " - each spent is -1 to their cast.");
    public override string? RowsTitle => _r.SpellName;

    public override void Shown(BattleScreen s)
    {
        s.SelectUnit(_r.AssistingUnit.GetValue());
        s.Table.SetGhosts("targets", Array.Empty<BattleTable.Ghost>(),
            BattleTable.Outline(_r.CastingUnit.GetValue(), 0.45f, _r.IsFriendly ? HudKit.Teal : Palette.WoundsRed, 0.18f));
    }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("targets");
    public override void AutoPlay(BattleScreen s) => Complete(CastAssistReply.None);

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>
            {
                new(_r.SpellName, _r.SpellDescription, true, null, null),
                new("Spend nothing", null, true, null, () => Complete(CastAssistReply.None)),
            };
            for (int t = 1; t <= _r.AvailableTokens; t++)
            {
                int tokens = t;
                rows.Add(new ChoiceRow($"Spend {t} token{(t == 1 ? "" : "s")} ({(_r.IsFriendly ? "+" : "-")}{t})", null, true, null,
                    () => Complete(new CastAssistReply(tokens, 0))));
            }
            for (int p = 1; p <= _r.AvailablePower; p++)
            {
                int power = p;
                rows.Add(new ChoiceRow($"Spend {p} Power ({(_r.IsFriendly ? "+" : "-")}{p})", null, true, null,
                    () => Complete(new CastAssistReply(0, power))));
            }
            return rows;
        }
    }
}
