using System;
using System.Collections.Generic;
using System.Linq;

namespace FdgRaylib.Rendering;

/// <summary>
/// The classic-RTS player colour palette (#221): eight named options a player can pick from the lobby
/// dropdown, plus the deterministic resolution from per-slot picks to effective colours. Pure data + logic
/// (unit-tested); the lobby draws the dropdown and <see cref="GameGuiWiring"/> consumes the resolution at
/// launch.
///
/// Picks sync through the lobby protocol (<c>LobbyPlayerInfoSummary.ColorIndex</c>, engine-side): the host
/// applies and rebroadcasts them, so every machine resolves the same effective colours from the same synced
/// roster. The engine treats the index as an opaque int - this palette is the only decoder.
/// </summary>
/// <summary>Drawing-free copy point: both front ends read the seat colours from here (Raylib wraps it in
/// PlayerColorOptions, Godot converts to its own Color).</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

public static class PlayerColorCodes
{
    /// <summary>
    /// Order matters twice: it is the dropdown order, and unchosen slots consume these in order - so the
    /// first four preserve the pre-picker defaults (P1 orange, P2 purple, P3 green, P4 yellow).
    /// </summary>
    public static readonly (string Name, Rgb Color)[] Options =
    {
        ("Orange", new Rgb(255, 161, 0)),                     // (255, 161, 0)
        ("Purple", new Rgb(150, 70, 200)),     // the old GameGuiWiring.TeamPurple
        ("Green",  new Rgb(0, 228, 48)),                      // (0, 228, 48)
        ("Yellow", new Rgb(253, 249, 0)),                     // (253, 249, 0)
        ("Red",    new Rgb(230, 41, 55)),                        // (230, 41, 55)
        ("Blue",   new Rgb(0, 121, 241)),                       // (0, 121, 241)
        ("Teal",   new Rgb(0, 180, 170)),      // no Raylib built-in
        ("Pink",   new Rgb(255, 109, 194)),                       // (255, 109, 194)
    };

    public static int Count => Options.Length;

    // ── Any colour from the wheel (Kai, 2026-11-05: "a button that says Choose Color and when you click it,
    // a small popup appears with a color wheel") ─────────────────────────────────────────────────────────
    //
    // A pick is still ONE int, so it rides the existing lobby sync, the save's crew list and the host's
    // first-committed-wins check unchanged - no protocol bump. 0..Count-1 is a palette entry (kept so old
    // saves and the defaults still mean what they meant); a wheel colour is CustomFlag | 0xRRGGBB. -1 is
    // "no pick", as before. The engine never decodes it; this class is the only decoder.

    /// <summary>Marks a pick as a free RGB colour rather than a palette index.</summary>
    public const int CustomFlag = 0x01000000;

    /// <summary>The pick code for an arbitrary colour.</summary>
    public static int EncodeCustom(byte r, byte g, byte b) => CustomFlag | (r << 16) | (g << 8) | b;

    /// <summary>True for a palette index in range or a well-formed wheel colour.</summary>
    public static bool IsValidCode(int code) =>
        (code >= 0 && code < Options.Length) || (code & ~0x00FFFFFF) == CustomFlag;

    /// <summary>The colour a pick code stands for. An invalid code falls back to the first palette colour.</summary>
    public static Rgb ColorFor(int code)
    {
        if (code >= 0 && code < Options.Length) return Options[code].Color;
        if ((code & ~0x00FFFFFF) == CustomFlag)
            return new Rgb((byte)((code >> 16) & 0xFF), (byte)((code >> 8) & 0xFF), (byte)(code & 0xFF));
        return Options[0].Color;
    }

    /// <summary>
    /// How far apart two colours look, 0 (same) to about 765. The "redmean" weighting: plain RGB distance
    /// rates greens as further apart than the eye does and blues as closer, which is exactly the pair a
    /// player would be fooled by. Cheap and good enough to stop two armies looking alike.
    /// </summary>
    public static float Difference(Rgb a, Rgb b)
    {
        float rMean = (a.R + b.R) / 2f;
        float dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return MathF.Sqrt((2f + rMean / 256f) * dr * dr + 4f * dg * dg + (2f + (255f - rMean) / 256f) * db * db);
    }

