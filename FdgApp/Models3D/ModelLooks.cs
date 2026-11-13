namespace FdgRaylib.Models3D;

/// <summary>
/// Per-model looks (Kai, 2026-11-12): each model in a unit can stand on the table as any of the unit's
/// downloaded versions, picked in the army builder. A look is stored as the model file's NAME only
/// ("grunt_v2.glb"), so it survives the folder moving and works on a friend's PC with the same downloads.
/// </summary>
public static class ModelLooks
{
    /// <summary>The file for a model: its picked look when that file is among the unit's versions, else the
    /// first version (unpicked models all look the same). Null when the unit has no downloaded model.</summary>
    public static string? Pick(IReadOnlyList<string> files, string? look)
    {
        if (files.Count == 0) return null;
        if (!string.IsNullOrEmpty(look))
            foreach (string f in files)
                if (string.Equals(Path.GetFileName(f), look, StringComparison.OrdinalIgnoreCase)) return f;
        return files[0];
    }

    /// <summary>
    /// A look that names its own folder - "Faction/Unit/file.glb", or "Faction/Unit/" for that unit's first
    /// version - for a model whose unit isn't named after its models (a Quest hero is named "Krrek", its models
    /// are the base hero's). Null for a plain file-name look.
    /// </summary>
    public static (string Faction, string Unit, string? File)? Folder(string? look)
    {
        if (string.IsNullOrEmpty(look)) return null;
        string[] parts = look.Split('/');
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
        return (parts[0], parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null);
    }

    /// <summary>"Faction/Unit/file.glb" for a picked file of a unit.</summary>
    public static string FolderLook(string faction, string unit, string file) => $"{faction}/{unit}/{Path.GetFileName(file)}";

    /// <summary>The look name stored for a file.</summary>
    public static string NameOf(string file) => Path.GetFileName(file);

    /// <summary>A readable label for a version: "Version 2 - grunt_v2".</summary>
    public static string Label(IReadOnlyList<string> files, string file)
    {
        int i = files.ToList().FindIndex(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        return $"Version {i + 1} - {Path.GetFileNameWithoutExtension(file)}";
    }
}
