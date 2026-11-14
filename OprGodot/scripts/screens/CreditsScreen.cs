using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>Who made what the game uses (main menu -> Credits). The icons' CC BY 3.0 licence requires it.</summary>
public partial class CreditsScreen : Control
{
    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 40);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 20);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(140, 48), TooltipText = "Back to the main menu (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var title = new Label { Text = "Credits", ThemeTypeVariation = "HeaderLabel", VerticalAlignment = VerticalAlignment.Center };
        Ui.ScreenTitle(title);
        head.AddChild(title);
        col.AddChild(head);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 22);
        scroll.AddChild(body);

        foreach (CreditsText.Section s in CreditsText.Sections)
        {
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 6);
            card.AddChild(HudKit.Heading(s.Title, 18));
            foreach (CreditsText.Entry e in s.Entries)
            {
                var row = new VBoxContainer();
                row.AddThemeConstantOverride("separation", -2);
                row.AddChild(HudKit.Text(e.Name, 19, HudKit.Fg, HudKit.Bold));
                row.AddChild(HudKit.Text($"{e.By}  -  {e.Licence}", 16, HudKit.Dim, HudKit.Semi));
                if (e.Link is { } link)
                {
                    var b = new LinkButton { Text = link, Uri = link, FocusMode = FocusModeEnum.None };
                    b.AddThemeColorOverride("font_color", HudKit.Teal);
                    row.AddChild(b);
                }
                card.AddChild(row);
            }
            if (s.Note is { } note)
            {
                var n = HudKit.Text(note, 15, HudKit.Gold, HudKit.Medium);
                n.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                card.AddChild(n);
            }
            body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(card, 20, 16, 20, 16)));
        }
    }
}
