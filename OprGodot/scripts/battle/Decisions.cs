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
public sealed record ActionSlot(string Name, string Icon, string Label, string Key, bool Enabled, string Tooltip, Action Choose);

/// <summary>
/// One row of a choice list, docked in the actions area. <paramref name="Pick"/> null = an information row
/// (not clickable). <paramref name="Secondary"/> is a companion button on the same row (#321).
/// </summary>
public sealed record ChoiceRow(string Label, string? Detail, bool Enabled, string? Reason, Action? Pick,
    string? SecondaryLabel = null, Action? Secondary = null, Action<bool>? Hover = null);

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

    public override IReadOnlyList<ActionSlot> Actions => Book.Grid.Select(g => new ActionSlot(
        g.Action.ToString(), g.Icon, g.Action.ToString(), g.Key, _pick != null,
        _pick != null ? Book.Tip(g.Action) : "Pick a unit first.", () => Act(g.Action))).ToList();

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
            return Book.Grid.Select(g => Slot(g.Action.ToString(), g.Icon, g.Action.ToString(), g.Key, Allowed(g.Action),
                Allowed(g.Action) ? Book.Tip(g.Action) : NotAllowed(g.Action), () => Do(g.Action, confirm: true))).ToList();
        }
    }

    private bool Allowed(BookAction a) => a switch
    {
        BookAction.Hold => Can(ShootOpt) || Can(PassOpt),
        BookAction.Charge => Can(ChargeOpt) || Can(MoveOpt),
        _ => Can(MoveOpt),
    };

    private string NotAllowed(BookAction a) => a switch
    {
        BookAction.Hold => Why(PassOpt) ?? Why(ShootOpt) ?? "Not available.",
        BookAction.Charge => Why(ChargeOpt) ?? Why(MoveOpt) ?? "Not available.",
        _ => Why(MoveOpt) ?? "Not available.",
    };

    private static ActionSlot Slot(string name, string icon, string label, string key, bool ok, string? tip, Action go) =>
        new(name, icon, label, key, ok, tip ?? "Not available.", go);

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
                .Select(o => new ChoiceRow(o, Describe(o), true, null, () => Complete(o)))
                .Concat(_r.InvalidOptions.Where(i => !Handled.Contains(i.Option))
                    .Select(i => new ChoiceRow(i.Option, null, false, i.Reason, null)))
                .ToList();
            return extra.Count > 0 ? extra : null;
        }
    }

    public override string? RowsTitle => "Other actions";

    public override void AutoPlay(BattleScreen s)
    {
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
/// there, with why). A click PLACES the move; Space or the confirm box sends it (Kai, 2026-11-10). Click
/// elsewhere to move the placed ghost, right-click to drop it, right-click again to go back.
/// </summary>
public sealed class MoveDecision : Decision<CancellableResult<List<ModelMoveEntry>>>
{
    private readonly DefineMovementPathRequest _r;
    private MovePlanner.Plan? _plan, _pending;
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

    public override string Hint => _pending != null ? "Space to confirm - click elsewhere to change it - right-click to clear."
        : _note.Length > 0 ? _note : _kind switch
        {
            MoveKind.Advance => $"Click where to go - up to {_r.MaxAdvanceDistance:0.#}\". Right-click = back.",
            MoveKind.Rush => $"Click where to go - up to {_r.MaxRushDistance:0.#}\". Right-click = back.",
            MoveKind.Charge => $"Click the enemy unit to charge - up to {_r.MaxDistanceInches:0.#}\". Right-click = back.",
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

    public override string? ConfirmText => _pending is { } p ? $"{KindName(p)} {p.Distance:0.0}\" to here?" : null;

    public override void Confirm()
    {
        if (_pending is not { } p) return;
        _s?.Table.ClearGhosts("move");
        Complete(new Selected<List<ModelMoveEntry>>(p.Moves));
    }

    public override void ClearPending()
    {
        _pending = null;
        _lastHover = new(-9999, -9999);
        _s?.Table.ClearGhosts("move");
        Version++;
    }

    public override void Shown(BattleScreen s) { _s = s; s.SelectUnit(Unit); }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("move");

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

    private MovePlanner.Plan? PlanFor(BattleScreen s, Vector2 at)
    {
        IUnit? enemy = s.Table.UnitAt(at) is { } target && !s.Session.IsMine(target.PlayerID) && !target.ID.Equals(Unit.ID) ? target : null;
        // A charge goes into contact with the enemy pointed at, and nowhere else.
        if (_kind == MoveKind.Charge) return enemy == null ? null : MovePlanner.IntoContact(_r, s.Session.Table, enemy);
        // Advance and Rush may not end in contact (that is a charge); with no action picked, an enemy is a charge.
        if (enemy != null && _kind == null) return MovePlanner.IntoContact(_r, s.Session.Table, enemy);
        return s.Table.TableAt(at) is { } p ? MovePlanner.Towards(_r, s.Session.Table, p.X, p.Y, Cap) : null;
    }

    private void Draw(BattleScreen s, MovePlanner.Plan plan, bool placed)
    {
        Color c = !plan.Valid ? Palette.WoundsRed : plan.Distance <= _r.MaxAdvanceDistance + 0.01f ? HudKit.Lime
            : plan.Distance <= _r.MaxRushDistance + 0.01f ? HudKit.Gold : HudKit.Enemy;
        if (placed) c = c.Lightened(0.25f);
        var ghosts = plan.Ends.Select(e => new BattleTable.Ghost(e.X, e.Z, Mathf.Max(0.4f, e.Radius), c with { A = placed ? 1.6f : 1f })).ToList();
        var lines = new List<BattleTable.GhostLine>();
        var models = Unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (models.Count > 0 && plan.Ends.Count > 0)
        {
            var from = new Vector2(models.Average(m => m.Position.x), models.Average(m => m.Position.z));
            var to = new Vector2(plan.Ends.Average(e => e.X), plan.Ends.Average(e => e.Z));
            lines.Add(new BattleTable.GhostLine(from, to, c with { A = 0.85f }));
        }
        s.Table.SetGhosts("move", ghosts, lines);
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null) return;   // the placed move stays put until confirmed or changed
        if (at.DistanceTo(_lastHover) < 3) return;
        _lastHover = at;
        _plan = PlanFor(s, at);
        if (_plan == null || _plan.Valid && _plan.Distance < 0.05f) { s.Table.ClearGhosts("move"); _note = ""; Version++; return; }
        Draw(s, _plan, placed: false);
        _note = _plan.Valid ? $"{KindName(_plan)}: {_plan.Distance:0.0}\" - click to place the move." : $"Can't go there: {_plan.Problem}.";
        Version++;
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        MovePlanner.Plan? plan = PlanFor(s, at);
        if (plan == null)
        {
            if (_kind == MoveKind.Charge) s.Toast("Click the enemy unit you want to charge.");
            return true;
        }
        if (!plan.Valid || plan.Distance < 0.05f)
        {
            s.Toast(plan.Valid ? "That is where the unit already is." : $"Can't move there: {plan.Problem}.");
            return true;
        }
        _pending = plan;
        Draw(s, plan, placed: true);
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
            return At(t.ID).Count() > 1 ? $"Shoot {t.Name} with:" : $"Shoot {t.Name}?";
        }
    }

    public override IReadOnlyList<ConfirmCheck>? ConfirmChecks => _target is { } t
        ? At(t.ID).Select(x => new ConfirmCheck(x.W.Weapon.Name, Forecast(x.W, x.T), !_unticked.Contains(Key(x.W.Weapon)),
            on => { if (on) _unticked.Remove(Key(x.W.Weapon)); else _unticked.Add(Key(x.W.Weapon)); Version++; })).ToList()
        : null;

    public override void Confirm()
    {
        if (_target is not { } t) return;
        var ticked = At(t.ID).Where(x => !_unticked.Contains(Key(x.W.Weapon))).ToList();
        if (ticked.Count == 0) { _s?.Toast("Tick at least one weapon - or right-click to choose another target."); return; }
        BattleResolvers.ShootAt = (Shooter.ID, t.ID, ticked.Select(x => Key(x.W.Weapon)).ToHashSet());
        Fire(ticked[0].W, ticked[0].T);
    }

    public override void ClearPending() { _target = null; _unticked.Clear(); _s?.Table.ClearGhosts("target"); Version++; }

    /// <summary>The weapons, each with where it can shoot and a Hold fire button.</summary>
    public override IReadOnlyList<ChoiceRow> Rows => _r.WeaponOptions.Select(w =>
    {
        var targets = w.WeaponTargetStats.Where(CanFire).Select(t => t.TargetUnit.GetValue().Name).ToList();
        string limited = w.LimitedRule != null ? (w.LimitedAlreadyFired ? $" - {w.LimitedRule}: spent" : $" - {w.LimitedRule}: once per game") : "";
        string range = w.Weapon.RangeInches > 0 ? $" {w.Weapon.RangeInches:0}\"" : "";
        return new ChoiceRow($"{w.Weapon.Name}{range}{limited}", targets.Count > 0 ? "Can hit: " + string.Join(", ", targets) : "Nothing in range or sight.",
            true, null, null, "Hold fire", () => Complete(new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(w.Weapon))));
    }).ToList();

    public override void Shown(BattleScreen s)
    {
        _s = s;
        s.SelectUnit(Shooter);
        // The rest of a confirmed volley: every other ticked weapon fires at the same target.
        if (BattleResolvers.ShootAt is { } again && again.Shooter.Equals(Shooter.ID))
        {
            if (At(again.Target).FirstOrDefault(x => again.Weapons.Contains(Key(x.W.Weapon))) is { W: not null } next) { Fire(next.W, next.T); return; }
            BattleResolvers.ShootAt = null;
        }
    }

    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("target");

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
        Version++;
        return true;
    }
}

