using FDG;
using FdgRaylib.Rendering;
using FdgRaylib.Tutorial;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;
using static FdgRaylib.Tutorial.HowToPlay;

namespace OprGodot.HowTo;

/// <summary>
/// The practice battle's coach (How to Play, Kai 2026-11-24, mockup screen 3): a panel under the prompt line with the
/// lesson, its steps and what to do now; the green ring on the table where to go; and the lock on everything else
/// (<see cref="BattleDecisions.IGate"/>) - only the step's unit and action work. It also steers the dice while the
/// step's unit acts, so the lesson's moment happens (<see cref="HowToPlay.Faces"/>).
///
/// <para>Steps move on by themselves when the game shows they're done (the unit is on the table, it has activated,
/// it shot, it fought); read-only steps wait for Next. A finished lesson is saved (<see cref="HowToPlayProgress"/>).</para>
/// </summary>
public partial class HowToPlayCoach : Control, BattleDecisions.IGate
{
    public BattleScreen Screen { get; set; } = null!;
    public EGameSystem System { get; set; }
    /// <summary>Lessons below this have been done before: their read-only steps are skipped (the actions are still needed).</summary>
    public int ContinueFrom { get; set; } = 1;

    private IReadOnlyList<Lesson> _lessons = null!;
    private int _lesson, _step;
    private PlayerID _me;
    private bool _over, _shotSeen, _foughtSeen;
    private int? _baselineRound;
    private HashSet<UnitID> _activatedAtStart = new();

    private Label _head = null!, _text = null!, _stepsLine = null!;
    private VBoxContainer _rules = null!;
    private Button _next = null!, _prev = null!, _book = null!, _hide = null!;
    /// <summary>Reading back an earlier step with Previous (lesson, step), or null for the step being played. Reading
    /// back changes nothing in the battle.</summary>
    private (int Lesson, int Step)? _review;
    /// <summary>When Next / Previous last went off (ms) - one press moves one step, however the press arrives.</summary>
    private ulong _lastTurnMs;
    private Control _body = null!;
    private PanelContainer _panel = null!;

