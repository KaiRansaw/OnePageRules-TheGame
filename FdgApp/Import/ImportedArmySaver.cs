using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgRaylib.Import;

/// <summary>
/// Files an Army Forge import into its own game's armies folder (armies/GDF, armies/AoF, ...), so it shows
/// up in that game's lobby picker. Shared by the Godot import popup; no drawing, no dialogs.
///
/// <para>The folder comes from the ARMY's game system (the slug Army Forge sent), never from whatever game
/// the lobby is set to - a Firefight list imported from a Grimdark Future lobby belongs in the Firefight
/// folder, because serving one mode's list to another mode is wrong data (CLAUDE.md).</para>
/// </summary>
public static class ImportedArmySaver
{
    /// <summary>The game an imported army is for.</summary>
    public static EGameSystem SystemOf(ArmyListFile army) =>
        GameSystemInfo.FromSlug(army.GameSystem) ?? EGameSystem.GrimdarkFuture;

    /// <summary>
    /// What gets written: Army Forge's verbatim import (their units, their points), with the editable session
    /// riding along when the rebuild against the bundled book worked (#356) - same as the Raylib Save As.
    /// </summary>
    public static ArmyListFile FileToWrite(ArmyForgeShareService.ImportOutcome outcome) =>
        outcome.ForgeSession?.Selections is { } selections && outcome.BundledBook is { } book
            ? EditableSession.Attach(outcome.Result.Army, selections, book)
            : outcome.Result.Army;

    /// <summary>
    /// Writes the import to armies/&lt;its game&gt;/&lt;name&gt;.fdgarmy (a free name: "Name (2)" if taken) and
    /// tells the army catalog it changed. Returns the path written. Throws with a plain message on failure -
    /// a write that did not happen is never reported as a success (#307).
    /// </summary>
    public static string Save(ArmyForgeShareService.ImportOutcome outcome, string? name = null)
    {
        if (ArmyPaths.FolderPath is not { } root)
            throw new InvalidOperationException("No armies folder was found to save into.");

        ArmyListFile army = FileToWrite(outcome);
        if (!string.IsNullOrWhiteSpace(name)) army.Name = name.Trim();
        string folder = Path.Combine(root, ArmyPaths.FolderNameFor(SystemOf(outcome.Result.Army)));
        Directory.CreateDirectory(folder);

        string path = FreePath(folder, SafeFileName(army.Name));
        File.WriteAllText(path, JsonSerializer.Serialize(army, army.GetType(), RuleJson.Options));
        ArmyCatalogCache.Invalidate();
        return path;
    }

    /// <summary>A file name made from a list name: path-illegal characters dropped, blank -> "Imported Army".</summary>
    public static string SafeFileName(string? name)
    {
        char[] bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToArray();
        string clean = new string((name ?? "").Where(c => !bad.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "Imported Army" : clean;
    }

    /// <summary>folder/name.fdgarmy, or folder/name (2).fdgarmy, (3)... - never overwrites a list.</summary>
    public static string FreePath(string folder, string name)
    {
        string ext = ArmyListFile.EXTENSION_WITH_PERIOD;
        string path = Path.Combine(folder, name + ext);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n}){ext}");
        return path;
    }
}
