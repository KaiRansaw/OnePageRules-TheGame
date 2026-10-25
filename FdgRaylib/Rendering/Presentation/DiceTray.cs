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
                IUnit? att = a.From.Count > 0 ? UnitAt(a.From[0]) : PopupAnchor.Progress?.ActivatingUnit;
                IUnit? tgt = a.To.Count > 0 ? UnitAt(a.To[0]) : null;
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
                    last.LossCount++;
                    last.Text = $"{died.UnitName} loses {last.LossCount} models.";
                    break;
                }
                Add(new Entry { Kind = Kind.Line, Text = $"{died.UnitName} loses a model.", Color = Bad,
                    LossUnit = died.UnitName, LossCount = 1 });
                break;
            }

            case UnitRoutedBeat routed:
                if (open != null && open.Target != null && open.Target.ID.Equals(routed.Unit)) open.Routed = true;
                else Add(new Entry { Kind = Kind.Line, Text = $"{routed.UnitName} routs!", Color = Bad });
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
        Rich(new() { (e.Text, e.Color) }, width - bar - gap);
        float bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
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
        bool hovered = ImGui.IsMouseHoveringRect(start, end);
        dl.AddRectFilled(start, end, U(hovered ? new Vector4(0.15f, 0.14f, 0.13f, 1f) : new Vector4(0.11f, 0.10f, 0.09f, 1f)), 5f);
        dl.AddRectFilled(start, new Vector2(start.X + 4f, end.Y), U(attCol), 5f, ImDrawFlags.RoundCornersLeft);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y));
        ImGui.Dummy(new Vector2(width, 0f));

        if (hovered && e.Rolls.Count > 0)
        {
            ImGui.BeginTooltip();
            // Both sides of the exchange, in order and named: your attack dice, then the dice the enemy
            // threw to save against them. Each block is headed by whoever rolled it, in their own colour.
            foreach (var (d, who, col) in e.Rolls) DrawRollDetail(d, who, col);
            ImGui.EndTooltip();
        }
    }

    /// <summary>One roll in the hover card: who rolled it, then the dice - passes bright, fails greyed.</summary>
    private static void DrawRollDetail(DiceRolledBeat d, string who, Vector4 whoColor)
    {
        // UiText, not TextColored: a unit's name is data and may carry a '%' (#398).
        FdgRaylib.Rendering.UiText.Colored(whoColor, who);
        ImGui.TextUnformatted($"{d.Label} ({d.SuccessThreshold}+)");
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
    private static void Rich(List<(string text, Vector4 color)> pieces, float width)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 o = ImGui.GetCursorScreenPos();
        float lh = ImGui.GetTextLineHeight();
        float space = ImGui.CalcTextSize(" ").X;
        float x = 0f, y = 0f;
        foreach (var (text, color) in pieces)
        {
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
                x += ww;
            }
        }
        ImGui.Dummy(new Vector2(width, y + lh));
    }
}
