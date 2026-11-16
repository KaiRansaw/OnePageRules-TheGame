using System.Globalization;

namespace FdgRaylib.Models3D;

/// <summary>
/// The colour painter (Kai, 2026-11-15: "individual models, and I'd like the opponents to see them" - level 1, colour
/// swaps). A model's paint is a short list of swaps - every part of the model painted in colour A is repainted in
/// colour B, keeping its shading - stored as text on the model ("RRGGBB&gt;RRGGBB;RRGGBB&gt;RRGGBB"), so it saves with
/// the army file and travels to the other players with it.
/// </summary>
public readonly record struct ColourSwap(int From, int To)
{
    public string FromHex => From.ToString("X6", CultureInfo.InvariantCulture);
    public string ToHex => To.ToString("X6", CultureInfo.InvariantCulture);
}

public static class ModelPaint
{
    /// <summary>At most this many swaps per model - enough for armour, trim, cloth and skin.</summary>
    public const int MaxSwaps = 4;

    public static List<ColourSwap> Parse(string? paint)
    {
        var swaps = new List<ColourSwap>();
        if (string.IsNullOrWhiteSpace(paint)) return swaps;
        foreach (string part in paint.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] ab = part.Split('>');
            if (ab.Length != 2) continue;
            if (int.TryParse(ab[0].Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int from)
                && int.TryParse(ab[1].Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int to))
                swaps.Add(new ColourSwap(from & 0xFFFFFF, to & 0xFFFFFF));
            if (swaps.Count == MaxSwaps) break;
        }
        return swaps;
    }

    /// <summary>The stored text, or null for no swaps.</summary>
    public static string? Format(IEnumerable<ColourSwap> swaps)
    {
        var list = swaps.Take(MaxSwaps).ToList();
        return list.Count == 0 ? null : string.Join(";", list.Select(s => $"{s.FromHex}>{s.ToHex}"));
    }

    /// <summary>Adds (or replaces) the swap for colour <paramref name="from"/>. A full list drops its oldest swap.</summary>
    public static string? With(string? paint, int from, int to)
    {
        var swaps = Parse(paint).Where(s => s.From != from).ToList();
        if (swaps.Count >= MaxSwaps) swaps.RemoveAt(0);
        swaps.Add(new ColourSwap(from & 0xFFFFFF, to & 0xFFFFFF));
        return Format(swaps);
    }

    public static string? Without(string? paint, int from) => Format(Parse(paint).Where(s => s.From != from));
}
