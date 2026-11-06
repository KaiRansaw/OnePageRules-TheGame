using System.Numerics;
using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// The battle report: the top of the right column. Dice are rolled in the background; this keeps only what
/// you need to follow the game, one card per attack, newest at the top:
///
/// <code>
///   Assault Squad shoots Ork Boyz with 5x Pistol, 2x Rifle
///   5/10 hit. Ork Boyz save 2/5, taking 3 wounds and losing 3 models.
/// </code>
///
/// <para>Everything for one attacker → one target in one activation (several weapons, re-rolls, Regeneration)
/// folds into the same card. Morale tests, roll-offs, dangerous terrain and so on get a one-line entry.
/// Names are blue for your units and red for the opponent's. Hover a card to see the actual dice.</para>
///
/// <para>Built on the render thread from the presentation beats as they start playing
/// (<see cref="OnBeat"/>), so a card fills in at the same moment the shots land on the table.</para>
/// </summary>
public static class DiceTray
{
    private const int MaxEntries = 150;

    /// <summary>True for units of a player on this computer (blue); everything else is the opponent (red).</summary>
    public static Func<IUnit, bool>? IsMine;

    private enum Kind { Attack, Line, Divider }

    private sealed class Entry
    {
        public Kind Kind;
        public string Key = "";            // round + activating unit: only one activation's beats fold together
        // Attack
        public bool Melee;
        public IUnit? Attacker, Target;
        public string AttackerName = "", TargetName = "", Heading = "";
        public string Weapons = "";
        public float Hits, Attacks, Saves, SaveTotal, Ignored;
        public bool HasHits, HasSaves, Routed;
        public int Lost;
        public char Last;                  // 'a' attack, 'h' hit roll, 's' save, 'i' ignore roll
        /// <summary>Every die of this exchange, each tagged with who threw it so the hover card can say.</summary>
        public readonly List<(DiceRolledBeat Roll, string Who, Vector4 Color)> Rolls = new();
        // Line / divider
        public string Text = "";
        public Vector4 Color = new(0.75f, 0.75f, 0.78f, 1f);
        public bool Loud;                  // a headline announcement: drawn with its own accent bar
        public string? LossUnit;           // "X loses N models" lines merge
        public int LossCount;
        /// <summary>#441: rule names occurring in <see cref="Text"/>, highlighted with a hover.</summary>
        public IReadOnlyList<string> RuleNames = Array.Empty<string>();
        /// <summary>#464: words with their own hover text (items, events), drawn with a dotted underline.</summary>
        public IReadOnlyList<ReportTerm> Terms = Array.Empty<ReportTerm>();
        /// <summary>#464: the line's own hover - the rolls, the cause, the models lost.</summary>
        public IReadOnlyList<string> Details = Array.Empty<string>();

        // What the table highlights while this card is hovered (Kai, 2026-11-05).
        public UnitID? AttackerId, TargetId;
        /// <summary>Where the shots came from / landed, at the time - used when a unit has since died.</summary>
        public Position? FromAt, ToAt;
        /// <summary>Units a one-line entry is about ("X loses a model", "X routs").</summary>
        public readonly List<UnitID> Units = new();
    }

    /// <summary>
    /// What a hovered battle-report card is about, for the table to show: the attacker and target (a line
    /// between them) and every unit the card names (highlighted). See <see cref="ReportFocusOverlay"/>.
    /// </summary>
    public readonly record struct ReportFocus(UnitID? Attacker, UnitID? Target, Position? FromAt, Position? ToAt,
        IReadOnlyList<UnitID> Units);

    private static ReportFocus? _hover;
    private static int _hoverFrame = -1;

    /// <summary>The card under the mouse this frame, or null. Read after the report has drawn.</summary>
    public static ReportFocus? Hovered => _hoverFrame == ImGui.GetFrameCount() ? _hover : null;

    private static void SetHover(Entry e)
    {
        var units = new List<UnitID>(e.Units);
        if (e.AttackerId is { } a && !units.Contains(a)) units.Add(a);
        if (e.TargetId is { } t && !units.Contains(t)) units.Add(t);
        if (units.Count == 0 && e.FromAt == null && e.ToAt == null) return;
        _hover = new ReportFocus(e.AttackerId, e.TargetId, e.FromAt, e.ToAt, units);
        _hoverFrame = ImGui.GetFrameCount();
    }

    private static readonly List<Entry> Entries = new();   // oldest first; drawn newest first
    private static Entry? _open;
    private static int? _lastRound;

    private static readonly Vector4 Mine = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Theirs = new(1f, 0.45f, 0.42f, 1f);
    private static readonly Vector4 Body = new(0.86f, 0.86f, 0.84f, 1f);
    private static readonly Vector4 Dim = new(0.60f, 0.60f, 0.62f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.35f, 1f);

