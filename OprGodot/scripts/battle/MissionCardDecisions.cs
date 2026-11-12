using FDG;
using FDG.Missions;
using FDG.StageResolution.Requests;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// Every moment the game asks about mission cards (Mission Cards v3.6.0), shown as real cards laid out over the
/// dimmed table - Kai's pick on 2026-11-11 ("cards on the table", style C, card design C6; the "Mission Card Screens"
/// page). The engine asks with plain pick lists named by what they are for ("Building a mission deck", "Planned
/// Gambit"...); this reads which cards the options are, lays them out where they are (your hand, your table, the
/// opponent's table, your discard pile) and turns a click on a card back into that option.
/// </summary>
public enum CardMoment { Deck, HandRedraw, Place, Replace, Hide, Ruse, Score, DiscardOne, TableRedraw, Gambit, Challenge, Paragon, Recovery }

internal static class CardMoments
{
    /// <summary>The engine's display names (MissionCardHooks / MissionCardCustomDecks) -> the moment.</summary>
    public static CardMoment? Of(string displayName) => displayName switch
    {
        "Building a mission deck" => CardMoment.Deck,
        "Discarding mission cards to redraw" => CardMoment.HandRedraw,
        "Placing mission cards" => CardMoment.Place,
        "Replacing a mission card" => CardMoment.Replace,
        "Hiding a mission card" => CardMoment.Hide,
        "Strategic Ruse" => CardMoment.Ruse,
        "Choosing which mission card to score" => CardMoment.Score,
        "Choosing a mission card to discard" => CardMoment.DiscardOne,
        "Choosing mission cards to redraw" => CardMoment.TableRedraw,
        "Planned Gambit" => CardMoment.Gambit,
        "Direct Challenge" => CardMoment.Challenge,
        "Tactical Paragons" => CardMoment.Paragon,
        "Coordinated Recovery" => CardMoment.Recovery,
        _ => null,
    };

    public static string Title(CardMoment m, bool many) => m switch
    {
        CardMoment.Deck => "Build your deck",
        CardMoment.HandRedraw => "Round 1: redraw",
        CardMoment.Place => "Place your cards",
        CardMoment.Replace => "Place a replacement",
        CardMoment.Hide => "Place one face down?",
        CardMoment.Ruse => many ? "Strategic Ruse: turn cards face down" : "Strategic Ruse: keep one face down",
        CardMoment.Score => "Only one card scores",
        CardMoment.DiscardOne => "Discard a card?",
        CardMoment.TableRedraw => "Round 1: redraw",
        CardMoment.Gambit => "Planned Gambit",
        CardMoment.Challenge => "Direct Challenge",
        CardMoment.Paragon => "Tactical Paragons",
        _ => "Coordinated Recovery",
    };

    public static (string Text, Color Colour)? Stamp(CardMoment m, bool many) => m switch
    {
        CardMoment.Deck => null,
        CardMoment.HandRedraw or CardMoment.DiscardOne or CardMoment.TableRedraw => ("DISCARD", MissionCardView.StampRed),
        CardMoment.Place or CardMoment.Replace => ("PLACE", HudKit.Lime),
        CardMoment.Hide => ("FACE DOWN", MissionCardView.StampDim),
        CardMoment.Ruse => (many ? "FACE DOWN" : "KEEP HIDDEN", MissionCardView.StampDim),
        CardMoment.Score => ("SCORES", MissionCardView.StampGold),
        CardMoment.Gambit => ("SEND BACK", MissionCardView.StampRed),
        CardMoment.Challenge => ("CHALLENGE", MissionCardView.StampGold),
        CardMoment.Paragon => ("PARAGON +1", HudKit.Lime),
        _ => ("BACK TO DECK", HudKit.Lime),
    };

    public static string Verb(CardMoment m, int count) => m switch
    {
        CardMoment.Deck => "Use this deck",
        CardMoment.HandRedraw => "Discard and draw",
        CardMoment.Place => count == 1 ? "Place it" : "Place them",
        CardMoment.Replace => "Place it",
        CardMoment.Hide => "Hide it",
        CardMoment.Ruse => "Done",
        CardMoment.Score => "Score it",
        CardMoment.DiscardOne or CardMoment.TableRedraw => "Discard it",
        CardMoment.Gambit => "Send it back",
        CardMoment.Challenge => "Challenge it",
        CardMoment.Paragon => "Make it the paragon",
        _ => "Shuffle back",
    };

