using FDG;
using FdgRaylib.Config;
using FdgRaylib.Lobby;
using FdgRaylib.Tutorial;
using Godot;
using OprGodot.Hud;
using static FdgRaylib.Tutorial.HowToPlay;

namespace OprGodot.HowTo;

/// <summary>
/// How to Play's start screen (Kai, 2026-11-24, mockup screen 2 - kept): the two set armies, the twelve lessons in the
/// order the practice battle teaches them, how far you've got, and Continue / Start over / Rules Guide. Grimdark
/// Future or Age of Fantasy at the top right - each has its own armies.
///
/// <para>It hosts the practice battle and keeps its lobby ticking while the battle is on top (as the Lobby screen does
/// for an ordinary game); coming back here closes it.</para>
/// </summary>
public partial class HowToPlayScreen : Control, ILooked
{
    public EGameSystem System { get; set; } = EGameSystem.AgeOfFantasy;
    public Looks.ELook Look => Looks.For(System);

    private LobbySession? _session;
    private int _continueFrom = 1;
    private Label? _error;

    public override void _Ready()
    {
        Build();
        VisibilityChanged += () =>
        {
            if (!Visible || _session == null || !_session.LaunchStarted) return;
            // Back from the practice battle: close it, and show the progress it made.
            _session.Dispose();
            _session = null;
            HowToPlayLauncher.Stop();
            Rebuild();
        };
    }

    public override void _ExitTree()
    {
        // Left for the main menu from the battle (or quit): the practice game closes with this screen.
        _session?.Dispose();
        _session = null;
        HowToPlayLauncher.Stop();
    }

    public override void _Process(double delta)
    {
        if (_session == null) return;
        _session.Tick();
        if (_session.TakeLaunchedGame() is not { } game) return;
        FdgRaylib.Game.GameSession battle;
        try { battle = FdgRaylib.Game.GameSession.Start(game, _session, Battle.BattleResolvers.Register); }
        catch (Exception e)
        {
            GD.PrintErr("How to Play: opening the table failed: " + e);
            ShowError("The practice battle started but its table couldn't open: " + e.Message);
            return;
        }
        int from = _continueFrom;
        App.I.Open<Battle.BattleScreen>(Scenes.Battle, s => { s.Session = battle; s.HowToPlayFrom = from; });
    }

    private void Rebuild()
    {
        foreach (Node c in GetChildren()) if (c is not ColorRect) c.QueueFree();
        App.I.SetLook(this, Look);
        Build();
    }