    private Step Now => _lessons[_lesson].Steps[_step];
    private Lesson NowLesson => _lessons[_lesson];
    private ScriptedDiceRoller? Dice => HowToPlayLauncher.Dice;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _lessons = Lessons(System);
        _me = Screen.Session.LocalPlayer ?? default;
        Screen.Session.Report.BeatSeen += OnBeat;
        BuildPanel();
        Screen.Decisions.Gate = this;
        StartStep();
    }

    public override void _ExitTree()
    {
        Screen.Session.Report.BeatSeen -= OnBeat;
        if (ReferenceEquals(Screen.Decisions.Gate, this)) Screen.Decisions.Gate = null;
        if (Dice != null) Dice.Steer = null;
        Screen.Table.ClearGhosts("howto");
        HowToPlayLauncher.Stop();
    }

    // ================================================================== the panel

    private void BuildPanel()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        v.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        _head = HudKit.Text("", 14, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2));
        top.AddChild(_head);
        top.AddChild(HudKit.Grow());
        _stepsLine = HudKit.Text("", 13, HudKit.Dim, HudKit.Semi);
        top.AddChild(_stepsLine);
        _hide = Ui.Button("Hide", ToggleBody, "Fold the coach away (the lesson keeps going).", "BackButton");
        _hide.Name = "CoachHide";
        top.AddChild(_hide);
        v.AddChild(top);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        _text = HudKit.Text("", 19, HudKit.Fg, HudKit.Semi);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(600, 0);
        body.AddChild(_text);

        _rules = new VBoxContainer { Visible = false };
        _rules.AddThemeConstantOverride("separation", 6);
        body.AddChild(_rules);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _book = Ui.Button("Show the book's rule", ToggleRules, "The rules this lesson teaches, word for word from the core rules.");
        _book.Name = "CoachBook";
        row.AddChild(_book);
        var guide = Ui.Button("Open in Rules Guide", OpenGuide, "The picture walkthrough of this lesson. Back returns here.");
        guide.Name = "CoachGuide";
        row.AddChild(guide);
        row.AddChild(HudKit.Grow());
        _prev = Ui.Button("Previous", Back, "Read the step before again (the battle isn't changed).");
        _prev.Name = "CoachPrev";
        row.AddChild(_prev);
        _next = Ui.Button("Next  (Enter)", Advance, "On to the next step.", "CommitButton");
        _next.Name = "CoachNext";
        row.AddChild(_next);
        body.AddChild(row);
        _body = body;
        v.AddChild(body);

        _panel = HudKit.Panel(new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.95f }, Border = HudKit.Lime, Cut = 12, Glow = HudKit.Lime with { A = 0.25f },
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 14,
        }, v);
        _panel.Name = "Coach";
        // Centred under the prompt line, whatever its width.
        var strip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        strip.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        strip.OffsetTop = 110; // under the two-line prompt banner (3.44)
        strip.AddChild(_panel);
        AddChild(strip);
    }

    private void ToggleBody()
    {
        _body.Visible = !_body.Visible;
        _hide.Text = _body.Visible ? "Hide" : "Show coach";
    }

    private void ToggleRules()
    {
        _rules.Visible = !_rules.Visible;
        _book.Text = _rules.Visible ? "Hide the book's rule" : "Show the book's rule";
    }

    private void OpenGuide() => App.I.Open<RulesGuideScreen>(Scenes.RulesGuide, g => { g.System = System; g.StartLesson = NowLesson.Number; g.FromBattle = true; });

    private void ShowStep()
    {
        (int li, int si) = _review ?? (_lesson, _step);
        Lesson l = _lessons[li];
        Step step = l.Steps[si];
        _head.Text = $"LESSON {l.Number} OF {_lessons.Count}  -  {l.Title.ToUpperInvariant()}";
        _stepsLine.Text = _review != null ? $"reading back - step {si + 1} of {l.Steps.Count}" : $"step {si + 1} of {l.Steps.Count}";
        _text.Text = Fill(_review == null && UsingOrElse && step.OrElseCoach is { } other ? other : step.Coach, System);
        _next.Visible = _review != null || Now.IsInfo;
        _next.Text = _review != null ? "Next" : "Next  (Enter)";
        _prev.Visible = (li, si) != (0, 0);
        foreach (Node c in _rules.GetChildren()) c.QueueFree();
        foreach (RuleText.Section s in RuleText.ForLesson(System, l))
            _rules.AddChild(BookBlock(s.Title, s.Text));
        if (l.RuleIds.Contains("special-rules"))
            foreach ((string name, string text) in ArmyRules(System))
                _rules.AddChild(BookBlock(name, text, "ARMY FORGE"));
        if (!_body.Visible) ToggleBody();
        _panel.Size = Vector2.Zero;
    }

    /// <summary>One rule, the book's words, with where it comes from - the same look as the Rules Guide.</summary>
    internal static Control BookBlock(string title, string text, string source = "FROM THE BOOK")
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        v.AddChild(HudKit.Text($"{source}  -  {title.ToUpperInvariant()}", 11, HudKit.Gold, HudKit.Tracked(HudKit.Bold, 1)));
        var t = HudKit.Text(text, 15, HudKit.Fg, HudKit.Medium);
        t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        t.CustomMinimumSize = new Vector2(560, 0);
        v.AddChild(t);
        return HudKit.Panel(new ChamferBox { Fill = new Color(0.07f, 0.10f, 0.11f, 0.95f), Border = HudKit.Gold with { A = 0.6f }, Cut = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 }, v);
    }

    // ================================================================== steps

    private void StartStep()
    {
        if (Auto) GD.Print($"HowTo: lesson {NowLesson.Number} step {_step + 1} ({Now.Do}) round {Screen.Session.Table.Progress.RoundCount?.ToString() ?? "set-up"}  "
            + string.Join("; ", Screen.Session.Table.Units.Objects.Where(u => u.GetIsOnBattlefield()).Select(u => $"{u.Name} at {Centre(u).X:0.0},{Centre(u).Z:0.0}")));
        // Lessons done before: skip what's only reading; the actions still have to be played.
        while (!_over && Now.IsInfo && NowLesson.Number < ContinueFrom) { if (!MoveOn()) return; }
        _shotSeen = _foughtSeen = false;
        _baselineRound = Screen.Session.Table.Progress.RoundCount;
        _activatedAtStart = MyUnits().Where(Activated).Select(u => u.ID).ToHashSet();
        if (Dice != null)
        {
            var units = RoleIds(Now.Unit);
            var steer = SteerFor(Now.Luck, _me);
            var progress = Screen.Session.Table.Progress;
            // Only while the step's own unit is the one activating (read on the engine's thread, at the roll).
            Dice.Steer = steer == null ? null : (kind, roller) =>
                progress.ActivatingUnit is { } acting && units.Contains(acting.ID) ? steer(kind, roller) : null;
        }
        ShowStep();
        Screen.Decisions.Refresh();
        if (ShotsTo != null) SnapStep($"lesson{NowLesson.Number:00}-step{_step + 1}.png");
    }

    /// <summary><c>--howto-shots=folder</c> (with --howto-auto): a screenshot as every step starts.</summary>
    private static readonly string? ShotsTo = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--howto-shots="))?["--howto-shots=".Length..];

    private async void SnapStep(string file)
    {
        for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree()) return;
        global::System.IO.Directory.CreateDirectory(ShotsTo!);
        GetViewport().GetTexture().GetImage().SavePng(global::System.IO.Path.Combine(ShotsTo!, file));
    }

    /// <summary>A second "go" within this many ms of the last is the same press arriving twice (the button and the
    /// Enter / A check both see it), so it's ignored.</summary>
    private const ulong PressGapMs = 300;

    private bool TooSoon()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastTurnMs < PressGapMs) return true;
        _lastTurnMs = now;
        return false;
    }

    private void Advance()
    {
        if (TooSoon()) return;
        if (_review is { } r)
        {
            // Reading back: step forward through what you've already seen, then back to the live step.
            (int li, int si) = r.Step + 1 < _lessons[r.Lesson].Steps.Count ? (r.Lesson, r.Step + 1) : (r.Lesson + 1, 0);
            _review = (li, si) == (_lesson, _step) || li > _lesson || (li == _lesson && si > _step) ? null : (li, si);
            ShowStep();
            Screen.Decisions.Refresh();
            return;
        }
        if (!Now.IsInfo) return;
        if (MoveOn()) StartStep();
    }

    /// <summary>Previous: show the step before this one again. Only the coach's words go back - what you did in the
    /// battle stays done, and Next comes forward again to the step you're on.</summary>
    private void Back()
    {
        if (TooSoon()) return;
        (int li, int si) = _review ?? (_lesson, _step);
        if (si > 0) si--;
        else if (li > 0) { li--; si = _lessons[li].Steps.Count - 1; }
        else return;
        _review = (li, si);
        ShowStep();
        Screen.Decisions.Refresh();
    }

    /// <summary>Goes to the next step; false when the lessons are over.</summary>
    private bool MoveOn()
    {
        if (_step + 1 < NowLesson.Steps.Count) { _step++; return true; }
        HowToPlayProgress.Finished(System, NowLesson.Number);
        if (_lesson + 1 < _lessons.Count) { _lesson++; _step = 0; return true; }
        _over = true;
        return false;
    }

    private void OnBeat(FDG.Presentation.PresentationBeat beat)
    {
        if (Auto && beat is FDG.Presentation.Beats.BannerBeat banner) GD.Print("HowTo: banner - " + banner.BannerText);
        if (Auto && beat is FDG.Presentation.Beats.RollOffBeat rollOff) GD.Print("HowTo: roll-off - " + rollOff.Label + " " + string.Join(", ", rollOff.Entries));
        if (beat is not FDG.Presentation.Beats.AttackBeat a) return;
        if (Auto) GD.Print($"HowTo: attack - {(a.IsMelee ? "melee" : "shot")} by {(a.AttackerUnit is { } au ? Unit(au)?.Name ?? "?" : "(no unit)")} with {a.WeaponName}, {a.HitCount:0.#} hits");
        if (a.AttackerUnit is not { } who) return;
        var ids = RoleIds(Now.Unit);
        if (!ids.Contains(who)) return;
        if (a.IsMelee) _foughtSeen = true; else _shotSeen = true;
    }

    public override void _Process(double delta)
    {
        if (Screen.Session.EndedText != null)
        {
            if (!_over) { HowToPlayProgress.Finished(System, _lessons.Count); _over = true; }
            Visible = false;
            return;
        }
        if (Input.IsActionJustPressed("ui_accept") && (Now.IsInfo || _review != null) && _body.Visible
            && GetViewport().GuiGetFocusOwner() is not BaseButton) Advance();

        // Finished steps move on first, so nothing is answered under the step before.
        if (!Now.IsInfo && !_over && StepDone()) { _review = null; if (MoveOn()) StartStep(); return; }
        UpdateRing();
        bool orElse = UsingOrElse;
        if (orElse != _shownOrElse) { _shownOrElse = orElse; if (_review == null) ShowStep(); Screen.Decisions.Refresh(); }
        if (Auto) Drive(delta);
    }

    private bool _shownOrElse;

    /// <summary>Whether the step's own action can't be done right now, so its "or else" actions are open instead.</summary>
    private bool UsingOrElse
    {
        get
        {
            if (Now.OrElse == null) return false;
            IUnit? unit = ActingCandidate();
            if (unit == null) return false;
            return Now.Do switch
            {
                Ask.Shoot => Battle.Book.NothingToShoot(Screen, unit) != null,
                Ask.Charge => !Battle.Book.ChargeReach(Screen, unit).InReach,
                Ask.ToObjective => NearestObjective(unit) is not { } o || Distance(Centre(unit), o) > 15f,
                _ => false,
            };
        }
    }

    /// <summary>The unit the step is about right now: the activating one, the one picked, or the step's role.</summary>
    private IUnit? ActingCandidate()
    {
        if (Screen.Decisions.Current is ActivateDecision { Picked: { } picked }) return picked;
        if (Screen.Decisions.Current is ActionDecision ad) return Unit(ad.ActingUnitID);
        if (Screen.Decisions.Current is MoveDecision md) return Unit(md.MovingUnitID);
        return Now.Unit is { } role ? RoleUnits(role).FirstOrDefault(u => u.GetIsOnBattlefield()) : null;
    }

    private bool StepDone()
    {
        var table = Screen.Session.Table;
        switch (Now.Do)
        {
            case Ask.PickEdge:
                return Screen.Decisions.Current is PlaceDecision || table.Progress.RoundCount != null
                    || MyUnits().Any(u => u.GetIsOnBattlefield());
            case Ask.Deploy:
                return Now.Unit is { } role && RoleUnits(role).Any(u => u.GetIsOnBattlefield());
            case Ask.FreePlay:
                return false;
        }
        if (table.Progress.RoundCount != _baselineRound) { _baselineRound = table.Progress.RoundCount; _activatedAtStart.Clear(); }
        var fresh = MyUnits().Where(u => Activated(u) && !_activatedAtStart.Contains(u.ID)).ToList();
        if (fresh.Count == 0) return false;
        var mine = Now.Unit is { } r ? fresh.Where(u => RoleIds(r).Contains(u.ID)).ToList() : fresh;
        switch (Now.Do)
        {
            case Ask.Shoot: if (_shotSeen) return true; break;
            case Ask.Charge: if (_foughtSeen) return true; break;
            case Ask.ToObjective:
                if (mine.Any(u => NearestObjective(u) is { } o && Distance(Centre(u), o) <= 3.5f)) return true;
                break;
            default:
                if (mine.Count > 0) return true;
                break;
        }
        // The step's unit acted without doing it (its "or else"): wait for its next go, from a fresh baseline.
        foreach (IUnit u in fresh) _activatedAtStart.Add(u.ID);
        return false;
    }

    // ================================================================== the gate (BattleDecisions.IGate)

    public string Refusal => _over || Now.Do == Ask.FreePlay ? "" :
        Now.IsInfo ? "Read the coach first - press Next when you're ready." : "Practice battle: do the step the coach shows.";

    private bool Free => _over || Now.Do == Ask.FreePlay;

    /// <summary>The step's unit has already gone this round (or isn't there): any of your other units may act.</summary>
    private bool StepUnitUnavailable(Decision d) =>
        Now.Unit is { } role && d is ActivateDecision ad && !RoleUnits(role).Any(u => ad.ListMark(u) is { Pickable: true });

    public bool AllowUnit(Decision d, IUnit unit)
    {
        if (Free) return true;
        if (d is not ActivateDecision) return true;
        if (Now.IsInfo || Now.Do is Ask.PickEdge or Ask.Deploy) return false;
        if (Now.Unit is not { } role || StepUnitUnavailable(d)) return true;
        return RoleIds(role).Contains(unit.ID);
    }

    public bool AllowAction(Decision d, string action)
    {
        if (Free) return true;
        IUnit? unit = d switch
        {
            ActivateDecision ad => ad.Picked,
            ActionDecision act => Unit(act.ActingUnitID),
            _ => null,
        };
        if (d is ActionDecision { IsFresh: false }) return true;   // Shoot / Charge / End after a move
        if (d is not (ActivateDecision or ActionDecision)) return true;
        if (Now.IsInfo || Now.Do is Ask.PickEdge or Ask.Deploy) return false;
        if (unit != null && Now.Unit is { } role && !RoleIds(role).Contains(unit.ID)) return StepUnitUnavailable(d) || d is ActionDecision;
        var ok = UsingOrElse && Now.OrElse != null ? Now.OrElse.SelectMany(Actions).ToHashSet() : Actions(Now.Do);
        return ok.Contains(action);
    }

    private static HashSet<string> Actions(Ask ask) => ask switch
    {
        Ask.Hold => new() { "Hold" },
        Ask.Advance => new() { "Advance" },
        Ask.Rush => new() { "Rush" },
        Ask.Charge => new() { "Charge" },
        Ask.Shoot => new() { "Hold" },
        Ask.ToObjective => new() { "Advance", "Rush" },
        _ => new(),
    };

    public bool AllowTablePoint(Decision d, Vector2 table, IUnit? unitThere)
    {
        bool ok = TablePointOk(d, table, unitThere);
        if (Auto) GD.Print($"HowTo: click {d.GetType().Name} at {table.X:0.0},{table.Y:0.0} on {unitThere?.Name ?? "-"} -> {(ok ? "ok" : "refused")}");
        return ok;
    }

    private bool TablePointOk(Decision d, Vector2 table, IUnit? unitThere)
    {
        if (Free) return true;
        switch (d)
        {
            case ActivateDecision:
                return unitThere == null || !Screen.Session.IsMine(unitThere.PlayerID) || AllowUnit(d, unitThere);
            case ZoneDecision:
                return Now.Do != Ask.PickEdge || table.Y < Screen.Table.D / 2;
            case PlaceDecision:
                return Now.Do != Ask.Deploy || Now.Zone is not { } dz || Distance((table.X, table.Y), (dz.X, dz.Z)) <= dz.Radius;
            case MoveDecision:
                if (UsingOrElse) return true;
                if (Now.Do == Ask.Charge && unitThere != null && Screen.Session.IsEnemy(_me, unitThere.PlayerID)) return true;
                return Zone() is not { } z || Distance((table.X, table.Y), (z.X, z.Z)) <= z.Radius;
        }
        return true;
    }

    public bool AllowExtraRows(Decision d) => Free || d is not (ActivateDecision or ActionDecision or ZoneDecision);

    public bool AllowDrag(Decision d) => Free || d is not MoveDecision || Zone() == null || UsingOrElse;

    // ================================================================== the ring

    /// <summary>Where the step wants the unit's middle: its spot, or the objective nearest the unit (lesson 11).</summary>
    private Spot? Zone()
    {
        if (Now.Do == Ask.ToObjective && ActingCandidate() is { } u && NearestObjective(u) is { } o) return new Spot(o.X, o.Z, 2.5f);
        return Now.Zone;
    }

    private string _ringSig = "";

    private void UpdateRing()
    {
        Spot? z = Free || UsingOrElse ? null : Zone();
        string sig = z == null ? "" : $"{z.X:0.0},{z.Z:0.0},{z.Radius:0.0}";
        if (sig == _ringSig) return;
        _ringSig = sig;
        if (z == null) { Screen.Table.ClearGhosts("howto"); return; }
        var lines = new List<BattleTable.GhostLine>();
        const int n = 48;
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n;
            lines.Add(new BattleTable.GhostLine(new Vector2(z.X + z.Radius * Mathf.Cos(a0), z.Z + z.Radius * Mathf.Sin(a0)),
                new Vector2(z.X + z.Radius * Mathf.Cos(a1), z.Z + z.Radius * Mathf.Sin(a1)), HudKit.Lime, 0.18f));
        }
        Screen.Table.SetGhosts("howto", Array.Empty<BattleTable.Ghost>(), lines,
            new[] { new BattleTable.GhostText(z.X, z.Z + z.Radius + 1.2f, "HERE", HudKit.Lime, 0.05f) });
    }

    // ================================================================== the armies on the table

    private IEnumerable<IUnit> MyUnits() => Screen.Session.Table.Units.Objects.Where(u => u.PlayerID.Equals(_me));

    private IUnit? Unit(UnitID id) => Screen.Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(id));

    /// <summary>The units on the table playing a role: by name, on the right side (a joined hero counts as its unit).</summary>
    private IEnumerable<IUnit> RoleUnits(Role role)
    {
        string name = ArmiesFor(System).Name(role);
        bool mine = role is Role.MyShooters or Role.MyMelee or Role.MyHero;
        var side = Screen.Session.Table.Units.Objects.Where(u => mine == u.PlayerID.Equals(_me)).ToList();
        var named = side.Where(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (role == Role.MyMelee)
        {
            // The hero's unit is the melee unit's too, joined or not.
            string hero = ArmiesFor(System).Name(Role.MyHero);
            named.AddRange(side.Where(u => !named.Contains(u) && string.Equals(u.Name, hero, StringComparison.OrdinalIgnoreCase)));
        }
        return named;
    }

    private HashSet<UnitID> RoleIds(Role? role) => role is { } r ? RoleUnits(r).Select(u => u.ID).ToHashSet() : MyUnits().Select(u => u.ID).ToHashSet();

    private bool Activated(IUnit u) => UnitActivation.HasActivated(Screen.Session.Table.Progress, u);

    private static (float X, float Z) Centre(IUnit u)
    {
        var alive = u.Models.Where(m => m.GetIsAlive()).ToList();
        if (alive.Count == 0) return (0, 0);
        return (alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
    }

    private (float X, float Z)? NearestObjective(IUnit u)
    {
        var c = Centre(u);
        var objs = Screen.Session.Table.Objectives.Objects.Select(o => (o.Position.x, o.Position.z)).ToList();
        return objs.Count == 0 ? null : objs.OrderBy(o => Distance(c, o)).First();
    }

    private static float Distance((float X, float Z) a, (float X, float Z) b) => Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    // ================================================================== the test student (--howto-auto)

    /// <summary>
    /// <c>--howto-auto</c>: plays the practice battle the way the coach says, through the same gated inputs a player
    /// uses (so the lock is tested too), and prints each step - the check that every lesson's moment happens.
    /// </summary>
    public static readonly bool Auto = OS.GetCmdlineUserArgs().Contains("--howto-auto");

    private double _driveWait;
    private Decision? _driven;
    private int _drivenTimes;

    private void Drive(double delta)
    {
        _driveWait -= delta;
        if (_driveWait > 0) return;
        _driveWait = 0.35; // longer than PressGapMs, so each Next counts
        foreach (Node n in Screen.FindChildren("*", nameof(ConfirmationDialog), true, false))
            if (n is ConfirmationDialog { Visible: true } cd) { cd.EmitSignal(AcceptDialog.SignalName.Confirmed); return; }
        if (Now.IsInfo) { Advance(); return; }
        Screen.Decisions.Prompt();
        if (Screen.Decisions.Current is not { Done: false } d) return;
        if (!ReferenceEquals(d, _driven)) { _driven = d; _drivenTimes = 0; }
        if (++_drivenTimes > 12) { GD.Print($"HowTo: {d.GetType().Name} not answered the coach's way - autoplay"); d.AutoPlay(Screen); return; }
        var bd = Screen.Decisions;
        switch (d)
        {
            case ZoneDecision when Now.Do == Ask.PickEdge:
                if (d.ConfirmText != null) bd.ConfirmPending(); else bd.TableClick(ScreenAt(Screen.Table.W / 2, 4));
                return;
            case PlaceDecision when Now.Do == Ask.Deploy && Now.Zone is { } dz:
                if (d.ConfirmText != null || _drivenTimes > 2) { bd.ConfirmPending(); return; }
                bd.TableClick(ScreenAt(dz.X, dz.Z));
                return;
            case ActivateDecision ad when !Free:
                if (ad.Picked == null || !AllowUnit(d, ad.Picked))
                {
                    IUnit? want = MyUnits().FirstOrDefault(u => AllowUnit(d, u) && ad.ListMark(u) is { Pickable: true });
                    if (want != null) bd.UnitPicked(want);
                    return;
                }
                PressAllowed(d);
                return;
            case ActionDecision { IsFresh: true } when !Free:
                PressAllowed(d);
                return;
            case MoveDecision when !Free && !UsingOrElse:
                if (d.ConfirmText != null) { bd.ConfirmPending(); return; }
                if (Now.Do == Ask.Charge && Now.Target is { } tr && RoleUnits(tr).FirstOrDefault(u => u.GetIsOnBattlefield()) is { } target)
                { var c = Centre(target); bd.TableClick(ScreenAt(c.X, c.Z)); return; }
                if (Zone() is { } z) { bd.TableClick(ScreenAt(z.X, z.Z)); return; }
                break;
            case ShootDecision when Now.Do == Ask.Shoot && Now.Target is { } st && _drivenTimes <= 4
                && RoleUnits(st).FirstOrDefault(u => u.GetIsOnBattlefield() && u.Models.Any(m => m.GetIsAlive())) is { } aim:
                if (d.ConfirmText != null) { bd.ConfirmPending(); return; }
                var ac = Centre(aim);
                bd.TableClick(ScreenAt(ac.X, ac.Z));
                return;
        }
        d.AutoPlay(Screen);
    }

    private void PressAllowed(Decision d)
    {
        if (d.Actions is not { } slots) { d.AutoPlay(Screen); return; }
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].Enabled && AllowAction(d, slots[i].Name)) { Screen.Decisions.HotKey(i); return; }
        GD.Print($"HowTo: no allowed action on {d.GetType().Name} for lesson {NowLesson.Number} step {_step + 1}");
        d.AutoPlay(Screen);
    }

    private Vector2 ScreenAt(float x, float z) => Screen.Table.Camera.UnprojectPosition(Screen.Table.World(x, z, 0f));
}
