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

/// <summary>One row of a choice list. <paramref name="Secondary"/> is a companion button on the same row (#321).</summary>
public sealed record ChoiceRow(string Label, string? Detail, bool Enabled, string? Reason, Action Pick,
    string? SecondaryLabel = null, Action? Secondary = null);

/// <summary>
/// An open decision as the battle screen shows it: the prompt line, optionally the action grid, optionally a
/// list of choices, and what table clicks and Esc do. Built on the engine thread, used on the main thread.
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
    /// <summary>Bumped when the rows or grid change while the decision stays open.</summary>
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

// ====================================================================== which unit to activate

/// <summary>Your turn: pick a unit. The army list on the left and the table are the list - no panel.</summary>
public sealed class ActivateDecision : Decision<DataBinding<UnitData>>
{
    private readonly ChooseUnitToActivateRequest _r;
    public ActivateDecision(ChooseUnitToActivateRequest r) => _r = r;

    public override string Prompt => "Your turn";
    public override string Hint => "Pick a unit to activate - click it in your army list or on the table (Tab cycles).";

    private DataBinding<UnitData>? Option(IUnit unit) =>
        _r.ValidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID))?.Option;

    public override void Shown(BattleScreen s)
    {
        if (BattleResolvers.PendingActivation is { } next)
        {
            BattleResolvers.PendingActivation = null;
            if (Option(next) is { } b) { Complete(b); return; }
        }
        // Select the first ready unit so its card and the grid are showing.
        if (_r.ValidOptions.FirstOrDefault() is { } first && s.SelectedUnit == null) s.SelectUnit(first.Option.GetValue(), focus: true);
    }

    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);

    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (Option(unit) is { } b) Complete(b);
        else if (_r.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID)) is { } bad)
            s.Toast($"{unit.Name}: {bad.Reason}");
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } unit || !s.Session.IsMine(unit.PlayerID)) return false;
        s.SelectUnit(unit, focus: false);
        UnitPicked(s, unit);
        return true;
    }
}

// ====================================================================== the action menu

/// <summary>Choose Action: the 2x2 grid holds Move / Shoot / Charge / Pass; anything else (Cast, special rules' own actions) is a list.</summary>
public sealed class ActionDecision : Decision<string>
{
    private readonly ChooseActionRequest _r;
    private BattleScreen? _s;
    public ActionDecision(ChooseActionRequest r) => _r = r;

    private static readonly (string Option, string Icon, string Key)[] Grid =
    {
        (FDG.Stages.ChooseActionStage.MOVEMENT_CHOICE_NAME, "walk", "1"),
        (FDG.Stages.ChooseActionStage.SHOOT_CHOICE_NAME, "crosshair", "2"),
        (FDG.Stages.ChooseActionStage.CHARGE_CHOICE_NAME, "sword-clash", "3"),
        (FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME, "stop-sign", "4"),
    };

