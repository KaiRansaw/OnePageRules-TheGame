using System.Reflection;

namespace FdgRaylib;

/// <summary>
/// The build stamp bug reports carry (#226) - without it a report can't be tied to the binary
/// that produced it. Dist/CI builds are stamped via
/// <c>-p:InformationalVersion=&lt;version&gt;+git-&lt;short-sha&gt;-&lt;utc-date&gt;</c>; anything else
/// (<c>dotnet run</c>, IDE builds) reports the Directory.Build.props default
/// "&lt;version&gt;-dev". The SDK may append "+&lt;commit&gt;" on its own when building inside
/// a git checkout - extra precision, kept.
/// </summary>
public static class AppVersion
{
    public static string Value { get; } =
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        is { Length: > 0 } info ? info : "unknown";

    /// <summary>
    /// The build number shown bottom-right on the main menu, so a pull can be confirmed at a glance
    /// before starting a game. <b>Bump this line on any push worth noticing.</b>
    /// <para>
    /// Deliberately NOT tied to <c>Directory.Build.props</c>'s &lt;Version&gt;: that tracks releases,
    /// this answers "is the binary in front of me the one I just pulled". It lives here as a plain
    /// constant rather than as MSBuild metadata because an AssemblyMetadata item in
    /// Directory.Build.props does not survive into the generated assembly info in this solution -
    /// and a build stamp that silently stops updating is worse than no build stamp.
    /// <see cref="ShortCommit"/> is the backstop: it updates itself when this line is forgotten.
    /// </para>
    /// </summary>
    public const string BuildNumber = "0.01";

    /// <summary>
    /// The short git commit this binary was built from, when one is available. The SDK appends
    /// "+&lt;full sha&gt;" to the informational version inside a git checkout, and dist/CI builds
    /// stamp "+git-&lt;short-sha&gt;-&lt;date&gt;"; both are reduced to the first 7 characters here.
    /// Null when the binary carries no commit at all.
    /// </summary>
    public static string? ShortCommit { get; } = ExtractCommit(Value);

    /// <summary>
    /// What the main menu shows, bottom right: the build number so a pull is obvious at a glance,
    /// and the commit beside it because that one updates itself when the build number is forgotten.
    /// </summary>
    public static string BuildLabel { get; } = ShortCommit is { } commit
        ? $"Build {BuildNumber} ({commit})"
        : $"Build {BuildNumber}";

    private static string? ExtractCommit(string informational)
    {
        int plus = informational.IndexOf('+');
        if (plus < 0 || plus + 1 >= informational.Length) return null;
        string tail = informational[(plus + 1)..];
        if (tail.StartsWith("git-", System.StringComparison.OrdinalIgnoreCase)) tail = tail[4..];
        // Keep only the leading hex run, so a "-<date>" suffix never reaches the label.
        int end = 0;
        while (end < tail.Length && System.Uri.IsHexDigit(tail[end])) end++;
        return end >= 7 ? tail[..7] : null;
    }
}
