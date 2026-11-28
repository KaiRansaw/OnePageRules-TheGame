using System.Reflection;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;

namespace FdgRaylib.Paper;

/// <summary>
/// Turns the pages of a PDF into pictures, for the paper-model cutter. Uses PDFium (Google's PDF renderer, BSD / Apache
/// 2.0) through Docnet.Core (MIT), which ships PDFium for Windows, Linux and Mac in its NuGet package - nothing for
/// the player to install, and both licences allow shipping it with the game (THIRD-PARTY-NOTICES.txt).
/// <para>
/// Godot loads the game's assemblies in its own load context, where .NET's usual search of runtimes/&lt;rid&gt;/native
/// doesn't happen; <see cref="EnsureNativeLoader"/> points the PDFium import at the file wherever the build put it.
/// </para>
/// </summary>
public static class PdfPages
{
    /// <summary>Paper minis are drawn to be printed; 200 dpi keeps their line work sharp after the cut-out is scaled down.</summary>
    public const int DefaultDpi = 200;

    private static readonly object Gate = new();
    private static bool _resolverSet;

    /// <summary>How many pages the PDF has.</summary>
    public static int PageCount(string path)
    {
        EnsureNativeLoader();
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(File.ReadAllBytes(path), new PageDimensions(1.0));
            return reader.GetPageCount();
        }
    }

    /// <summary>One page (0-based) at <paramref name="dpi"/>, laid on white paper (PDFium leaves blank paper see-through).</summary>
    public static RgbaImage Render(string path, int page, int dpi = DefaultDpi) => Render(File.ReadAllBytes(path), page, dpi);

    public static RgbaImage Render(byte[] pdf, int page, int dpi = DefaultDpi)
    {
        EnsureNativeLoader();
        lock (Gate) // PDFium is not thread-safe
        {
            using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(dpi / 72.0));
            using var pr = reader.GetPageReader(page);
            int w = pr.GetPageWidth(), h = pr.GetPageHeight();
            byte[] bgra = pr.GetImage();
            var img = new RgbaImage(w, h);
            for (int i = 0; i < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                // straight colour over white (PDFium hands back premultiplied-looking pixels with alpha 0 on blank paper)
                img.Pixels[i] = (byte)((bgra[i + 2] * a + 255 * (255 - a)) / 255);
                img.Pixels[i + 1] = (byte)((bgra[i + 1] * a + 255 * (255 - a)) / 255);
                img.Pixels[i + 2] = (byte)((bgra[i] * a + 255 * (255 - a)) / 255);
                img.Pixels[i + 3] = 255;
            }
            return img;
        }
    }

    /// <summary>Lets the PDFium import find the native file inside a Godot build or a plain dotnet run alike.</summary>
    public static void EnsureNativeLoader()
    {
        lock (Gate)
        {
            if (_resolverSet) return;
            _resolverSet = true;
            try { NativeLibrary.SetDllImportResolver(typeof(DocLib).Assembly, Resolve); }
            catch (InvalidOperationException) { } // someone set one already
        }
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? search)
    {
        if (!name.Contains("pdfium", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        if (NativeLibrary.TryLoad(name, asm, search, out IntPtr h)) return h;
        foreach (string file in Candidates(asm))
            if (File.Exists(file) && NativeLibrary.TryLoad(file, out h)) return h;
        return IntPtr.Zero;
    }

    private static IEnumerable<string> Candidates(Assembly asm)
    {
        string lib = OperatingSystem.IsWindows() ? "pdfium.dll" : OperatingSystem.IsMacOS() ? "pdfium.dylib" : "pdfium.so";
        string arch = RuntimeInformation.ProcessArchitecture switch { Architecture.Arm64 => "arm64", Architecture.X86 => "x86", Architecture.Arm => "arm", _ => "x64" };
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var dirs = new List<string>();
        void Add(string? d) { if (!string.IsNullOrEmpty(d) && !dirs.Contains(d)) dirs.Add(d); }
        Add(Path.GetDirectoryName(asm.Location));
        Add(Path.GetDirectoryName(typeof(PdfPages).Assembly.Location));
        Add(AppContext.BaseDirectory);
        Add(AppAssets.Root);
        foreach (string d in dirs.ToList())
        {
            yield return Path.Combine(d, lib);
            yield return Path.Combine(d, "runtimes", $"{os}-{arch}", "native", lib);
            yield return Path.Combine(d, "runtimes", os, "native", lib);
        }
    }
}
