using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.TextInterface;

namespace FdgRaylib.Game;

/// <summary>
/// The battle report as data, for any front end: what happened, one line per thing, built from the engine's
/// presentation beats (the same source as the Raylib build's report, <c>DiceTray</c>, whose rules this follows)
/// - plus who activated, and where a moved unit came from.
///
/// <para>Every line carries what a hover needs: the units it is about (rings on the table), an attacker and a
/// target (an arrow), a moved unit's start and end (a ghost where it stood), the rule names and terms it
/// mentions (book text on hover) and the dice behind it. Nothing here draws.</para>
///
/// <para>Beats arrive on the engine thread (<see cref="IPresentationSink.OnBeat"/>); screens read
/// <see cref="Snapshot"/> on their own thread. <see cref="Version"/> changes whenever a line is added or changed.</para>
/// </summary>
public sealed class BattleReport : IPresentationSink
{
    public enum Kind { Divider, Activation, Attack, Move, Line }

    /// <summary>One run of text in a line: plain, a unit's name, or a word with a hover (a rule, a term).</summary>
    public enum PieceKind { Plain, Unit, Rule, Term }

    public sealed record Piece(string Text, PieceKind Kind = PieceKind.Plain, UnitID? Unit = null, string? Hover = null);

    /// <summary>A model's footprint at one moment (where it stood before a move, where it ended).</summary>
    public readonly record struct Spot(float X, float Z, float Radius);

    /// <summary>One die batch of an attack, as the hover shows it.</summary>
    public sealed record Roll(string Who, UnitID? WhoUnit, string Label, int Threshold, string Result, IReadOnlyList<int> Faces,
        IReadOnlyList<string> Modifiers);

    public sealed record Line(
        int Id, Kind Kind, int? Round,
        IReadOnlyList<Piece> Title, string Body, TextColor Colour, bool Loud,
        UnitID? Attacker, UnitID? Target, PlayerID? Player,
        IReadOnlyList<UnitID> Units,
        Position? FromAt, Position? ToAt,
        IReadOnlyList<Spot> MovedFrom, IReadOnlyList<Spot> MovedTo,
        IReadOnlyList<Roll> Rolls, IReadOnlyList<string> Details,
        Outcome? Attack = null, float MovedInches = 0f, string? TargetName = null);

    /// <summary>What an attack did, as numbers (the block view shows these, not the sentence).</summary>
    public sealed record Outcome(bool Melee, float Hits, float Attacks, float Wounds, int ModelsLost, bool Routed, string Weapons);

    /// <summary>
    /// One activation as the report's block view shows it (Kai, 2026-11-11, mockup A): who, what they did,
    /// and the lines that happened during it. Lines before the first activation of a round (deployment, the
    /// round's own events) form a block with no unit.
    /// </summary>
    public sealed record Block(int? Round, Line? Activation, IReadOnlyList<Line> Lines)
    {
        public UnitID? Unit => Activation?.Units.FirstOrDefault();
        public float Moved => Lines.Where(l => l.Kind == Kind.Move && l.Units.Contains(Unit ?? default)).Sum(l => l.MovedInches);
        public bool Shot => Lines.Any(l => l.Attack is { Melee: false } && l.Attacker.Equals(Unit));
        public bool Fought => Lines.Any(l => l.Attack is { Melee: true } && (l.Attacker.Equals(Unit) || l.Target.Equals(Unit)));
        public bool Charged => Moved > 0 && Lines.Any(l => l.Attack is { Melee: true } && l.Attacker.Equals(Unit));
    }

    /// <summary>Groups lines into activation blocks, oldest first. Round dividers start a new block.</summary>
    public static List<Block> Blocks(IReadOnlyList<Line> lines)
    {
        var blocks = new List<Block>();
        Line? head = null;
        var current = new List<Line>();
        int? round = null;
        void Flush()
        {
            if (head != null || current.Count > 0) blocks.Add(new Block(round, head, current.ToList()));
            head = null;
            current.Clear();
        }
        foreach (Line l in lines)
        {
            switch (l.Kind)
            {
                case Kind.Divider: Flush(); round = l.Round; break;
                case Kind.Activation: Flush(); head = l; round = l.Round ?? round; break;
                default: current.Add(l); break;
            }
        }
        Flush();
        return blocks;
    }

    private const int MaxLines = 400;

