using System;
using System.Collections.Generic;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// A persistent top-center status strip over the table: the current round ("ROUND 2 / 4"), a live
/// objective scoreboard (one player-colored pip + controlled-objective count per player), and -- when
/// another player holds up the game (#322) -- a smaller "Waiting on Bob: Place Unit Models" line
/// beneath, one per outstanding non-local task. Answers the three questions the log otherwise
/// buries -- what round is it, who's ahead, and why is nothing happening.
///
/// Drawn with Raylib primitives in the same pass as the other table overlays (banners, dice), so it sits
/// over the board but under the ImGui panels and never captures the mouse. Purely presentational: the
/// renderer gathers the round from <c>ITableState.Progress</c> and the counts from the objectives.
/// </summary>
public static class StatusHudOverlay
{
    private const int   FontSize   = 24;
    private const int   Gap        = 16;
    private const int   PipGap     = 7;
    private const int   PipRadius  = 9;
    private const int   TopMargin  = 12;

    // #322 "Waiting on" lines: smaller than the main strip, capped so a many-player pileup can't
    // curtain the board.
    private const int   WaitFontSize = 20;
    private const int   WaitLineGap  = 5;
    private const int   MaxWaitLines = 3;

    private static readonly Color Label   = new(235, 235, 235, 255);
    private static readonly Color RoundClr = new(105, 170, 245, 255); // blue, matches the UI accent
    private static readonly Color SepClr   = new(90, 96, 106, 200);
    private static readonly Color WaitDim  = new(190, 196, 205, 235);

    /// <summary>
    /// Renders the strip centered across <paramref name="areaWidth"/> (the full table width). Pass
    /// <paramref name="round"/> null before the main phase (only the scoreboard shows).
    /// <paramref name="scores"/> is one entry per player, in a stable order.
    /// <paramref name="waiting"/> is one entry per outstanding non-local task, oldest first (may be
    /// empty; during deployment it is often the only thing on screen).
    /// Returns the strip's left edge in pixels (where the round / score text starts), or null when no
    /// round or score is shown - the measuring-tool button docks against it.
    /// </summary>
    /// <param name="segments">Extra text items drawn after the round, each behind a separator, IN PLACE of
    /// the objective pips (Horde Mode: "WAVE 2 / 4", "120 PTS"). Null for the standard strip.</param>
    public static int? Draw(int areaWidth, int? round, int totalRounds,
        IReadOnlyList<(Color color, int count)> scores,
        IReadOnlyList<(Color color, string playerName, string taskName)> waiting,
        IReadOnlyList<(string text, Color color)>? segments = null,
        IReadOnlyList<(string text, Color color)>? subline = null)
    {
        // Second line under the strip: whose turn it is and units left to activate per player.
        int sublineH = 0;
        if (subline != null && subline.Count > 0)
        {
            DrawSubline(areaWidth, subline, TopMargin + FontSize + 8);
            sublineH = WaitFontSize + WaitLineGap;
        }
        _waitOffset = sublineH;

        if (segments != null && segments.Count > 0)
            return DrawSegments(areaWidth, round, totalRounds, segments, waiting);

        bool hasRound  = round.HasValue;
        bool hasScores = scores.Count > 0;
        if (!hasRound && !hasScores && waiting.Count == 0) return null;

        string roundText = !hasRound ? "" : totalRounds >= 999 ? $"ROUND {round}" : $"ROUND {round} / {totalRounds}";

        int roundW  = hasRound ? Raylib.MeasureText(roundText, FontSize) : 0;
        int sepW    = (hasRound && hasScores) ? Gap * 2 + 1 : 0;

        int scoresW = 0;
        for (int i = 0; i < scores.Count; i++)
        {
            scoresW += PipRadius * 2 + PipGap + Raylib.MeasureText(scores[i].count.ToString(), FontSize);
            if (i < scores.Count - 1) scoresW += Gap;
        }

        // A drop shadow alone was not enough: over pale terrain or a light unit the strip washed out, and
        // this is the one line that has to be readable at a glance. A translucent plate behind it fixes
        // that without hiding the board - it is only as wide as the text. (This overlay is Raylib-drawn,
        // not an ImGui window, so it still never captures the mouse.)
        int innerW  = roundW + sepW + scoresW;
        int x       = (areaWidth - innerW) / 2;
        int stripLeft = x;
        int textY   = TopMargin;
        int centerY = TopMargin + FontSize / 2;
        DrawPlate(x, textY, innerW);

        if (hasRound)
        {
            DrawTextShadow(roundText, x, textY, RoundClr);
            x += roundW;
        }

        if (hasRound && hasScores)
        {
            x += Gap;
            Raylib.DrawLine(x, textY + 3, x, textY + FontSize - 3, SepClr);
            x += Gap;
        }

        for (int i = 0; i < scores.Count; i++)
        {
            var (color, count) = scores[i];
            int pipCx = x + PipRadius;
            Raylib.DrawCircle(pipCx, centerY + 1, PipRadius, Shadow);   // pip shadow
            Raylib.DrawCircle(pipCx, centerY, PipRadius, color);
            Raylib.DrawCircleLines(pipCx, centerY, PipRadius, Color.Black);
            x += PipRadius * 2 + PipGap;

            string cnt = count.ToString();
            DrawTextShadow(cnt, x, textY, Label);
            x += Raylib.MeasureText(cnt, FontSize) + Gap;
        }

        DrawWaitingLines(areaWidth, waiting, startY: ((hasRound || hasScores)
            ? TopMargin + FontSize + 8
            : TopMargin) + _waitOffset);

        return (hasRound || hasScores) ? stripLeft : null;
    }

