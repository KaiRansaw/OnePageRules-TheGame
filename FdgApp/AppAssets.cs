namespace FdgRaylib;

/// <summary>
/// Where the app's shipped files live - the folder holding <c>Assets/</c> (books, Quest datasets, fonts,
/// UI art) and the default home of saves, heroes and recovery saves when no repo folder is found.
///
/// <para>
/// The Raylib build is one executable with its files copied beside it, so <see cref="AppContext.BaseDirectory"/>
/// is the answer and that is the default. The Godot build runs its C# from a folder inside the Godot project
/// (<c>.godot/mono/temp/bin/...</c> in the editor, beside the export in a release) where the base directory is
/// not reliably that folder, so it sets <see cref="Root"/> once at start-up, before anything reads a book.
/// </para>
/// </summary>
public static class AppAssets
{
    private static string? _root;

    /// <summary>The folder that contains <c>Assets/</c>. Settable once by a front end; defaults to the exe's folder.</summary>
    public static string Root
    {
        get => _root ?? AppContext.BaseDirectory;
        set => _root = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary><c>Root/Assets</c>.</summary>
    public static string AssetsFolder => Path.Combine(Root, "Assets");
}