    /// <summary>Forget the previous game.</summary>
    public static void Reset()
    {
        Entries.Clear();
        _open = null;
        _lastRound = null;
        _pins.Clear();
        _hoveredRule = null;
        _hoveredFor = 0f;
    }

    /// <summary>A plain message line (game saved, an error...).</summary>
    public static void AddNote(string text, Vector4 color) =>
        Add(new Entry { Kind = Kind.Line, Text = text, Color = color });

    private static void Add(Entry e)
    {
        Entries.Add(e);
        if (Entries.Count > MaxEntries) Entries.RemoveAt(0);
    }

    // ── Building the report from the beats ───────────────────────────────────────────────────────────

    private static string ActivationKey()
    {
        IGameProgress? p = PopupAnchor.Progress;
        return $"{p?.RoundCount}:{p?.ActivatingUnit?.ID}";
    }

    private static void CheckRound()
    {
        int? round = PopupAnchor.Progress?.RoundCount;
        if (round == null || round == _lastRound) return;
        _lastRound = round;
        _open = null;
        Add(new Entry { Kind = Kind.Divider, Text = $"Round {round}" });
    }

    /// <summary>Feed every beat here as it starts playing (PresentationPlayer.BeatStarted).</summary>
    public static void OnBeat(PresentationBeat beat)
    {
        try { Handle(beat); }
        catch { /* the report is cosmetic - never let it break the game */ }
    }

    private static void Handle(PresentationBeat beat)
    {
        CheckRound();
        string key = ActivationKey();
        Entry? open = _open is { Kind: Kind.Attack } o && o.Key == key ? o : null;

        switch (beat)
        {
            case AttackBeat a:
            {
                // The engine names the units now; the nearest-model guess is only for a beat from an older
                // host or save, and it is the guess that picks the wrong unit in a melee scrum.
                IUnit? att = (a.AttackerUnit is { } aid ? UnitById(aid) : null)
                    ?? (a.From.Count > 0 ? UnitAt(a.From[0]) : PopupAnchor.Progress?.ActivatingUnit);
                IUnit? tgt = (a.TargetUnit is { } tid ? UnitById(tid) : null)
                    ?? (a.To.Count > 0 ? UnitAt(a.To[0]) : null);
                if (open != null && Same(open.Attacker, att) && Same(open.Target, tgt) && open.Melee == a.IsMelee)
                {
                    open.Last = 'a';
                    break;
                }
                var e = new Entry
                {
                    Kind = Kind.Attack, Key = key, Melee = a.IsMelee, Attacker = att, Target = tgt,
                    AttackerName = att?.Name ?? "", TargetName = tgt?.Name ?? "",
                    Weapons = att == null ? "" : WeaponList(att, a.IsMelee), Last = 'a',
                    AttackerId = a.AttackerUnit ?? att?.ID, TargetId = a.TargetUnit ?? tgt?.ID,
                    FromAt = Centroid(a.From), ToAt = Centroid(a.To),
                };
                Add(e);
                _open = e;
                break;
            }

            case DiceRolledBeat d when d.Category == ERollBeatCategory.Offense:
            {
                if (open != null && open.Last == 'a')
                {
                    open.Hits += d.Successes; open.Attacks += d.Total; open.HasHits = true;
                    open.Last = 'h';
                    open.Rolls.Add((d, Side(open.AttackerName, open.Attacker), SideColor(open.Attacker)));
                    break;
                }
                // A damage roll with no attack animation (a spell, an ability): its own card.
                var e = new Entry
                {
                    Kind = Kind.Attack, Key = key, Heading = string.IsNullOrEmpty(d.Context) ? d.Label : $"{d.Context} - {d.Label}",
                    Hits = d.Successes, Attacks = d.Total, HasHits = true, Last = 'h',
                };
                e.Rolls.Add((d, "Attacker", Body));
                Add(e);
                _open = e;
                break;
            }

            case DiceRolledBeat d when d.Category == ERollBeatCategory.Defense && open != null:
            {
                if (open.Last == 'h')
                {
                    open.Saves += d.Successes; open.SaveTotal += d.Total; open.HasSaves = true;
                    open.Last = 's';
                }
                else
                {
                    open.Ignored += d.Successes;   // Regeneration and other "ignore the wound" rolls
                    open.Last = 'i';
                }
                open.Rolls.Add((d, Side(open.TargetName, open.Target), SideColor(open.Target)));
                break;
            }

            case DiceRolledBeat d:
            {
                string head = string.IsNullOrEmpty(d.Context) ? "" : d.Context + ": ";
                string result = d.ResultSummary ?? $"{Num(d.Successes)}/{Num(d.Total)}";
                Add(new Entry
                {
                    Kind = Kind.Line, Text = $"{head}{d.Label} ({d.SuccessThreshold}+) - {result}",
                    Color = d.Category == ERollBeatCategory.Magic ? new Vector4(0.55f, 0.78f, 1f, 1f) : Dim,
                });
                break;
            }

            case ModelDiedBeat died:
            {
                if (open != null && (open.Target == null || open.Target.ID.Equals(died.Unit)))
                {
                    open.Lost++;
                    if (open.Target == null && open.TargetName.Length == 0) open.TargetName = died.UnitName;
                    break;
                }
                Entry? last = Entries.Count > 0 ? Entries[^1] : null;
                if (last is { Kind: Kind.Line } && last.LossUnit == died.UnitName)
                {
                    if (!last.Units.Contains(died.Unit)) last.Units.Add(died.Unit);
                    last.LossCount++;
                    last.Text = $"{died.UnitName} loses {last.LossCount} models.";
                    break;
                }
                var lossLine = new Entry { Kind = Kind.Line, Text = $"{died.UnitName} loses a model.", Color = Bad,
                    LossUnit = died.UnitName, LossCount = 1, ToAt = died.Position };
                lossLine.Units.Add(died.Unit);
                Add(lossLine);
                break;
            }

            case UnitRoutedBeat routed:
                if (open != null && open.Target != null && open.Target.ID.Equals(routed.Unit)) open.Routed = true;
                else
                {
                    var routLine = new Entry { Kind = Kind.Line, Text = $"{routed.UnitName} routs!", Color = Bad };
                    routLine.Units.Add(routed.Unit);
                    Add(routLine);
                }
                break;

            case RollOffBeat r:
                Add(new Entry
                {
                    Kind = Kind.Line, Color = Dim,
                    Text = $"{r.Label}: " + string.Join(", ", r.Entries.Select(e => e.Name + " " + e.Roll +
                        (e.Result == ERollOffResult.Won ? " (wins)" : e.Result == ERollOffResult.TiedForWin ? " (tie)" : ""))),
                });
                break;

            // Everything the game says out loud: a skill used, an item found or used, a condition landing
            // or wearing off, an objective seized, a wave arriving. The engine already announces all of it
            // with its own colour, so the report shows the same words in the same colour instead of leaving
            // the player to work out from "X loses a model" that it was the poison.
            case BannerBeat b:
            {
                string text = (b.BannerText ?? "").Trim();
                if (text.Length == 0) break;
                // The round banner is already drawn as the divider above these lines.
                if (text.StartsWith("Round ", StringComparison.OrdinalIgnoreCase) && text.Length <= 10) break;
                Entry? prev = Entries.Count > 0 ? Entries[^1] : null;
                if (prev is { Kind: Kind.Line } && prev.Text == text) break;   // the same thing twice
                Add(new Entry
                {
                    Kind = Kind.Line, Text = text, Color = From(b.Color),
                    Loud = b.Tier == EBannerTier.Headline,
                    // #441: the engine names the rules it mentioned; DrawLine highlights them and hovers
                    // their book wording.
                    RuleNames = b.ReferencedRules,
                    Terms = b.Terms ?? Array.Empty<ReportTerm>(),
                    Details = b.Details ?? Array.Empty<string>(),
                });
                break;
            }
        }
    }

