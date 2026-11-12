using System.Runtime.InteropServices;

namespace FdgRaylib.Models3D;

/// <summary>
/// One model file shown under several unit names without taking the space twice. The same Warhammer model often stands
/// for a unit in several army books (Battle Brothers' Terminators are also Blood, Dark, Wolf... Destroyers), and a model
/// file can be 20-100 MB. A hard link is the same file under a second name: no extra space, and either name can be
/// opened, turned (its own settings file) or deleted on its own. Where a link isn't possible (another drive, FAT32),
/// it falls back to a copy.
/// </summary>
public static class FileLinks
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkWindows(string newFile, string existingFile, IntPtr security);

    [DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int LinkUnix(string existing, string newFile);

    /// <summary>Puts <paramref name="source"/> at <paramref name="dest"/> as a hard link, or a copy. True = linked.</summary>
    public static bool LinkOrCopy(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest)) File.Delete(dest);
        try
        {
            bool ok = OperatingSystem.IsWindows() ? CreateHardLinkWindows(dest, source, IntPtr.Zero) : LinkUnix(source, dest) == 0;
            if (ok) return true;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        File.Copy(source, dest, true);
        return false;
    }
}
