using FDG;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Game;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

// Build 1.54 (Kai, 2026-11-11: "finish the battle decisions"): the requests the bot used to answer for the player.
// Each one follows the same shape as the rest - choices docked in the actions area, the table shows what is being
// decided, a click places, Space confirms anything that can't be taken back.

// ====================================================================== wounds

/// <summary>
/// Your unit was hit and you choose which models take the wounds. The engine only asks when the choice matters
/// (more than one model could take them, and not every order kills the whole unit). The book's order is enforced by
/// the engine's own <see cref="AssignWoundsResults"/>: an already-wounded model first, a model mid-fill finished
/// before another is started, a joined hero last - a model that can't take the next wound is greyed with the reason.
/// Click a model on the table or in the list: it fills up. "Auto-assign the rest" finishes it the usual way.
/// </summary>
public sealed class WoundsDecision : Decision<AssignWoundsResults>
{
    private readonly AssignWoundsRequest _r;
    private AssignWoundsResults _res;
    private BattleScreen? _s;
    private int _hovered = -1;

    public WoundsDecision(AssignWoundsRequest r)
    {
        _r = r;
        _res = new AssignWoundsResults(r.UnitReceivingWounds, r.Packets);
    }

    private IUnit Unit => _r.UnitReceivingWounds.GetValue();

    public override string Prompt => $"{Unit.Name} takes wounds - choose who takes them";
    public override string Hint => WoundAssignmentText.Progress(_res).Replace("\n", "  -  ")
        + "  -  click a model (table or list) to give it the next wounds.";
    public override string? RowsTitle => "Assign wounds";

    public override void AutoPlay(BattleScreen s) { _res.AutoFill(); Finish(); }

    private void Finish()
    {
        _s?.Table.ClearGhosts("pick");
        Complete(_res);
    }

    private void Give(PendingWounds entry)
    {
        if (!_res.CanAssignWoundTo(entry)) { _s?.Toast(WhyNot(entry)); return; }
        _res.TryAddWounds(entry.Model);
        if (_res.IsFinishedAssigning) { Finish(); return; }
        Draw();
        Version++;
    }

    private string WhyNot(PendingWounds entry)
    {
        ModelData m = entry.Model.GetValue();
        if (m.TotalWounds - m.WoundsDealt - entry.Wounds <= AssignWoundsResults.WoundEpsilon) return "That model can't take any more.";
        if (Unit is UnitData u && u.HeroAttachment?.HeroModelId is { } hero && hero.Equals(m.ID)) return "The hero takes wounds last - the rest of the unit first.";
        if (u2(m)) return "The unit's leader takes wounds after the rest of the unit.";
        return "Finish the model that is already taking wounds first.";
        bool u2(ModelData md) => Unit is UnitData ud && ud.LeaderModelId is { } l && l.Equals(md.ID);
    }

    private static string Describe(IModel m, PendingWounds e)
    {
        string weapons = string.Join(", ", m.Weapons.Select(w => w.Name).Distinct());
        float left = m.TotalWounds - m.WoundsDealt - e.Wounds;
        string wounds = m.TotalWounds > 1 ? $"{WoundFormat.Fraction(Math.Max(0, left), m.TotalWounds)} wounds left" : (left <= 0.001f ? "dies" : "1 wound");
        string taking = e.Wounds > 0.001f ? $"  -  taking {WoundFormat.Format(e.Wounds)}" : "";
        return (weapons.Length > 0 ? weapons + "  -  " : "") + wounds + taking;
    }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            string explain = WoundAssignmentText.Explanation(_res);
            if (explain.Length > 0) rows.Add(new ChoiceRow("Deadly", explain, true, null, null));
            if (_res.HasConfinedPackets) rows.Add(new ChoiceRow(WoundAssignmentText.Next(_res), null, true, null, null));
            rows.Add(new ChoiceRow("Auto-assign the rest", "Fills the models in the book's order.", true, null, () => { _res.AutoFill(); Finish(); }));
            if (_res.Commits.Count > 0)
                rows.Add(new ChoiceRow("Start over", "Take back the wounds placed so far.", true, null, () =>
                {
                    _res = new AssignWoundsResults(_r.UnitReceivingWounds, _r.Packets);
                    Draw();
                    Version++;
                }));
            for (int i = 0; i < _res.PendingWounds.Count; i++)
            {
                PendingWounds e = _res.PendingWounds[i];
                ModelData m = e.Model.GetValue();
                bool can = _res.CanAssignWoundTo(e);
                int n = i;
                string name = $"Model {i + 1}" + (Unit is UnitData ud && ud.HeroAttachment?.HeroModelId is { } hm && hm.Equals(m.ID) ? " (hero)" : "");
                rows.Add(new ChoiceRow(name, Describe(m, e), can, can ? null : WhyNot(e) + "  " + Describe(m, e), can ? () => Give(e) : null,
                    Hover: on => { _hovered = on ? n : -1; Draw(); }));
            }
            return rows;
        }
    }

    public override void Shown(BattleScreen s) { _s = s; s.SelectUnit(Unit); Draw(); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("pick");

    /// <summary>A number on each model (matches the list); lime = can take the next wounds; red = wounds placed on it.</summary>
    private void Draw()
    {
        if (_s == null) return;
        var rings = new List<BattleTable.Ghost>();
        var texts = new List<BattleTable.GhostText>();
        var lines = BattleTable.Outline(Unit, 0.35f, Palette.WoundsRed, 0.16f).ToList();
        for (int i = 0; i < _res.PendingWounds.Count; i++)
        {
            PendingWounds e = _res.PendingWounds[i];
            ModelData m = e.Model.GetValue();
            bool can = _res.CanAssignWoundTo(e);
            bool hot = i == _hovered;
            if (can || hot) rings.Add(new BattleTable.Ghost(m.Position.x, m.Position.z, m.BaseRadiusInches + 0.25f, hot ? Colors.White : HudKit.Lime, Ring: true));
            string label = (i + 1).ToString() + (e.Wounds > 0.001f ? $" (-{WoundFormat.Format(e.Wounds)})" : "");
            texts.Add(new BattleTable.GhostText(m.Position.x, m.Position.z, label,
                e.Wounds > 0.001f ? Palette.WoundsRed : can ? HudKit.Lime : HudKit.Dim, 0.02f));
        }
        _s.Table.SetGhosts("pick", rings, lines, texts);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.ModelAt(at) is not { } model) return false;
        PendingWounds? e = _res.PendingWounds.FirstOrDefault(p => p.Model.GetValue().ID.Equals(model.ID));
        if (e == null) return false;
        Give(e);
        return true;
    }
}