    private IUnit? Unit => _s?.Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(_r.ActivatingUnitID));

    public override string Prompt => $"Choose an action";
    public override string Hint => $"{Unit?.Name ?? "Your unit"} - keys 1-4" + (_r.AllowCancel ? ", Esc to pick a different unit" : "");

    private string Describe(string option) =>
        _r.OptionDescriptions != null && _r.OptionDescriptions.TryGetValue(option, out string? d) ? d : "";

    private string? Why(string option) => _r.InvalidOptions.FirstOrDefault(i => i.Option == option)?.Reason;

    public override IReadOnlyList<ActionSlot> Actions => Grid.Select(g =>
    {
        bool ok = _r.ValidOptions.Contains(g.Option);
        string tip = ok ? Describe(g.Option) : Why(g.Option) ?? "Not available.";
        return new ActionSlot(g.Option, g.Icon, g.Option == FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME ? "End" : g.Option,
            g.Key, ok, tip, () => Choose(g.Option));
    }).ToList();

    public override IReadOnlyList<ChoiceRow>? Rows
    {
        get
        {
            var extra = _r.ValidOptions.Where(o => Grid.All(g => g.Option != o))
                .Select(o => new ChoiceRow(o, Describe(o), true, null, () => Choose(o)))
                .Concat(_r.InvalidOptions.Where(i => Grid.All(g => g.Option != i.Option))
                    .Select(i => new ChoiceRow(i.Option, null, false, i.Reason, () => { })))
                .ToList();
            return extra.Count > 0 ? extra : null;
        }
    }

    public override string? RowsTitle => "Other actions";

    private void Choose(string option)
    {
        if (option == FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME && _s != null)
        {
            // #311: ending an activation can't be taken back, so it asks first.
            _s.Confirm("End this activation?", Describe(option).Length > 0 ? Describe(option) : "The unit does nothing more this round.",
                "End activation", () => Complete(option));
            return;
        }
        Complete(option);
    }

    public override void AutoPlay(BattleScreen s)
    {
        foreach (string o in new[] { FDG.Stages.ChooseActionStage.SHOOT_CHOICE_NAME, FDG.Stages.ChooseActionStage.CHARGE_CHOICE_NAME,
                     FDG.Stages.ChooseActionStage.MOVEMENT_CHOICE_NAME, FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME })
            if (_r.ValidOptions.Contains(o)) { Complete(o); return; }
        Complete(_r.ValidOptions[0]);
    }

    public override void Shown(BattleScreen s)
    {
        _s = s;
        if (Unit is { } u) s.SelectUnit(u, focus: true);
    }

    public override string? CancelLabel => null;   // Esc / clicking another unit is the way back (the grid has no room)

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
/// Move a unit: point at the table and the unit's ghost follows (green = Advance distance, amber = Rush,
/// red = can't go there); click to move. Point at an enemy to move into base contact with it.
/// </summary>
public sealed class MoveDecision : Decision<CancellableResult<List<ModelMoveEntry>>>
{
    private readonly DefineMovementPathRequest _r;
    private MovePlanner.Plan? _plan;
    private Vector2 _lastHover = new(-9999, -9999);
    private string _note = "";
    public MoveDecision(DefineMovementPathRequest r) => _r = r;

    private IUnit Unit => _r.UnitDataBinding.GetValue();

    public override string Prompt => $"Move {Unit.Name}";
    public override string Hint => _note.Length > 0 ? _note
        : $"Click where to go - click an enemy to move into contact. Advance {_r.MaxAdvanceDistance:0.#}\", Rush {_r.MaxRushDistance:0.#}\""
          + (_r.MaxDistanceInches > _r.MaxRushDistance + 0.01f ? $", Charge {_r.MaxDistanceInches:0.#}\"" : "") + ".";