    /// <summary>An engine colour as an ImGui one, floored so a dark announcement stays readable.</summary>
    private static Vector4 From(TextColor c) => new(
        Math.Clamp(c.R / 255f, 0.35f, 1f), Math.Clamp(c.G / 255f, 0.35f, 1f), Math.Clamp(c.B / 255f, 0.35f, 1f), 1f);

    /// <summary>The name to put on a roll in the hover card.</summary>
    private static string Side(string name, IUnit? unit) =>
        name.Length > 0 ? name : unit?.Name ?? "Unknown";

    private static Vector4 SideColor(IUnit? unit) =>
        unit == null ? Body : IsMine?.Invoke(unit) == true ? Mine : Theirs;

    private static IUnit? UnitById(UnitID id) =>
        PopupAnchor.TableState?.Units.Objects.FirstOrDefault(u => u.ID.Equals(id));

    private static Position? Centroid(IReadOnlyList<Position> ps)
    {
        if (ps.Count == 0) return null;
        float x = 0f, z = 0f;
        foreach (Position p in ps) { x += p.x; z += p.z; }
        return new Position(x / ps.Count, z / ps.Count);
    }

    private static bool Same(IUnit? a, IUnit? b) => a == null || b == null ? a == b : a.ID.Equals(b.ID);

