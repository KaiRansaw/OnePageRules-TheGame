using Godot;

namespace OprGodot;

/// <summary>Small builders for controls made in code, so every screen makes them the same way.</summary>
public static class Ui
{
    public static Label Label(string text, string? variation = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        if (variation != null) l.ThemeTypeVariation = variation;
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(10, 0);
        }
        return l;
    }

    public static Button Button(string text, Action onPressed, string? tooltip = null, string? variation = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "" };
        if (variation != null) b.ThemeTypeVariation = variation;
        b.Pressed += onPressed;
        return b;
    }

    public static Control Spacer(float height) => new Control { CustomMinimumSize = new Vector2(0, height) };

    public static Control Grow()
    {
        var c = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        return c;
    }
}
