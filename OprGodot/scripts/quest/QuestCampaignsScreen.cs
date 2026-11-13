using FDG;
using FDG.Quest;
using FdgRaylib;
using FdgRaylib.Lobby;
using FdgRaylib.Quest;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Quest;

/// <summary>
/// Quest Campaigns (port step 5; Kai's decision 20, Godot mockup approved item by item 2026-11-12). One screen per Quest
/// game (Star Quest, AoF: Quest) with four views:
/// <list type="bullet">
/// <item><b>Campaigns</b> - the list on the left (progress bar of every mission); the selected campaign on the right:
/// party cards with wounds / power going into the next mission, chapter rows (enemy, theme, difficulty, a box per
/// mission, and Start chapter between chapters), history, and Structure / Journal / Shop / Delete / PLAY NEXT
/// MISSION.</item>
/// <item><b>New Campaign</b> - name, pick 2-4 heroes (heroes already in a campaign are greyed), and how to build it:
/// generate (the book), build it myself, or start from a saved campaign file.</item>
/// <item><b>Structure</b> - the chapters and missions, editable where unplayed: enemy, planned difficulty, theme,
/// objectives (pick or Roll), briefing; add and remove; save as a file to share.</item>
/// <item><b>Journal</b> - the campaign's story, and a page per mission: the game's record plus your account.</item>
/// </list>
/// The shop is the Hero Builder's, limited to the party. PLAY NEXT MISSION readies the heroes and opens the lobby in
/// Mission mode with the party and the chapter's enemy filled in; the result comes back here.
/// All the rules and bookkeeping are <see cref="QuestCampaignSession"/> (FdgApp).
/// </summary>
public partial class QuestCampaignsScreen : Control, IBackGuard
{
    private enum View { List, New, Structure, Journal }

    public EGameSystem System { get; set; } = EGameSystem.GFStarQuest;

    private QuestCampaignSession _s = null!;
    private View _view = View.List;
    private int _selected;

    // New campaign
    private string _newName = "";
    private readonly List<string> _newParty = new();
    private QuestCampaignSession.EBuild _build = QuestCampaignSession.EBuild.Generate;
    private int _planPick = -1;

    // Structure: the campaign being edited, and its file (null = a new campaign not saved yet)
    private QuestCampaign? _draft;
    private string? _draftPath;

    // Journal
    private int _jChapter, _jMission;

    private readonly Dictionary<int, int> _difficultyPick = new();
    private readonly Dictionary<int, string> _enemyPick = new();

    private HBoxContainer _top = null!;
    private MarginContainer _body = null!;
    private Label _status = null!;

    private string Game => GameSystemInfo.DisplayName(System);