// ====================================================================== generic lists (docked in the actions area)

public sealed class StringListDecision : Decision<string>
{
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
    private readonly YesNoRequest _r;
    public YesNoDecision(YesNoRequest r) => _r = r;
    public override string Prompt => _r.QuestionText;
    public override string? RowsTitle => "Choose";
    public override void AutoPlay(BattleScreen s) => Complete(_r.DefaultAnswer);
    public override IReadOnlyList<ChoiceRow> Rows => new[]
    {
        new ChoiceRow("Yes", null, true, null, () => Complete(true)),
        new ChoiceRow("No", null, true, null, () => Complete(false)),
    };
}

/// <summary>
/// Pick a unit (which to deploy, a spell target, a transport...). Kai, 2026-11-10: no pop-up list - the
/// left-hand list marks the choices (and why the others can't be picked), and the table is clickable.
/// </summary>
public sealed class UnitPickDecision : Decision<DataBinding<UnitData>>
{
    private readonly SelectionRequest<UnitData> _r;
    private BattleScreen? _s;
    public UnitPickDecision(SelectionRequest<UnitData> r) => _r = r;
    public override string Prompt => _r.Instructions;
    public override string Hint => "Click it in the list on the left, or on the table.";
    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);
    public override string? CancelLabel => _r.AllowCancel ? _r.CancelLabel : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }

    public override UnitMark? ListMark(IUnit unit)
    {
        if (_r.ValidOptions.Any(o => o.Option.GetValue().ID.Equals(unit.ID))) return new UnitMark(true, null);
        var bad = _r.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID));
        return bad != null ? new UnitMark(false, bad.Reason) : null;
    }

    public override bool WantsEnemyList => _s != null && _r.ValidOptions.Count > 0
        && _r.ValidOptions.All(o => !_s.Session.IsMine(o.Option.GetValue().PlayerID));

    public override void Shown(BattleScreen s) => _s = s;

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit) return false;
        var o = _r.ValidOptions.FirstOrDefault(v => v.Option.GetValue().ID.Equals(unit.ID));
        if (o == null) return false;
        Complete(o.Option);
        return true;
    }

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (_r.ValidOptions.FirstOrDefault(v => v.Option.GetValue().ID.Equals(unit.ID)) is { } o) Complete(o.Option);
        else if (_r.InvalidOptions.FirstOrDefault(v => v.Option.GetValue().ID.Equals(unit.ID)) is { } bad) s.Toast($"{unit.Name}: {bad.Reason}");
    }
}