// ====================================================================== consolidation (the move after a melee)

/// <summary>
/// After a melee. If one side was wiped out, the survivor may move up to 3"; if both survived, the charger must move
/// back by 1" (if possible) - GF/GFF/AoF/AoFS Core Rules 3.6.0, Melee. Point where to go and click; Space confirms.
/// Checked with the ConsolidateStage's own validation, so a placed move is always one the engine accepts.
/// </summary>
public sealed class ConsolidateDecision : Decision<List<ModelMoveEntry>>
{
    private readonly ConsolidationMoveRequest _r;
    private BattleScreen? _s;
    private MovePlanner.Plan? _pending;
    private Vector2 _lastHover = new(-9999, -9999);
    private string _note = "";

    public ConsolidateDecision(ConsolidationMoveRequest r) => _r = r;

    private IUnit Unit => _r.UnitDataBinding.GetValue();
    private bool Wipeout => _r.Reason == EConsolidationReason.Wipeout;

    public override string Prompt => Wipeout
        ? $"{Unit.Name} won the fight - it may move up to {_r.MaxDistanceInches:0.#}\""
        : $"{Unit.Name} must move back {_r.MaxDistanceInches:0.#}\" from the melee";
    public override string Hint => _pending != null ? "Space to confirm - click elsewhere to change it - right-click to clear."
        : _note.Length > 0 ? _note : "Point where to go and click.";

    /// <summary>The engine's own choice (the official bot's), for "Let the game do it" and the test driver.</summary>
    private List<ModelMoveEntry> BotMove() =>
        new AiConsolidationMoveResolver(_s!.Session.Table, _r.TargetPlayerID).Resolve(_r).GetAwaiter().GetResult();

    public override void AutoPlay(BattleScreen s) { _s = s; Complete(BotMove()); }

    public override IReadOnlyList<ActionSlot>? Actions
    {
        get
        {
            var slots = new List<ActionSlot>();
            if (Wipeout) slots.Add(new ActionSlot("StayPut", "shield", "Stay put", "1", true, "Don't move.", () => Complete(MovePlanner.StayInPlace(_r))));
            slots.Add(new ActionSlot("BotMove", "walk", Wipeout ? "Game picks" : "Step back", Wipeout ? "2" : "1", true,
                Wipeout ? "Let the game move it towards the nearest enemy." : "Move straight back from the enemy.", () => { if (_s != null) Complete(BotMove()); }));
            return slots;
        }
    }

    public override string? ConfirmText => _pending is { } p ? $"Move {p.Distance:0.0}\" to here?" : null;
    public override void Confirm()
    {
        if (_pending is not { } p) return;
        _s?.Table.ClearGhosts("move");
        Complete(p.Moves);
    }
    public override void ClearPending() { _pending = null; _lastHover = new(-9999, -9999); _s?.Table.ClearGhosts("move"); Version++; }

    public override void Shown(BattleScreen s) { _s = s; s.SelectUnit(Unit); }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("move");

    private MovePlanner.Plan? PlanFor(BattleScreen s, Vector2 at) =>
        s.Table.TableAt(at) is { } p ? MovePlanner.Consolidate(_r, s.Session.Table, p.X, p.Y) : null;

