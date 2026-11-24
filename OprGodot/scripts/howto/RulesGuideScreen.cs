using FDG;
using FdgRaylib.Tutorial;
using Godot;
using OprGodot.Hud;
using static FdgRaylib.Tutorial.HowToPlay;

namespace OprGodot.HowTo;

/// <summary>
/// The Rules Guide (Kai, 2026-11-24, mockup screen 4 - kept): the same twelve lessons as How to Play, one page each -
/// a drawn picture of the step, the rule in the book's own words with where it comes from, and a short note on which
/// buttons do it in this game. Chapters down the left; Previous / Next, the arrow keys, or "Try it" to play the lesson.
/// Opened from the main menu, How to Play, and the practice battle's coach (Back returns to the battle).
/// </summary>
public partial class RulesGuideScreen : Control, ILooked
{
    public EGameSystem System { get; set; } = EGameSystem.AgeOfFantasy;
    public int StartLesson { get; set; } = 1;
    /// <summary>Opened from the practice battle: no game switch, and Back goes back to it.</summary>
    public bool FromBattle { get; set; }
    public Looks.ELook Look => Looks.For(System);

    private IReadOnlyList<Lesson> _lessons = null!;
    private int _page;
    private VBoxContainer _chapters = null!, _text = null!;
    private Label _title = null!, _pageOf = null!;
    private GuideDiagram _picture = null!;
    private Button _prev = null!, _next = null!;

    public override void _Ready()
    {
        _page = Math.Clamp(StartLesson - 1, 0, 11);
        Build();
    }

    private void Build()
    {
        _lessons = Lessons(System);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 32);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 20);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(140, 48),
            TooltipText = FromBattle ? "Back to the practice battle (Esc)." : "Back (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var title = new Label { Text = "Rules Guide", ThemeTypeVariation = "HeaderLabel", VerticalAlignment = VerticalAlignment.Center };
        Ui.ScreenTitle(title);
        head.AddChild(title);
        head.AddChild(HudKit.Text(RuleText.For(System).Book, 17, HudKit.Dim, HudKit.Semi));
        head.AddChild(HudKit.Grow());
        if (!FromBattle)
            foreach (EGameSystem s in Systems)
            {
                var b = Ui.Button(s == EGameSystem.AgeOfFantasy ? "Age of Fantasy" : "Grimdark Future", () => Switch(s), "This game's rules and armies.", s == System ? "CommitButton" : null);
                b.Name = "HowTo_" + s;
                b.Disabled = s == System;
                head.AddChild(b);
            }
        col.AddChild(head);

        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        col.AddChild(row);

        _chapters = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) };
        _chapters.AddThemeConstantOverride("separation", 3);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_chapters, 12, 12, 12, 12)));

        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 12);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        _title = HudKit.Text("", 30, HudKit.Fg, HudKit.Exo(800));
        top.AddChild(_title);
        _pageOf = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _pageOf.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        top.AddChild(_pageOf);
        page.AddChild(top);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        _picture = new GuideDiagram { CustomMinimumSize = new Vector2(560, 400), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        body.AddChild(_picture);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _text.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_text);
        body.AddChild(scroll);
        page.AddChild(body);

        var nav = new HBoxContainer();
        nav.AddThemeConstantOverride("separation", 10);
        _prev = Ui.Button("", () => Show(_page - 1), "Previous page (left arrow).");
        _prev.Name = "GuidePrev";
        nav.AddChild(_prev);
        if (!FromBattle)
        {
            var tryIt = Ui.Button("Try it: How to Play", () => App.I.Open<HowToPlayScreen>(Scenes.HowToPlay, h => h.System = System), "Learn it by playing - the practice battle.", "CommitButton");
            tryIt.Name = "GuideTryIt";
            nav.AddChild(tryIt);
        }
        nav.AddChild(HudKit.Grow());
        _next = Ui.Button("", () => Show(_page + 1), "Next page (right arrow).");
        _next.Name = "GuideNext";
        nav.AddChild(_next);
        page.AddChild(nav);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(page, 20, 16, 20, 16)).With(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));

        Show(_page);
    }

    private void Switch(EGameSystem s)
    {
        System = s;
        foreach (Node c in GetChildren()) if (c is not ColorRect) c.QueueFree();
        App.I.SetLook(this, Look);
        Build();
    }

    private void Show(int page)
    {
        _page = Math.Clamp(page, 0, _lessons.Count - 1);
        Lesson l = _lessons[_page];

        foreach (Node c in _chapters.GetChildren()) c.QueueFree();
        _chapters.AddChild(HudKit.Text("CHAPTERS", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        foreach (Lesson c in _lessons)
        {
            int n = c.Number - 1;
            var b = Ui.Button($"{c.Number}.  {c.Title}", () => Show(n), c.Summary, n == _page ? "CommitButton" : null);
            b.Name = $"Chapter{c.Number}";
            b.Alignment = HorizontalAlignment.Left;
            _chapters.AddChild(b);
        }

        _title.Text = l.Title;
        _pageOf.Text = $"Page {l.Number} of {_lessons.Count}";
        _picture.Picture = l.Picture;
        _picture.QueueRedraw();

        foreach (Node c in _text.GetChildren()) c.QueueFree();
        foreach (RuleText.Section s in RuleText.ForLesson(System, l))
            _text.AddChild(HowToPlayCoach.BookBlock(s.Title, s.Text, "FROM THE BOOK - CORE RULES"));
        if (l.RuleIds.Contains("special-rules"))
            foreach ((string name, string text) in ArmyRules(System))
                _text.AddChild(HowToPlayCoach.BookBlock(name, text, "ARMY FORGE"));
        var inGame = new VBoxContainer();
        inGame.AddThemeConstantOverride("separation", 2);
        inGame.AddChild(HudKit.Text("IN THIS GAME", 11, HudKit.Teal, HudKit.Tracked(HudKit.Bold, 1)));
        var t = HudKit.Text(l.InThisGame, 16, HudKit.Fg, HudKit.Medium);
        t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        t.CustomMinimumSize = new Vector2(300, 0);
        inGame.AddChild(t);
        _text.AddChild(HudKit.Panel(new ChamferBox { Fill = new Color(0.06f, 0.10f, 0.10f, 0.95f), Border = HudKit.Teal with { A = 0.7f }, Cut = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 }, inGame));

        _prev.Visible = _page > 0;
        _next.Visible = _page < _lessons.Count - 1;
        if (_page > 0) _prev.Text = $"< Previous: {_lessons[_page - 1].Title}";
        if (_page < _lessons.Count - 1) _next.Text = $"Next: {_lessons[_page + 1].Title} >";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e.IsActionPressed("ui_left")) { Show(_page - 1); GetViewport().SetInputAsHandled(); }
        else if (e.IsActionPressed("ui_right")) { Show(_page + 1); GetViewport().SetInputAsHandled(); }
    }
}