public sealed class ModelPickDecision : Decision<DataBinding<ModelData>>
{
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
        if (_s == null) return;
        var rings = new List<BattleTable.Ghost>();
        var texts = new List<BattleTable.GhostText>();
        for (int i = 0; i < _r.ValidOptions.Count; i++)
        {
            ModelData m = _r.ValidOptions[i].Option.GetValue();
            bool hot = i == _hovered;
            rings.Add(new BattleTable.Ghost(m.Position.x, m.Position.z, m.BaseRadiusInches + (hot ? 0.5f : 0.25f), hot ? HudKit.Lime : HudKit.Gold, Ring: true));
            texts.Add(new BattleTable.GhostText(m.Position.x, m.Position.z, (i + 1).ToString(), hot ? HudKit.Lime : HudKit.Gold));
        }
        _s.Table.SetGhosts("pick", rings, null, texts);
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
        var rings = _r.EnemyKeepOutDiscs.Select(d => new BattleTable.Ghost(d.Center.x, d.Center.z, d.RadiusInches, Palette.WoundsRed, Ring: true));
        s.Table.SetGhosts("zone", rings, lines);
        Version++;
    }

    public override void Closed(BattleScreen s) { s.Table.ClearGhosts("zone"); s.Table.ClearGhosts("move"); }

    public override void AutoPlay(BattleScreen s) =>
        Complete(new FDG.Ai.Resolvers.AiPlaceObjectsResolver<ModelData>(s.Session.Table).Resolve(_r).GetAwaiter().GetResult());

    private List<PlacedObjectEntry<ModelData>>? PlanAt(BattleScreen s, Vector2 at) =>
        AimedPlacement.IsReposition(_r) || _placer == null || s.Table.TableAt(at) is not { } p ? null : _placer.At(_r, p.X, p.Y);

    private void Draw(BattleScreen s, List<PlacedObjectEntry<ModelData>> plan, bool placed)
    {
        Color c = placed ? HudKit.Lime.Lightened(0.25f) : HudKit.Lime;
        s.Table.SetGhosts("move", plan.Select(e => new BattleTable.Ghost(e.Position.x, e.Position.z,
            Mathf.Max(0.4f, e.Binding.GetValue().BaseRadiusInches), c with { A = placed ? 1.6f : 1f })));
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null || at.DistanceTo(_lastHover) < 6) return;
        _lastHover = at;
        if (PlanAt(s, at) is { } plan) Draw(s, plan, placed: false);
        else s.Table.ClearGhosts("move");
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (PlanAt(s, at) is not { } plan) return AimedPlacement.IsReposition(_r);
        _pending = plan;
        Draw(s, plan, placed: true);
        Version++;
        return true;
    }
}