    private void Draw(BattleScreen s, MovePlanner.Plan plan, bool placed)
    {
        Color c = plan.Valid ? HudKit.Lime : Palette.WoundsRed;
        if (placed) c = c.Lightened(0.25f);
        s.Table.SetGhosts("move", plan.Ends.Select(e => new BattleTable.Ghost(e.X, e.Z, Mathf.Max(0.4f, e.Radius), c)).ToList());
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null || at.DistanceTo(_lastHover) < 2) return;
        _lastHover = at;
        if (PlanFor(s, at) is not { } plan || plan.Valid && plan.Distance < 0.05f) { s.Table.ClearGhosts("move"); _note = ""; Version++; return; }
        Draw(s, plan, false);
        _note = plan.Valid ? $"{plan.Distance:0.0}\" - click to place it." : $"Can't go there: {plan.Problem}.";
        Version++;
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (PlanFor(s, at) is not { } plan) return true;
        if (!plan.Valid || plan.Distance < 0.05f) { s.Toast(plan.Valid ? "That is where the unit already is." : $"Can't move there: {plan.Problem}."); return true; }
        _pending = plan;
        Draw(s, plan, true);
        Version++;
        return true;
    }
}

// ====================================================================== Fast-Play leaders

/// <summary>
/// Fast-Play: every unit is measured from its leader, and the leader is the last of its models to die. The game has
/// suggested one per unit; click a unit's row (or a model on the table) to change it, Space to confirm.
/// </summary>
public sealed class LeadersDecision : Decision<CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>
{
    private readonly PickUnitLeadersRequest _r;
    private readonly Dictionary<UnitID, ModelID> _leader = new();
    private PickUnitLeadersRequest.UnitEntry? _open;
    private BattleScreen? _s;

    public LeadersDecision(PickUnitLeadersRequest r)
    {
        _r = r;
        foreach (var e in r.Units) _leader[e.Unit.GetValue().ID] = e.Suggested;
    }

    public override string Prompt => "Fast-Play: choose each unit's leader";
    public override string Hint => _open != null ? $"Pick {_open.UnitName}'s leader - on the table or in the list." : "Space to confirm - click a unit to change its leader.";
    public override string? RowsTitle => _open?.UnitName ?? "Unit leaders";
    public override void AutoPlay(BattleScreen s) => Complete(new Selected<List<PickUnitLeadersRequest.LeaderPick>>(_r.Suggested()));

    private List<PickUnitLeadersRequest.LeaderPick> Picks() =>
        _r.Units.Select(e => new PickUnitLeadersRequest.LeaderPick(e.Unit, _leader[e.Unit.GetValue().ID])).ToList();

    public override string? ConfirmText => _open == null ? "Use these leaders?" : null;
    public override void Confirm() { _s?.Table.ClearGhosts("pick"); Complete(new Selected<List<PickUnitLeadersRequest.LeaderPick>>(Picks())); }
    public override void ClearPending() { foreach (var e in _r.Units) _leader[e.Unit.GetValue().ID] = e.Suggested; Draw(); Version++; }
    public override bool RightClick(BattleScreen s) { if (_open == null) return false; _open = null; Version++; return true; }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            if (_open is { } o)
                return o.Models.Select(m => new ChoiceRow(m.Description, m.Model.Equals(_leader[o.Unit.GetValue().ID]) ? "leader now" : null, true, null,
                    () => { _leader[o.Unit.GetValue().ID] = m.Model; _open = null; Draw(); Version++; }))
                    .Append(new ChoiceRow("Back  (right-click)", null, true, null, () => { _open = null; Version++; })).ToList();
            var rows = new List<ChoiceRow> { new("Fast-Play", _r.Instructions, true, null, null) };
            foreach (var e in _r.Units)
            {
                ModelID id = _leader[e.Unit.GetValue().ID];
                string who = e.Models.FirstOrDefault(m => m.Model.Equals(id))?.Description ?? "?";
                var entry = e;
                rows.Add(new ChoiceRow(e.UnitName, "Leader: " + who, true, null, e.Models.Count > 1 ? () => { _open = entry; Version++; } : null));
            }
            return rows;
        }
    }

    public override void Shown(BattleScreen s) { _s = s; Draw(); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("pick");

    private void Draw()
    {
        if (_s == null) return;
        var rings = new List<BattleTable.Ghost>();
        var texts = new List<BattleTable.GhostText>();
        foreach (var e in _r.Units)
        {
            IUnit u = e.Unit.GetValue();
            if (u.Models.FirstOrDefault(m => m.ID.Equals(_leader[u.ID])) is { } m)
            {
                rings.Add(new BattleTable.Ghost(m.Position.x, m.Position.z, m.BaseRadiusInches + 0.25f, HudKit.Gold, Ring: true));
                texts.Add(new BattleTable.GhostText(m.Position.x, m.Position.z, "L", HudKit.Gold, 0.02f));
            }
        }
        _s.Table.SetGhosts("pick", rings, null, texts);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.ModelAt(at) is not { } model) return false;
        var e = _r.Units.FirstOrDefault(u => u.Models.Any(m => m.Model.Equals(model.ID)));
        if (e == null) return false;
        _leader[e.Unit.GetValue().ID] = model.ID;
        _open = null;
        Draw();
        Version++;
        return true;
    }
}

// ====================================================================== Command Group style picks