    /// <summary>What a "pick none" answer is called, for moments where none is allowed.</summary>
    public static string NoneLabel(CardMoment m) => m switch
    {
        CardMoment.HandRedraw => "Keep my hand",
        CardMoment.Recovery => "Leave them",
        CardMoment.Ruse => "Keep them face up",
        _ => "None",
    };

    /// <summary>Which pile the cards being picked are in.</summary>
    public static string PileName(CardMoment m) => m switch
    {
        CardMoment.Deck => "All 36 cards",
        CardMoment.HandRedraw or CardMoment.Place or CardMoment.Replace => "Your hand",
        CardMoment.Gambit or CardMoment.Challenge => "Their table",
        CardMoment.Recovery => "Your discard pile",
        CardMoment.DiscardOne or CardMoment.TableRedraw => "The cards on the table",
        _ => "Your table",
    };
}

/// <summary>
/// The card layer over the table: a bar (what this is, how many picked, the confirm button, any "no thanks"), then the
/// piles as rows of cards. "Look at the table" hides it until clicked again (scoring and placing depend on the board).
/// </summary>
public sealed partial class MissionCardOverlay : Control
{
    public sealed record CardItem(int? Number, string? HiddenLabel, bool Lit, bool Pickable, Action? Click,
        string? Stamp = null, Color? StampColour = null, string? Caption = null, string? Tooltip = null, bool Slot = false, bool Dim = false);
    public sealed record Pile(string Label, IReadOnlyList<CardItem> Cards, float Width);
    public sealed record Spec(string Title, string? Count, string Verb, bool CanConfirm, Action Confirm,
        IReadOnlyList<(string Label, Action Go)> Others, IReadOnlyList<Pile> Piles, bool DeckGrid);

    private readonly EGameSystem _system;
    private Control _body = null!;
    private Button _peek = null!;
    private bool _hidden;
    private float _scroll;

    public MissionCardOverlay(EGameSystem system)
    {
        _system = system;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(Spec spec)
    {
        foreach (Node c in GetChildren()) c.QueueFree();
        _body = new Control { MouseFilter = MouseFilterEnum.Stop };
        _body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _body.OffsetTop = 62;   // the prompt line stays readable above it
        AddChild(_body);
        _body.AddChild(new ColorRect { Color = new Color(0.016f, 0.03f, 0.035f, 0.82f), MouseFilter = MouseFilterEnum.Ignore }
            .With(r => r.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect)));

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 24; col.OffsetRight = -24; col.OffsetTop = 10; col.OffsetBottom = -14;
        col.AddThemeConstantOverride("separation", 10);
        _body.AddChild(col);

        // The bar.
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 16);
        bar.AddChild(HudKit.Text(spec.Title.ToUpperInvariant(), 24, HudKit.Fg, HudKit.Exo(800)));
        if (spec.Count != null) bar.AddChild(HudKit.Text(spec.Count, 22, HudKit.Lime, HudKit.Bold));
        var ok = Ui.Button($"{spec.Verb}  (Space)", spec.Confirm, "Confirm your pick.", "CommitButton");
        ok.Name = "CardConfirm";
        ok.Disabled = !spec.CanConfirm;
        bar.AddChild(ok);
        foreach (var (label, go) in spec.Others)
            bar.AddChild(Ui.Button(label, go, "", "BackButton").With(b => b.Name = "CardOther"));
        bar.AddChild(Ui.Button("Look at the table", () => SetHidden(true), "Hide the cards to look at the battlefield.", "BackButton"));
        col.AddChild(bar);

        if (spec.DeckGrid && spec.Piles.Count > 0)
        {
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var grid = new GridContainer { Columns = 7, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            foreach (CardItem c in spec.Piles[0].Cards) grid.AddChild(Card(c, spec.Piles[0].Width));
            var centre = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            centre.AddChild(grid);
            scroll.AddChild(centre);
            col.AddChild(scroll);
            // Keep the scroll position when a click redraws the grid.
            scroll.Ready += () => scroll.CallDeferred(ScrollContainer.MethodName.SetVScroll, (int)_scroll);
            scroll.GetVScrollBar().ValueChanged += v => _scroll = (float)v;
        }
        else
        {
            col.AddChild(HudKit.Grow());
            foreach (Pile p in spec.Piles)
            {
                var zone = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                zone.AddThemeConstantOverride("separation", 8);
                zone.AddChild(HudKit.Text(p.Label.ToUpperInvariant(), 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 3), HorizontalAlignment.Center));
                var cards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
                cards.AddThemeConstantOverride("separation", 14);
                foreach (CardItem c in p.Cards) cards.AddChild(Card(c, p.Width));
                zone.AddChild(cards);
                col.AddChild(zone);
                col.AddChild(HudKit.Grow());
            }
        }