// ====================================================================== smaller choices

/// <summary>Who to fight (Charge into several units, or a fight with several in reach): click the enemy, or pick it from the list.</summary>
public sealed class MeleeTargetDecision : Decision<CancellableResult<DataBinding<UnitData>>>
{
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

    public override IReadOnlyList<ChoiceRow> Rows =>
        _r.ValidOptions.Select(o => new ChoiceRow(o.Name, null, true, null, () => Complete(new Selected<DataBinding<UnitData>>(o.Option))))
            .Concat(_r.InvalidOptions.Select(i => new ChoiceRow(i.Name, null, false, i.Reason, null))).ToList();
    public override string? RowsTitle => "Fight";
}

/// <summary>"Pick one effect" rules at deployment / activation (Versatile..., army rules like Vinci Tech): a list with the book text.</summary>
public sealed class AbilityEffectDecision : Decision<int>
{
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
    private readonly ConcedeCheckRequest _r;
    private BattleScreen? _s;
    public ConcedeDecision(ConcedeCheckRequest r) => _r = r;
    public override string Prompt => $"Round {_r.Round} - keep fighting?";
    public override string? RowsTitle => "Concede";
    public override void Shown(BattleScreen s) => _s = s;
    public override void AutoPlay(BattleScreen s) => Complete(false);
    public override IReadOnlyList<ChoiceRow> Rows => new[]
    {
        new ChoiceRow("Keep fighting", null, true, null, () => Complete(false)),
        new ChoiceRow("Concede the game", "Ends the game as a loss.", true, null,
            () => { if (_s != null) _s.Confirm("Concede?", "End the game now as a loss?", "Concede", () => Complete(true)); else Complete(true); }),
    };
}