/// <summary>
/// A rule that hands itself to up to N friendly units (Command Group, Hold the Line Aura...): click units in the
/// list or on the table to tick them, Space to confirm. Skip gives it to nobody.
/// </summary>
public sealed class CommandGroupDecision : Decision<CancellableResult<List<DataBinding<UnitData>>>>
{
    private readonly PickCommandGroupUnitsRequest _r;
    private readonly List<DataBinding<UnitData>> _picked = new();
    private BattleScreen? _s;

    public CommandGroupDecision(PickCommandGroupUnitsRequest r) => _r = r;

    public override string Prompt => _r.Instructions;
    public override string Hint => $"{_picked.Count} of up to {_r.MaxPicks} picked - click units in the list or on the table.";
    public override string? RowsTitle => _r.RuleName;
    public override string? CancelLabel => "Skip";
    public override void Cancel() => Complete(new Cancelled<List<DataBinding<UnitData>>>());
    public override void AutoPlay(BattleScreen s) => Complete(new Selected<List<DataBinding<UnitData>>>(_r.ValidOptions.Take(_r.MaxPicks).Select(o => o.Option).ToList()));

    public override string? ConfirmText => $"Give {_r.RuleName} to {(_picked.Count == 0 ? "no units" : string.Join(", ", _picked.Select(p => p.GetValue().Name)))}?";
    public override void Confirm() => Complete(new Selected<List<DataBinding<UnitData>>>(_picked.ToList()));
    public override void ClearPending() { _picked.Clear(); Draw(); Version++; }
    public override bool RightClick(BattleScreen s) { if (_picked.Count == 0) { Cancel(); return true; } ClearPending(); return true; }

    private bool IsPicked(UnitID id) => _picked.Any(p => p.GetValue().ID.Equals(id));

    private void Toggle(DataBinding<UnitData> b)
    {
        var id = b.GetValue().ID;
        if (IsPicked(id)) _picked.RemoveAll(p => p.GetValue().ID.Equals(id));
        else if (_picked.Count < _r.MaxPicks) _picked.Add(b);
        else { _s?.Toast($"Up to {_r.MaxPicks} - untick one first."); return; }
        Draw();
        Version++;
    }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            if (_s?.RuleText(_r.RuleName) is { } text) rows.Add(new ChoiceRow(_r.RuleName, text, true, null, null));
            rows.AddRange(_r.ValidOptions.Select(o => new ChoiceRow((IsPicked(o.Option.GetValue().ID) ? "[x] " : "[  ] ") + o.Name, null, true, null, () => Toggle(o.Option))));
            return rows;
        }
    }

    public override UnitMark? ListMark(IUnit unit) => _r.ValidOptions.Any(o => o.Option.GetValue().ID.Equals(unit.ID)) ? new UnitMark(true, null) : null;
    public override void UnitPicked(BattleScreen s, IUnit unit)
    {
        if (_r.ValidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(unit.ID)) is { } o) Toggle(o.Option);
    }
    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.UnitAt(at) is not { } u) return false;
        if (_r.ValidOptions.FirstOrDefault(o => o.Option.GetValue().ID.Equals(u.ID)) is not { } o) return false;
        Toggle(o.Option);
        return true;
    }

    public override void Shown(BattleScreen s) { _s = s; Draw(); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("targets");

    private void Draw()
    {
        _s?.Table.SetGhosts("targets", Array.Empty<BattleTable.Ghost>(), _r.ValidOptions.SelectMany(o =>
            BattleTable.Outline(o.Option.GetValue(), 0.45f, IsPicked(o.Option.GetValue().ID) ? HudKit.Lime : HudKit.Gold, IsPicked(o.Option.GetValue().ID) ? 0.24f : 0.14f)));
    }
}

// ====================================================================== pick several from a list

/// <summary>Tick some options from a list (mission card decks, ...), Space to confirm.</summary>
public sealed class PickStringsDecision : Decision<List<string>>
{
    private readonly PickStringsRequest _r;
    private readonly List<string> _picked;

    public PickStringsDecision(PickStringsRequest r) { _r = r; _picked = r.Suggested.ToList(); }

    public override string Prompt => _r.Instructions;
    public override string Hint => _r.MinPicks == _r.MaxPicks ? $"Pick {_r.MaxPicks} ({_picked.Count} picked)." : $"Pick {_r.MinPicks} to {_r.MaxPicks} ({_picked.Count} picked).";
    public override string? RowsTitle => _r.DisplayName;
    public override void AutoPlay(BattleScreen s) => Complete(_r.Sanitise(_picked));
    public override string? ConfirmText => _r.Legal(_picked) ? $"Use these {_picked.Count}?" : null;
    public override void Confirm() { if (_r.Legal(_picked)) Complete(_r.Sanitise(_picked)); }
    public override void ClearPending() { _picked.Clear(); Version++; }

