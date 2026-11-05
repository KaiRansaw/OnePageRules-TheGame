using System;
using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Fast-Play "this model is the unit's leader" marker (#456 follow-up, Kai 2026-11-05: the leader "isnt
/// made clear", and the star he saw was the joined-Hero marker, not the leader).
/// <para>
/// Two rank chevrons drawn INSIDE the base, white with a thick black outline. Kai's brief: a colour that
/// won't blend in with any player colour, and players will be able to pick any of the palette. Every hue
/// is somebody's base colour (orange, purple, green, yellow, red, blue, teal, pink), so no single hue can
/// be guaranteed to stand out - black and white together can: the black edge carries it on the light
/// colours (yellow, pink) and the white body on the dark ones (purple, blue). Same reasoning the hero star
/// used. Inside the base rather than a ring round it, because rings around bases are already the
/// selection, hover and range effects (and gold was rejected for the star for blending with those).
/// A chevron rather than a second star so leader and Hero read as different things at a glance.
/// </para>
/// </summary>
public static class LeaderMarkerRenderer
{
    /// <summary>True if <paramref name="model"/> is the Fast-Play leader of <paramref name="unit"/>.</summary>
    public static bool IsLeaderModel(IUnit unit, IModel model) =>
        unit.LeaderModelId is { } leaderId && model.ID == leaderId;

    private const float RadiusFraction = 0.55f;
    private const float MinHalfWidthPx = 4f;
    private const float MaxHalfWidthPx = 13f;

    /// <summary>Half the chevron's width in pixels for a base of <paramref name="baseRadiusPx"/>.</summary>
    public static float HalfWidthPx(float baseRadiusPx) =>
        Math.Clamp(baseRadiusPx * RadiusFraction, MinHalfWidthPx, MaxHalfWidthPx);

    /// <summary>
    /// The two chevrons as polylines (left end, apex, right end), upper one first, centred on (cx, cy).
    /// Apex points up (screen -y). Pure geometry - unit-tested.
    /// </summary>
    public static Vector2[][] Chevrons(float cx, float cy, float halfWidth)
    {
        float rise = halfWidth * 0.6f;      // how far the apex sits above the ends
        float gap  = halfWidth * 0.55f;     // vertical spacing between the two stripes
        float top  = cy - gap * 0.5f;       // the pair is centred on the base
        var result = new Vector2[2][];
        for (int i = 0; i < 2; i++)
        {
            float y = top + i * gap;
            result[i] = new[]
            {
                new Vector2(cx - halfWidth, y + rise * 0.5f),
                new Vector2(cx,             y - rise * 0.5f),
                new Vector2(cx + halfWidth, y + rise * 0.5f),
            };
        }
        return result;
    }

    /// <summary>
    /// Draws the chevrons, black outline under a white stripe, centred at pixel (cx, cy) and sized to the
    /// base. <paramref name="alpha"/> fades it with the model.
    /// </summary>
    public static void DrawRaylib(float cx, float cy, float baseRadiusPx, byte alpha)
    {
        float half = HalfWidthPx(baseRadiusPx);
        float thick = MathF.Max(2f, half * 0.32f);
        var white = new Color((byte)255, (byte)255, (byte)255, alpha);
        var black = new Color((byte)10, (byte)10, (byte)10, alpha);
        foreach (Vector2[] c in Chevrons(cx, cy, half))
        {
            Raylib.DrawLineEx(c[0], c[1], thick + 3f, black);
            Raylib.DrawLineEx(c[1], c[2], thick + 3f, black);
            Raylib.DrawCircleV(c[1], (thick + 3f) * 0.5f, black);
        }
        foreach (Vector2[] c in Chevrons(cx, cy, half))
        {
            Raylib.DrawLineEx(c[0], c[1], thick, white);
            Raylib.DrawLineEx(c[1], c[2], thick, white);
            Raylib.DrawCircleV(c[1], thick * 0.5f, white);
        }
    }
}