    private readonly ITableState _table;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();
    private Entry? _open;
    private int? _lastRound;
    private string _lastActivation = "";
    private int _nextId;

    public BattleReport(ITableState table) => _table = table;

    /// <summary>A seat's name for the activation lines ("Kai: Automa Drones").</summary>
    public Func<PlayerID, string>? PlayerName { get; set; }

    public int Version { get; private set; }

    public static readonly TextColor Dim = new(170, 175, 185, 255);
    public static readonly TextColor Bad = new(235, 110, 100, 255);
    public static readonly TextColor Plain = new(215, 220, 228, 255);

    // ================================================================== reading

    public IReadOnlyList<Line> Snapshot()
    {
        lock (_gate) return _entries.Select(e => e.Freeze()).ToList();
    }

    /// <summary>The whole report as text (Save Battle Report).</summary>
    public string ExportText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (Line l in Snapshot())
        {
            string title = string.Concat(l.Title.Select(p => p.Text));
            if (l.Kind == Kind.Divider) { sb.AppendLine().AppendLine($"=== {title} ==="); continue; }
            sb.AppendLine(title);
            if (l.Body.Length > 0) sb.AppendLine("    " + l.Body);
            foreach (Roll r in l.Rolls)
                sb.AppendLine($"    {r.Who}: {r.Label} ({r.Threshold}+) - {(r.Faces.Count > 0 ? "rolled " + string.Join(" ", r.Faces) + " - " : "")}{r.Result}");
            foreach (string d in l.Details) sb.AppendLine("    " + d);
        }
        return sb.ToString();
    }

    // ================================================================== writing

    /// <summary>A unit started its activation (the screen calls this when the game's activating unit changes).</summary>
    public void NoteActivation()
    {
        lock (_gate)
        {
            CheckRound();
            EnsureActivation();
        }
    }

    /// <summary>
    /// Adds the "who activates" line the first time anything is heard from a new activation - from the engine
    /// thread before that activation's first beat, or from the screen's poll if it has no beats at all - so
    /// the line always comes before what the unit did.
    /// </summary>
    private void EnsureActivation()
    {
        IUnit? unit = _table.Progress?.ActivatingUnit;
        string key = ActivationKey();
        if (unit == null || key == _lastActivation) return;
        _lastActivation = key;
        _open = null;
        var e = new Entry { Kind = Kind.Activation, Key = key, Player = unit.PlayerID, Colour = Plain };
        e.Title.Add(new Piece((PlayerName?.Invoke(unit.PlayerID) ?? "") + ": "));
        e.Title.Add(UnitPiece(unit.ID, unit.Name));
        e.Units.Add(unit.ID);
        Add(e);
    }

    /// <summary>A plain message line (game saved, an error...).</summary>
    public void AddNote(string text, TextColor colour)
    {
        lock (_gate) Add(new Entry { Kind = Kind.Line, Colour = colour, Title = { new Piece(text) } });
    }

    public void OnBeat(PresentationBeat beat)
    {
        try { lock (_gate) Handle(beat); }
        catch { /* the report is information only - it never stops the game */ }
        try { BeatSeen?.Invoke(beat); }
        catch { /* a screen's effect must never stop the game either */ }
    }

    /// <summary>Every beat, as it arrives (engine thread) - for effects on the table (shots, swings).</summary>
    public event Action<PresentationBeat>? BeatSeen;

    private void Add(Entry e)
    {
        e.Id = ++_nextId;
        e.Round = _table.Progress?.RoundCount;
        _entries.Add(e);
        if (_entries.Count > MaxLines) _entries.RemoveAt(0);
        Version++;
    }

    private string ActivationKey()
    {
        IGameProgress? p = _table.Progress;
        return $"{p?.RoundCount}:{p?.ActivatingUnit?.ID}";
    }

    private void CheckRound()
    {
        int? round = _table.Progress?.RoundCount;
        if (round == null || round == _lastRound) return;
        _lastRound = round;
        _open = null;
        Add(new Entry { Kind = Kind.Divider, Colour = Dim, Title = { new Piece($"Round {round}") } });
    }

    private void Handle(PresentationBeat beat)
    {
        CheckRound();
        EnsureActivation();
        string key = ActivationKey();
        Entry? open = _open is { Kind: Kind.Attack } o && o.Key == key ? o : null;

        switch (beat)
        {
            case AttackBeat a:
            {
                IUnit? att = (a.AttackerUnit is { } aid ? UnitById(aid) : null) ?? (a.From.Count > 0 ? UnitAt(a.From[0]) : _table.Progress?.ActivatingUnit);
                IUnit? tgt = (a.TargetUnit is { } tid ? UnitById(tid) : null) ?? (a.To.Count > 0 ? UnitAt(a.To[0]) : null);
                string? fired = a.WeaponName is { Length: > 0 } wn ? (a.WeaponCount > 1 ? $"{a.WeaponCount}x {wn}" : wn) : null;
                if (open != null && Same(open.AttackerUnit, att) && Same(open.TargetUnit, tgt) && open.Melee == a.IsMelee)
                {
                    open.Last = 'a';
                    if (fired != null && open.FiredNamed && !open.Weapons.Contains(fired)) open.Weapons.Add(fired);
                    Version++;
                    break;
                }
                var e = new Entry
                {
                    Kind = Kind.Attack, Key = key, Melee = a.IsMelee, AttackerUnit = att, TargetUnit = tgt,
                    AttackerName = att?.Name ?? "", TargetName = tgt?.Name ?? "", FiredNamed = fired != null, Last = 'a',
                    Attacker = a.AttackerUnit ?? att?.ID, Target = a.TargetUnit ?? tgt?.ID, Player = att?.PlayerID,
                    FromAt = Centroid(a.From), ToAt = Centroid(a.To), Colour = Plain,
                };
                if (fired != null) e.Weapons.Add(fired);
                else if (att != null) e.Weapons.AddRange(WeaponList(att, a.IsMelee));
                if (e.Attacker is { } x) e.Units.Add(x);
                if (e.Target is { } y) e.Units.Add(y);
                Add(e);
                _open = e;
                break;
            }

            case DiceRolledBeat d when d.Category == ERollBeatCategory.Offense:
            {
                if (open != null && open.Last == 'a')
                {
                    open.Hits += d.Successes; open.Attacks += d.Total; open.HasHits = true; open.Last = 'h';
                    open.Rolls.Add(RollOf(d, Side(open.AttackerName, open.AttackerUnit), open.Attacker));
                    Version++;
                    break;
                }
                // A damage roll with no attack (a spell, an ability): its own line.
                var e = new Entry
                {
                    Kind = Kind.Attack, Key = key, Heading = string.IsNullOrEmpty(d.Context) ? d.Label : $"{d.Context} - {d.Label}",
                    Hits = d.Successes, Attacks = d.Total, HasHits = true, Last = 'h', Colour = Plain,
                };
                e.Rolls.Add(RollOf(d, "Attacker", null));
                Add(e);
                _open = e;
                break;
            }

            case DiceRolledBeat d when d.Category == ERollBeatCategory.Defense && open != null:
            {
                // Two save thresholds in one attack send two save rolls; only a roll after the saves that is
                // not itself a save is an "ignore the wound" roll (Regeneration) - see DiceTray.
                bool isSave = open.Last == 'h' || (open.Last == 's' && (d.Context ?? "").EndsWith(" saves", StringComparison.Ordinal));
                if (isSave) { open.Saves += d.Successes; open.SaveTotal += d.Total; open.HasSaves = true; open.Last = 's'; }
                else { open.Ignored += d.Successes; open.Last = 'i'; }
                open.Rolls.Add(RollOf(d, Side(open.TargetName, open.TargetUnit), open.Target));
                Version++;
                break;
            }

            case DiceRolledBeat d:
            {
                string head = string.IsNullOrEmpty(d.Context) ? "" : d.Context + ": ";
                string result = d.ResultSummary ?? $"{Num(d.Successes)}/{Num(d.Total)}";
                var e = new Entry
                {
                    Kind = Kind.Line, Colour = d.Category == ERollBeatCategory.Magic ? new TextColor(140, 200, 255, 255) : Dim,
                    Title = { new Piece($"{head}{d.Label} ({d.SuccessThreshold}+) - {result}") },
                };
                e.Rolls.Add(RollOf(d, d.Context ?? "", null));
                Add(e);
                break;
            }

            case ModelDiedBeat died:
            {
                if (open != null && (open.TargetUnit == null || open.TargetUnit.ID.Equals(died.Unit)))
                {
                    open.Lost++;
                    if (open.TargetUnit == null && open.TargetName.Length == 0) open.TargetName = died.UnitName;
                    Version++;
                    break;
                }
                Entry? last = _entries.Count > 0 ? _entries[^1] : null;
                if (last is { Kind: Kind.Line } && last.LossUnit == died.UnitName)
                {
                    last.LossCount++;
                    last.Title.Clear();
                    last.Title.Add(UnitPiece(died.Unit, died.UnitName));
                    last.Title.Add(new Piece($" loses {last.LossCount} models."));
                    Version++;
                    break;
                }
                var loss = new Entry { Kind = Kind.Line, Colour = Bad, LossUnit = died.UnitName, LossCount = 1, ToAt = died.Position };
                loss.Title.Add(UnitPiece(died.Unit, died.UnitName));
                loss.Title.Add(new Piece(" loses a model."));
                loss.Units.Add(died.Unit);
                Add(loss);
                break;
            }

            case UnitRoutedBeat routed:
                if (open != null && open.TargetUnit != null && open.TargetUnit.ID.Equals(routed.Unit)) { open.Routed = true; Version++; }
                else
                {
                    var rout = new Entry { Kind = Kind.Line, Colour = Bad };
                    rout.Title.Add(UnitPiece(routed.Unit, routed.UnitName));
                    rout.Title.Add(new Piece(" routs!"));
                    rout.Units.Add(routed.Unit);
                    Add(rout);
                }
                break;

            case UnitMovedBeat moved:
            {
                float longest = 0f;
                var from = new List<Spot>();
                var to = new List<Spot>();
                foreach (ModelMove mv in moved.Moves)
                {
                    if (mv.Waypoints.Count == 0) continue;
                    float len = 0f;
                    for (int i = 1; i < mv.Waypoints.Count; i++)
                    {
                        float dx = mv.Waypoints[i].x - mv.Waypoints[i - 1].x, dz = mv.Waypoints[i].z - mv.Waypoints[i - 1].z;
                        len += MathF.Sqrt(dx * dx + dz * dz);
                    }
                    longest = MathF.Max(longest, len);
                    float r = ModelRadius(mv.Model);
                    from.Add(new Spot(mv.Waypoints[0].x, mv.Waypoints[0].z, r));
                    to.Add(new Spot(mv.Waypoints[^1].x, mv.Waypoints[^1].z, r));
                }
                if (longest < 0.05f) break;
                var e = new Entry { Kind = Kind.Move, Colour = Dim, Player = UnitById(moved.Unit)?.PlayerID };
                e.Title.Add(UnitPiece(moved.Unit, moved.UnitName));
                e.Title.Add(new Piece($" moves {longest:0.#}\""));
                e.Units.Add(moved.Unit);
                e.MovedInches = longest;
                e.MovedFrom.AddRange(from);
                e.MovedTo.AddRange(to);
                Add(e);
                break;
            }

            case RollOffBeat r:
                Add(new Entry
                {
                    Kind = Kind.Line, Colour = Dim,
                    Title = { new Piece($"{r.Label}: " + string.Join(", ", r.Entries.Select(x => x.Name + " " + x.Roll +
                        (x.Result == ERollOffResult.Won ? " (wins)" : x.Result == ERollOffResult.TiedForWin ? " (tie)" : "")))) },
                });
                break;

            // Everything the game says out loud: a rule used, an item, a condition, an objective seized.
            case BannerBeat b:
            {
                string text = (b.BannerText ?? "").Trim();
                if (text.Length == 0) break;
                if (text.StartsWith("Round ", StringComparison.OrdinalIgnoreCase) && text.Length <= 10) break;
                Entry? prev = _entries.Count > 0 ? _entries[^1] : null;
                if (prev is { Kind: Kind.Line } && string.Concat(prev.Title.Select(p => p.Text)) == text) break;
                var e = new Entry { Kind = Kind.Line, Colour = b.Color, Loud = b.Tier == EBannerTier.Headline };
                e.Title.AddRange(Split(text, b.ReferencedRules, b.Terms ?? Array.Empty<ReportTerm>()));
                e.Details.AddRange(b.Details ?? Array.Empty<string>());
                Add(e);
                break;
            }
        }
    }

    /// <summary>Cuts a line into plain runs, unit names and the rule names / terms the engine said it mentions.</summary>
    private List<Piece> Split(string text, IReadOnlyList<string> rules, IReadOnlyList<ReportTerm> terms)
    {
        var marks = new List<(int At, int Len, Piece Piece)>();
        void Mark(string word, Func<string, Piece> make)
        {
            if (word.Length < 2) return;
            int at = 0;
            while ((at = text.IndexOf(word, at, StringComparison.Ordinal)) >= 0)
            {
                bool wordStart = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                bool wordEnd = at + word.Length >= text.Length || !char.IsLetterOrDigit(text[at + word.Length]);
                if (wordStart && wordEnd && !marks.Any(m => at < m.At + m.Len && m.At < at + word.Length))
                    marks.Add((at, word.Length, make(text.Substring(at, word.Length))));
                at += word.Length;
            }
        }
        // Longest first, so "Automa Guard" wins over "Guard".
        foreach (IUnit u in _table.Units.Objects.ToList().OrderByDescending(u => u.Name.Length))
            Mark(u.Name, s => UnitPiece(u.ID, s));
        // The rules the engine says it used, then every other rule a unit on the table has (Kai, 2026-11-10:
        // "abilities / special rules highlighted and hoverable").
        var known = rules.Concat(KnownRules()).Distinct().OrderByDescending(r => r.Length);
        foreach (string r in known) Mark(r, s => new Piece(s, PieceKind.Rule, Hover: r));
        foreach (ReportTerm t in terms.OrderByDescending(t => t.Name.Length)) Mark(t.Name, s => new Piece(s, PieceKind.Term, Hover: t.Text));
        var pieces = new List<Piece>();
        int pos = 0;
        foreach (var m in marks.OrderBy(m => m.At))
        {
            if (m.At > pos) pieces.Add(new Piece(text[pos..m.At]));
            pieces.Add(m.Piece);
            pos = m.At + m.Len;
        }
        if (pos < text.Length) pieces.Add(new Piece(text[pos..]));
        return pieces;
    }

    private List<string>? _knownRules;

    private List<string> KnownRules() => _knownRules ??= _table.Units.Objects.ToList()
        .SelectMany(u => u.RuleDefinitions.Select(r => r.RequestedName)
            .Concat(u.Models.SelectMany(m => m.Weapons).SelectMany(w => w.RuleDefinitions.Select(r => r.RequestedName))))
        .Where(n => n.Length >= 3).Distinct().ToList();

    private static Piece UnitPiece(UnitID id, string name) => new(name, PieceKind.Unit, id);

    private static Roll RollOf(DiceRolledBeat d, string who, UnitID? unit)
    {
        var faces = new List<int>();
        if (d.Mode == ERandomnessType.Realistic)
            for (int i = 0; i < d.FaceCounts.Count; i++)
                for (int k = 0; k < (int)MathF.Round(d.FaceCounts[i]); k++) faces.Add(d.SideMin + i);
        faces.Sort((a, b) => b.CompareTo(a));
        string result = d.Mode != ERandomnessType.Realistic
            ? $"about {Num(d.Successes)} of {Num(d.Total)} (average)"
            : d.ResultSummary ?? $"{Num(d.Successes)} of {Num(d.Total)} succeed";
        return new Roll(who, unit, d.Label, d.SuccessThreshold, result, faces,
            (IReadOnlyList<string>?)d.ModifierTags ?? Array.Empty<string>());
    }

    private static string Side(string name, IUnit? unit) => name.Length > 0 ? name : unit?.Name ?? "Unknown";

    private IUnit? UnitById(UnitID id) => _table.Units.Objects.ToList().FirstOrDefault(u => u.ID.Equals(id));

    private float ModelRadius(ModelID id) =>
        _table.Models.Objects.ToList().FirstOrDefault(m => m.ID.Equals(id))?.BaseRadiusInches ?? 0.5f;

    private static Position? Centroid(IReadOnlyList<Position> ps)
    {
        if (ps.Count == 0) return null;
        float x = 0f, z = 0f;
        foreach (Position p in ps) { x += p.x; z += p.z; }
        return new Position(x / ps.Count, z / ps.Count);
    }

    private static bool Same(IUnit? a, IUnit? b) => a == null || b == null ? a == b : a.ID.Equals(b.ID);

    private IUnit? UnitAt(Position p)
    {
        IUnit? best = null;
        float bestD = float.MaxValue;
        foreach (IUnit u in _table.Units.Objects.ToList())
            foreach (IModel m in u.Models)
            {
                float dx = m.Position.x - p.x, dz = m.Position.z - p.z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = u; }
            }
        return best;
    }

    private static IEnumerable<string> WeaponList(IUnit u, bool melee) =>
        u.Models.Where(m => m.GetIsAlive()).SelectMany(m => m.Weapons)
            .Where(w => melee ? w.RangeInches <= 0f : w.RangeInches > 0f)
            .GroupBy(w => w.Name)
            .Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key);

    private static string Num(float v) => v.ToString("0.#");
    private static string Plural(float n, string one, string many) => Math.Abs(n - 1f) < 0.01f ? one : many;

    // ================================================================== a line while it is being built

    private sealed class Entry
    {
        public int Id;
        public int? Round;
        public Kind Kind;
        public string Key = "";
        public TextColor Colour;
        public bool Loud;
        public PlayerID? Player;
        public readonly List<Piece> Title = new();
        public readonly List<UnitID> Units = new();
        public readonly List<Spot> MovedFrom = new(), MovedTo = new();
        public readonly List<Roll> Rolls = new();
        public readonly List<string> Details = new();
        public Position? FromAt, ToAt;
        // attacks
        public bool Melee, FiredNamed, HasHits, HasSaves, Routed;
        public IUnit? AttackerUnit, TargetUnit;
        public UnitID? Attacker, Target;
        public string AttackerName = "", TargetName = "", Heading = "";
        public readonly List<string> Weapons = new();
        public float Hits, Attacks, Saves, SaveTotal, Ignored;
        public int Lost;
        public char Last;
        // loss lines that merge
        public string? LossUnit;
        public int LossCount;
        public float MovedInches;

        public Line Freeze()
        {
            IReadOnlyList<Piece> title = Title.ToList();
            string body = "";
            if (Kind == Kind.Attack)
            {
                var t = new List<Piece>();
                if (Heading.Length > 0) t.Add(new Piece(Heading));
                else
                {
                    t.Add(Attacker is { } a ? UnitPiece(a, AttackerName.Length > 0 ? AttackerName : "A unit") : new Piece("A unit"));
                    t.Add(new Piece(Melee ? " fights " : " shoots "));
                    t.Add(Target is { } g ? UnitPiece(g, TargetName.Length > 0 ? TargetName : "the target") : new Piece("the target"));
                }
                if (Weapons.Count > 0) t.Add(new Piece(" with " + string.Join(", ", Weapons)));
                title = t;
                body = AttackSummary();
            }
            Outcome? outcome = Kind == Kind.Attack && Heading.Length == 0
                ? new Outcome(Melee, Hits, Attacks, HasSaves ? MathF.Max(0f, SaveTotal - Saves - Ignored) : 0f, Lost, Routed, string.Join(", ", Weapons))
                : null;
            return new Line(Id, Kind, Round, title, body, Colour, Loud, Attacker, Target, Player, Units.ToList(), FromAt, ToAt,
                MovedFrom.ToList(), MovedTo.ToList(), Rolls.ToList(), Details.ToList(), outcome, MovedInches,
                TargetName.Length > 0 ? TargetName : null);
        }

        private string AttackSummary()
        {
            string target = TargetName.Length > 0 ? TargetName : "the target";
            var res = new System.Text.StringBuilder();
            if (HasHits) res.Append($"{Num(Hits)}/{Num(Attacks)} hit. ");
            if (HasSaves)
            {
                float wounds = MathF.Max(0f, SaveTotal - Saves - Ignored);
                res.Append($"{target} {(TargetName.EndsWith('s') ? "save" : "saves")} {Num(Saves)}/{Num(SaveTotal)}");
                if (Ignored > 0f) res.Append($", ignores {Num(Ignored)}");
                res.Append(wounds > 0f ? $", taking {Num(wounds)} {Plural(wounds, "wound", "wounds")}" : ", taking no wounds");
                if (Lost > 0) res.Append($" and losing {Lost} {Plural(Lost, "model", "models")}");
                res.Append('.');
            }
            else if (HasHits && Hits <= 0f) res.Append("No damage.");
            else if (Lost > 0) res.Append($"{target} loses {Lost} {Plural(Lost, "model", "models")}.");
            if (Routed) res.Append($" {target} routs!");
            return res.ToString().Trim();
        }
    }
}