    public override IReadOnlyList<ChoiceRow> Rows => _r.Options.Select(o => new ChoiceRow((_picked.Contains(o) ? "[x] " : "[  ] ") + o,
        _r.Descriptions != null && _r.Descriptions.TryGetValue(o, out var d) ? d : null, true, null, () =>
        {
            if (!_picked.Remove(o))
            {
                if (_picked.Count >= _r.MaxPicks) return;
                _picked.Add(o);
            }
            Version++;
        })).ToList();
}

// ====================================================================== deployment zone

/// <summary>Choose which deployment zone is yours: the zones are outlined on the table; click one (or its row), Space confirms.</summary>
public sealed class ZoneDecision : Decision<DataBinding<RectangularZone>>
{
    private readonly ChooseDeploymentZoneRequest _r;
    private BattleScreen? _s;
    private DataBinding<RectangularZone>? _pending;
    private int _hovered = -1;

    public ZoneDecision(ChooseDeploymentZoneRequest r) => _r = r;

    public override string Prompt => "Choose your deployment zone";
    public override string Hint => _pending != null ? "Space to confirm - right-click to choose again." : "Click a zone on the table or in the list.";
    public override string? RowsTitle => "Deployment zones";
    public override void AutoPlay(BattleScreen s) => Complete(_r.AvailableZones[0]);

    /// <summary>Where a zone sits as you look at the table ("near edge", "far-left corner").</summary>
    private string Where(RectangularZone z)
    {
        float w = _s?.Table.W ?? 72, h = _s?.Table.D ?? 48;
        float cx = (z.Left + z.Right) / 2, cz = (z.Bottom + z.Top) / 2;
        bool fullW = z.Right - z.Left >= w * 0.9f, fullH = z.Top - z.Bottom >= h * 0.9f;
        string vert = cz > h * 0.6f ? "far" : cz < h * 0.4f ? "near" : "";
        string horiz = cx < w * 0.4f ? "left" : cx > w * 0.6f ? "right" : "";
        if (fullW && vert.Length > 0) return $"{vert} edge";
        if (fullH && horiz.Length > 0) return $"{horiz} edge";
        if (vert.Length > 0 && horiz.Length > 0) return $"{vert}-{horiz} corner";
        return vert.Length > 0 ? $"{vert} side" : horiz.Length > 0 ? $"{horiz} side" : "centre";
    }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            for (int i = 0; i < _r.AvailableZones.Count; i++)
            {
                var z = _r.AvailableZones[i];
                int n = i;
                rows.Add(new ChoiceRow($"The {Where(z.GetValue())}", null, true, null, () => Pick(z), Hover: on => { _hovered = on ? n : -1; Draw(); }));
            }
            rows.AddRange(_r.UnavailableZones.Select(z => new ChoiceRow($"The {Where(z.GetValue())}", null, false, "Taken by another player.", null)));
            return rows;
        }
    }

    private void Pick(DataBinding<RectangularZone> z) { _pending = z; Draw(); Version++; }
    public override string? ConfirmText => _pending is { } p ? $"Deploy in the {Where(p.GetValue())}?" : null;
    public override void Confirm() { if (_pending is { } p) { _s?.Table.ClearGhosts("zone"); Complete(p); } }
    public override void ClearPending() { _pending = null; Draw(); Version++; }

    public override void Shown(BattleScreen s) { _s = s; Draw(); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("zone");

    private static IEnumerable<BattleTable.GhostLine> Box(RectangularZone z, Color c, float w)
    {
        var a = new Vector2(z.Left, z.Bottom); var b = new Vector2(z.Right, z.Bottom);
        var d = new Vector2(z.Right, z.Top); var e = new Vector2(z.Left, z.Top);
        yield return new(a, b, c, w); yield return new(b, d, c, w); yield return new(d, e, c, w); yield return new(e, a, c, w);
    }

    private void Draw()
    {
        if (_s == null) return;
        var lines = new List<BattleTable.GhostLine>();
        var texts = new List<BattleTable.GhostText>();
        for (int i = 0; i < _r.AvailableZones.Count; i++)
        {
            RectangularZone z = _r.AvailableZones[i].GetValue();
            bool chosen = _pending != null && ReferenceEquals(_pending.GetValue(), z);
            lines.AddRange(Box(z, chosen ? HudKit.Lime : i == _hovered ? Colors.White : HudKit.Gold, chosen ? 0.3f : 0.18f));
            texts.Add(new BattleTable.GhostText((z.Left + z.Right) / 2, (z.Bottom + z.Top) / 2, chosen ? "YOURS" : (i + 1).ToString(), chosen ? HudKit.Lime : HudKit.Gold, 0.04f));
        }
        foreach (var u in _r.UnavailableZones) lines.AddRange(Box(u.GetValue(), Palette.WoundsRed with { A = 0.6f }, 0.12f));
        _s.Table.SetGhosts("zone", Array.Empty<BattleTable.Ghost>(), lines, texts);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.TableAt(at) is not { } p) return false;
        var z = _r.AvailableZones.FirstOrDefault(z => z.GetValue().IsPointWithinZone(new Float2(p.X, p.Y)));
        if (z == null) { s.Toast("Click inside one of the outlined zones."); return true; }
        Pick(z);
        return true;
    }
}