        _peek = Ui.Button("Show the cards", () => SetHidden(false), "Back to the mission cards.", "CommitButton");
        _peek.SetAnchorsPreset(LayoutPreset.CenterTop);
        _peek.Position = new Vector2(0, 74);
        _peek.GrowHorizontal = GrowDirection.Both;
        AddChild(_peek);
        SetHidden(_hidden);
    }

    private void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (IsInstanceValid(_body)) _body.Visible = !hidden;
        if (IsInstanceValid(_peek)) _peek.Visible = hidden;
    }

    private Control Card(CardItem c, float w)
    {
        float h = Mathf.Round(w * 1.32f);
        if (c.Slot) return MissionCardView.EmptySlot(w, h);
        var look = new MissionCardView.Look(w, h, c.Lit, c.Dim, c.Stamp, c.StampColour, c.Tooltip);
        Control card = c.Number is { } n
            ? MissionCardView.Card(MissionCardCatalog.Card(_system, n), look, c.Pickable ? c.Click : null)
            : MissionCardView.FaceDown(c.HiddenLabel ?? "Hidden card", look, c.Pickable ? c.Click : null);
        if (c.Caption == null) return card;
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 6);
        v.AddChild(card);
        var cap = HudKit.Text(c.Caption, 14, HudKit.Gold, HudKit.Semi, HorizontalAlignment.Center);
        cap.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cap.CustomMinimumSize = new Vector2(w, 0);
        v.AddChild(cap);
        return v;
    }
}

/// <summary>
/// The shared part of a mission-card decision: which card each option is, what else is on the table for context,
/// and the overlay's life (shown with the decision, gone when it is answered).
/// </summary>
public abstract class MissionCardDecisionBase<T> : Decision<T>
{
    protected readonly CardMoment Moment;
    protected readonly string DisplayName, Instructions;
    protected readonly IReadOnlyList<string> Options;
    protected readonly IReadOnlyDictionary<string, string>? Descriptions;
    private readonly PlayerID _player;
    protected BattleScreen? S;
    private MissionCardOverlay? _overlay;
    private readonly Dictionary<string, (int? Number, string? Hidden)> _cards = new();

    protected MissionCardDecisionBase(CardMoment moment, string displayName, string instructions, PlayerID player,
        IReadOnlyList<string> options, IReadOnlyDictionary<string, string>? descriptions)
    {
        Moment = moment;
        DisplayName = displayName;
        Instructions = instructions;
        _player = player;
        Options = options;
        Descriptions = descriptions;
    }

    public override string Prompt => Instructions;
    public override string SnapName => "MissionCard" + Moment;
    public override bool HideConfirmBox => true;

    protected EGameSystem System => S?.Session.Table.Progress?.GameSystem ?? EGameSystem.GrimdarkFuture;

    /// <summary>Which card an option names: "12 Seize 2", "Discard 34 Hold 4", "Send back Bot's Hidden card 1".</summary>
    protected (int? Number, string? Hidden) CardOf(string option)
    {
        if (_cards.TryGetValue(option, out var known)) return known;
        (int?, string?) found = (null, null);
        foreach (MissionCardDef c in MissionCardCatalog.All(System))
            if (option.Contains($"{c.Number} {c.Title}", StringComparison.Ordinal)) { found = (c.Number, null); break; }
        if (found.Item1 == null && global::System.Text.RegularExpressions.Regex.Match(option, @"Hidden card \d+") is { Success: true } hit)
            found = (null, hit.Value);
        return _cards[option] = found;
    }

    protected bool IsCard(string option) => CardOf(option) is { Number: not null } or { Hidden: not null };

    protected string? Describe(string option) => Descriptions != null && Descriptions.TryGetValue(option, out string? d) ? d : null;