    // Round, then each text segment, all separated by the same thin divider as the standard strip.
    private static int? DrawSegments(int areaWidth, int? round, int totalRounds,
        IReadOnlyList<(string text, Color color)> segments,
        IReadOnlyList<(Color color, string playerName, string taskName)> waiting)
    {
        var items = new List<(string text, Color color)>();
        if (round.HasValue) items.Add((totalRounds >= 999 ? $"ROUND {round}" : $"ROUND {round} / {totalRounds}", RoundClr));
        items.AddRange(segments);

        int sepW = Gap * 2 + 1;
        int innerW = 0;
        for (int i = 0; i < items.Count; i++)
        {
            innerW += Raylib.MeasureText(items[i].text, FontSize);
            if (i < items.Count - 1) innerW += sepW;
        }

        int x = (areaWidth - innerW) / 2;
        int stripLeft = x;
        int textY = TopMargin;
        DrawPlate(x, textY, innerW);
        for (int i = 0; i < items.Count; i++)
        {
            DrawTextShadow(items[i].text, x, textY, items[i].color);
            x += Raylib.MeasureText(items[i].text, FontSize);
            if (i < items.Count - 1)
            {
                x += Gap;
                Raylib.DrawLine(x, textY + 3, x, textY + FontSize - 3, SepClr);
                x += Gap + 1;
            }
        }

        DrawWaitingLines(areaWidth, waiting, startY: TopMargin + FontSize + 8 + _waitOffset);
        return stripLeft;
    }

    /// <summary>Top of the status strip's text line, for widgets docked beside it.</summary>
    private static int _waitOffset;

    /// <summary>One centered line of coloured text parts at <paramref name="y"/>, in the smaller font.</summary>
    private static void DrawSubline(int areaWidth, IReadOnlyList<(string text, Color color)> parts, int y)
    {
        int total = 0;
        foreach (var (text, _) in parts) total += Raylib.MeasureText(text, WaitFontSize);
        int x = (areaWidth - total) / 2;
        foreach (var (text, color) in parts)
        {
            Raylib.DrawText(text, x + 1, y + 1, WaitFontSize, Shadow);
            Raylib.DrawText(text, x, y, WaitFontSize, color);
            x += Raylib.MeasureText(text, WaitFontSize);
        }
    }

    public const int StripTop = TopMargin;

    /// <summary>Height of the status strip's text line.</summary>
    public const int StripHeight = FontSize;