    public override void _Ready()
    {
        _s = new QuestCampaignSession(System);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        _top = new HBoxContainer();
        _top.AddThemeConstantOverride("separation", 12);
        root.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_top, 14, 8, 14, 8)));
        _body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_body);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        root.AddChild(_status);
        VisibilityChanged += () => { if (Visible) { _s.Reload(); HudKit.ForgetPictures(); Say(""); Rebuild(); } };
        Rebuild();
    }

    /// <summary>Back / Esc inside a view returns to the list (Structure of a new campaign: to New); from the list it leaves.</summary>
    public bool AllowBack()
    {
        switch (_view)
        {
            case View.List: return true;
            case View.Structure when _draftPath == null: _view = View.New; break;
            case View.Structure: SaveDraft(); _draft = null; _view = View.List; _s.Reload(); break;
            case View.Journal: SaveCurrent(); _view = View.List; break;
            default: _view = View.List; break;
        }
        Rebuild();
        return false;
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    private (string Path, QuestCampaign Campaign)? Current =>
        _s.Campaigns.Count == 0 ? null : _s.Campaigns[Math.Clamp(_selected, 0, _s.Campaigns.Count - 1)];

    private void Rebuild()
    {
        foreach (Control holder in new Control[] { _top, _body })
            foreach (Node c in holder.GetChildren()) { holder.RemoveChild(c); c.QueueFree(); }
        TopBar();
        _body.AddChild(_view switch
        {
            View.New => NewView(),
            View.Structure => StructureView(),
            View.Journal => JournalView(),
            _ => ListView(),
        });
    }

    // ================================================================== top bar

    private void TopBar()
    {
        string back = _view switch { View.New => "Cancel", View.List => "Back", _ => "Back" };
        var b = Ui.Button(back, App.I.Back, _view == View.List ? "Back to the game screen (Esc)." : "Back (Esc).", "BackButton");
        b.Name = "Back";
        b.CustomMinimumSize = new Vector2(110, 44);
        _top.AddChild(b);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        string name = _view switch
        {
            View.New => "NEW CAMPAIGN",
            View.Structure => "STRUCTURE - " + ((_draftPath == null ? _newName.Trim() : _draft?.Name) ?? "").ToUpperInvariant(),
            View.Journal => "JOURNAL - " + (Current?.Campaign.Name ?? "").ToUpperInvariant(),
            _ => $"{QuestShortName} CAMPAIGNS",
        };
        t.AddChild(HudKit.Text(name, 30, HudKit.Fg, HudKit.Exo(800)));
        string sub = _view switch
        {
            View.New => Game,
            View.Structure => StructureSubtitle(),
            View.Journal => "The campaign's story, and a page per mission",
            _ => $"{Game} - {QuestCampaignSession.Plural(_s.Campaigns.Count, "campaign")}",
        };
        t.AddChild(HudKit.Text(sub, 14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(t);
        _top.AddChild(Ui.Grow());

        switch (_view)
        {
            case View.List:
                _top.AddChild(Ui.Button("+ NEW CAMPAIGN", StartNew, "Start a campaign: name it, pick 2-4 heroes, and build its chapters.", "CommitButton")
                    .Also(x => { x.Name = "NewCampaign"; x.CustomMinimumSize = new Vector2(190, 44); }));
                break;
            case View.New:
            {
                string? problem = NewProblem();
                _top.AddChild(Ui.Button("NEXT: STRUCTURE", OpenNewStructure, problem ?? "See the chapters and missions before anything is saved.", "CommitButton")
                    .Also(x => { x.Name = "NextStructure"; x.Disabled = problem != null; x.CustomMinimumSize = new Vector2(190, 44); }));
                break;
            }
            case View.Structure when _draft is { } c:
            {
                if (!c.HasStarted)
                    _top.AddChild(Ui.Button("Re-roll all", () => { if (_s.Reroll(c)) { SaveDraft(); Say("Rolled again, the book's way.", Palette.Lime); } Rebuild(); },
                        "Roll the whole campaign again the book's way: D3+2 chapters of D3+1 missions, objectives from the book's tables.").Also(x => x.Name = "Reroll"));
                _top.AddChild(Ui.Button("Save as a file...", () =>
                {
                    if (_draftPath == null) c.Name = _newName.Trim();
                    string? saved = _s.SavePlan(c);
                    if (saved != null) Say($"Saved the plan as {saved} - share it, or use it with \"Start from a saved campaign\".", Palette.Lime);
                    else Say("Couldn't write the campaign file.", Palette.WoundsRed);
                }, "Save this structure (chapters, missions, objectives, notes - no heroes, no progress) to replay it or give it to a friend.").Also(x => x.Name = "SavePlan"));
                string? problem = c.StructureProblem();
                string done = _draftPath == null ? "CREATE CAMPAIGN" : "DONE";
                _top.AddChild(Ui.Button(done, FinishStructure, problem ?? (_draftPath == null ? "Save the campaign and go to it." : "Back to the campaign (changes are already saved)."), "CommitButton")
                    .Also(x => { x.Name = "CreateCampaign"; x.Disabled = problem != null; x.CustomMinimumSize = new Vector2(190, 44); }));
                break;
            }
            case View.Journal:
                _top.AddChild(Ui.Button("Export journal", ExportJournal, "Write the whole journal (story, every mission's record and your accounts) to a text file beside the campaign.")
                    .Also(x => x.Name = "ExportJournal"));
                break;
        }
    }

    private string QuestShortName => System == EGameSystem.AoFQuest ? "AOF: QUEST" : "STAR QUEST";

    private string StructureSubtitle()
    {
        if (_draft is not { } c) return "";
        int missions = c.Chapters.Sum(ch => ch.Missions);
        string what = $"{QuestCampaignSession.Plural(c.Chapters.Count, "chapter")}, {QuestCampaignSession.Plural(missions, "mission")}";
        return c.HasStarted ? $"{what}. Played ones are locked; change anything not played yet." : $"{what}. Change anything you like.";
    }

    // ================================================================== list

    private Control ListView()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        // Left: the campaigns.
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 6);
        left.AddChild(HudKit.Heading("Campaigns", 14));
        if (_s.Campaigns.Count == 0)
            left.AddChild(ForgeWidgets.Wrapped("No campaigns yet. Press + NEW CAMPAIGN.", 15, HudKit.Dim));
        for (int i = 0; i < _s.Campaigns.Count; i++)
        {
            int idx = i;
            left.AddChild(CampaignCard(_s.Campaigns[i].Campaign, i == _selected, () => { _selected = idx; Rebuild(); }));
        }
        left.AddChild(HudKit.Grow());
        left.AddChild(ForgeWidgets.Wrapped("The bar is every mission in the campaign: teal = won, lime = next.", 12, HudKit.Dim));
        var leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftScroll.AddChild(left);
        var lp = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(leftScroll, 12, 10, 12, 10));
        lp.CustomMinimumSize = new Vector2(330, 0);
        row.AddChild(lp);

        // Right: the selected campaign.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        if (Current is { } cur) Detail(right, cur.Path, cur.Campaign);
        else right.AddChild(ForgeWidgets.Wrapped("Start a campaign with + NEW CAMPAIGN (top right). A campaign is a run of chapters, each a few missions, " +
                                                "played by the same party of 2-4 heroes - the Campaign Builder's rules.", 16, HudKit.Dim));
        var rp = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(right, 16, 12, 16, 12));
        rp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(rp);
        return row;
    }

    private Control CampaignCard(QuestCampaign c, bool selected, Action click)
    {
        var b = new Button { Name = ForgeWidgets.SafeName("Campaign_" + c.Name), CustomMinimumSize = new Vector2(0, 96), FocusMode = FocusModeEnum.None };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 };
        if (selected) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 1);
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 10; col.OffsetRight = -10; col.OffsetTop = 6; col.OffsetBottom = -6;
        col.AddChild(QuestWidgets.Line(c.Name, 18, selected ? HudKit.Lime : HudKit.Fg, 280, HudKit.Bold));
        col.AddChild(QuestWidgets.Line(QuestCampaignSession.ProgressLine(c), 14, c.Finished ? Palette.Lime : HudKit.Fg, 280));
        var names = QuestCampaignSession.Party(c).Select(p => p.Hero?.Name ?? "?");
        col.AddChild(QuestWidgets.Line(string.Join(", ", names), 12, HudKit.Dim, 280));
        var marks = QuestCampaignSession.Marks(c);
        float box2 = marks.Count > 0 ? Math.Clamp((290f - 2 * marks.Count) / marks.Count, 4, 14) : 10;
        col.AddChild(QuestWidgets.Marks(marks, box2, 2));
        b.AddChild(col);
        b.Pressed += click;
        return b;
    }

    private void Detail(VBoxContainer col, string path, QuestCampaign c)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(HudKit.Text(c.Name.ToUpperInvariant(), 26, HudKit.Fg, HudKit.Exo(800)));
        head.AddChild(HudKit.Text(QuestCampaignSession.StatusLine(c), 17, c.Finished ? Palette.Lime : Palette.HeroGold, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        head.AddChild(Ui.Grow());
        bool shop = QuestCampaignSession.ShopOpen(c);
        head.AddChild(HudKit.Text(c.Finished ? "" : shop ? "Shop open" : "Shop closed until this chapter ends", 14, shop ? Palette.Lime : HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        col.AddChild(head);

        col.AddChild(HudKit.Heading("Party - hover a hero for its full sheet", 13));
        var party = new HFlowContainer();
        party.AddThemeConstantOverride("h_separation", 8);
        party.AddThemeConstantOverride("v_separation", 8);
        foreach (var (file, hero) in QuestCampaignSession.Party(c)) party.AddChild(QuestWidgets.PartyCard(file, hero, c.Active != null, System));
        col.AddChild(party);
        if (c.Active != null) col.AddChild(HudKit.Text("Green bar = wounds, blue = power, as they are going into the next mission.", 12, HudKit.Dim, HudKit.Semi));

        col.AddChild(HudKit.Heading("Chapters - any order; a started chapter must be finished first", 13));
        for (int i = 0; i < c.Chapters.Count; i++) col.AddChild(ChapterRow(path, c, i));

        col.AddChild(HudKit.Heading("History", 13));
        if (c.History.Count == 0) col.AddChild(HudKit.Text("Nothing yet.", 14, HudKit.Dim, HudKit.Semi));
        var hist = new VBoxContainer();
        hist.AddThemeConstantOverride("separation", 2);
        foreach (string line in Enumerable.Reverse(c.History).Take(6))
            hist.AddChild(ForgeWidgets.Wrapped(line, 13, HudKit.Dim));
        var hs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        hist.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hs.AddChild(hist);
        col.AddChild(hs);

        // Bottom row.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Ui.Button("Structure", () => OpenStructure(path, c), "Change the chapters and missions you haven't played yet, or save the plan as a file.").Also(x => x.Name = "Structure"));
        row.AddChild(Ui.Button("Journal", () => OpenJournal(c), "The campaign's story, and what happened in every mission.").Also(x => x.Name = "Journal"));
        row.AddChild(Ui.Button("Shop", () => OpenShop(c), shop ? "Buy and sell for the party." : c.Finished ? "The campaign is complete." : "The shop opens only between chapters - finish this one first (book).")
            .Also(x => { x.Name = "Shop"; x.Disabled = !shop; }));
        row.AddChild(Ui.Button("Delete", () => AskDelete(path, c), "Delete this campaign (asks first). The heroes are kept, with everything they carry, and are free again.", "BackButton").Also(x => x.Name = "Delete"));
        string? cant = QuestCampaignSession.CantPlay(c);
        var next = HudKit.Text(cant == null ? "Next: " + QuestCampaignSession.NextLine(c) : cant, 15, cant == null ? HudKit.Fg : HudKit.Dim, HudKit.Semi);
        next.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        next.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        next.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        next.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(next);
        row.AddChild(Ui.Button("PLAY NEXT MISSION", () => PlayNext(path, c),
            cant ?? "Opens the lobby with the party and this chapter's enemy filled in. Difficulty, objectives and the heroes' wounds and power carry over; the result comes back here.",
            "CommitButton").Also(x => { x.Name = "PlayNext"; x.Disabled = cant != null; x.CustomMinimumSize = new Vector2(240, 52); x.AddThemeFontSizeOverride("font_size", 19); }));
        col.AddChild(row);
    }

    private Control ChapterRow(string path, QuestCampaign c, int i)
    {
        CampaignChapter ch = c.Chapters[i];
        var state = QuestCampaignSession.StateOf(c, i);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(QuestWidgets.Line($"Chapter {ch.Number}", 16, HudKit.Fg, 96, HudKit.Bold));
        bool canStart = c.CanStart(i);
        bool needsEnemy = QuestCampaignSession.NeedsEnemy(ch);
        if (canStart && needsEnemy)
        {
            var books = QuestEnemyCollections.BooksFor(System);
            var ob = new OptionButton { Name = $"Enemy{i}", CustomMinimumSize = new Vector2(190, 0), TooltipText = "This chapter's enemy force: a whole army book. The waves are drawn from it to each wave's points." };
            ob.AddItem("Enemy army...", 0);
            for (int k = 0; k < books.Count; k++) ob.AddItem(books[k].Faction, k + 1);
            int sel = _enemyPick.TryGetValue(i, out string? picked) ? books.ToList().FindIndex(x => x.FileName == picked) + 1 : 0;
            ob.Selected = Math.Max(0, sel);
            ob.ItemSelected += k => { if (k > 0) _enemyPick[i] = books[(int)k - 1].FileName; else _enemyPick.Remove(i); };
            row.AddChild(ob);
        }
        else row.AddChild(QuestWidgets.Line(QuestCampaignSession.EnemyLabel(ch), 15, HudKit.Fg, 190));
        string detail = (ch.Theme.Trim().Length > 0 ? $"\"{ch.Theme.Trim()}\" - " : "")
            + (state == QuestCampaignSession.EChapterState.NotStarted
                ? (ch.PlannedDifficulty > 0 ? $"{QuestJournal.DifficultyName(ch.PlannedDifficulty)} planned - " : "") + QuestCampaignSession.Plural(ch.Missions, "mission")
                : QuestJournal.DifficultyName(ch.Difficulty) + (state == QuestCampaignSession.EChapterState.Done ? $" - won {ch.MissionsWon} of {ch.Missions}" : ""))
            + (ch.Restarts > 0 ? $" - restarted {ch.Restarts}x" : "");
        var d = HudKit.Text(detail, 14, HudKit.Dim, HudKit.Semi);
        d.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        d.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        d.CustomMinimumSize = new Vector2(60, 0);
        d.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(d);
        row.AddChild(QuestWidgets.Marks(QuestCampaignSession.Marks(c, i)).Also(m => m.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        if (canStart)
        {
            var diff = new OptionButton { Name = $"Difficulty{i}", CustomMinimumSize = new Vector2(150, 0),
                TooltipText = "The difficulty of every mission in this chapter (book: Beginner 1 to Legendary 4). Enemy waves are 25% / 50% / 100% / 200% of the party's points; hazard pay is 5 x the difficulty." };
            for (int k = 0; k < 4; k++) diff.AddItem(QuestJournal.Difficulties[k], k + 1);
            int pick = _difficultyPick.TryGetValue(i, out int p) ? p : ch.PlannedDifficulty > 0 ? ch.PlannedDifficulty : 1;
            diff.Selected = pick - 1;
            diff.ItemSelected += k => _difficultyPick[i] = (int)k + 1;
            row.AddChild(diff);
            row.AddChild(Ui.Button("Start chapter", () =>
            {
                int level = _difficultyPick.TryGetValue(i, out int lv) ? lv : ch.PlannedDifficulty > 0 ? ch.PlannedDifficulty : 1;
                string? book = _enemyPick.TryGetValue(i, out string? eb) ? eb : null;
                if (needsEnemy && book == null) { Say("Pick this chapter's enemy army first.", Palette.HeroGold); return; }
                string? error = _s.StartChapter(path, c, i, level, book);
                _difficultyPick.Clear(); _enemyPick.Clear();
                if (error != null) Say(error, Palette.WoundsRed);
                else Say($"Chapter {ch.Number} started - {QuestJournal.DifficultyName(level)}.", Palette.Lime);
                ReselectAfterReload(path);
                Rebuild();
            }, "Start this chapter. Its missions are played one after another until it is done. The shop closes until it ends.", "CommitButton").Also(x => x.Name = $"StartChapter{i}"));
        }
        else
        {
            var (text, colour) = state switch
            {
                QuestCampaignSession.EChapterState.Done => ("DONE", Palette.Lime),
                QuestCampaignSession.EChapterState.InProgress => ("IN PROGRESS", Palette.HeroGold),
                _ => ("NOT STARTED", HudKit.Dim),
            };
            row.AddChild(HudKit.Chip(text, colour, false, 12).Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        }
        var panel = HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = state == QuestCampaignSession.EChapterState.InProgress ? HudKit.Lime : HudKit.Edge, Cut = 6 }, HudKit.Pad(row, 10, 5, 10, 5));
        if (state == QuestCampaignSession.EChapterState.Done) panel.Modulate = new Color(1, 1, 1, 0.75f);
        return panel;
    }

    private void ReselectAfterReload(string path)
    {
        int i = _s.Campaigns.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _selected = i;
    }

    // ================================================================== list actions

    private void PlayNext(string path, QuestCampaign c)
    {
        if (_s.PrepareNextMission(path, c) is null) { Say(QuestCampaignSession.CantPlay(c) ?? "Can't play that mission.", Palette.WoundsRed); return; }
        if (LobbySession.TryHost(System, EGameMode.StarQuestMission, out LobbySession? session, out string error) && session != null)
            App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = session);
        else
        {
            QuestCampaignSession.Pending = null;
            App.I.Message("Could not start the mission", error);
        }
    }

    private void OpenShop(QuestCampaign c) =>
        App.I.Open<HeroBuilderScreen>(Scenes.HeroBuilder, s => { s.System = System; s.CampaignParty = c.HeroFiles.ToList(); s.CampaignName = c.Name; });

    private void AskDelete(string path, QuestCampaign c)
    {
        var dlg = new ConfirmationDialog
        {
            Title = "Delete campaign?",
            DialogText = $"Delete the campaign \"{c.Name}\"? Its journal goes with it. This can't be undone.\nThe heroes are kept, with everything they carry, and are free to join another campaign.",
            OkButtonText = "Delete",
        };
        dlg.Confirmed += () =>
        {
            string? error = _s.Delete(path, c);
            if (error != null) Say(error, Palette.WoundsRed); else Say($"Deleted \"{c.Name}\".");
            _selected = 0;
            Rebuild();
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    // ================================================================== new campaign

    private void StartNew()
    {
        _newName = "";
        _newParty.Clear();
        _build = QuestCampaignSession.EBuild.Generate;
        _planPick = -1;
        _draft = null;
        _draftPath = null;
        _view = View.New;
        Say("");
        Rebuild();
    }

    private string? NewProblem()
    {
        string? p = _s.NewProblem(_newName, _newParty);
        if (p == null && _build == QuestCampaignSession.EBuild.Saved && _planPick < 0) return "Pick the saved campaign to start from.";
        return p;
    }

    private Control NewView()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        nameRow.AddChild(HudKit.Text("Name", 17, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        var name = new LineEdit { Name = "CampaignName", Text = _newName, PlaceholderText = "Name your campaign", CustomMinimumSize = new Vector2(420, 40), MaxLength = 40 };
        name.TextChanged += t => { _newName = t; RefreshTopOnly(); };
        nameRow.AddChild(name);
        col.AddChild(nameRow);

        var heroes = _s.HeroesForNew();
        col.AddChild(HudKit.Heading($"Party - pick 2 to 4 heroes ({_newParty.Count} picked)", 14));
        var cards = new HFlowContainer();
        cards.AddThemeConstantOverride("h_separation", 8);
        cards.AddThemeConstantOverride("v_separation", 8);
        foreach (var (path, hero, taken) in heroes)
        {
            string p = path;
            bool on = _newParty.Contains(p);
            cards.AddChild(QuestWidgets.PickCard(hero, taken != null ? $"in \"{taken.Name}\"" : on ? "IN PARTY" : $"{hero.Points} pts",
                taken != null ? Palette.HeroGold : on ? Palette.Lime : HudKit.Dim, on, taken != null, () =>
                {
                    if (on) _newParty.Remove(p);
                    else if (_newParty.Count < 4) _newParty.Add(p);
                    else Say("A party is 2 to 4 heroes - take one out first.", Palette.HeroGold);
                    Rebuild();
                }));
        }
        if (heroes.Count == 0) cards.AddChild(ForgeWidgets.Wrapped($"No heroes yet - make some in the Hero Builder first.", 15, HudKit.Dim));
        var cs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 200) };
        cards.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cs.AddChild(cards);
        col.AddChild(cs);
        var heroLine = new HBoxContainer();
        heroLine.AddThemeConstantOverride("separation", 10);
        heroLine.AddChild(ForgeWidgets.Wrapped("Heroes from any faction can party up. A hero in another campaign is greyed: a hero belongs to one campaign (finished campaigns free them).", 13, HudKit.Dim)
            .Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        heroLine.AddChild(Ui.Button("Hero Builder...", () => App.I.Open<HeroBuilderScreen>(Scenes.HeroBuilder, s => s.System = System), "Make a new hero, then come back.").Also(x => x.Name = "OpenHeroBuilder"));
        col.AddChild(heroLine);

        col.AddChild(HudKit.Heading("How to build it", 14));
        var how = new HBoxContainer();
        how.AddThemeConstantOverride("separation", 8);
        how.AddChild(HowCard("Generate everything", "The book's way: D3+2 chapters, D3+1 missions each, objectives rolled. You can still change any of it on the next screen.", QuestCampaignSession.EBuild.Generate));
        how.AddChild(HowCard("Build it myself", "Start with one empty chapter. Add chapters and missions, pick or roll each objective.", QuestCampaignSession.EBuild.Craft));
        how.AddChild(HowCard("Start from a saved campaign", "A campaign file - yours, or one a friend shared. Its chapters and missions, with a fresh party.", QuestCampaignSession.EBuild.Saved));
        col.AddChild(how);
        if (_build == QuestCampaignSession.EBuild.Saved)
        {
            var plans = _s.Plans();
            var ob = new OptionButton { Name = "PlanPick", CustomMinimumSize = new Vector2(420, 0) };
            ob.AddItem(plans.Count == 0 ? "No saved campaigns yet (Save as a file... on a Structure)" : "Pick a saved campaign...", 0);
            for (int i = 0; i < plans.Count; i++)
                ob.AddItem($"{plans[i].Plan.Name} - {QuestCampaignSession.Plural(plans[i].Plan.Chapters.Count, "chapter")}, {QuestCampaignSession.Plural(plans[i].Plan.Chapters.Sum(x => x.Missions), "mission")}", i + 1);
            ob.Selected = _planPick + 1;
            ob.ItemSelected += k => { _planPick = (int)k - 1; RefreshTopOnly(); };
            col.AddChild(ob);
            col.AddChild(ForgeWidgets.Wrapped($"Campaign files live in heroes\\{_s.GameType}\\campaign plans. Drop a friend's file there to use it.", 12, HudKit.Dim));
        }
        string? problem = NewProblem();
        var line = HudKit.Text(problem ?? "Ready - NEXT shows the structure before anything is saved.", 15, problem != null ? Palette.HeroGold : Palette.Lime, HudKit.Semi);
        col.AddChild(line);
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(col, 16, 12, 16, 12));
    }

    /// <summary>Typing the name only changes the top bar's NEXT (rebuilding the page would lose the caret).</summary>
    private void RefreshTopOnly()
    {
        foreach (Node c in _top.GetChildren()) { _top.RemoveChild(c); c.QueueFree(); }
        TopBar();
    }

    private Control HowCard(string title, string text, QuestCampaignSession.EBuild what)
    {
        bool on = _build == what;
        var b = new Button { Name = ForgeWidgets.SafeName("How_" + title), CustomMinimumSize = new Vector2(0, 92), FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = on ? HudKit.Lime : HudKit.Edge, Cut = 8 };
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 10; col.OffsetRight = -10; col.OffsetTop = 8; col.OffsetBottom = -8;
        col.AddChild(HudKit.Text(title, 17, on ? HudKit.Lime : HudKit.Fg, HudKit.Bold).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
        col.AddChild(ForgeWidgets.Wrapped(text, 13, HudKit.Dim).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
        b.AddChild(col);
        b.Pressed += () => { _build = what; if (what != QuestCampaignSession.EBuild.Saved) _planPick = -1; _draft = null; Rebuild(); };
        return b;
    }

    private void OpenNewStructure()
    {
        if (NewProblem() != null) return;
        // Back from the structure to the name-and-party page and NEXT again keeps the structure as it was.
        if (_draft == null || _draftPath != null)
        {
            QuestCampaign? plan = _build == QuestCampaignSession.EBuild.Saved && _planPick >= 0 ? _s.Plans().ElementAtOrDefault(_planPick).Plan : null;
            _draft = _s.Draft(_newName, _newParty, _build, plan);
            _draftPath = null;
        }
        _view = View.Structure;
        Say("");
        Rebuild();
    }

    private void OpenStructure(string path, QuestCampaign c)
    {
        _draft = c;
        _draftPath = path;
        _view = View.Structure;
        Say("");
        Rebuild();
    }

    private void FinishStructure()
    {
        if (_draft is not { } c) return;
        if (_draftPath == null)
        {
            string? path = _s.Create(c, _newName, _newParty, out string? error);
            if (path == null) { Say(error ?? "Couldn't save the campaign.", Palette.WoundsRed); return; }
            ReselectAfterReload(path);
            Say($"\"{c.Name}\" created. Start its first chapter below.", Palette.Lime);
        }
        else
        {
            SaveDraft();
            _s.Reload();
            ReselectAfterReload(_draftPath);
            Say("Structure saved.", Palette.Lime);
        }
        _draft = null;
        _draftPath = null;
        _view = View.List;
        Rebuild();
    }

    /// <summary>A saved campaign's structure saves as it is edited; a new one only on CREATE.</summary>
    private void SaveDraft()
    {
        if (_draft == null || _draftPath == null) return;
        if (!QuestCampaignSession.Save(_draftPath, _draft)) Say("Couldn't save the campaign file.", Palette.WoundsRed);
    }

    // ================================================================== structure

    private Control StructureView()
    {
        if (_draft is not { } c) return new Control();
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        for (int i = 0; i < c.Chapters.Count; i++) col.AddChild(ChapterEditor(c, i));
        col.AddChild(Ui.Button("+ Add chapter", () => { c.AddChapter(); Changed(); }, "Add a chapter of one mission at the end.").Also(x => { x.Name = "AddChapter"; x.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; }));
        col.AddChild(ForgeWidgets.Wrapped(QuestJournal.BookStructureLine(System) + " Any other number of chapters or missions is a house rule. "
            + "Book: the secondary objective is set up when the primary is done, so it stays \"roll when reached\" unless you pick one (a house rule). "
            + "A difficulty left at \"Pick when it starts\" is picked with Start chapter.", 13, HudKit.Dim));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(col);
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(scroll, 14, 10, 14, 10));
    }

    private void Changed()
    {
        SaveDraft();
        Rebuild();
    }

    private static readonly string[] PlannedDifficulties = { "Pick when it starts", "Beginner", "Intermediate", "Expert", "Legendary" };

    private Control ChapterEditor(QuestCampaign c, int i)
    {
        CampaignChapter ch = c.Chapters[i];
        bool editable = c.CanEditChapter(i);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(HudKit.Text($"Chapter {ch.Number}", 18, HudKit.Fg, HudKit.Exo(800)).Also(l => { l.CustomMinimumSize = new Vector2(110, 0); l.SizeFlagsVertical = SizeFlags.ShrinkCenter; }));
        var state = QuestCampaignSession.StateOf(c, i);
        if (state != QuestCampaignSession.EChapterState.NotStarted)
            head.AddChild(HudKit.Chip(state == QuestCampaignSession.EChapterState.Done ? "PLAYED - LOCKED" : "IN PROGRESS", state == QuestCampaignSession.EChapterState.Done ? Palette.Lime : Palette.HeroGold, false, 12)
                .Also(x => x.SizeFlagsVertical = SizeFlags.ShrinkCenter));

        if (!editable)
        {
            string what = $"vs {QuestCampaignSession.EnemyLabel(ch)} - {QuestJournal.DifficultyName(ch.Difficulty)}" + (ch.Theme.Trim().Length > 0 ? $" - \"{ch.Theme.Trim()}\"" : "");
            head.AddChild(HudKit.Text(what, 15, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        }
        else
        {
            head.AddChild(Label("Enemy"));
            head.AddChild(EnemyChoice(ch, i));
            head.AddChild(Label("Plan difficulty"));
            var diff = new OptionButton { Name = $"PlanDifficulty{i}", CustomMinimumSize = new Vector2(170, 0),
                TooltipText = "The difficulty of every mission in this chapter. \"Pick when it starts\" asks at Start chapter." };
            foreach (string d in PlannedDifficulties) diff.AddItem(d);
            diff.Selected = Math.Clamp(ch.PlannedDifficulty, 0, 4);
            diff.ItemSelected += k => { ch.PlannedDifficulty = (int)k; SaveDraft(); };
            head.AddChild(diff);
            head.AddChild(Label("Theme"));
            var theme = new LineEdit { Name = $"Theme{i}", Text = ch.Theme, PlaceholderText = "optional - where, who, why", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 200 };
            theme.TextChanged += t => ch.Theme = t;
            theme.FocusExited += SaveDraft;
            head.AddChild(theme);
            head.AddChild(Ui.Button("Remove", () => { if (c.RemoveChapter(i)) Changed(); },
                c.Chapters.Count <= 1 ? "A campaign keeps at least one chapter." : "Remove this chapter and its missions.", "BackButton").Also(x => x.Disabled = c.Chapters.Count <= 1));
        }
        if (editable) head.AddChild(HudKit.Gap(0));
        box.AddChild(head);

        for (int m = 0; m < ch.MissionPlan.Count; m++) box.AddChild(MissionEditor(c, i, m));
        if (c.CanAddMission(i))
            box.AddChild(HudKit.Pad(Ui.Button("+ Add mission", () => { if (c.AddMission(i)) Changed(); }, "Add a mission at the end of this chapter.").Also(x => { x.Name = $"AddMission{i}"; x.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; }), 24, 0, 0, 0));
        var panel = HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8 }, HudKit.Pad(box, 12, 8, 12, 8));
        if (!editable && state == QuestCampaignSession.EChapterState.Done) panel.Modulate = new Color(1, 1, 1, 0.7f);
        return panel;
    }

    private static Label Label(string s) => HudKit.Text(s, 14, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter);

    private OptionButton EnemyChoice(CampaignChapter ch, int i)
    {
        var books = QuestEnemyCollections.BooksFor(System);
        var catalog = ArmyCatalogCache.Current;
        var armies = catalog.IsLoaded ? ArmyPaths.EntriesFor(System, catalog.Entries).ToList() : new List<ArmyCatalogEntry>();
        var ob = new OptionButton { Name = $"ChapterEnemy{i}", CustomMinimumSize = new Vector2(220, 0),
            TooltipText = $"The enemy force this chapter is fought against: a whole {Game} army book (the waves are drawn from it, rebuilt from the current book every mission), or one of your saved armies." };
        ob.AddItem("Pick when it starts");
        foreach (var b in books) ob.AddItem($"{b.Faction} (whole book)");
        foreach (var a in armies) ob.AddItem($"{a.FileName} ({a.Faction})");
        int sel = 0;
        if (ch.EnemyBook.Length > 0) sel = books.ToList().FindIndex(b => b.FileName == ch.EnemyBook) + 1;
        else if (ch.EnemyArmy.Length > 0) { int k = armies.FindIndex(a => string.Equals(a.Path, ch.EnemyArmy, StringComparison.OrdinalIgnoreCase)); sel = k >= 0 ? 1 + books.Count + k : 0; }
        ob.Selected = Math.Max(0, sel);
        ob.ItemSelected += k =>
        {
            int idx = (int)k;
            if (idx == 0) QuestCampaignSession.ClearEnemy(ch);
            else if (idx <= books.Count) _s.SetEnemyBook(ch, books[idx - 1].FileName);
            else { var a = armies[idx - 1 - books.Count]; QuestCampaignSession.SetEnemyArmy(ch, a.Path, $"{a.FileName} ({a.Faction})"); }
            SaveDraft();
        };
        return ob;
    }

    private Control MissionEditor(QuestCampaign c, int chapter, int m)
    {
        CampaignMission mission = c.Chapters[chapter].MissionPlan[m];
        bool editable = c.CanEditMission(chapter, m);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(QuestWidgets.Line($"Mission {m + 1}", 15, editable ? HudKit.Fg : HudKit.Dim, 90));
        if (!editable)
        {
            row.AddChild(HudKit.Text($"{QuestJournal.PrimaryName(mission.Primary)} / {QuestJournal.SecondaryName(mission.Secondary)} - played, locked", 14, HudKit.Dim, HudKit.Semi));
            return HudKit.Pad(row, 24, 0, 0, 0);
        }
        var prim = new OptionButton { Name = $"Primary{chapter}_{m}", CustomMinimumSize = new Vector2(230, 0),
            TooltipText = "The primary objective, from the book's Primary Objective table (D6). \"Roll when it starts\" rolls it at mission set-up." };
        for (int k = 0; k < 7; k++) prim.AddItem(k == 0 ? "Primary: roll when it starts" : "Primary: " + QuestJournal.PrimaryName(k));
        prim.Selected = Math.Clamp(mission.Primary, 0, 6);
        prim.ItemSelected += k => { mission.Primary = (int)k; SaveDraft(); };
        row.AddChild(prim);
        var sec = new OptionButton { Name = $"Secondary{chapter}_{m}", CustomMinimumSize = new Vector2(250, 0),
            TooltipText = "The secondary objective, from the book's Secondary Objective table (D6). The book sets it up once the primary is done." };
        for (int k = 0; k < 7; k++) sec.AddItem(k == 0 ? "Secondary: roll when reached" : "Secondary: " + QuestJournal.SecondaryName(k));
        sec.Selected = Math.Clamp(mission.Secondary, 0, 6);
        sec.ItemSelected += k => { mission.Secondary = (int)k; SaveDraft(); };
        row.AddChild(sec);
        row.AddChild(Ui.Button("Roll", () => { _s.RollMission(mission); Changed(); }, "Roll the primary objective now, a D6 on the book's table. The secondary is rolled in play once the primary is done, unless you pick one.").Also(x => x.Name = $"Roll{chapter}_{m}"));
        var notes = new LineEdit { Text = mission.Notes, PlaceholderText = "Briefing / notes...", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 2000 };
        notes.TextChanged += t => mission.Notes = t;
        notes.FocusExited += SaveDraft;
        row.AddChild(notes);
        row.AddChild(Ui.Button("X", () => { if (c.RemoveMission(chapter, m)) Changed(); },
            c.Chapters[chapter].MissionPlan.Count <= 1 ? "A chapter keeps at least one mission - remove the chapter instead." : "Remove this mission.", "BackButton")
            .Also(x => x.Disabled = c.Chapters[chapter].MissionPlan.Count <= 1));
        return HudKit.Pad(row, 24, 0, 0, 0);
    }

    // ================================================================== journal

    private void OpenJournal(QuestCampaign c)
    {
        _view = View.Journal;
        if (c.Active is { } a) { _jChapter = c.CurrentChapter; _jMission = Math.Min(a.MissionsWon, a.MissionPlan.Count - 1); }
        else
        {
            var last = QuestCampaignSession.LastRecord(c);
            _jChapter = last == null ? 0 : c.Chapters.IndexOf(last.Value.Chapter);
            _jMission = last?.Mission ?? 0;
        }
        Say("");
        Rebuild();
    }

    private void SaveCurrent()
    {
        if (Current is { } cur && !QuestCampaignSession.Save(cur.Path, cur.Campaign)) Say("Couldn't save the campaign file.", Palette.WoundsRed);
    }

    private Control JournalView()
    {
        if (Current is not { } cur) return new Control();
        QuestCampaign c = cur.Campaign;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var story = new VBoxContainer();
        story.AddThemeConstantOverride("separation", 6);
        story.AddChild(HudKit.Heading("The campaign", 14));
        story.AddChild(HudKit.Text("The story so far - who the heroes are and why they fight.", 13, HudKit.Dim, HudKit.Semi));
        var storyText = new TextEdit { Name = "Story", Text = c.Story, SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Write the campaign's story..." };
        storyText.TextChanged += () => c.Story = storyText.Text;
        storyText.FocusExited += SaveCurrent;
        story.AddChild(storyText);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(story, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(420, 0)));

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        list.AddChild(HudKit.Heading("Missions", 14));
        CampaignMission? next = c.NextMission;
        if (_jChapter < 0 || _jChapter >= c.Chapters.Count) _jChapter = 0;
        for (int i = 0; i < c.Chapters.Count; i++)
        {
            CampaignChapter ch = c.Chapters[i];
            list.AddChild(HudKit.Text($"CHAPTER {ch.Number}", 13, Palette.Header, HudKit.Tracked(HudKit.Bold, 2)));
            for (int m = 0; m < ch.MissionPlan.Count; m++)
            {
                int ci = i, mi = m;
                CampaignMission mission = ch.MissionPlan[m];
                string mark = mission.Records.Count > 0 ? (mission.Won ? " - won" : " - failed") : ReferenceEquals(mission, next) ? " - next" : "";
                bool sel = i == _jChapter && m == _jMission;
                var b = new Button { Text = $"Mission {m + 1}{mark}", Flat = true, Alignment = HorizontalAlignment.Left, ToggleMode = true, ButtonPressed = sel, FocusMode = FocusModeEnum.None, Name = $"J{i}_{m}" };
                b.AddThemeColorOverride("font_color", mission.Records.Count > 0 ? HudKit.Fg : HudKit.Dim);
                if (sel) b.AddThemeColorOverride("font_pressed_color", Palette.Lime);
                b.Pressed += () => { SaveCurrent(); _jChapter = ci; _jMission = mi; Rebuild(); };
                list.AddChild(b);
            }
        }
        var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ls.AddChild(list);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(240, 0)));

        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 6);
        if (c.Chapters.Count > 0)
        {
            CampaignChapter ch = c.Chapters[_jChapter];
            _jMission = Math.Clamp(_jMission, 0, Math.Max(0, ch.MissionPlan.Count - 1));
            CampaignMission mission = ch.MissionPlan[_jMission];
            page.AddChild(HudKit.Heading($"Chapter {ch.Number} - mission {_jMission + 1}", 14));
            if (ch.Theme.Trim().Length > 0) page.AddChild(HudKit.Text("Theme: " + ch.Theme.Trim(), 14, HudKit.Dim, HudKit.Semi));
            page.AddChild(HudKit.Text($"Planned: primary {QuestJournal.PrimaryName(mission.Primary)}, secondary {QuestJournal.SecondaryName(mission.Secondary)}.", 14, HudKit.Dim, HudKit.Semi));
            page.AddChild(HudKit.Text("What happened - written by the game", 13, HudKit.Dim, HudKit.Semi));
            var rec = new VBoxContainer();
            rec.AddThemeConstantOverride("separation", 2);
            if (mission.Records.Count == 0) rec.AddChild(HudKit.Text("Not played yet.", 15, HudKit.Dim, HudKit.Semi));
            for (int r = 0; r < mission.Records.Count; r++)
            {
                if (mission.Records.Count > 1) rec.AddChild(HudKit.Text($"Attempt {r + 1}", 15, HudKit.Fg, HudKit.Bold));
                foreach (string line in QuestJournal.RecordLines(mission.Records[r], System)) rec.AddChild(ForgeWidgets.Wrapped(line, 15, HudKit.Fg));
            }
            page.AddChild(HudKit.Panel(new ChamferBox { Fill = new Color(0.03f, 0.05f, 0.06f, 0.9f), Border = HudKit.Edge, Cut = 6 }, HudKit.Pad(rec, 12, 8, 12, 8)));
            page.AddChild(HudKit.Text(mission.Records.Count > 0 ? "Your account - the story of the mission" : "The briefing - before it's played (it becomes the start of your account)", 13, HudKit.Dim, HudKit.Semi));
            var notes = new TextEdit { Name = "MissionNotes", Text = mission.Notes, SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Write what happened..." };
            notes.TextChanged += () => mission.Notes = notes.Text;
            notes.FocusExited += SaveCurrent;
            page.AddChild(notes);
        }
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(page, 14, 10, 14, 10)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return row;
    }

    private void ExportJournal()
    {
        if (Current is not { } cur) return;
        string? file = _s.ExportJournal(cur.Path, cur.Campaign, out string? error);
        if (file != null) Say("Journal saved to " + file, Palette.Lime);
        else Say("Couldn't write the journal file: " + error, Palette.WoundsRed);
    }

    public override void _ExitTree() { if (_view == View.Journal) SaveCurrent(); else SaveDraft(); }

    // ================================================================== screenshot driver

    /// <summary>
    /// Drives the screen for checking without a person: "new:Name", "pick:N" (first N free heroes), "build:gen|craft",
    /// "next", "create", "select:i", "start:i:difficulty[:book file]", "play", "journal", "structure", "shop".
    /// </summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        switch (verb)
        {
            case "new": StartNew(); _newName = arg; break;
            case "pick" when int.TryParse(arg, out int n):
                _newParty.Clear();
                _newParty.AddRange(_s.HeroesForNew().Where(h => h.TakenBy == null).Take(n).Select(h => h.Path));
                break;
            case "build": _build = arg == "craft" ? QuestCampaignSession.EBuild.Craft : QuestCampaignSession.EBuild.Generate; _draft = null; break;
            case "next": OpenNewStructure(); return;
            case "create": FinishStructure(); return;
            case "select" when int.TryParse(arg, out int i): _selected = i; _view = View.List; break;
            case "start" when Current is { } cur && arg.Split(':') is { Length: >= 2 } parts && int.TryParse(parts[0], out int ch) && int.TryParse(parts[1], out int d):
            {
                string? book = parts.Length > 2 ? parts[2] : QuestEnemyCollections.BooksFor(System).FirstOrDefault()?.FileName;
                string? err = _s.StartChapter(cur.Path, cur.Campaign, ch, d, QuestCampaignSession.NeedsEnemy(cur.Campaign.Chapters[ch]) ? book : null);
                if (err != null) Say(err, Palette.WoundsRed);
                ReselectAfterReload(cur.Path);
                break;
            }
            case "play" when Current is { } cur2: PlayNext(cur2.Path, cur2.Campaign); return;
            case "journal" when Current is { } cur3: OpenJournal(cur3.Campaign); return;
            case "structure" when Current is { } cur4: OpenStructure(cur4.Path, cur4.Campaign); return;
            case "shop" when Current is { } cur5: OpenShop(cur5.Campaign); return;
        }
        Rebuild();
    }
}