    /// <summary>This player's side of the mission-card state (hand, table, hidden cards), when the table has it.</summary>
    protected MissionCardSide? MySide()
    {
        if (S == null || GameProgressUtilities.MissionCards(S.Session.Table.DataStore) is not { } state) return null;
        int slot = S.Session.Table.Players.Objects.FirstOrDefault(p => p.IsFilled && p.PlayerID.Equals(_player))?.SlotID ?? -1;
        return state.Sides.TryGetValue(slot, out MissionCardSide? side) ? side : null;
    }

    /// <summary>Your table as it stands, for context (your own face-down card shown face up with a tag).</summary>
    protected MissionCardOverlay.Pile? MyTable(float w, IEnumerable<int>? leaveOut = null, IEnumerable<MissionCardOverlay.CardItem>? extra = null, int slots = 0)
    {
        MissionCardSide? side = MySide();
        var skip = leaveOut?.ToHashSet() ?? new HashSet<int>();
        var cards = (side?.Table ?? new List<int>()).Where(c => !skip.Contains(c))
            .Select(c => new MissionCardOverlay.CardItem(c, null, false, false, null,
                side!.Hidden.Contains(c) ? "FACE DOWN" : null, MissionCardView.StampDim)).ToList();
        if (extra != null) cards.AddRange(extra);
        for (int i = 0; i < slots; i++) cards.Add(new MissionCardOverlay.CardItem(null, null, false, false, null, Slot: true));
        return cards.Count == 0 ? null : new MissionCardOverlay.Pile("Your table", cards, w);
    }

    protected abstract MissionCardOverlay.Spec Build();

    protected void Redraw()
    {
        Version++;
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay)) _overlay.Show(Build());
    }

    public override void Shown(BattleScreen s)
    {
        S = s;
        _overlay = new MissionCardOverlay(System) { Name = "MissionCards" };
        s.Hud.AddChild(_overlay);
        _overlay.Show(Build());
    }

    public override void Closed(BattleScreen s)
    {
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay)) _overlay.QueueFree();
        _overlay = null;
    }

    protected MissionCardOverlay.CardItem Item(string option, bool lit, Action click, float stampIfLit = 1, string? caption = null)
    {
        var (number, hidden) = CardOf(option);
        var stamp = CardMoments.Stamp(Moment, this is MissionCardPickMany);
        string? tip = Describe(option) is { } d && (number == null || Moment is CardMoment.Score or CardMoment.Paragon or CardMoment.Hide) ? d : null;
        return new MissionCardOverlay.CardItem(number, hidden, lit, true, click,
            lit && stampIfLit > 0 ? stamp?.Text : null, stamp?.Colour, caption, tip);
    }
}

/// <summary>Pick several cards (build the deck, redraw, place from hand, turn face down, shuffle discards back).</summary>
public sealed class MissionCardPickMany : MissionCardDecisionBase<List<string>>
{
    private readonly PickStringsRequest _r;
    private readonly List<string> _picked;

    public MissionCardPickMany(PickStringsRequest r, CardMoment moment)
        : base(moment, r.DisplayName, r.Instructions, r.TargetPlayerID, r.Options, r.Descriptions)
    {
        _r = r;
        // The deck starts as the book's suggested deck (Kai can change any card); every other pick starts empty.
        _picked = moment == CardMoment.Deck ? r.Suggested.ToList() : new List<string>();
    }

    public override string Hint => _r.MinPicks == _r.MaxPicks
        ? $"Click {_r.MaxPicks} card{(_r.MaxPicks == 1 ? "" : "s")}, then Space."
        : $"Click up to {_r.MaxPicks} cards, then Space.";
    public override void AutoPlay(BattleScreen s) => Complete(_r.Sanitise(_r.Suggested.ToList()));
    public override string? ConfirmText => _r.Legal(_picked) ? "ok" : null;
    public override void Confirm() { if (_r.Legal(_picked)) Complete(_r.Sanitise(_picked)); }
    public override void ClearPending() { _picked.Clear(); Redraw(); }

    /// <summary>Test driver: pick the first card(s), so a photo shows picked cards with their tags.</summary>
    public void PickForPhoto() { foreach (string o in Options.Take(Math.Max(1, _r.MaxPicks)).ToList()) if (!_picked.Contains(o)) Toggle(o); }

    private void Toggle(string o)
    {
        if (!_picked.Remove(o))
        {
            if (_r.MaxPicks == 1) _picked.Clear();
            else if (_picked.Count >= _r.MaxPicks) { S?.Toast($"You have picked {_r.MaxPicks} already - click one to put it back."); return; }
            _picked.Add(o);
        }
        Redraw();
    }

