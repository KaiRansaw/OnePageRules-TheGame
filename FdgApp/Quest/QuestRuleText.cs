using FDG;
using FDG.Rules.Dispatch;
using FdgRaylib.Quest;

namespace FdgRaylib.Rendering;

/// <summary>
/// The text behind a Quest hero's rule name, worded for the Quest game being played (Kai, 2026-11-05).
/// Order: the hero's own faction rule from its hero book (a faction rule only that book has), then OPR's
/// wording for THIS Quest system with no fallback to its parent game.
/// </summary>
public static class QuestRuleText
{
    public static string? For(string rule, EGameSystem system, SqFaction? faction)
    {
        string name = BaseName(rule);
        string? own = faction?.Rules.FirstOrDefault(r => string.Equals(r.N, name, StringComparison.OrdinalIgnoreCase))?.D;
        if (!string.IsNullOrWhiteSpace(own)) return own;
        return OfficialRuleText.OwnText(name, GameSystemInfo.Slug(system));
    }

    /// <summary>"Tough(3)" -> "Tough"; a name with no argument is returned trimmed.</summary>
    public static string BaseName(string rule)
    {
        string name = rule.Trim();
        int paren = name.IndexOf('(');
        return paren > 0 ? name[..paren].TrimEnd() : name;
    }
}
