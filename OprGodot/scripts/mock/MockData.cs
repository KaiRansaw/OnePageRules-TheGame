using System.Text.Json;
using FdgRaylib;

namespace OprGodot.Mock;

/// <summary>A unit as the mockups show it - read straight from a bundled army book, so names and stats are real.</summary>
public sealed record SampleWeapon(string Name, int Range, int Attacks, int Ap, string Rules);
public sealed record SampleUnit(string Name, int Quality, int Defense, int Models, int Points, float Base, List<SampleWeapon> Weapons, List<string> Rules, bool Hero, int Tough);

public static class MockData
{
    private static readonly Dictionary<string, Dictionary<string, SampleUnit>> Books = new();

    /// <summary>The unit called <paramref name="unit"/> in the AoF book file <paramref name="book"/> (e.g. "AoF-Orcs").</summary>
    public static SampleUnit Unit(string book, string unit)
    {
        if (!Books.TryGetValue(book, out var units))
        {
            string path = Path.Combine(AppAssets.Root, "Assets", "Books", "Age of Fantasy", book + ".fdgbook");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            units = new Dictionary<string, SampleUnit>();
            foreach (var u in doc.RootElement.GetProperty("units").EnumerateArray())
            {
                var rules = u.GetProperty("rules").EnumerateArray().Select(RuleText).ToList();
                var weapons = u.GetProperty("weapons").EnumerateArray().Select(w => new SampleWeapon(
                    w.GetProperty("name").GetString()!, (int)w.GetProperty("rangeInches").GetDouble(),
                    w.GetProperty("attacks").GetInt32(), w.GetProperty("armorPenetration").GetInt32(),
                    string.Join(", ", w.GetProperty("specialRules").EnumerateArray().Select(RuleText)))).ToList();
                int tough = u.GetProperty("rules").EnumerateArray()
                    .Where(r => r.GetProperty("name").GetString() == "Tough")
                    .Select(r => r.TryGetProperty("numericValue", out var v) ? v.GetInt32() : 1).FirstOrDefault(1);
                string name = u.GetProperty("name").GetString()!.Trim();
                units[name] = new SampleUnit(name, u.GetProperty("quality").GetInt32(), u.GetProperty("defense").GetInt32(),
                    u.GetProperty("baseModelCount").GetInt32(), u.GetProperty("basePointCost").GetInt32(),
                    (float)u.GetProperty("base").GetProperty("diameterInches").GetDouble(), weapons, rules,
                    rules.Contains("Hero"), tough);
            }
            Books[book] = units;
        }
        return units[unit];
    }

    private static string RuleText(JsonElement r)
    {
        string name = r.GetProperty("name").GetString()!;
        return r.TryGetProperty("numericValue", out var v) && v.ValueKind == JsonValueKind.Number ? $"{name}({v.GetInt32()})" : name;
    }
}