    /// <summary>
    /// Below this, two armies' bases are too alike to tell apart at a glance. Tuned so the palette's own
    /// neighbours stay legal with room to spare: the nearest pair, orange/yellow, is about 176.
    /// </summary>
    public const float MinDifference = 110f;

    /// <summary>
    /// The row whose colour is too close to <paramref name="candidate"/> for <paramref name="rowIdx"/> to
    /// take it, or -1 when it is free. Compares against every OTHER row's effective colour - explicit pick
    /// or default alike, the same "defaults are reserved" rule the palette dropdown had.
    /// </summary>
    public static int ClashingRow(int[] effectiveCodes, int rowIdx, Rgb candidate)
    {
        for (int k = 0; k < effectiveCodes.Length; k++)
            if (k != rowIdx && Difference(ColorFor(effectiveCodes[k]), candidate) < MinDifference) return k;
        return -1;
    }

    /// <summary>
    /// True when option <paramref name="optionIdx"/> is unavailable to row <paramref name="rowIdx"/>
    /// because it is some OTHER row's effective colour - explicit pick or assigned default alike. The
    /// dropdown disables these, so no pick can ever change another player's colour (defaults are reserved,
    /// not stealable). A row's own current colour is never "taken" from itself.
    /// </summary>
    public static bool IsTakenByAnother(int[] effectiveIndices, int rowIdx, int optionIdx)
    {
        for (int k = 0; k < effectiveIndices.Length; k++)
            if (k != rowIdx && effectiveIndices[k] == optionIdx) return true;
        return false;
    }

    /// <summary>
    /// Resolves each slot's effective option index. Explicit picks (non-null, in range) always win.
    /// Unchosen slots are SLOT-ANCHORED: slot i prefers Options[i] (P1 orange, P2 purple, ...) and keeps
    /// it as long as it's free - so one player picking a free colour never shifts anyone else's default.
    /// Only when a slot's own colour is actually taken does it fall to the first free option in palette
    /// order (deterministic bump - covers the stale-pick races the host resolves first-committed-wins,
    /// since the host can't reserve app-side defaults). The dropdown (via <see cref="IsTakenByAnother"/>)
    /// never offers a colour that is currently anyone else's, so in the normal flow picks and defaults
    /// stay disjoint. Slots beyond <see cref="Count"/> wrap by slot index.
    /// </summary>
    public static int[] ResolveIndices(IReadOnlyList<int?> chosenPerSlot)
    {
        int n = chosenPerSlot.Count;
        var result = new int[n];
        var taken = new HashSet<int>();
        var customPicks = new List<Rgb>();
        for (int i = 0; i < n; i++)
            if (chosenPerSlot[i] is int c && IsValidCode(c))
            {
                if (c < Options.Length) taken.Add(c);
                else customPicks.Add(ColorFor(c));
            }
        // A wheel colour reserves every palette default it looks like, so an unpicked seat never lands on
        // a near-twin of somebody's custom colour.
        for (int o = 0; o < Options.Length; o++)
            if (customPicks.Any(cp => Difference(cp, Options[o].Color) < MinDifference)) taken.Add(o);

        for (int i = 0; i < n; i++)
        {
            if (chosenPerSlot[i] is int chosen && IsValidCode(chosen))
            {
                result[i] = chosen;
                continue;
            }

            int preferred = i % Options.Length; // the slot's own palette colour
            if (!taken.Contains(preferred))
            {
                result[i] = preferred;
                taken.Add(preferred);
                continue;
            }

            int next = 0;
            while (next < Options.Length && taken.Contains(next)) next++;
            if (next < Options.Length)
            {
                result[i] = next;
                taken.Add(next);
            }
            else
            {
                result[i] = preferred; // more slots than colours: wrap, duplicates unavoidable
            }
        }
        return result;
    }
}