// ====================================================================== aircraft

/// <summary>
/// An Aircraft's move: straight ahead along its heading, between the shortest and furthest the rule allows. Point
/// along the line and click, Space confirms. A spot past the table edge flies it off (it comes back next round, its
/// activation ends) - the confirm box says so.
/// </summary>
public sealed class AircraftDecision : Decision<AircraftAdvanceResult>
{
    private readonly AircraftAdvanceRequest _r;
    private BattleScreen? _s;
    private float? _pending;
    private float _shown;

    public AircraftDecision(AircraftAdvanceRequest r) { _r = r; _shown = r.MinDistanceInches; }

    private IUnit Unit => _r.UnitDataBinding.GetValue();
    private bool Leaves(float d) => ForcedAircraftMove.WouldLeaveTable(ForcedAircraftMove.BuildPaths(_r.UnitDataBinding, _r.HeadingNormal, d));

    public override string Prompt => $"{Unit.Name} flies {_r.MinDistanceInches:0}\"-{_r.MaxDistanceInches:0}\" straight ahead";
    public override string Hint => _pending != null ? "Space to confirm - click elsewhere on the line to change it." : "Point along the line and click where it ends.";
    public override void AutoPlay(BattleScreen s) => Complete(new AircraftAdvanceResult(_r.MinDistanceInches, Leaves(_r.MinDistanceInches)));

    public override IReadOnlyList<ActionSlot> Actions => new[]
    {
        new ActionSlot("Shortest", "walk", $"{_r.MinDistanceInches:0}\"", "1", true, "The shortest move allowed.", () => Place(_r.MinDistanceInches)),
        new ActionSlot("Furthest", "run", $"{_r.MaxDistanceInches:0}\"", "2", true, "The furthest move allowed.", () => Place(_r.MaxDistanceInches)),
    };

    public override string? ConfirmText => _pending is { } d
        ? Leaves(d) ? $"Fly {d:0.0}\" - off the table? Its activation ends; it comes back on next round." : $"Fly {d:0.0}\" to here?"
        : null;
    public override void Confirm() { if (_pending is { } d) { _s?.Table.ClearGhosts("move"); Complete(new AircraftAdvanceResult(d, Leaves(d))); } }
    public override void ClearPending() { _pending = null; Draw(_shown, false); Version++; }

    public override void Shown(BattleScreen s) { _s = s; s.SelectUnit(Unit); Draw(_shown, false); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("move");

    private void Place(float d) { _pending = d; _shown = d; Draw(d, true); Version++; }

    private float DistanceAt(Vector2 p)
    {
        var living = Unit.Models.Where(m => m.GetIsAlive()).ToList();
        float cx = living.Average(m => m.Position.x), cz = living.Average(m => m.Position.z);
        float t = (p.X - cx) * _r.HeadingNormal.X + (p.Y - cz) * _r.HeadingNormal.Y;
        return Math.Clamp(t, _r.MinDistanceInches, _r.MaxDistanceInches);
    }

    private void Draw(float d, bool placed)
    {
        if (_s == null) return;
        var living = Unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (living.Count == 0) return;
        float cx = living.Average(m => m.Position.x), cz = living.Average(m => m.Position.z);
        Float2 h = _r.HeadingNormal;
        var lines = new List<BattleTable.GhostLine>
        {
            new(new Vector2(cx, cz), new Vector2(cx + h.X * _r.MinDistanceInches, cz + h.Y * _r.MinDistanceInches), HudKit.Dim, 0.08f),
            new(new Vector2(cx + h.X * _r.MinDistanceInches, cz + h.Y * _r.MinDistanceInches), new Vector2(cx + h.X * _r.MaxDistanceInches, cz + h.Y * _r.MaxDistanceInches), HudKit.Gold, 0.2f),
        };
        Color c = Leaves(d) ? Palette.WoundsRed : HudKit.Lime;
        if (placed) c = c.Lightened(0.25f);
        _s.Table.SetGhosts("move", living.Select(m => new BattleTable.Ghost(m.Position.x + h.X * d, m.Position.z + h.Y * d, Mathf.Max(0.4f, m.BaseRadiusInches), c)), lines);
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null || s.Table.TableAt(at) is not { } p) return;
        float d = DistanceAt(p);
        if (Mathf.Abs(d - _shown) < 0.1f) return;
        _shown = d;
        Draw(d, false);
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.TableAt(at) is not { } p) return false;
        Place(DistanceAt(p));
        return true;
    }
}

// ====================================================================== objective markers

/// <summary>
/// Place an objective marker (Player-Placed objectives): the area it may go in is outlined, every marker already down
/// shows how close the next may come. Click, Space confirms. Checked with the stage's own validator.
/// </summary>
public sealed class ObjectiveDecision : Decision<Position>
{
    private readonly PlaceObjectiveRequest _r;
    private BattleScreen? _s;
    private Position? _pending;
    private string _note = "";

    public ObjectiveDecision(PlaceObjectiveRequest r) => _r = r;

