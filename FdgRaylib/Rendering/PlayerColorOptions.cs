using System;
using System.Collections.Generic;
using System.Linq;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The seat colours as Raylib colours. The palette and every rule (custom wheel codes, clash distance,
/// default resolution) live in <see cref="PlayerColorCodes"/> in FdgApp, which the Godot build reads too;
/// this is only the conversion.
/// </summary>
public static class PlayerColorOptions
{
    public static readonly (string Name, Color Color)[] Options =
        PlayerColorCodes.Options.Select(o => (o.Name, ToColor(o.Color))).ToArray();

    public static int Count => Options.Length;
    public const int CustomFlag = PlayerColorCodes.CustomFlag;
    public const float MinDifference = PlayerColorCodes.MinDifference;

    private static Color ToColor(Rgb c) => new(c.R, c.G, c.B, (byte)255);
    private static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    public static int EncodeCustom(byte r, byte g, byte b) => PlayerColorCodes.EncodeCustom(r, g, b);
    public static bool IsValidCode(int code) => PlayerColorCodes.IsValidCode(code);
    public static Color ColorFor(int code) => ToColor(PlayerColorCodes.ColorFor(code));
    public static float Difference(Color a, Color b) => PlayerColorCodes.Difference(ToRgb(a), ToRgb(b));
    public static int ClashingRow(int[] effectiveCodes, int rowIdx, Color candidate) =>
        PlayerColorCodes.ClashingRow(effectiveCodes, rowIdx, ToRgb(candidate));
    public static bool IsTakenByAnother(int[] effectiveIndices, int rowIdx, int optionIdx) =>
        PlayerColorCodes.IsTakenByAnother(effectiveIndices, rowIdx, optionIdx);
    public static int[] ResolveIndices(IReadOnlyList<int?> chosenPerSlot) => PlayerColorCodes.ResolveIndices(chosenPerSlot);
}
