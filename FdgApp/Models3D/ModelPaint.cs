using System.Globalization;

namespace FdgRaylib.Models3D;

/// <summary>
/// The colour painter (Kai, 2026-11-15: "individual models, and I'd like the opponents to see them" - level 1, colour
/// swaps). A model's paint is a short list of swaps - every part of the model painted in colour A is repainted in
/// colour B, keeping its shading - stored as text on the model ("RRGGBB&gt;RRGGBB;RRGGBB&gt;RRGGBB"), so it saves with
/// the army file and travels to the other players with it.
///
/// <para><b>Areas</b> (2026-11-15, the colouring book for printable models): an imported STL is split into numbered
/// areas (<see cref="MeshAreas"/>), and each can be given a colour - "A3=RRGGBB" in the same text. Swaps and areas
/// live side by side and every edit keeps the other kind.</para>
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
            if (part.Contains('=')) continue; // an area colour, read by Areas
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
        return Compose(swaps, Areas(paint));
    }

    public static string? Without(string? paint, int from) => Compose(Parse(paint).Where(s => s.From != from), Areas(paint));

    /// <summary>Area number (1 = the biggest) -> colour, for an imported printable model.</summary>
    public static SortedDictionary<int, int> Areas(string? paint)
    {
        var areas = new SortedDictionary<int, int>();
        if (string.IsNullOrWhiteSpace(paint)) return areas;
        foreach (string part in paint.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=');
            if (kv.Length != 2 || !kv[0].Trim().StartsWith('A')) continue;
            if (int.TryParse(kv[0].Trim()[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int area) && area > 0
                && int.TryParse(kv[1].Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                areas[area] = rgb & 0xFFFFFF;
        }
        return areas;
    }

    /// <summary>Colours area <paramref name="area"/> (or, with null, puts it back to plain).</summary>
    public static string? WithArea(string? paint, int area, int? rgb)
    {
        var areas = Areas(paint);
        if (rgb is int c) areas[area] = c & 0xFFFFFF;
        else areas.Remove(area);
        return Compose(Parse(paint), areas);
    }

    private static string? Compose(IEnumerable<ColourSwap> swaps, IDictionary<int, int> areas)
    {
        var parts = swaps.Take(MaxSwaps).Select(s => $"{s.FromHex}>{s.ToHex}")
            .Concat(areas.OrderBy(kv => kv.Key).Select(kv => $"A{kv.Key}={kv.Value.ToString("X6", CultureInfo.InvariantCulture)}"))
            .ToList();
        return parts.Count == 0 ? null : string.Join(";", parts);
    }
}