    public override string Prompt => $"Place objective {_r.MarkerIndex} of {_r.TotalMarkers}";
    public override string Hint => _pending != null ? "Space to confirm - right-click to clear." : _note.Length > 0 ? _note
        : $"Inside the outlined area, at least {_r.MinSeparationInches:0}\" from other objectives.";

    private string? Problem(BattleScreen s, Position p)
    {
        if (!_r.IsInLegalArea(p)) return "outside the area objectives may go in";
        var impassable = s.Session.Table.Terrain.Objects.Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible));
        return ObjectivePlacementValidator.Check(p, _r.LegalBand, _r.MinSeparationInches, s.Session.Table.Objectives.Objects, impassable) switch
        {
            ObjectivePlacementValidity.Valid => null,
            ObjectivePlacementValidity.TooCloseToObjective => $"closer than {_r.MinSeparationInches:0}\" to another objective",
            ObjectivePlacementValidity.ImpassableTerrain => "on impassable terrain",
            ObjectivePlacementValidity.Unreachable => "no unit could reach it",
            _ => "outside the area objectives may go in",
        };
    }

    public override void AutoPlay(BattleScreen s)
    {
        ZoneBounds b = _r.LegalBand.Bounds;
        for (float z = b.Bottom + 1; z < b.Top; z += 2)
            for (float x = b.Left + 1; x < b.Right; x += 2)
                if (Problem(s, new Position(x, z)) == null) { Complete(new Position(x, z)); return; }
        Complete(new Position((b.Left + b.Right) / 2, (b.Bottom + b.Top) / 2));
    }

    public override string? ConfirmText => _pending != null ? $"Place objective {_r.MarkerIndex} here?" : null;
    public override void Confirm() { if (_pending is { } p) { _s?.Table.ClearGhosts("zone"); Complete(p); } }
    public override void ClearPending() { _pending = null; Draw(null, false); Version++; }

    public override void Shown(BattleScreen s) { _s = s; Draw(null, false); Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("zone");

    private void Draw(Position? at, bool ok)
    {
        if (_s == null) return;
        ZoneBounds b = _r.LegalBand.Bounds;
        var lines = new List<BattleTable.GhostLine>
        {
            new(new Vector2(b.Left, b.Bottom), new Vector2(b.Right, b.Bottom), HudKit.Gold, 0.16f),
            new(new Vector2(b.Right, b.Bottom), new Vector2(b.Right, b.Top), HudKit.Gold, 0.16f),
            new(new Vector2(b.Right, b.Top), new Vector2(b.Left, b.Top), HudKit.Gold, 0.16f),
            new(new Vector2(b.Left, b.Top), new Vector2(b.Left, b.Bottom), HudKit.Gold, 0.16f),
        };
        var ghosts = _s.Session.Table.Objectives.Objects
            .Select(o => new BattleTable.Ghost(o.Position.x, o.Position.z, _r.MinSeparationInches, HudKit.Dim, Ring: true)).ToList();
        if ((_pending ?? at) is { } p)
            ghosts.Add(new BattleTable.Ghost(p.x, p.z, 1.5f, _pending != null ? HudKit.Lime.Lightened(0.25f) : ok ? HudKit.Lime : Palette.WoundsRed));
        _s.Table.SetGhosts("zone", ghosts, lines);
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_pending != null || s.Table.TableAt(at) is not { } v) return;
        var p = new Position(v.X, v.Y);
        string? problem = Problem(s, p);
        Draw(p, problem == null);
        string note = problem == null ? "Click to place it here." : $"Can't go here: {problem}.";
        if (note != _note) { _note = note; Version++; }
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (s.Table.TableAt(at) is not { } v) return false;
        var p = new Position(v.X, v.Y);
        if (Problem(s, p) is { } problem) { s.Toast($"Can't place it here: {problem}."); return true; }
        _pending = p;
        Draw(p, true);
        Version++;
        return true;
    }
}

// ====================================================================== terrain pieces

/// <summary>
/// Players take turns placing terrain: pick a piece from the list, point where it goes (its outline follows the
/// pointer, red where it may not go), Turn rotates it, click places, Space confirms. Checked with the stage's own
/// validator; in points mode a piece you can't afford is greyed with the reason.
/// </summary>
public sealed class TerrainDecision : Decision<TerrainPlacementResult>
{
    private readonly PlaceOneTerrainRequest _r;
    private BattleScreen? _s;
    private int _piece = -1;
    private float _turn;
    private Float2? _at, _pending;

    public TerrainDecision(PlaceOneTerrainRequest r) => _r = r;

    public override string Prompt => _r.TotalPieces > 0 ? $"Place terrain ({_r.PiecesPlaced + 1} of {_r.TotalPieces})" : "Place a terrain piece";
    public override string Hint => _piece < 0 ? "Pick a piece from the list." : _pending != null ? "Space to confirm - right-click to clear."
        : "Point where it goes and click. Turn rotates it.";
    public override string? RowsTitle => "Terrain";

    private string? Blocked(int i) => _r.PointsBudget is { } b && b.Evaluate(TerrainPointsBudget.CostOf(_r.Pool[i])) is { Playable: false } v ? v.BlockedReason : null;

