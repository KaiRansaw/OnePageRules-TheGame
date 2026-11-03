using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// ImGui's Begin/End scopes have to balance within a frame. When they do not, the C++ runtime fires an
/// assertion dialog - "Missing EndDisabled()" - and the app is dead on the spot, before anything is
/// drawn.
///
/// <para>
/// Kai hit exactly that on build 0.70. Replacing the lobby's terrain-mode block with the new Map button
/// deleted an <c>EndDisabled()</c> along with it, so the Table settings section pushed one disabled
/// level every frame and never popped it. <b>The whole app suite - 8,821 tests - passed, the engine's
/// 4,687 passed, and the headless smoke exited 0</b>, because none of them draw an ImGui frame. The
/// first thing that could tell was opening the lobby.
/// </para>
///
/// <para>
/// This is a counter, not a proof. It catches an unmatched call in a method, which is the mistake that
/// actually happens when a block of drawing code is deleted or moved. It cannot catch an imbalance that
/// only appears on one branch (a Begin inside an <c>if</c> whose End is outside it), so a balanced count
/// is not a guarantee - it is the cheap half of the check, and the half that would have caught this one.
/// </para>
/// </summary>
[TestFixture]
public class DisabledScopeBalanceTests
{
    /// <summary>Scope pairs that must appear in matched numbers inside a single method.</summary>
    private static readonly (string Begin, string End)[] Pairs =
    {
        ("ImGui.BeginDisabled(", "ImGui.EndDisabled("),
        ("ImGui.BeginGroup(", "ImGui.EndGroup("),
        ("ImGui.BeginTooltip(", "ImGui.EndTooltip("),
    };

    /// <summary>Statement keywords that a signature-shaped regex cannot tell from a method name.</summary>
    private static readonly HashSet<string> Keywords = new()
    {
        "if", "for", "foreach", "while", "switch", "using", "lock", "catch", "fixed", "do", "else",
    };

    private static string RenderingFolder()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "FdgRaylib", "Rendering");
            if (Directory.Exists(candidate)) return candidate;
        }
        Assert.Ignore("Could not find FdgRaylib/Rendering beside the test run.");
        return "";
    }

    [Test]
    public void EveryDrawingMethodBalancesItsImGuiScopes()
    {
        string folder = RenderingFolder();
        var complaints = new List<string>();

        foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            foreach ((string method, string body) in Methods(File.ReadAllText(file)))
            {
                // A deliberate Begin/End wrapper pair - ImGuiTheme.BeginTranslucentTooltip and its
                // partner - is unbalanced by design: opening is the whole job of one and closing is
                // the whole job of the other. The caller's balance is what matters there, and this
                // counter checks the caller.
                if (method.StartsWith("Begin") || method.StartsWith("End")) continue;

                foreach ((string begin, string end) in Pairs)
                {
                    int opens = Occurrences(body, begin), closes = Occurrences(body, end);
                    if (opens == closes) continue;

                    complaints.Add(
                        $"{Path.GetFileName(file)} :: {method} has {opens} {begin.Replace("ImGui.", "")}) "
                        + $"and {closes} {end.Replace("ImGui.", "")}). An unmatched scope kills the app "
                        + "with an assertion dialog the moment this method draws.");
                }
            }
        }

        Assert.That(complaints, Is.Empty, string.Join("\n", complaints));
    }

    /// <summary>
    /// Splits a C# file into top-level method bodies by brace depth. Crude on purpose: it only needs to
    /// know where one method's braces end and the next begins, and it is reading our own code, not
    /// arbitrary C#. Lambdas and local functions nest inside a method's body, which is what we want -
    /// a scope opened in a lambda and closed outside it would not balance anyway.
    /// </summary>
    private static IEnumerable<(string Name, string Body)> Methods(string source)
    {
        source = StripCommentsAndStrings(source);

        // A member signature at class-member indentation, followed by a brace on that line or the next.
        var signature = new Regex(@"^\s{4}(?:[\w\[\]<>,\?\s]*?)\b(\w+)\s*\([^;]*?\)\s*$",
            RegexOptions.Multiline);

        foreach (Match match in signature.Matches(source))
        {
            int brace = source.IndexOf('{', match.Index + match.Length);
            if (brace < 0) continue;
            // Anything other than whitespace between the signature and the brace means this was not a
            // method header (an expression body, an attribute, a multi-line call).
            if (source[(match.Index + match.Length)..brace].Trim().Length > 0) continue;
            // `if (...)` at method indentation looks exactly like a signature followed by a block.
            if (Keywords.Contains(match.Groups[1].Value)) continue;

            int depth = 0, i = brace;
            for (; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) break;
            }
            if (depth != 0) continue;

            yield return (match.Groups[1].Value, source[brace..i]);
        }
    }

    /// <summary>
    /// Blanks out comments and string literals so a mention of EndDisabled in a comment - and this file
    /// is full of them - is not counted as a call. Replaced with spaces rather than removed so every
    /// offset in the file stays where it was.
    /// </summary>
    private static string StripCommentsAndStrings(string source)
    {
        // The three shapes we actually write: line comments, block comments, and string literals
        // (verbatim and ordinary). Order matters - a // inside a string must not start a comment, so
        // the string alternatives have to be able to win, which they do by appearing in the same pass.
        return Regex.Replace(source,
            @"//[^\n]*|/\*.*?\*/|@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""",
            m => new string(' ', m.Length), RegexOptions.Singleline);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}