    /// <summary>The unit with a model nearest to <paramref name="p"/>.</summary>
    private static IUnit? UnitAt(Position p)
    {
        ITableState? ts = PopupAnchor.TableState;
        if (ts == null) return null;
        IUnit? best = null;
        float bestD = float.MaxValue;
        foreach (IUnit u in ts.Units.Objects)
            foreach (IModel m in u.Models)
            {
                float dx = m.Position.x - p.x, dz = m.Position.z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = u; }
            }
        return best;
    }

    /// <summary>"5x Pistol, 2x Rifle": the weapons of the unit's living models, ranged or melee.</summary>
    private static string WeaponList(IUnit u, bool melee)
    {
        var groups = u.Models.Where(m => m.GetIsAlive()).SelectMany(m => m.Weapons)
            .Where(w => melee ? w.RangeInches <= 0f : w.RangeInches > 0f)
            .GroupBy(w => w.Name)
            .Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key);
        return string.Join(", ", groups);
    }

    private static string Num(float v) => v.ToString("0.#");
    private static string Plural(float n, string one, string many) => Math.Abs(n - 1f) < 0.01f ? one : many;

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────────

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static void Draw(float x, float y, float w, float h)
    {
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.07f, 0.08f, 0.97f));
        ImGui.Begin("##dicetray", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleColor();

        ImGui.TextDisabled("BATTLE REPORT");
        ImGui.Separator();
        DrawBody();
        ImGui.End();
        DrawPinnedRuleCards();
    }

    /// <summary>The report's lines, newest first, drawn into the current window - the Battle Report tab.</summary>
    public static void DrawBody()
    {
        if (Entries.Count == 0)
            ImGui.TextDisabled("Attacks, saves and casualties show up here.");

        float width = ImGui.GetContentRegionAvail().X;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry e = Entries[i];
            ImGui.PushID(i);
            switch (e.Kind)
            {
                case Kind.Attack: DrawAttack(e, width); break;
                case Kind.Divider: DrawDivider(e.Text, width); break;
                default: DrawLine(e, width); break;
            }
            ImGui.PopID();
            ImGui.Dummy(new Vector2(1f, 3f));
        }
    }

    /// <summary>
    /// A one-line entry: a thin bar in the line's own colour down the left, then the text. The bar is what
    /// makes the report scannable - a run of orange is damage, blue is something a hero did, gold is the
    /// mission moving on - without having to read every word.
    /// </summary>
    private static void DrawLine(Entry e, float width)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 start = ImGui.GetCursorScreenPos();
        const float bar = 3f, gap = 7f;
        ImGui.SetCursorScreenPos(start + new Vector2(bar + gap, 0f));

        // #441: a line that names special rules draws them highlighted, with a hover giving the rule's
        // own wording from the books. Everything else takes the single-piece path unchanged.
        List<(string text, Vector4 color)> pieces = SplitOnRuleNames(e, out HashSet<int> hot,
            out Dictionary<int, (string Name, string? TermText)> hotNames);
        var hotRects = hot.Count > 0 ? new List<(Vector2 Min, Vector2 Max)>() : null;
        var rectPieces = hot.Count > 0 ? new List<int>() : null;
        // #464 (Kai): item and event names get a DOTTED underline; rule names keep the solid one.
        var dotted = hotNames.Where(kv => kv.Value.TermText != null).Select(kv => kv.Key).ToHashSet();
        Rich(pieces, width - bar - gap, hot.Count > 0 ? hot : null, hotRects, dotted, rectPieces);

        bool overWord = false;
        if (hotRects is { Count: > 0 }) overWord = HoverRuleTooltip(e, hotRects, rectPieces!, hotNames);

        float bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
        // #464: the line's own breakdown, anywhere on it except a highlighted word (which has its own).
        if (!overWord && e.Details.Count > 0 && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseHoveringRect(start, new Vector2(start.X + width, bottom)))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(380f);
            foreach (string detail in e.Details) ImGui.TextUnformatted(detail);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        if (e.Units.Count > 0 && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseHoveringRect(start, new Vector2(start.X + width, bottom)))
            SetHover(e);
        dl.AddRectFilled(start, new Vector2(start.X + bar, bottom), U(e.Color), 2f);
        if (e.Loud) dl.AddRectFilled(new Vector2(start.X, bottom - 1f), new Vector2(start.X + width, bottom), U(e.Color), 1f);
    }

    private static void DrawDivider(string text, float width)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 p = ImGui.GetCursorScreenPos();
        Vector2 ts = ImGui.CalcTextSize(text);
        float mid = p.Y + ts.Y / 2f;
        float tx = p.X + (width - ts.X) / 2f;
        uint line = U(new Vector4(0.35f, 0.35f, 0.38f, 1f));
        dl.AddLine(new Vector2(p.X, mid), new Vector2(tx - 8f, mid), line);
        dl.AddLine(new Vector2(tx + ts.X + 8f, mid), new Vector2(p.X + width, mid), line);
        dl.AddText(new Vector2(tx, p.Y), U(new Vector4(0.80f, 0.72f, 0.50f, 1f)), text);
        ImGui.Dummy(new Vector2(width, ts.Y));
    }

    private static void DrawAttack(Entry e, float width)
    {
        bool attMine = e.Attacker != null && IsMine?.Invoke(e.Attacker) == true;
        bool tgtMine = e.Target != null && IsMine?.Invoke(e.Target) == true;
        Vector4 attCol = e.Attacker == null ? Body : attMine ? Mine : Theirs;
        Vector4 tgtCol = e.Target == null ? Body : tgtMine ? Mine : Theirs;
        string target = e.TargetName.Length > 0 ? e.TargetName : "the target";

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        Vector2 start = ImGui.GetCursorScreenPos();
        const float padX = 10f, padY = 6f;
        ImGui.SetCursorScreenPos(start + new Vector2(padX, padY));
        float inner = width - padX * 2f;

        // Who did what to whom.
        var head = new List<(string, Vector4)>();
        if (e.Heading.Length > 0) head.Add((e.Heading, Body));
        else
        {
            head.Add((e.AttackerName.Length > 0 ? e.AttackerName : "A unit", attCol));
            head.Add((e.Melee ? " fights " : " shoots ", Dim));
            head.Add((target, tgtCol));
        }
        Rich(head, inner);
        if (e.Weapons.Length > 0) Rich(new() { ("with " + e.Weapons, Dim) }, inner);

        // The outcome, in one sentence.
        var res = new List<(string, Vector4)>();
        if (e.HasHits) res.Add(($"{Num(e.Hits)}/{Num(e.Attacks)} hit. ", Body));
        if (e.HasSaves)
        {
            float wounds = MathF.Max(0f, e.SaveTotal - e.Saves - e.Ignored);
            res.Add((target, tgtCol));
            res.Add(($" {(e.TargetName.EndsWith('s') ? "save" : "saves")} {Num(e.Saves)}/{Num(e.SaveTotal)}", Body));
            if (e.Ignored > 0f) res.Add(($", ignores {Num(e.Ignored)}", Body));
            res.Add((wounds > 0f ? $", taking {Num(wounds)} {Plural(wounds, "wound", "wounds")}" : ", taking no wounds",
                wounds > 0f ? Bad : Good));
            if (e.Lost > 0) res.Add(($" and losing {e.Lost} {Plural(e.Lost, "model", "models")}", Bad));
            res.Add((".", Body));
        }
        else if (e.HasHits && e.Hits <= 0f) res.Add(("No damage.", Good));
        else if (e.Lost > 0) res.Add(($"{target} loses {e.Lost} {Plural(e.Lost, "model", "models")}.", Bad));
        if (e.Routed) res.Add(($" {target} routs!", Bad));
        if (res.Count > 0) Rich(res, inner);

        Vector2 end = new(start.X + width, ImGui.GetCursorScreenPos().Y + padY - ImGui.GetStyle().ItemSpacing.Y);
        dl.ChannelsSetCurrent(0);
        bool hovered = ImGui.IsMouseHoveringRect(start, end) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        if (hovered) SetHover(e);
        dl.AddRectFilled(start, end, U(hovered ? new Vector4(0.15f, 0.14f, 0.13f, 1f) : new Vector4(0.11f, 0.10f, 0.09f, 1f)), 5f);
        dl.AddRectFilled(start, new Vector2(start.X + 4f, end.Y), U(attCol), 5f, ImDrawFlags.RoundCornersLeft);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y));
        ImGui.Dummy(new Vector2(width, 0f));

        if (hovered && e.Rolls.Count > 0)
        {
            ImGui.BeginTooltip();
            DrawBreakdown(e);
            // Both sides of the exchange, in order and named: your attack dice, then the dice the enemy
            // threw to save against them. Each block is headed by whoever rolled it, in their own colour.
            foreach (var (d, who, col) in e.Rolls) DrawRollDetail(d, who, col);
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// #464 - the attack's arithmetic in one place, at the top of the hover: attacks, hits, saves, ignored,
    /// wounds, models lost. The dice blocks below it are the detail; this is the sum a player wants first.
    /// </summary>
    private static void DrawBreakdown(Entry e)
    {
        var lines = new List<string>();
        // The threshold only when every roll of that kind shared it - several weapons can need different numbers.
        static string Needed(IEnumerable<DiceRolledBeat> rolls)
        {
            var t = rolls.Select(r => r.SuccessThreshold).Distinct().ToList();
            return t.Count == 1 ? $" (needed {t[0]}+)" : "";
        }
        string hitNeeded = Needed(e.Rolls.Select(r => r.Roll).Where(r => r.Category == ERollBeatCategory.Offense));
        string saveNeeded = Needed(e.Rolls.Select(r => r.Roll).Where(r => r.Category == ERollBeatCategory.Defense));
        if (e.HasHits)
            lines.Add($"Attacks: {Num(e.Attacks)}  ->  hits: {Num(e.Hits)}{hitNeeded}");
        if (e.HasSaves)
        {
            lines.Add($"Saves: {Num(e.Saves)} of {Num(e.SaveTotal)}{saveNeeded}");
            if (e.Ignored > 0f) lines.Add($"Wounds ignored: {Num(e.Ignored)}");
            lines.Add($"Wounds: {Num(MathF.Max(0f, e.SaveTotal - e.Saves - e.Ignored))}");
        }
        if (e.Lost > 0 || e.HasSaves) lines.Add($"Models lost: {e.Lost}" + (e.Routed ? " - and it routs" : ""));
        if (lines.Count == 0) return;
        foreach (string line in lines) ImGui.TextUnformatted(line);
        ImGui.Separator();
    }

    /// <summary>One roll in the hover card: who rolled it, then the dice - passes bright, fails greyed.</summary>
    private static void DrawRollDetail(DiceRolledBeat d, string who, Vector4 whoColor)
    {
        // UiText, not TextColored: a unit's name is data and may carry a '%' (#398).
        FdgRaylib.Rendering.UiText.Colored(whoColor, who);
        ImGui.TextUnformatted($"{d.Label} ({d.SuccessThreshold}+)");

        // #407: show WHY the threshold is what it is. The beat has carried these all along (#245
        // composes them where the arithmetic happens - "Quality 4+", "Vengeful Strike +1", "buff +1",
        // "Fatigued: 6s only") and the dice overlay draws them as chips, but this hover - the one you
        // actually read in the Battle Report, after the roll has gone past - dropped them. So a
        // player had no way to tell whether a skill's modifier applied, which is what put several QA
        // items on standby. Nothing new is recorded; this just stops the report hiding it.
        if (d.ModifierTags is { Count: > 0 })
        {
            ImGui.TextDisabled(string.Join("   ", d.ModifierTags));
        }
        if (d.Mode != ERandomnessType.Realistic)
        {
            ImGui.TextDisabled($"about {Num(d.Successes)} of {Num(d.Total)} (average)");
            return;
        }
        var faces = new List<int>();
        for (int i = 0; i < d.FaceCounts.Count; i++)
            for (int k = 0; k < (int)MathF.Round(d.FaceCounts[i]); k++) faces.Add(d.SideMin + i);
        faces.Sort((a, b) => b.CompareTo(a));

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        const float size = 18f, gap = 3f;
        int perRow = 14;
        Vector2 o = ImGui.GetCursorScreenPos();
        int shown = Math.Min(faces.Count, 84);
        for (int i = 0; i < shown; i++)
        {
            Vector2 p = o + new Vector2(i % perRow * (size + gap), i / perRow * (size + gap));
            bool ok = faces[i] >= d.SuccessThreshold;
            dl.AddRectFilled(p, p + new Vector2(size, size), U(ok ? new Vector4(0.93f, 0.92f, 0.88f, 1f) : new Vector4(0.30f, 0.30f, 0.32f, 1f)), 3f);
            if (ok) dl.AddRect(p, p + new Vector2(size, size), U(new Vector4(0.30f, 0.90f, 0.35f, 1f)), 3f, ImDrawFlags.None, 2f);
            string t = faces[i].ToString();
            Vector2 ts = ImGui.CalcTextSize(t);
            dl.AddText(p + (new Vector2(size, size) - ts) / 2f, U(ok ? new Vector4(0.1f, 0.1f, 0.12f, 1f) : new Vector4(0.62f, 0.62f, 0.64f, 1f)), t);
        }
        int rows = Math.Max(1, (shown + perRow - 1) / perRow);
        ImGui.Dummy(new Vector2(Math.Min(shown, perRow) * (size + gap), rows * (size + gap)));
        if (faces.Count > shown) ImGui.TextDisabled($"+{faces.Count - shown} more");
        ImGui.TextDisabled(d.ResultSummary ?? $"{Num(d.Successes)} of {Num(d.Total)} succeed");
        ImGui.Spacing();
    }

    /// <summary>Word-wrapped text made of differently coloured pieces.</summary>
    /// <summary>The rule-name highlight: the report's gold, the same colour the mission lines use.</summary>
    /// <summary>
    /// Where this gold started (#441). The literal moved to <see cref="ImGuiTheme.RuleGold"/> on
    /// 2026-11-02 when Kai asked for the same treatment on every rule name in the game, so the report
    /// and every other screen cannot drift apart.
    /// </summary>
    private static Vector4 RuleName => ImGuiTheme.RuleGold;

    /// <summary>
    /// #441: how a rule name becomes the wording a player reads. Set once per game by the renderer, which
    /// is the only thing that knows which system is being played - and the system matters, because OPR
    /// words the same rule differently per game (Banner is the example this was built for). Null before a
    /// game starts, and a null ANSWER just means no tooltip for that name.
    /// </summary>
    public static Func<string, string?>? RuleTextLookup;

    /// <summary>
    /// Splits the line into alternating plain and rule-name pieces, reporting which piece indices are
    /// the rule names. Longest name first, so "Banner Boost" is not split by "Banner" sitting inside it.
    /// </summary>
    private static List<(string text, Vector4 color)> SplitOnRuleNames(Entry e, out HashSet<int> hot,
        out Dictionary<int, (string Name, string? TermText)> hotNames)
    {
        hot = new HashSet<int>();
        hotNames = new Dictionary<int, (string Name, string? TermText)>();
        if (e.RuleNames.Count == 0 && e.Terms.Count == 0) return new() { (e.Text, e.Color) };

        var pieces = new List<(string text, Vector4 color)>();
        string rest = e.Text;
        // Rule names hover the books' wording; terms carry their own text (#464).
        var termText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReportTerm t in e.Terms)
            if (!string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Text)) termText[t.Name] = t.Text;
        List<string> names = e.RuleNames.Concat(termText.Keys)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .ToList();

        while (rest.Length > 0)
        {
            int at = -1;
            string found = "";
            foreach (string name in names)
            {
                int i = rest.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                if (at < 0 || i < at) { at = i; found = name; }
            }

            if (at < 0) { pieces.Add((rest, e.Color)); break; }

            if (at > 0) pieces.Add((rest[..at], e.Color));
            hot.Add(pieces.Count);
            hotNames[pieces.Count] = (found, termText.TryGetValue(found, out string? tt) ? tt : null);
            pieces.Add((rest.Substring(at, found.Length), RuleName));
            rest = rest[(at + found.Length)..];
        }

        return pieces;
    }

    // One timer for the whole report: only one rule name can be under the cursor at a time, and resting
    // on a different one restarts the wait rather than inheriting the last one's.
    private static string? _hoveredRule;
    private static float _hoveredFor;

    /// <summary>
    /// A rule card the player clicked to keep (#441, Kai 2026-11-01): once a hover tooltip is up,
    /// clicking the highlighted word turns it into a card that stays, so the cursor is free to go and
    /// raise another one. Several can be open at once, which is the point - comparing two rules means
    /// reading both at the same time, and a tooltip that dies when the mouse moves cannot do that.
    /// </summary>
    private sealed class Pin
    {
        public string Name = "";
        public string Text = "";
        /// <summary>Where it first appeared - under the word it came from. Draggable after that.</summary>
        public Vector2 At;
        /// <summary>Distinct per card so two cards for different rules keep their own window state.</summary>
        public int Id;
    }

    private static readonly List<Pin> _pins = new();
    private static int _nextPinId = 1;

    /// <summary>
    /// Shows the rule's book wording once the cursor has rested on it, reusing
    /// <see cref="TableTooltipOverlay.HoverDelaySeconds"/> rather than inventing a second delay - the
    /// table tooltips already wait exactly this long, and two different waits in one UI reads as a bug.
    ///
    /// <para>
    /// A click while the tooltip is up pins it (see <see cref="Pin"/>); a click on an already-pinned word
    /// takes the card away again, so the same gesture is both the on and the off.
    /// </para>
    /// </summary>
    /// <returns>Whether the cursor is on one of the highlighted words.</returns>
    private static bool HoverRuleTooltip(Entry e, List<(Vector2 Min, Vector2 Max)> rects, List<int> rectPieces,
        Dictionary<int, (string Name, string? TermText)> hotNames)
    {
        Vector2 mouse = ImGui.GetMousePos();
        int hit = rects.FindIndex(r => mouse.X >= r.Min.X && mouse.X <= r.Max.X
            && mouse.Y >= r.Min.Y && mouse.Y <= r.Max.Y);
        bool over = hit >= 0;

        // #464: the word actually under the cursor - a line can now name several (an item and a rule).
        (string Name, string? TermText) word = over && hit < rectPieces.Count && hotNames.TryGetValue(rectPieces[hit], out var w)
            ? w : (e.RuleNames.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "", null);
        string? name = word.Name.Length > 0 ? word.Name : null;
        if (!over || name == null)
        {
            if (!over && _hoveredRule != null && hotNames.Values.Any(v => v.Name == _hoveredRule)) { _hoveredRule = null; _hoveredFor = 0f; }
            return over;
        }

        if (_hoveredRule != name) { _hoveredRule = name; _hoveredFor = 0f; }
        _hoveredFor += ImGui.GetIO().DeltaTime;
        if (_hoveredFor < TableTooltipOverlay.HoverDelaySeconds) return true;

        string? text = word.TermText ?? RuleTextLookup?.Invoke(name);
        if (string.IsNullOrWhiteSpace(text)) return true;

        // Already pinned: its card is on screen, so a second floating copy beside it would just be in the
        // way. The click here is the way to dismiss it.
        int pinned = _pins.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (pinned >= 0)
            {
                _pins.RemoveAt(pinned);
            }
            else
            {
                // Just below the word, so the card does not cover the line it came from.
                _pins.Add(new Pin
                {
                    Name = name, Text = text!, Id = _nextPinId++,
                    At = new Vector2(rects[hit].Min.X, rects[hit].Max.Y + 4f),
                });
            }

            return true;
        }

        if (pinned >= 0) return true;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(380f);
        ImGui.PushStyleColor(ImGuiCol.Text, RuleName);
        ImGui.TextUnformatted(name);
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.TextUnformatted(text);
        ImGui.TextDisabled("Click to keep this open.");
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        return true;
    }

    /// <summary>
    /// Draws the pinned rule cards. Called AFTER the report's own window has ended, because each card is
    /// a top-level window of its own - started inside the report it would be clipped to the panel and
    /// could not be dragged out of it.
    /// </summary>
    public static void DrawPinnedRuleCards()
    {
        if (_pins.Count == 0) return;

        for (int i = _pins.Count - 1; i >= 0; i--)
        {
            Pin pin = _pins[i];
            ImGui.SetNextWindowPos(pin.At, ImGuiCond.Appearing);
            ImGui.SetNextWindowSizeConstraints(new Vector2(240f, 0f), new Vector2(380f, 10000f));
            bool open = true;
            if (ImGui.Begin($"{pin.Name}##pinnedrule{pin.Id}", ref open,
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing))
            {
                ImGui.PushTextWrapPos(360f);
                ImGui.TextUnformatted(pin.Text);
                ImGui.PopTextWrapPos();
            }
            ImGui.End();

            if (!open) _pins.RemoveAt(i);
        }
    }

    private static void Rich(List<(string text, Vector4 color)> pieces, float width)
        => Rich(pieces, width, null, null);

    private static void Rich(List<(string text, Vector4 color)> pieces, float width,
        HashSet<int>? hotPieces, List<(Vector2 Min, Vector2 Max)>? hotRects)
        => Rich(pieces, width, hotPieces, hotRects, null, null);

    /// <summary>
    /// #441: the wrapping renderer, optionally collecting the on-screen rectangles of whichever pieces
    /// are flagged in <paramref name="hotPieces"/>. It draws straight to the draw list rather than
    /// emitting ImGui items - which is what makes the wrapping possible - so there is no item to ask
    /// <c>IsItemHovered</c> about, and a hover has to be hit-tested against rectangles we kept.
    /// </summary>
    private static void Rich(List<(string text, Vector4 color)> pieces, float width,
        HashSet<int>? hotPieces, List<(Vector2 Min, Vector2 Max)>? hotRects, HashSet<int>? dottedPieces,
        List<int>? rectPieces)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 o = ImGui.GetCursorScreenPos();
        float lh = ImGui.GetTextLineHeight();
        float space = ImGui.CalcTextSize(" ").X;
        float x = 0f, y = 0f;
        for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
        {
            (string text, Vector4 color) = pieces[pieceIndex];
            bool hot = hotPieces != null && hotPieces.Contains(pieceIndex);
            uint col = U(color);
            string[] words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                // No space at the start of a line (a wrapped line used to start indented).
                if (i > 0 && x > 0f) x += space;
                string word = words[i];
                if (word.Length == 0) continue;
                float ww = ImGui.CalcTextSize(word).X;
                if (x > 0f && x + ww > width) { x = 0f; y += lh; }
                // A word longer than the whole line (a long name or path) is broken across lines instead of
                // running off the side of the report.
                while (ww > width && word.Length > 1)
                {
                    int fit = 1;
                    while (fit < word.Length && ImGui.CalcTextSize(word[..(fit + 1)]).X <= width - x) fit++;
                    dl.AddText(o + new Vector2(x, y), col, word[..fit]);
                    word = word[fit..];
                    ww = ImGui.CalcTextSize(word).X;
                    x = 0f; y += lh;
                }
                dl.AddText(o + new Vector2(x, y), col, word);
                if (hot)
                {
                    Vector2 min = o + new Vector2(x, y);
                    Vector2 max = min + new Vector2(ww, lh);
                    // Underlined in place, the same treatment #292/#336 gave rule names elsewhere, so the
                    // highlight does not rely on colour alone.
                    if (dottedPieces != null && dottedPieces.Contains(pieceIndex))
                    {
                        // #464: dotted - "this explains itself" for items and events.
                        for (float dx = min.X; dx < max.X; dx += 4f)
                            dl.AddLine(new Vector2(dx, max.Y - 1f), new Vector2(MathF.Min(dx + 2f, max.X), max.Y - 1f), col);
                    }
                    else
                    {
                        dl.AddLine(new Vector2(min.X, max.Y - 1f), new Vector2(max.X, max.Y - 1f), col);
                    }
                    hotRects?.Add((min, max));
                    rectPieces?.Add(pieceIndex);
                }
                x += ww;
            }
        }
        ImGui.Dummy(new Vector2(width, y + lh));
    }
}