    private void Build()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 32);
        AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);

        // Top bar: Back, the title, the game.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 20);
        var back = new Button { Name = "Back", Text = "Back", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(140, 48), TooltipText = "Back to the main menu (Esc)." };
        back.Pressed += App.I.Back;
        head.AddChild(back);
        var title = new Label { Text = "How to Play", ThemeTypeVariation = "HeaderLabel", VerticalAlignment = VerticalAlignment.Center };
        Ui.ScreenTitle(title);
        head.AddChild(title);
        head.AddChild(HudKit.Text($"{SystemName(System)}  -  a practice battle, {Rounds} rounds", 17, HudKit.Dim, HudKit.Semi));
        head.AddChild(HudKit.Grow());
        foreach (EGameSystem s in Systems)
        {
            var b = Ui.Button(SystemName(s), () => { System = s; Rebuild(); }, $"Learn {SystemName(s)} with its own armies.", s == System ? "CommitButton" : null);
            b.Name = "HowTo_" + s;
            b.Disabled = s == System;
            head.AddChild(b);
        }
        col.AddChild(head);

        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        col.AddChild(row);
        row.AddChild(Armies());
        row.AddChild(LessonList());
        row.AddChild(ProgressPanel());
    }

    private static string SystemName(EGameSystem s) => s == EGameSystem.AgeOfFantasy ? "Age of Fantasy" : "Grimdark Future";

    // ------------------------------------------------------------------ the armies

    private Control Armies()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        v.AddThemeConstantOverride("separation", 6);
        var a = ArmiesFor(System);
        v.AddChild(HudKit.Heading($"Your army - {a.MyBook}", 16));
        Unit(v, a.Name(Role.MyHero), "Hero - leads the " + a.Name(Role.MyMelee));
        Unit(v, a.Name(Role.MyMelee), "fights in melee");
        Unit(v, a.Name(Role.MyShooters), "shoots");
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
        v.AddChild(HudKit.Heading($"The enemy - {a.FoeBook} (the bot plays it)", 16));
        Unit(v, a.Name(Role.FoeMelee), "fights in melee");
        Unit(v, a.Name(Role.FoeShooters), "shoots");
        v.AddChild(HudKit.Grow());
        var note = HudKit.Text("Units exactly as the Army Forge books list them, no upgrades.", 14, HudKit.Dim, HudKit.Medium);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(note);
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(v, 18, 14, 18, 14));
    }

    private void Unit(VBoxContainer v, string name, string what)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        h.AddChild(HudKit.Text(name, 18, HudKit.Fg, HudKit.Bold));
        h.AddChild(HudKit.Grow());
        h.AddChild(HudKit.Text(what, 15, HudKit.Dim, HudKit.Semi));
        v.AddChild(h);
    }

    // ------------------------------------------------------------------ the lessons

    private int Done => UserConfig.Current.HowToPlay.Done(System);

    private Control LessonList()
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 5);
        v.AddChild(HudKit.Heading("Lessons - in the order the battle teaches them", 16));
        int done = Done;
        foreach (Lesson l in Lessons(System))
        {
            bool isDone = l.Number <= done, isNext = l.Number == done + 1;
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 12);
            h.AddChild(HudKit.Text(l.Number.ToString(), 18, isDone ? HudKit.Lime : HudKit.Dim, HudKit.Exo(800)).With(x => x.CustomMinimumSize = new Vector2(28, 0)));
            var t = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            t.AddThemeConstantOverride("separation", -2);
            t.AddChild(HudKit.Text(l.Title, 18, HudKit.Fg, HudKit.Bold));
            t.AddChild(HudKit.Text(l.Summary, 14, HudKit.Dim, HudKit.Medium));
            h.AddChild(t);
            h.AddChild(HudKit.Text(isDone ? "done" : isNext ? "next" : "", 14, isDone ? HudKit.Lime : HudKit.Gold, HudKit.Semi));
            var box = new ChamferBox { Fill = isNext ? new Color(0.09f, 0.13f, 0.06f, 0.95f) : HudKit.GlassLight, Border = isNext ? HudKit.Lime : HudKit.Edge, Cut = 6,
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 5, ContentMarginBottom = 5 };
            v.AddChild(HudKit.Panel(box, h));
        }
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(v, 18, 14, 18, 14)).With(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill);
    }

    // ------------------------------------------------------------------ progress and start

    private Control ProgressPanel()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        v.AddThemeConstantOverride("separation", 10);
        v.AddChild(HudKit.Heading("Progress", 16));
        int done = Done, total = Lessons(System).Count;
        v.AddChild(HudKit.Text($"{done} / {total}", 52, HudKit.Fg, HudKit.Exo(800)));
        v.AddChild(HudKit.Text("Saved after every lesson.", 15, HudKit.Dim, HudKit.Medium));
        bool started = done > 0 && done < total;
        string go = done == 0 ? "START" : done >= total ? "PLAY AGAIN" : $"CONTINUE - LESSON {done + 1}";
        var start = Ui.Button(go, () => Launch(started ? done + 1 : 1),
            started ? "The battle starts from the beginning; lessons you've done skip their reading, so you're quickly back where you were." : "Start the practice battle.", "CommitButton");
        start.Name = "StartHowToPlay";
        start.CustomMinimumSize = new Vector2(0, 56);
        v.AddChild(start);
        if (done > 0)
        {
            var over = Ui.Button("Start over", () => { HowToPlayProgress.Reset(System); Rebuild(); }, "Forget your progress in this game and start from lesson 1.");
            over.Name = "StartOver";
            v.AddChild(over);
        }
        var guide = Ui.Button("Open Rules Guide", () => App.I.Open<RulesGuideScreen>(Scenes.RulesGuide, g => g.System = System), "The same lessons as a picture walkthrough.");
        guide.Name = "OpenRulesGuide";
        v.AddChild(guide);
        v.AddChild(HudKit.Grow());
        _error = HudKit.Text("", 15, Palette.WoundsRed, HudKit.Semi);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(_error);
        var note = HudKit.Text("Each lesson links to its Rules Guide page, and each guide page links back here.", 14, HudKit.Dim, HudKit.Medium);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(note);
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(v, 18, 14, 18, 14));
    }

    private void Launch(int from)
    {
        if (_session != null) return;
        _continueFrom = from;
        _session = HowToPlayLauncher.Launch(System, out string error);
        if (_session == null) ShowError(error);
    }

    private void ShowError(string text)
    {
        if (_error != null) _error.Text = text;
    }
}
