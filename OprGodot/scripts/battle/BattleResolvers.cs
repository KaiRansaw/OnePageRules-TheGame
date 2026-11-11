using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Game;

namespace OprGodot.Battle;

/// <summary>
/// The decisions the Godot build asks the player for. Every request type not registered here is played by the
/// official bot for the player (autopilot) and the prompt line says so - port step 4 moves them over one by one.
///
/// <para>Each resolver runs on the engine thread: it wraps the request in a <see cref="Decision"/>, drops it in
/// the <see cref="DecisionInbox"/> and returns the decision's task. The battle screen (main thread) shows the
/// open decision and completes it.</para>
/// </summary>
public static class BattleResolvers
{
    public static DecisionInbox Inbox { get; private set; } = new();

    /// <summary>
    /// A unit the player clicked while the previous unit's action menu was still untouched: Back was sent for
    /// that menu, and the next "choose a unit to activate" picks this one straight away (same as the Raylib
    /// build, #248).
    /// </summary>
    public static IUnit? PendingActivation { get; set; }

    public static void Register(HybridResolverRegistry registry)
    {
        Inbox = new DecisionInbox();
        PendingActivation = null;
        var unitPick = new Ask<SelectionRequest<UnitData>, DataBinding<UnitData>>(r => new UnitPickDecision(r));
        registry.RegisterResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>(
            new Ask<ChooseUnitToActivateRequest, DataBinding<UnitData>>(r => new ActivateDecision(r)));
        registry.RegisterResolver(unitPick);
        registry.RegisterResolver(new Ask<SelectionRequest<ModelData>, DataBinding<ModelData>>(r => new ModelPickDecision(r)));
        registry.RegisterResolver(new Ask<ChooseActionRequest, string>(r => new ActionDecision(r)));
        registry.RegisterResolver(new Ask<StringSelectionRequest, string>(r => new StringListDecision(r)));
        registry.RegisterResolver(new Ask<YesNoRequest, bool>(r => new YesNoDecision(r)));
        registry.RegisterResolver(new Ask<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>(r => new MoveDecision(r)));
        registry.RegisterResolver(new Ask<ChooseRangedAttackRequest, CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>>(r => new ShootDecision(r)));
    }

    /// <summary>One request type -> one kind of decision, posted to the inbox.</summary>
    private sealed class Ask<TRequest, TReply> : IStageResolver<TRequest, TReply> where TRequest : IStageTaskRequest<TReply>
    {
        private readonly Func<TRequest, Decision<TReply>> _make;
        public Ask(Func<TRequest, Decision<TReply>> make) => _make = make;

        public Task<TReply> Resolve(TRequest request)
        {
            Decision<TReply> d = _make(request);
            Inbox.Offer(d);
            return d.Task;
        }
    }
}

/// <summary>Open decisions, oldest first. Written by the engine thread, read by the screen.</summary>
public sealed class DecisionInbox
{
    private readonly object _gate = new();
    private readonly List<Decision> _open = new();

    public void Offer(Decision d) { lock (_gate) _open.Add(d); }

    public Decision? Current
    {
        get
        {
            lock (_gate)
            {
                _open.RemoveAll(d => d.Done);
                return _open.Count > 0 ? _open[0] : null;
            }
        }
    }
}