    // One centered line per non-local outstanding task: dim "Waiting on ", the player's name in their
    // table color, dim ": <task>". Lines past the cap collapse into "+N more".
    private static void DrawWaitingLines(int areaWidth,
        IReadOnlyList<(Color color, string playerName, string taskName)> waiting, int startY)
    {
        int y = startY;
        int shown = Math.Min(waiting.Count, MaxWaitLines);
        for (int i = 0; i < shown; i++)
        {
            var (color, playerName, taskName) = waiting[i];
            string prefix = "Waiting on ";
            string rest   = $": {taskName}";

            // #399: the line is drawn in three runs so the player's name can carry their table colour,
            // and each run has to start where the previous run's LAST GLYPH ends - plus the gap raylib
            // would have put between two glyphs of one string. MeasureText returns
            // sum(advances) + (count - 1) * spacing: no trailing spacing, because a string has nothing
            // after its last glyph. Laying the runs out at raw measured widths therefore closed one
            // `spacing` at every seam, which is why the colon sat hard against the name.
            var (prefixX, nameX, restX) = WaitLineRuns(areaWidth,
                Raylib.MeasureText(prefix, WaitFontSize),
                Raylib.MeasureText(playerName, WaitFontSize),
                Raylib.MeasureText(rest, WaitFontSize),
                GlyphSpacing(WaitFontSize));

            DrawTextShadow(prefix, prefixX, y, WaitDim, WaitFontSize);
            DrawTextShadow(playerName, nameX, y, color, WaitFontSize);
            DrawTextShadow(rest, restX, y, WaitDim, WaitFontSize);
            y += WaitFontSize + WaitLineGap;
        }

        if (waiting.Count > shown)
        {
            string more = $"+{waiting.Count - shown} more";
            DrawTextShadow(more, (areaWidth - Raylib.MeasureText(more, WaitFontSize)) / 2, y,
                WaitDim, WaitFontSize);
        }
    }

    /// <summary>
    /// #399 - where each of the "Waiting on " / name / ": task" runs starts, centred as one line.
    /// The line is painted in three pieces so the name can carry the player's table colour, and each
    /// piece has to begin one <paramref name="glyphSpacing"/> past where the last one ended:
    /// <see cref="Raylib.MeasureText"/> returns sum(advances) + (count - 1) * spacing, with no
    /// trailing gap, because a string has nothing after its last glyph. Laying the runs out at raw
    /// measured widths therefore closed one gap at every seam - which is why the colon sat hard
    /// against the player's name. Two seams, so the centred whole is two spacings wider than the sum.
    ///
    /// <para>Pure integer layout, kept separate from the drawing so it can be pinned by a test:
    /// <see cref="Raylib.MeasureText"/> needs a loaded font and answers 0 without a window.</para>
    /// </summary>
    internal static (int prefixX, int nameX, int restX) WaitLineRuns(
        int areaWidth, int prefixW, int nameW, int restW, int glyphSpacing)
    {
        int totalW = prefixW + glyphSpacing + nameW + glyphSpacing + restW;
        int prefixX = (areaWidth - totalW) / 2;
        int nameX = prefixX + prefixW + glyphSpacing;
        return (prefixX, nameX, nameX + nameW + glyphSpacing);
    }

    /// <summary>
    /// The character spacing <c>DrawText</c>/<c>MeasureText</c> use with the default font: raylib
    /// derives it as fontSize / defaultFontSize (10), integer division, with the size floored at 10.
    /// Mirrored rather than guessed so the seam matches the glyph gaps on either side of it exactly.
    /// </summary>
    internal static int GlyphSpacing(int fontSize) => Math.Max(10, fontSize) / 10;

    private static readonly Color Shadow = new(0, 0, 0, 190);
    private static readonly Color PlateFill = new(12, 13, 16, 190);
    private static readonly Color PlateEdge = new(90, 96, 106, 140);

    /// <summary>The translucent plate the strip sits on, so its text reads over any terrain.</summary>
    private static void DrawPlate(int x, int y, int innerW)
    {
        if (innerW <= 0) return;
        const int padX = 14, padY = 6;
        var rect = new Rectangle(x - padX, y - padY, innerW + padX * 2, FontSize + padY * 2);
        Raylib.DrawRectangleRounded(rect, 0.35f, 8, PlateFill);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.35f, 8, 1.5f, PlateEdge);
    }

    private static void DrawTextShadow(string text, int x, int y, Color color, int size = FontSize)
    {
        Raylib.DrawText(text, x + 1, y + 1, size, Shadow);
        Raylib.DrawText(text, x, y, size, color);
    }
}