    protected override MissionCardOverlay.Spec Build()
    {
        bool hand = Moment is CardMoment.Place or CardMoment.Replace;
        var piles = new List<MissionCardOverlay.Pile>();
        if (Moment == CardMoment.Deck)
            piles.Add(new("All 36 cards", Options.Select(o => Item(o, _picked.Contains(o), () => Toggle(o))).ToList(), 186));
        else if (hand)
        {
            // Picked cards move up to the table; the rest of the hand stays below.
            var placed = _picked.Select(o => Item(o, true, () => Toggle(o), 0)).ToList();
            if (MyTable(176, null, placed, Math.Max(0, _r.MaxPicks - _picked.Count)) is { } table) piles.Add(table);
            piles.Add(new("Your hand", Options.Where(o => !_picked.Contains(o)).Select(o => Item(o, false, () => Toggle(o))).ToList(), 176));
        }
        else
        {
            if (Moment == CardMoment.HandRedraw && MyTable(150) is { } table) piles.Add(table);
            piles.Add(new(CardMoments.PileName(Moment), Options.Select(o => Item(o, _picked.Contains(o), () => Toggle(o))).ToList(), 190));
        }
        var others = new List<(string, Action)>();
        if (_r.MinPicks == 0) others.Add((CardMoments.NoneLabel(Moment), () => Complete(new List<string>())));
        if (Moment == CardMoment.Deck)
        {
            others.Add(("Suggested deck", () => { _picked.Clear(); _picked.AddRange(_r.Suggested); Redraw(); }));
            others.Add(("Clear", () => { _picked.Clear(); Redraw(); }));
        }
        string count = _r.MaxPicks > 1 ? $"{_picked.Count} / {_r.MaxPicks}" : "";
        return new MissionCardOverlay.Spec(CardMoments.Title(Moment, true), count.Length > 0 ? count : null,
            CardMoments.Verb(Moment, _r.MaxPicks), _r.Legal(_picked) && (_picked.Count > 0 || _r.MinPicks > 0), Confirm,
            others, piles, Moment == CardMoment.Deck);
    }
}

/// <summary>Pick one card, or one of the plain answers (keep, no paragon...), from the engine's one-pick list.</summary>
public sealed class MissionCardPickOne : MissionCardDecisionBase<string>
{
    private readonly StringSelectionRequest _r;
    private string? _pending;

    public MissionCardPickOne(StringSelectionRequest r, CardMoment moment)
        : base(moment, r.DisplayName, r.Instructions, r.TargetPlayerID, r.ValidOptions, r.OptionDescriptions)
        => _r = r;

    public override string Hint => "Click a card, then Space.";
    public override void AutoPlay(BattleScreen s) => Complete(_r.ValidOptions[0]);
    public override string? ConfirmText => _pending != null ? "ok" : null;
    public override void Confirm() { if (_pending is { } p) Complete(p); }
    public override void ClearPending() { _pending = null; Redraw(); }

    private void Pick(string o) { _pending = _pending == o ? null : o; Redraw(); }

    /// <summary>Test driver: pick the first card, so a photo shows it picked with its tag.</summary>
    public void PickForPhoto() { if (Options.FirstOrDefault(IsCard) is { } o) Pick(o); }

    protected override MissionCardOverlay.Spec Build()
    {
        var cardOptions = Options.Where(IsCard).ToList();
        var plain = Options.Where(o => !IsCard(o)).ToList();
        var piles = new List<MissionCardOverlay.Pile>();
        float w = Moment == CardMoment.Score ? 214 : Moment is CardMoment.Gambit or CardMoment.Challenge ? 170 : 190;
        piles.Add(new(CardMoments.PileName(Moment), cardOptions.Select(o => Item(o, _pending == o, () => Pick(o),
            caption: Moment == CardMoment.Score ? Describe(o) : null)).ToList(), w));
        if (Moment is CardMoment.Gambit or CardMoment.Challenge && MyTable(150) is { } mine) piles.Add(mine);
        return new MissionCardOverlay.Spec(CardMoments.Title(Moment, false), null, CardMoments.Verb(Moment, 1), _pending != null, Confirm,
            plain.Select(o => (o, (Action)(() => Complete(o)))).ToList(), piles, false);
    }
}
