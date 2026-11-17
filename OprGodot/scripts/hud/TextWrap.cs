using System.Text;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// Kai, 2026-11-17: tooltips and pop-up messages wrap instead of running off as one long line. Godot's own tooltip is a
/// plain Label it makes on the fly, and a dialog's text is a Label too, so both are caught as they appear (SceneTree
/// NodeAdded) and given line breaks at a set width, measured in the label's real font. Text that already fits is
/// left alone; the line breaks the text had stay.
/// </summary>
public static class TextWrap
{
    public const float TooltipWidth = 560;
    public const float DialogWidth = 640;

    public static void Attach(SceneTree tree) => tree.NodeAdded += OnAdded;

    private static void OnAdded(Node n)
    {
        if (n is Label l && l.ThemeTypeVariation.ToString() == "TooltipLabel")
        {
            Font font = l.GetThemeFont("font");
            int size = l.GetThemeFontSize("font_size");
            l.Text = Wrap(l.Text, font, size, TooltipWidth);
        }
        else if (n is AcceptDialog d && !string.IsNullOrEmpty(d.DialogText))
        {
            Label label = d.GetLabel();
            d.DialogText = Wrap(d.DialogText, label.GetThemeFont("font"), label.GetThemeFontSize("font_size"), DialogWidth);
        }
    }

    /// <summary>Breaks each paragraph of <paramref name="text"/> between words so no line is wider than <paramref name="width"/>.</summary>
    public static string Wrap(string text, Font? font, int size, float width)
    {
        if (string.IsNullOrEmpty(text) || font == null) return text;
        float W(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        var sb = new StringBuilder();
        string[] paragraphs = text.Split('\n');
        for (int p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0) sb.Append('\n');
            string para = paragraphs[p];
            if (W(para) <= width) { sb.Append(para); continue; }
            var line = new StringBuilder();
            foreach (string word in para.Split(' '))
            {
                string tryLine = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && W(tryLine) > width)
                {
                    sb.Append(line).Append('\n');
                    line.Clear().Append(word);
                }
                else line.Clear().Append(tryLine);
            }
            sb.Append(line);
        }
        return sb.ToString();
    }
}