    private IZone ShapeAt(int i, Float2 c) => TerrainTemplateUtilities.TranslateToCenter(TerrainTemplateUtilities.Rotate(_r.Pool[i].Shape, _turn), c);

    private bool Fits(BattleScreen s, int i, Float2 c) =>
        TerrainPlacementValidator.Check(ShapeAt(i, c), _r.TableWidthInches, _r.TableHeightInches, s.Session.Table.Terrain.Objects) == TerrainPlacementValidity.Valid;

    public override void AutoPlay(BattleScreen s)
    {
        for (int i = 0; i < _r.Pool.Count; i++)
        {
            if (Blocked(i) != null) continue;
            for (float z = 4; z < _r.TableHeightInches - 2; z += 2)
                for (float x = 4; x < _r.TableWidthInches - 2; x += 2)
                    if (Fits(s, i, new Float2(x, z))) { Complete(new TerrainPlacementResult(i, new Float2(x, z), 0)); return; }
        }
        Complete(new TerrainPlacementResult(0, new Float2(_r.TableWidthInches / 2, _r.TableHeightInches / 2), 0));
    }

    public override IReadOnlyList<ChoiceRow> Rows
    {
        get
        {
            var rows = new List<ChoiceRow>();
            if (_r.PointsBudget is { } b) rows.Add(new ChoiceRow(b.PointsSummaryLine, b.TurnSummaryLine + (b.DebtNoticeLine is { } d ? "  " + d : ""), true, null, null));
            if (_piece >= 0) rows.Add(new ChoiceRow($"Turn  ({_turn:0} deg)", "Rotate the piece 45 degrees.", true, null, () => { _turn = (_turn + 45) % 360; _pending = null; Redraw(); Version++; }));
            for (int i = 0; i < _r.Pool.Count; i++)
            {
                TerrainPieceEntry p = _r.Pool[i];
                string? blocked = Blocked(i);
                int n = i;
                string cost = _r.PointsBudget != null ? $"  -  {TerrainPointsBudget.Pts(TerrainPointsBudget.CostOf(p))}" : "";
                rows.Add(new ChoiceRow((i == _piece ? "> " : "") + p.Name, TypeText(p.TerrainType) + cost, blocked == null, blocked,
                    blocked == null ? () => { _piece = n; _pending = null; Redraw(); Version++; } : null));
            }
            return rows;
        }
    }

    private static string TypeText(ETerrainType t) => string.Join(", ", Enum.GetValues<ETerrainType>()
        .Where(f => Convert.ToInt64(f) != 0 && t.HasFlag(f)).Select(f => f.ToString()));

    public override string? ConfirmText => _pending != null ? $"Place {_r.Pool[_piece].Name} here?" : null;
    public override void Confirm() { if (_pending is { } c) { _s?.Table.ClearGhosts("zone"); Complete(new TerrainPlacementResult(_piece, c, _turn)); } }
    public override void ClearPending() { _pending = null; Redraw(); Version++; }
    public override bool RightClick(BattleScreen s)
    {
        if (_pending != null) { ClearPending(); return true; }
        if (_piece >= 0) { _piece = -1; _at = null; Redraw(); Version++; return true; }
        return false;
    }

    public override void Shown(BattleScreen s) { _s = s; Version++; }
    public override void Closed(BattleScreen s) => s.Table.ClearGhosts("zone");

    private void Redraw()
    {
        if (_s == null) return;
        if (_piece < 0 || (_pending ?? _at) is not { } c) { _s.Table.ClearGhosts("zone"); return; }
        Color col = _pending != null ? HudKit.Lime.Lightened(0.25f) : Fits(_s, _piece, c) ? HudKit.Lime : Palette.WoundsRed;
        var lines = new List<BattleTable.GhostLine>();
        foreach (var poly in ZoneOutlines.Polygons(ShapeAt(_piece, c)))
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                lines.Add(new BattleTable.GhostLine(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), col, 0.2f));
            }
        _s.Table.SetGhosts("zone", Array.Empty<BattleTable.Ghost>(), lines);
    }

    public override void TableHover(BattleScreen s, Vector2 at)
    {
        if (_piece < 0 || _pending != null || s.Table.TableAt(at) is not { } v) return;
        var c = new Float2(MathF.Round(v.X * 2) / 2, MathF.Round(v.Y * 2) / 2);
        if (_at is { } old && old.X == c.X && old.Y == c.Y) return;
        _at = c;
        Redraw();
    }

    public override bool TableClick(BattleScreen s, Vector2 at)
    {
        if (_piece < 0) { s.Toast("Pick a piece from the list first."); return true; }
        if (s.Table.TableAt(at) is not { } v) return false;
        var c = new Float2(MathF.Round(v.X * 2) / 2, MathF.Round(v.Y * 2) / 2);
        if (!Fits(s, _piece, c)) { s.Toast("It doesn't fit there - it must be on the table and clear of other terrain."); return true; }
        _pending = c;
        Redraw();
        Version++;
        return true;
    }
}
