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

    /// <summary>
    /// Which of the book's actions the player pressed before the engine's generic "Move" (Advance, Rush or a
    /// charge) - the move tool caps the distance by it. Taken by the next move request.
    /// </summary>
    public static MoveKind? NextMove { get; set; }

    /// <summary>The book action pressed on the activation grid, applied when the engine's action menu arrives.</summary>
    public static (UnitID Unit, BookAction Action)? QueuedAction { get; set; }

    /// <summary>A confirmed volley: the other ticked weapons fire at the same unit when the engine asks for them.</summary>
    public static (UnitID Shooter, UnitID Target, HashSet<string> Weapons)? ShootAt { get; set; }

    public static void Register(HybridResolverRegistry registry)
    {
        Inbox = new DecisionInbox();
        PendingActivation = null;
        NextMove = null;
        QueuedAction = null;
        ShootAt = null;
        var unitPick = new Ask<SelectionRequest<UnitData>, DataBinding<UnitData>>(r => new UnitPickDecision(r));
        registry.RegisterResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>(
            new Ask<ChooseUnitToActivateRequest, DataBinding<UnitData>>(r => new ActivateDecision(r)));
        registry.RegisterResolver(unitPick);
        registry.RegisterResolver(new Ask<SelectionRequest<ModelData>, DataBinding<ModelData>>(r => new ModelPickDecision(r)));
        registry.RegisterResolver(new Ask<ChooseActionRequest, string>(r => new ActionDecision(r)));
        registry.RegisterResolver(new Ask<StringSelectionRequest, string>(r =>
            CardMoments.Of(r.DisplayName) is { } m ? new MissionCardPickOne(r, m) : new StringListDecision(r)));
        registry.RegisterResolver(new Ask<YesNoRequest, bool>(r => new YesNoDecision(r)));
        registry.RegisterResolver(new Ask<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>(r => new MoveDecision(r)));
        registry.RegisterResolver<ChooseMeleeDefenderRequest, CancellableResult<DataBinding<UnitData>>>(
            new Ask<ChooseMeleeDefenderRequest, CancellableResult<DataBinding<UnitData>>>(r => new MeleeTargetDecision(r)));
        registry.RegisterResolver(new Ask<ChooseAbilityEffectRequest, int>(r => new AbilityEffectDecision(r)));
        registry.RegisterResolver<ConcedeCheckRequest, bool>(new Ask<ConcedeCheckRequest, bool>(r => new ConcedeDecision(r)));
        registry.RegisterResolver(new Ask<ChooseSpellRequest, ChooseSpellReply>(r => new SpellDecision(r)));
        registry.RegisterResolver(new Ask<CastAssistRequest, CastAssistReply>(r => new CastAssistDecision(r)));
        registry.RegisterResolver(new Ask<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(r => new PlaceDecision(r)));
        // Build 1.54: the rest of the battle's decisions (they were the bot's until now).
        registry.RegisterResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
            new Ask<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(r => new CancellableUnitPickDecision(r)));
        registry.RegisterResolver(new Ask<AssignWoundsRequest, AssignWoundsResults>(r => new WoundsDecision(r)));
        registry.RegisterResolver(new Ask<ConsolidationMoveRequest, List<ModelMoveEntry>>(r => new ConsolidateDecision(r)));
        registry.RegisterResolver(new Ask<PickUnitLeadersRequest, CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>(r => new LeadersDecision(r)));
        registry.RegisterResolver(new Ask<PickCommandGroupUnitsRequest, CancellableResult<List<DataBinding<UnitData>>>>(r => new CommandGroupDecision(r)));
        registry.RegisterResolver(new Ask<PickStringsRequest, List<string>>(r =>
            CardMoments.Of(r.DisplayName) is { } m ? new MissionCardPickMany(r, m) : new PickStringsDecision(r)));
        registry.RegisterResolver(new Ask<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>(r => new ZoneDecision(r)));
        registry.RegisterResolver(new Ask<AircraftAdvanceRequest, AircraftAdvanceResult>(r => new AircraftDecision(r)));
        registry.RegisterResolver(new Ask<PlaceObjectiveRequest, Position>(r => new ObjectiveDecision(r)));
        registry.RegisterResolver(new Ask<PlaceOneTerrainRequest, TerrainPlacementResult>(r => new TerrainDecision(r)));
        registry.RegisterResolver(new Ask<ChooseRangedAttackRequest, CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>>(r => new ShootDecision(r)));
        registry.RegisterResolver(new Ask<HordeShopRequest, HordeShopReply>(r => new HordeShopDecision(r)));
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

public enum MoveKind { Advance, Rush, Charge }

/// <summary>Open decisions, oldest first. Written by the engine thread, read by the screen.</summary>
public sealed class DecisionInbox
{
    private readonly object _gate = new();
    private readonly List<Decision> _open = new();

    public void Offer(Decision d) { lock (_gate) _open.Add(d); }
    /// <summary>Test driver only: show this before what is open (an example screen for the UI review).</summary>
    public void OfferFirst(Decision d) { lock (_gate) _open.Insert(0, d); }

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