    public override string? CancelLabel => _r.AllowCancel ? "Back" : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(new Cancelled<List<ModelMoveEntry>>()); }

    public override IReadOnlyList<ActionSlot> Actions => new[]
    {
        new ActionSlot("StayPut", "shield", "Stay put", "1", true, "Don't move this unit.", () => Complete(new Selected<List<ModelMoveEntry>>(MovePlanner.StayInPlace(_r)))),
        new ActionSlot("Back", "stop-sign", "Back", "Esc", _r.AllowCancel, _r.AllowCancel ? "Choose a different action." : "This move can't be called off.", Cancel),
    };

    public override void Shown(BattleScreen s) => s.SelectUnit(Unit, focus: false);
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts();

    public override void AutoPlay(BattleScreen s)
    {
        IUnit me = Unit;
        var mine = me.Models.Where(m => m.GetIsAlive()).ToList();
        IUnit? nearest = mine.Count == 0 ? null : s.Session.Table.Units.Objects
            .Where(u => !s.Session.IsMine(u.PlayerID) && u.GetIsOnBattlefield() && u.Models.Any(m => m.GetIsAlive()))
            .OrderBy(u => u.Models.Where(m => m.GetIsAlive()).Min(m => Mathf.Pow(m.Position.x - mine[0].Position.x, 2) + Mathf.Pow(m.Position.z - mine[0].Position.z, 2)))
            .FirstOrDefault();
        MovePlanner.Plan? plan = nearest == null ? null
            : _r.MaxDistanceInches > _r.MaxRushDistance + 0.01f ? MovePlanner.IntoContact(_r, s.Session.Table, nearest)
            : MovePlanner.Towards(_r, s.Session.Table, nearest.Models.Where(m => m.GetIsAlive()).Average(m => m.Position.x), nearest.Models.Where(m => m.GetIsAlive()).Average(m => m.Position.z));
        Complete(new Selected<List<ModelMoveEntry>>(plan is { Valid: true } ? plan.Moves : MovePlanner.StayInPlace(_r)));
    }

    private MovePlanner.Plan? PlanFor(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is { } target && !s.Session.IsMine(target.PlayerID) && !target.ID.Equals(Unit.ID))
            return MovePlanner.IntoContact(_r, s.Session.Table, target);
        return s.Table.TableAt(at) is { } p ? MovePlanner.Towards(_r, s.Session.Table, p.X, p.Y) : null;
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (at.DistanceTo(_lastHover) < 3) return;
        _lastHover = at;
        _plan = PlanFor(s, at);
        if (_plan == null || _plan.Valid && _plan.Distance < 0.05f) { s.Table.ClearGhosts(); _note = ""; Version++; return; }
        Color c = !_plan.Valid ? Palette.WoundsRed : _plan.Distance <= _r.MaxAdvanceDistance + 0.01f ? HudKit.Lime
            : _plan.Distance <= _r.MaxRushDistance + 0.01f ? HudKit.Gold : HudKit.Enemy;
        var ghosts = _plan.Ends.Select(e => new BattleTable.Ghost(e.X, e.Z, Mathf.Max(0.4f, e.Radius), c)).ToList();
        var lines = new List<(Vector2, Vector2, Color)>();
        var models = Unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (models.Count > 0 && _plan.Ends.Count > 0)
        {
            var from = new Vector2(models.Average(m => m.Position.x), models.Average(m => m.Position.z));
            var to = new Vector2(_plan.Ends.Average(e => e.X), _plan.Ends.Average(e => e.Z));
            lines.Add((from, to, c));
        }
        s.Table.SetGhosts(ghosts, lines);
        string kind = _plan.Distance <= _r.MaxAdvanceDistance + 0.01f ? "Advance" : _plan.Distance <= _r.MaxRushDistance + 0.01f ? "Rush" : "Charge";
        _note = _plan.Valid ? $"{kind}: {_plan.Distance:0.0}\" - click to move." : $"Can't go there: {_plan.Problem}.";
        Version++;
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        MovePlanner.Plan? plan = PlanFor(s, at);
        if (plan == null) return true;
        if (!plan.Valid || plan.Distance < 0.05f)
        {
            s.Toast(plan.Valid ? "That is where the unit already is." : $"Can't move there: {plan.Problem}.");
            return true;
        }
        s.Table.ClearGhosts();
        Complete(new Selected<List<ModelMoveEntry>>(plan.Moves));
        return true;
    }
}

// ====================================================================== shooting

/// <summary>Shoot: each weapon's targets as rows with the engine's forecast; clicking an enemy fires the first weapon that can reach it.</summary>
public sealed class ShootDecision : Decision<CancellableResult<RangedAttackChoice>>
{
    private readonly ChooseRangedAttackRequest _r;
    public ShootDecision(ChooseRangedAttackRequest r) => _r = r;

    public override string Prompt => _r.DeclareFirst ? "Declare shots" : "Shoot";
    public override string Hint => $"{_r.AttackingUnit.GetValue().Name}: pick a weapon and target, or click an enemy.";
    public override string? RowsTitle => "Weapons";

    public override string? CancelLabel => _r.AllowCancel ? "Back" : _r.AllowStopShooting ? "Done shooting" : null;
    public override void Cancel()
    {
        if (_r.AllowCancel || _r.AllowStopShooting) Complete(new Cancelled<RangedAttackChoice>());
    }

    private static bool CanFire(WeaponTargetStats t) => t.modelsThatCanShoot.Count > 0 && t.UnselectableReason == null;

