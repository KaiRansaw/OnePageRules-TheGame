namespace FdgRaylib.Models3D;

/// <summary>
/// The Warhammer 40k faction each of the catalogue's folders ("groups") holds, for the Match models screen's faction
/// dropdown (Kai, 2026-11-12: "make sure its in the 40k Factions"; chapters and legions kept separate).
///
/// <para>
/// The mod numbers its folders and tags each with a few keywords, never a name. These names were read off each
/// folder's models (2026-11-12, ForceOrg workshop 3753014527): group 8 is Night Lords ("NL Chosen", "Night Lords
/// Warp Talon"), 23 the Chaos Knights' War Dogs, 31 the Emperor's Children ("Noise Marines", Fulgrim), 36 the plain
/// Chaos Space Marines and cultists. Two folders that hold the same faction share its name (1 and 21 are both
/// Tyranids; 37 is a lone White Scars model, Suboden Khan). A folder this table doesn't know - another
/// catalogue, or a rebuilt one - shows as "Other: " and its keywords, so nothing is ever hidden.
/// </para>
/// </summary>
public static class CatalogueFactions
{
    private static readonly Dictionary<int, string> ForceOrg = new()
    {
        [1] = "Tyranids", [6] = "Space Marines", [7] = "Alpha Legion", [8] = "Night Lords", [9] = "Red Corsairs",
        [10] = "Word Bearers", [11] = "Imperial Fists", [12] = "Iron Hands", [13] = "Raven Guard", [14] = "World Eaters",
        [15] = "Thousand Sons", [16] = "Black Templars", [17] = "Grey Knights", [18] = "Drukhari", [19] = "Genestealer Cults",
        [20] = "T'au Empire", [21] = "Tyranids", [22] = "Adepta Sororitas", [23] = "Chaos Knights", [24] = "Space Wolves",
        [25] = "Adeptus Mechanicus", [26] = "Inquisition", [27] = "Imperial Knights", [28] = "Deathwatch", [29] = "Aeldari",
        [30] = "Death Guard", [31] = "Emperor's Children", [32] = "White Scars", [33] = "Leagues of Votann", [34] = "Blood Angels",
        [35] = "Necrons", [36] = "Chaos Space Marines", [37] = "White Scars", [38] = "Adeptus Custodes", [39] = "Salamanders",
        [40] = "Astra Militarum", [41] = "Chaos Daemons", [42] = "Orks", [43] = "Ultramarines", [44] = "Dark Angels",
    };

    /// <summary>The faction a catalogue folder holds.</summary>
    public static string For(ForceOrgCatalog cat, int group)
    {
        if (cat.Source.Contains("3753014527") && ForceOrg.TryGetValue(group, out string? name)) return name;
        return cat.Groups.TryGetValue(group, out string? words) ? $"Other: {words}" : $"Other: folder {group}";
    }

    /// <summary>Every faction in the catalogue, A-Z, with the models in it (two folders of one faction are merged).</summary>
    public static SortedDictionary<string, List<ForceOrgCatalog.Model>> ByFaction(ForceOrgCatalog cat)
    {
        var all = new SortedDictionary<string, List<ForceOrgCatalog.Model>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in cat.ById.Values)
        {
            string f = For(cat, m.Group);
            if (!all.TryGetValue(f, out var list)) all[f] = list = new();
            list.Add(m);
        }
        foreach (var list in all.Values) list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) is var c && c != 0 ? c : a.Id.CompareTo(b.Id));
        return all;
    }
}