    private void Fire(WeaponOption w, WeaponTargetStats t) =>
        Complete(new Selected<RangedAttackChoice>(new RangedAttackChoice(w.Weapon, t.TargetUnit)));

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            foreach (WeaponOption w in _r.WeaponOptions)
            {
                string limited = w.LimitedRule != null ? (w.LimitedAlreadyFired ? $" ({w.LimitedRule}: spent)" : $" ({w.LimitedRule}: once per game)") : "";
                foreach (WeaponTargetStats t in w.WeaponTargetStats)
                {
                    string target = t.TargetUnit.GetValue().Name;
                    string detail = t.Forecast is { } f
                        ? $"{(f.AttacksFiring > 0 ? f.AttacksFiring : w.Weapon.Attacks)} attacks, hit on {f.HitRollNeeded}+, they save on {f.SaveRollNeeded}+"
                          + (t.HasCover && !w.IgnoresCover ? " (cover)" : "")
                        : "";
                    string? why = t.UnselectableReason ?? (t.modelsThatCanShoot.Count == 0 ? "Out of range or line of sight." : null);
                    rows.Add(new ChoiceRow($"{w.Weapon.Name}{limited} -> {target}", detail, CanFire(t), why, () => Fire(w, t),
                        "Hold fire", () => Complete(new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(w.Weapon)))));
                }
            }
            return rows;
        }
    }

    public override void Shown(BattleScreen s) => s.SelectUnit(_r.AttackingUnit.GetValue(), focus: false);

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
        foreach (WeaponOption w in _r.WeaponOptions)
            foreach (WeaponTargetStats t in w.WeaponTargetStats)
                if (t.TargetUnit.GetValue().ID.Equals(unit.ID) && CanFire(t)) { Fire(w, t); return true; }
        s.Toast($"No weapon can shoot {unit.Name} right now.");
        return true;
    }
}

// ====================================================================== generic lists

public sealed class StringListDecision : Decision<string>
{
    private readonly StringSelectionRequest _r;
    public StringListDecision(StringSelectionRequest r) => _r = r;

    public override string Prompt => _r.Instructions;
    public override string? CancelLabel => _r.AllowCancel ? "Back" : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }
    public override void AutoPlay(BattleScreen s) => Rows.First(r => r.Enabled).Pick();

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
                rows.Add(new ChoiceRow(i.Option, Desc(i.Option), false, i.Reason, () => { }));
            return rows;
        }
    }
}

public sealed class YesNoDecision : Decision<bool>
{
    private readonly YesNoRequest _r;
    public YesNoDecision(YesNoRequest r) => _r = r;
    public override string Prompt => _r.QuestionText;
    public override void AutoPlay(BattleScreen s) => Complete(_r.DefaultAnswer);
    public override IReadOnlyList<ChoiceRow> Rows => new[]
    {
        new ChoiceRow("Yes", null, true, null, () => Complete(true)),
        new ChoiceRow("No", null, true, null, () => Complete(false)),
    };
}

/// <summary>Pick a unit (a spell target, a transport, a melee defender...): from the list or by clicking it on the table.</summary>
public sealed class UnitPickDecision : Decision<DataBinding<UnitData>>
{
    private readonly SelectionRequest<UnitData> _r;
    public UnitPickDecision(SelectionRequest<UnitData> r) => _r = r;
    public override string Prompt => _r.Instructions;
    public override string Hint => "Pick from the list or click the unit on the table.";
    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);
    public override string? CancelLabel => _r.AllowCancel ? _r.CancelLabel : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }

    public override IReadOnlyList<ChoiceRow> Rows =>
        _r.ValidOptions.Select(o => new ChoiceRow(o.Name, null, true, null, () => Complete(o.Option)))
            .Concat(_r.InvalidOptions.Select(i => new ChoiceRow(i.Name, null, false, i.Reason, () => { }))).ToList();

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
    }
}

public sealed class ModelPickDecision : Decision<DataBinding<ModelData>>
{
    private readonly SelectionRequest<ModelData> _r;
    public ModelPickDecision(SelectionRequest<ModelData> r) => _r = r;
    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0].Option);
    public override string Prompt => _r.Instructions;
    public override string? CancelLabel => _r.AllowCancel ? _r.CancelLabel : null;
    public override void Cancel() { if (_r.AllowCancel) Complete(null!); }

    public override IReadOnlyList<ChoiceRow> Rows =>
        _r.ValidOptions.Select(o => new ChoiceRow(o.Name, null, true, null, () => Complete(o.Option)))
            .Concat(_r.InvalidOptions.Select(i => new ChoiceRow(i.Name, null, false, i.Reason, () => { }))).ToList();
}
