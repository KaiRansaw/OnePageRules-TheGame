using System.Numerics;
using FDG;
using FDG.GameMasterTools;
using FDG.Quest;
using FDG.Stages;
using FDG.Rules.Foundation;
using FDG.StateMachine;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The host's Game Master panel (Play Style = Game Master): round control, Horde controls, and edits for a
/// selected unit, plus two table tools - Select (click a unit to edit it) and Move (click a unit, then
/// click where it should go; right-click cancels). Every button just queues a change on
/// <see cref="GameMasterHub"/>, which the engine applies at its next safe point - straight away when it is
/// waiting on you, or right after the bot finishes deciding. Hidden entirely outside Game Master games and
/// on remote clients (only the host process has an active hub).
/// <para>Like the ruler, its table clicks must be claimed before the hit tester and resolvers read
/// <c>io.WantCaptureMouse</c>, so <see cref="Draw"/> runs early in the ImGui frame.</para>
/// </summary>
public sealed class GameMasterPanel
{
    private enum ETool { Off, Select, Move }

    private ETool _tool = ETool.Off;
    private IUnit? _selected;
    private IUnit? _moving;
    private Position _moveAnchor;
    private int _jumpRound = 4;
    private int _priorityChoice;     // 0 = none, else objective number
    private int _traitChoice;

    private float _scale;
    private int _originX, _originY;
    private float _tableH;

    private static readonly (string Label, string Rule)[] Traits =
    {
        ("Agile", HordeExperience.Agile),
        ("Headstrong", HordeExperience.Headstrong),
        ("Melee Specialist", HordeExperience.MeleeSpecialist),
        ("Shooting Specialist", HordeExperience.ShootingSpecialist),
        ("Resilient", HordeExperience.Resilient),
        ("Elite", HordeExperience.Elite),
    };

    private static readonly uint SelectRing = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.2f, 1f));
    private static readonly uint MoveLine = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.55f, 0.15f, 1f));

    public void Reset()
    {
        _tool = ETool.Off;
        _selected = null;
        _moving = null;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    /// <summary>True while a GM table tool (Select / Move) is on: it needs clicks on the 2D table.</summary>
    public bool UsesTable => GameMasterHub.Active != null && _tool != ETool.Off;

    /// <summary>Call inside the ImGui frame, before the hit tester. <paramref name="hidden"/> suppresses the
    /// panel and tools (menu / army list / game-over overlays).</summary>
    public void Draw(bool hidden, bool tableTools = true, bool window = true)
    {
        GameMasterHub? hub = GameMasterHub.Active;
        ITableState? table = hub?.TableState;
        if (hub == null || table == null)
        {
            Reset();
            return;
        }
        if (hidden) return;

        if (_selected != null && !table.Units.Objects.Contains(_selected)) _selected = null;

        if (tableTools)
        {
            HandleTableInput(hub, table);
            DrawHighlights(table);
        }
        if (window) DrawWindow(hub, table);
    }

    /// <summary>Whether the Game Master tools are available in this game.</summary>
    public static bool Available => GameMasterHub.Active?.TableState != null;

    /// <summary>The tools, drawn into the current window - the in-game Game Master tab.</summary>
    public void DrawBody()
    {
        GameMasterHub? hub = GameMasterHub.Active;
        ITableState? table = hub?.TableState;
        if (hub == null || table == null) return;
        if (_selected != null && !table.Units.Objects.Contains(_selected)) _selected = null;
        ImGui.PushTextWrapPos(0f);
        DrawWindowBody(hub, table);
        ImGui.PopTextWrapPos();
    }

    // ------------------------------------------------------------------ table tools

    private void HandleTableInput(GameMasterHub hub, ITableState table)
    {
        if (_tool == ETool.Off) { _moving = null; return; }

        var io = ImGui.GetIO();
        bool overPanel = io.WantCaptureMouse;
        if (overPanel) return;

        Position cursor = ToTable(TableInput.MousePos);
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (_moving != null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _moving = null;
            io.WantCaptureMouse = true;
            return;
        }

        if (!clicked) return;
        io.WantCaptureMouse = true;   // the click is the tool's, not a resolver's

        if (_tool == ETool.Move && _moving != null)
        {
            hub.MoveUnit(_moving.ID, cursor.x - _moveAnchor.x, cursor.z - _moveAnchor.z);
            _selected = _moving;
            _moving = null;
            return;
        }

        IUnit? hit = UnitAt(table, cursor);
        if (hit == null) return;
        _selected = hit;
        if (_tool == ETool.Move)
        {
            _moving = hit;
            _moveAnchor = cursor;
        }
    }

    private static IUnit? UnitAt(ITableState table, Position cursor)
    {
        IUnit? best = null;
        float bestDist = float.MaxValue;
        foreach (IUnit unit in table.Units.Objects)
        {
            if (!unit.GetIsOnBattlefield()) continue;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                Position p = model.Position;
                if (p.x == 0f && p.z == 0f) continue;
                float dx = cursor.x - p.x, dz = cursor.z - p.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (model.BaseShape.ContainsLocalPoint(dx, dz) && dist < bestDist)
                {
                    bestDist = dist;
                    best = unit;
                }
            }
        }
        return best;
    }

    private void DrawHighlights(ITableState table)
    {
        var dl = ImGui.GetBackgroundDrawList();
        if (_selected != null)
        {
            foreach (IModel model in _selected.Models)
            {
                if (!model.GetIsAlive()) continue;
                dl.AddCircle(ToPixel(model.Position), (model.BaseRadiusInches + 0.25f) * _scale, SelectRing, 0, 2.5f);
            }
        }

        if (_moving != null)
        {
            Vector2 from = ToPixel(_moveAnchor);
            Vector2 to = TableInput.MousePos;
            dl.AddLine(from, to, MoveLine, 3f);
            float dx = (to.X - from.X) / _scale, dz = (to.Y - from.Y) / _scale;
            dl.AddText(to + new Vector2(12, -20), MoveLine,
                $"Move {_moving.Name} {MathF.Sqrt(dx * dx + dz * dz):0.0}\" (right-click cancels)");
        }
    }

    // ------------------------------------------------------------------ the window

    private int _waveChoice = 1;
    private int _alertnessChoice;
    private int _eventChoice;          // 0 = roll, else index into EventList
    private int _primaryChoice;        // 0 = roll, else 1-6
    private int _secondaryChoice;
    private int _itemChoice;
    private int _skillChoice;

    private static readonly string[] TimesOfDay = { "", "Dusk", "Night", "Dawn", "Day" };
    private static readonly string[] Weathers = { "", "Worsening", "Bad", "Improving", "Good" };
    private static readonly string[] AlertLevels = { "", "Vigilant", "Alarmed", "Full Assault" };
    private static readonly string[] Conditions =
        { QuestTreasure.Afflicted, QuestTreasure.Crippled, QuestTreasure.Diseased, QuestTreasure.Impaired };
    private static readonly string[] Challenges =
    {
        QuestChallenges.BossFights, QuestChallenges.HeroInjuries, QuestChallenges.IncreasedAlertness,
        QuestChallenges.TimeOfDayName, QuestChallenges.WeatherName, QuestChallenges.RandomEvents,
    };
    private static readonly List<KeyValuePair<int, string>> EventList = QuestChallenges.EventNames.OrderBy(e => e.Key).ToList();
    private static readonly string[] EventLabels =
        new[] { "Roll two dice" }.Concat(EventList.Select(e => $"{e.Key} - {e.Value}")).ToArray();
    private static readonly string[] ItemLabels = QuestItems.All.Select(i => $"{i.Name} ({i.Cost}c)").ToArray();
    private static readonly string[] SkillNames =
        QuestSkill.All.Where(s => !s.Common && !s.Feat).Select(s => s.Name).OrderBy(n => n).ToArray();

    private void DrawWindow(GameMasterHub hub, ITableState table)
    {
        // Resizable: drag the bottom-right corner (or any edge). Rows of buttons wrap to the width
        // (SameLineIfFits) and long text wraps too; a window shorter than its contents scrolls.
        ImGui.SetNextWindowPos(new Vector2(12, 60), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(460, 620), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(240, 140), new Vector2(4000, 4000));
        if (!ImGui.Begin("Game Master"))
        {
            ImGui.End();
            return;
        }
        ImGui.PushTextWrapPos(0f);
        DrawWindowBody(hub, table);
        ImGui.PopTextWrapPos();
        ImGui.End();
    }

    private void DrawWindowBody(GameMasterHub hub, ITableState table)
    {
        UiText.Colored(ImGuiTheme.HeroGold, "Testing tools - changes ignore the rules.");
        UiText.Disabled("Hover any button to see what it does. Changes land straight away while it is your turn.");
        if (hub.PendingCount > 0)
            UiText.Colored(ImGuiTheme.HitsYellow, $"{hub.PendingCount} change(s) waiting for the bot to finish its move...");

        DrawTableTools();
        DrawRounds(hub, table);
        if (hub.IsHorde) DrawHorde(hub, table);
        if (hub.IsQuest)
        {
            QuestProgress quest = table.Progress.Quest ?? new QuestProgress();
            DrawQuestStatus(hub, table, quest);
            if (hub.IsQuestMission) DrawQuestMission(hub, quest);
            else DrawQuestWaves(hub, quest);
            DrawQuestChallenges(hub, quest);
            DrawQuestParty(hub, quest);
        }
        DrawSelectedUnit(hub, table);
    }

    private enum EItemKind { Button, Radio }

    /// <summary>
    /// Puts the next button on the current row if it fits in the window's width, otherwise lets it start a
    /// new row - so rows of buttons wrap as the window is resized instead of running off its edge.
    /// </summary>
    private static void SameLineIfFits(string label, EItemKind kind = EItemKind.Button)
    {
        var style = ImGui.GetStyle();
        float text = ImGui.CalcTextSize(label, true).X;
        float width = kind == EItemKind.Radio
            ? ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + text
            : text + style.FramePadding.X * 2f;
        float lastRight = ImGui.GetItemRectMax().X;
        // The cursor already sits at the start of the next row: its x plus the room left on that row is the
        // window's right content edge.
        float limit = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (lastRight + style.ItemSpacing.X + width <= limit) ImGui.SameLine();
    }

    // The item the pointer is resting on, and how long for. Tooltips here used to appear the instant the
    // mouse touched a button, which meant dragging across the panel flashed a wall of text over whatever
    // was being read underneath.
    private static Vector2 _tipItem;
    private static float _tipRested;

    /// <summary>A wrapped tooltip for the item just drawn (also while it is disabled), after a short dwell.</summary>
    private static void Tip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        Vector2 at = ImGui.GetItemRectMin();
        if (at != _tipItem) { _tipItem = at; _tipRested = 0f; return; }
        _tipRested += ImGui.GetIO().DeltaTime;
        if (_tipRested < TableTooltipOverlay.HoverDelaySeconds) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>A button that wraps onto the next row when it doesn't fit, with a tooltip.</summary>
    private static bool Button(string label, string tip, bool first = false)
    {
        if (!first) SameLineIfFits(label);
        bool pressed = ImGui.Button(label);
        Tip(tip);
        return pressed;
    }

    private static bool SmallButton(string label, string? tip = null)
    {
        SameLineIfFits(label);
        bool pressed = ImGui.SmallButton(label);
        if (tip != null) Tip(tip);
        return pressed;
    }

    /// <summary>
    /// One editable hero stat: "Str 5+  [-] [+]". The buttons change the printed number, which for a roll
    /// target means "-" makes the hero better; the line above the block says so.
    /// </summary>
    private static void StatRow(GameMasterHub hub, UnitData hero, string label, string stat, int value,
        string suffix = "+", int step = 1)
    {
        ImGui.TextUnformatted($"{label} {value}{suffix}");
        ImGui.SameLine();
        if (ImGui.SmallButton($"-{step}##dn{stat}")) hub.HeroStat(hero.ID, stat, -step);
        ImGui.SameLine();
        if (ImGui.SmallButton($"+{step}##up{stat}")) hub.HeroStat(hero.ID, stat, step);
    }

    /// <summary>A sub-heading inside a section.</summary>
    private static void SubHeading(string text)
    {
        ImGui.Spacing();
        UiText.Colored(ImGuiTheme.HeaderAccent, text);
    }

    /// <summary>Two-column "label: value" rows, so state reads like a list.</summary>
    private static void InfoTable(string id, IEnumerable<(string Label, string Value)> rows)
    {
        if (!ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch);
        foreach (var (label, value) in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            UiText.Disabled(label);
            ImGui.TableNextColumn();
            UiText.Wrapped(value);
        }
        ImGui.EndTable();
    }

    private static string OrNone(string? s) => string.IsNullOrEmpty(s) ? "none" : s;

    // ------------------------------------------------------------------ table tools and rounds

    private void DrawTableTools()
    {
        if (!ImGui.CollapsingHeader("Table tools", ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (ImGui.RadioButton("Off", _tool == ETool.Off)) _tool = ETool.Off;
        SameLineIfFits("Select", EItemKind.Radio);
        if (ImGui.RadioButton("Select", _tool == ETool.Select)) _tool = ETool.Select;
        Tip("Click a unit on the table to edit it in the Selected unit section.");
        SameLineIfFits("Move", EItemKind.Radio);
        if (ImGui.RadioButton("Move", _tool == ETool.Move)) _tool = ETool.Move;
        Tip("Click a unit, then click where it should go. Right-click cancels. Works in the 2D view.");
        if (_tool == ETool.Select) UiText.Disabled("Click a unit to edit it below.");
        if (_tool == ETool.Move) UiText.Disabled("Click a unit, then click where it goes.");
    }

    private void DrawRounds(GameMasterHub hub, ITableState table)
    {
        if (!ImGui.CollapsingHeader("Rounds and game end", ImGuiTreeNodeFlags.DefaultOpen)) return;

        int total = table.Progress.TotalRounds;
        bool inRounds = table.Progress.RoundCount != null;
        ImGui.TextUnformatted(inRounds ? $"Round {table.Progress.RoundCount} of {total}" : "Before round 1 (set-up / deployment)");

        ImGui.BeginDisabled(!inRounds);
        if (Button("End round now", inRounds
                ? "After the current activation, no more units activate this round. The end-of-round steps run next (objectives, alertness, waves...)."
                : "Available once round 1 starts.", first: true))
            hub.EndRoundNow();
        ImGui.EndDisabled();

        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("##jumpRound", ref _jumpRound);
        _jumpRound = Math.Clamp(_jumpRound, 1, Math.Max(1, total));
        if (Button("Jump to round", "Ends this round (after the current activation) and starts the chosen one, running its " +
                                     "start-of-round steps (in Horde: a new priority on rounds 1/4/7/10 and a wave on 4/7/10). " +
                                     "Before round 1, the game simply starts at that round."))
            hub.JumpToRound(_jumpRound);

        if (!hub.IsQuest)
        {
            ImGui.BeginDisabled(!inRounds);
            if (Button("End the game now", "Ends the game after the current activation and goes straight to the result screen, " +
                                            "scored as things stand.", first: true))
                hub.EndGameNow();
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled(!inRounds);
            if (Button(hub.IsQuestMission ? "Win the mission" : "Win the game",
                    hub.IsQuestMission
                        ? "Ends the mission as a success after the current activation: hazard pay, XP for the objectives done, injuries for knocked-out heroes, and the campaign records a win."
                        : "Ends the game as a win (all 10 waves survived) after the current activation. Hero files get their XP as normal.",
                    first: true))
                hub.QuestFinish(win: true);
            if (Button(hub.IsQuestMission ? "Fail the mission" : "Lose the game",
                    "Knocks out every hero and ends the game as a loss after the current activation. Injuries and the campaign result are recorded as normal."))
                hub.QuestFinish(win: false);
            ImGui.EndDisabled();
        }
    }

    private void DrawHorde(GameMasterHub hub, ITableState table)
    {
        if (!ImGui.CollapsingHeader("Horde")) return;

        ImGui.TextUnformatted($"Points: {table.Progress.HordePointsAvailable ?? 0}");
        if (SmallButton("-40")) hub.AdjustPoints(-40);
        if (SmallButton("+40")) hub.AdjustPoints(40);
        if (SmallButton("+100")) hub.AdjustPoints(100);

        if (Button("Deploy wave now", "Shaken and destroyed enemies return at random corners, as at rounds 4/7/10.", first: true)) hub.DeployWaveNow();
        if (Button("Award wave points", "Pays the end-of-wave reward for the objectives held right now.")) hub.AwardWavePointsNow();
        if (Button("Open shop", "Opens the Horde shop now. Every objective counts as seized, so recruits can deploy at any of them.")) hub.OpenShopNow();
        if (Button("Damage objectives", "Runs the end-of-round objective damage check right now.", first: true)) hub.DamageObjectivesNow();
        if (Button("Roll priority", "Rolls a new priority objective.")) hub.RollPriorityNow();

        List<IObjective> objectives = table.Objectives.Objects.OrderBy(o => o.HordeNumber ?? 0).ToList();
        string[] priorityLabels = new[] { "None" }.Concat(objectives.Select(o => $"Objective {o.HordeNumber}")).ToArray();
        _priorityChoice = Math.Clamp(_priorityChoice, 0, priorityLabels.Length - 1);
        ImGui.SetNextItemWidth(140);
        ImGui.Combo("##priority", ref _priorityChoice, priorityLabels, priorityLabels.Length);
        if (Button("Set priority", "Makes the chosen objective the priority objective."))
            hub.SetPriority(_priorityChoice == 0 ? null : objectives[_priorityChoice - 1].HordeNumber);

        foreach (IObjective objective in objectives)
        {
            int number = objective.HordeNumber ?? 0;
            int health = HordeRules.ObjectiveDamageToDestroy - objective.HordeDamage;
            ImGui.TextUnformatted($"Objective {number}: health {health}/{HordeRules.ObjectiveDamageToDestroy}" +
                                  (objective.IsHordePriority ? " (priority)" : ""));
            if (SmallButton($"Damage##d{number}")) hub.AdjustObjectiveDamage(number, 1);
            if (SmallButton($"Repair##r{number}")) hub.AdjustObjectiveDamage(number, -1);
        }
    }

    // ------------------------------------------------------------------ Star Quest

    private static void DrawQuestStatus(GameMasterHub hub, ITableState table, QuestProgress q)
    {
        if (!ImGui.CollapsingHeader("Star Quest: status", ImGuiTreeNodeFlags.DefaultOpen)) return;

        var rows = new List<(string, string)>();
        if (hub.IsQuestMission)
        {
            rows.Add(("Difficulty", q.Difficulty is >= 1 and <= 4 ? StarQuestMissionRules.DifficultyNames[q.Difficulty] : "not set yet"));
            rows.Add(("Primary", q.Primary == 0 ? "not rolled yet"
                : $"{StarQuestMissionRules.PrimaryNames[q.Primary]} - {(q.PrimaryDone ? "done" : "not done")}"));
            rows.Add(("Secondary", q.Secondary == 0 ? "appears once the primary is done"
                : $"{StarQuestMissionRules.SecondaryNames[q.Secondary]} - {(q.SecondaryDone ? "done" : "not done")}"));
            rows.Add(("Reinforcements", $"{q.Reinforcements} wave(s) so far"));
            rows.Add(("Hero corner", q.HeroCorner == 0 ? "-" : HordeRules.CornerName(q.HeroCorner)));
        }
        else
        {
            rows.Add(("Wave", $"{q.Wave} of {StarQuestHordeRules.FinalWave}" + (q.WavePending ? " (starts next round)" : "")));
            rows.Add(("Waves finished", q.WavesFinished.ToString()));
            rows.Add(("Party XP", $"{q.PartyXp} (high score {q.PartyXp * q.WavesFinished})"));
        }
        rows.Add(("Alertness", $"{q.Alertness}/{StarQuestHordeRules.AlertnessToEndWave}"));
        rows.Add(("Rounds played", q.RoundsDone.ToString()));
        rows.Add(("Challenges", q.Challenges.Count == 0 ? "none" : string.Join(", ", q.Challenges)));
        if (q.AlertLevel != "") rows.Add(("Alert level", q.AlertLevel));
        if (q.TimeOfDay != "") rows.Add(("Time of day", q.TimeOfDay));
        if (q.Weather != "") rows.Add(("Weather", q.Weather));
        rows.Add(("Random event", OrNone(q.Event)));
        rows.Add(("Boss fight", q.BossFight switch { 1 => $"on ({q.BossWave.Count} boss units)", -1 => "declined", _ => "no" }));
        int search = q.Tokens.Count(t => !t.IsTreasure), treasure = q.Tokens.Count(t => t.IsTreasure);
        rows.Add(("Tokens", $"{search} search, {treasure} treasure"));
        int items = q.Markers.Count(m => m.Kind is not ("objective" or "delivery" or "extraction"));
        int objective = q.Markers.Count - items;
        rows.Add(("Markers", $"{objective} objective, {items} grenade / item"));
        if (q.Ambushers.Count > 0) rows.Add(("Ambushers waiting", q.Ambushers.Count.ToString()));
        if (q.Outcome != "") rows.Add(("Outcome", q.Outcome));
        InfoTable("##questStatus", rows);

        SubHeading("Heroes");
        foreach (UnitData hero in table.Units.Objects.OfType<UnitData>().Where(u => u.IsQuestHero))
        {
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null) continue;
            float total = hero.Models.Sum(m => m.TotalWounds);
            float left = hero.Models.Sum(m => m.TotalWounds - m.WoundsDealt);
            string state = hero.GetIsAlive() ? $"{WoundFormat.Format(left)}/{WoundFormat.Format(total)} wounds" : "UNCONSCIOUS";
            UiText.Bullet($"{sheet.Name} (level {sheet.Level}, {sheet.Xp} XP): {state}, power {hero.QuestPower}/{sheet.Power}, {sheet.Credits}c");
            var extra = new List<string>();
            if (sheet.Conditions.Count > 0) extra.Add("conditions: " + string.Join(", ", sheet.Conditions));
            if (sheet.Injuries.Count > 0) extra.Add("injuries: " + string.Join(", ", sheet.Injuries));
            if (sheet.Items.Count > 0) extra.Add("items: " + string.Join(", ", sheet.Items));
            List<string> active = QuestFlags.Describe(q, hero);
            if (active.Count > 0) extra.Add("this round: " + string.Join(", ", active));
            if (extra.Count > 0)
            {
                ImGui.Indent();
                UiText.Disabled(string.Join("; ", extra));
                ImGui.Unindent();
            }
        }
    }

    private void DrawQuestWaves(GameMasterHub hub, QuestProgress q)
    {
        if (!ImGui.CollapsingHeader("Star Quest: waves and alertness")) return;

        DrawAlertness(hub, q);

        SubHeading("Waves");
        if (Button("End the wave now", "Finishes the current wave straight away: XP for the objectives held, the shop, injuries. " +
                                       "The next wave arrives at the start of the next round. Finishing wave 10 wins the game.", first: true))
            hub.QuestEndWaveNow();
        if (Button("Deploy this wave again", "Brings the current wave's enemies on now, from random corners.")) hub.QuestDeployNow(boss: false);
        if (Button("Boss wave now", "Brings on a double-size wave now that counts as the boss fight (destroy it all for double XP).")) hub.QuestDeployNow(boss: true);

        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("##sqWave", ref _waveChoice);
        _waveChoice = Math.Clamp(_waveChoice, 1, StarQuestHordeRules.FinalWave);
        if (Button("Start this wave next round", "Skips ahead (or back): the chosen wave starts at the start of the next round, " +
                                                  "with its priority roll and enemies. Use End round now to get there quickly."))
            hub.QuestSetWave(_waveChoice);
        if (q.Ambushers.Count > 0 && Button("Ambushers arrive now", "Every AI Ambush unit still waiting arrives now, near the AI Goal.", first: true))
            hub.QuestAmbushNow();
    }

    private void DrawQuestMission(GameMasterHub hub, QuestProgress q)
    {
        if (!ImGui.CollapsingHeader("Star Quest: mission")) return;

        SubHeading("Difficulty");
        for (int d = 1; d <= 4; d++)
        {
            string label = $"{StarQuestMissionRules.DifficultyNames[d]}##diff{d}";
            if (d > 1) SameLineIfFits(StarQuestMissionRules.DifficultyNames[d], EItemKind.Radio);
            if (ImGui.RadioButton(label, q.Difficulty == d)) hub.QuestSetDifficulty(d);
        }
        Tip("Sets the size of reinforcement waves (25% of the party's points per level) and the rewards.");

        SubHeading("Objectives");
        ImGui.BeginDisabled(q.PrimaryDone);
        if (Button("Complete the primary", "Completes the primary objective now (as if the heroes had done it). " +
                                           "The secondary objective appears at the end of the round.", first: true))
            hub.QuestCompleteObjective(secondary: false);
        ImGui.EndDisabled();
        ImGui.BeginDisabled(q.SecondaryDone);
        if (Button("Complete the secondary", "Completes the secondary objective now. If none is rolled yet, the primary is completed and a secondary is rolled first."))
            hub.QuestCompleteObjective(secondary: true);
        ImGui.EndDisabled();

        string[] primaries = new[] { "Roll" }.Concat(StarQuestMissionRules.PrimaryNames.Skip(1)).ToArray();
        ImGui.SetNextItemWidth(170);
        ImGui.Combo("##primary", ref _primaryChoice, primaries, primaries.Length);
        if (Button("New primary", "Replaces the primary objective with the chosen one (or a roll) and sets up its markers.")) hub.QuestRerollObjective(false, _primaryChoice);
        string[] secondaries = new[] { "Roll" }.Concat(StarQuestMissionRules.SecondaryNames.Skip(1)).ToArray();
        ImGui.SetNextItemWidth(170);
        ImGui.Combo("##secondary", ref _secondaryChoice, secondaries, secondaries.Length);
        if (Button("New secondary", "Replaces the secondary objective with the chosen one (or a roll) and sets up its markers.")) hub.QuestRerollObjective(true, _secondaryChoice);

        DrawAlertness(hub, q);

        SubHeading("Enemies");
        if (Button("Reinforcements now", "A reinforcement wave arrives now from random corners (also moves time and weather on and rolls an event).", first: true))
            hub.QuestDeployNow(boss: false);
        if (Button("Boss wave now", "A double-size wave arrives now and counts as the boss fight.")) hub.QuestDeployNow(boss: true);
        if (q.Ambushers.Count > 0 && Button("Ambushers arrive now", "Every AI Ambush unit still waiting arrives now."))
            hub.QuestAmbushNow();
    }

    private void DrawAlertness(GameMasterHub hub, QuestProgress q)
    {
        SubHeading($"Alertness: {q.Alertness}/{StarQuestHordeRules.AlertnessToEndWave}");
        _alertnessChoice = q.Alertness;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("##alertness", ref _alertnessChoice, 0, StarQuestHordeRules.AlertnessToEndWave))
            hub.QuestSetAlertness(_alertnessChoice);
        Tip(hub.IsQuestMission
            ? "At 6, reinforcements arrive at the end of the round. Set it to 5 or 6, then press End round now."
            : "At 6, the wave ends at the end of the round. Set it to 5 or 6, then press End round now.");
    }

    private void DrawQuestChallenges(GameMasterHub hub, QuestProgress q)
    {
        if (!ImGui.CollapsingHeader("Star Quest: challenges and events")) return;

        SubHeading("Optional challenges");
        foreach (string challenge in Challenges)
        {
            bool on = q.Challenges.Contains(challenge);
            if (ImGui.Checkbox(challenge, ref on)) hub.QuestToggleChallenge(challenge);
        }

        Combo("Alert level", "##alertLevel", AlertLevels, q.AlertLevel, hub.QuestSetAlertLevel,
            "Increased Alertness: how likely alertness rises by an extra 1 each round.");
        Combo("Time of day", "##timeOfDay", TimesOfDay, q.TimeOfDay, hub.QuestSetTimeOfDay,
            "Night and Dawn give -1 to hit when shooting. It moves on with each new wave / reinforcement.");
        Combo("Weather", "##weather", Weathers, q.Weather, hub.QuestSetWeather,
            "Bad halves all movement and gives -1 to hit in melee; Improving gives -1 to hit in melee.");

        SubHeading("Random events");
        UiText.Disabled($"In play: {OrNone(q.Event)}");
        _eventChoice = Math.Clamp(_eventChoice, 0, EventLabels.Length - 1);
        ImGui.SetNextItemWidth(230);
        ImGui.Combo("##event", ref _eventChoice, EventLabels, EventLabels.Length);
        if (Button("Trigger event", "Resolves the chosen event now (or rolls one), even with Random Events off. " +
                                    "Hero events pick a random hero; it lasts until the next event."))
            hub.QuestRollEvent(_eventChoice == 0 ? null : EventList[_eventChoice - 1].Key);
        if (q.Event != "" && Button("End the event", "Clears the event in play (its stat bonuses and penalties stop).", first: true))
            hub.QuestClearEvent();
    }

    private static void Combo(string label, string id, string[] values, string current, Action<string> set, string tip)
    {
        string[] labels = values.Select(v => v == "" ? "Off" : v).ToArray();
        int index = Math.Max(0, Array.IndexOf(values, current));
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo(id, ref index, labels, labels.Length)) set(values[index]);
        Tip(tip);
    }

    private static void DrawQuestParty(GameMasterHub hub, QuestProgress q)
    {
        if (!ImGui.CollapsingHeader("Star Quest: party, shop and tokens")) return;

        SubHeading("Whole party");
        if (Button("+2 XP each", "Every hero gets 2 XP. Each level reached asks for an improvement.", first: true)) hub.QuestPartyXp(2);
        if (Button("+10 XP each", "Every hero gets 10 XP (several level-ups at once).")) hub.QuestPartyXp(10);
        if (Button("Open the shop", "Opens the shop for each hero in turn: buy, sell (full price) and give items or credits.")) hub.QuestShopNow();

        SubHeading("Search tokens and markers");
        if (Button("Place 4 search tokens", "Adds one search token in each table quarter.", first: true)) hub.QuestPlaceSearchTokens();
        if (Button("Show what 2 tokens hold", "Reveals the contents of two unrevealed tokens (like Prophetic Vision), without picking them up."))
            hub.QuestForeseeTokens(2);
        if (Button("Remove all tokens", "Removes every search token and treasure marker.", first: true)) hub.QuestClearTokens();
        if (Button("Remove grenade markers", "Removes every grenade / item marker (smoke, fire, noise, grease, silence, poison, phantasm, bio-trap)."))
            hub.QuestClearItemMarkers();
    }

    // ------------------------------------------------------------------ selected unit

    private void DrawSelectedUnit(GameMasterHub hub, ITableState table)
    {
        if (!ImGui.CollapsingHeader("Selected unit", ImGuiTreeNodeFlags.DefaultOpen)) return;

        IUnit? unit = _selected;
        if (unit == null)
        {
            UiText.Disabled("None - pick the Select tool and click a unit.");
            return;
        }

        string owner = table.Players.Objects.FirstOrDefault(p => p.IsFilled && p.PlayerID.Equals(unit.PlayerID))?.Name ?? "?";
        UiText.Colored(ImGuiTheme.HeaderAccent, unit.Name);
        UiText.Disabled($"Owner: {owner}");

        float total = unit.Models.Sum(m => m.TotalWounds);
        float left = unit.Models.Sum(m => m.TotalWounds - m.WoundsDealt);
        ImGui.TextUnformatted($"Wounds: {WoundFormat.Format(left)}/{WoundFormat.Format(total)}" +
                              (unit.Tokens.HasToken(TokenType.Shaken) ? "   SHAKEN" : ""));
        if (FDG.Stages.FirefightRules.MarkerSummary(unit) is { } markers) UiText.Disabled(markers);

        if (Button("Wound 1", "Deals 1 wound, ignoring saves.", first: true)) hub.WoundUnit(unit.ID, 1);
        if (Button("Heal 1", "Restores 1 wound (a dead model can come back).")) hub.HealUnit(unit.ID, 1);
        if (Button("Kill", "Destroys the unit outright (no XP for anyone). A hero becomes unconscious.")) hub.KillUnit(unit.ID);
        if (Button("Revive", "Restores every model to full wounds and clears Shaken and Fatigued.")) hub.ReviveUnit(unit.ID);
        if (Button(unit.Tokens.HasToken(TokenType.Shaken) ? "Remove Shaken" : "Make Shaken", "Toggles the Shaken token.")) hub.ToggleShaken(unit.ID);

        if (hub.IsHorde)
        {
            SubHeading($"XP: {unit.Tokens.GetTokenCount(TokenType.HordeXP)} / {HordeExperience.MaxXp}");
            if (SmallButton("+1 XP")) hub.AddXp(unit.ID, 1);
            if (SmallButton("+5 XP", "Levels up as normal (a Specialist / Fast Learner pick is made for you).")) hub.AddXp(unit.ID, 5);

            string[] labels = Traits.Select(t => t.Label).ToArray();
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("##trait", ref _traitChoice, labels, labels.Length);
            if (Button("Give trait", "Gives the unit the chosen trait.")) hub.GiveTrait(unit.ID, Traits[_traitChoice].Rule);

            HashSet<string> owned = HordeExperience.OwnedTraits(unit);
            if (owned.Count > 0)
                UiText.Disabled("Traits: " + string.Join(", ", owned.Select(t => t.Replace("Trait: ", ""))));
        }

        if (unit is UnitData hero && hero.IsQuestHero) DrawHero(hub, hero);

        ImGui.Spacing();
        if (ImGui.SmallButton("Deselect")) _selected = null;
    }

    private void DrawHero(GameMasterHub hub, UnitData hero)
    {
        QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
        if (sheet == null) return;

        if (ImGui.TreeNodeEx("Hero: power, XP and credits", ImGuiTreeNodeFlags.DefaultOpen))
        {
            InfoTable("##heroStats", new[]
            {
                ("Level", $"{sheet.Level} ({sheet.Xp} XP)"),
                ("Power", $"{hero.QuestPower}/{sheet.Power}"),
                ("Credits", $"{sheet.Credits}c"),
                ("Class feature", sheet.ClassFeat == "" ? "none" : sheet.ClassFeat + (hero.QuestFeatsUsed.Count > 0 ? " (used)" : " (ready)")),
                ("State", hero.GetIsAlive() ? "conscious" : "unconscious"),
            });
            if (SmallButton("-1 power")) hub.HeroPower(hero.ID, -1);
            if (SmallButton("+1 power")) hub.HeroPower(hero.ID, 1);
            if (SmallButton("Full power", "Back to the hero's full power.")) hub.HeroFullPower(hero.ID);
            if (SmallButton("+1 XP")) hub.HeroXp(hero.ID, 1);
            if (SmallButton("+5 XP", "Each level reached asks for an improvement.")) hub.HeroXp(hero.ID, 5);
            if (SmallButton("+10c")) hub.HeroCredits(hero.ID, 10);
            if (SmallButton("+50c")) hub.HeroCredits(hero.ID, 50);
            if (SmallButton("-10c")) hub.HeroCredits(hero.ID, -10);
            if (sheet.Caster > 0 && SmallButton("+1 spell token")) hub.HeroSpellTokens(hero.ID, 1);
            if (hero.QuestFeatsUsed.Count > 0 && SmallButton("Feature ready again", "Lets the hero use its class feature again this game."))
                hub.HeroResetFeat(hero.ID);
            if (hero.GetIsAlive())
            {
                if (SmallButton("Knock out", "The hero falls unconscious (as if it took its last wound).")) hub.HeroKnockOut(hero.ID);
            }
            else if (SmallButton("Wake up (3 wounds)", "Revives the hero where it fell with 3 wounds, as another hero's revive would.")) hub.HeroRevive(hero.ID);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Hero: stats and dice", ImGuiTreeNodeFlags.DefaultOpen))
        {
            UiText.Disabled("Roll targets: a SMALLER number is the better hero, so - makes it better.");
            StatRow(hub, hero, "Qua", "Quality", sheet.Quality);
            StatRow(hub, hero, "Def", "Defense", sheet.Defense);
            StatRow(hub, hero, "Str", "Strength", sheet.Strength);
            StatRow(hub, hero, "Dex", "Dexterity", sheet.Dexterity);
            StatRow(hub, hero, "Wil", "Willpower", sheet.Willpower);
            ImGui.Spacing();
            StatRow(hub, hero, "Max power", "Power", sheet.Power, suffix: "");
            StatRow(hub, hero, "Level", "Level", sheet.Level, suffix: "");
            StatRow(hub, hero, "XP", "Xp", sheet.Xp, suffix: "", step: 5);
            StatRow(hub, hero, "Credits", "Credits", sheet.Credits, suffix: "c", step: 10);

            ImGui.Spacing();
            UiText.Disabled("Next Str / Dex / Wil test:");
            if (SmallButton("Force a pass", "The hero's very next stat test passes whatever the die says, then this clears itself."))
                hub.HeroForceNextTest(hero.ID, 1);
            if (SmallButton("Force a fail", "The hero's very next stat test fails whatever the die says, then this clears itself."))
                hub.HeroForceNextTest(hero.ID, -1);
            if (SmallButton("Roll normally", "Cancels a forced pass or fail that hasn't been used yet."))
                hub.HeroForceNextTest(hero.ID, 0);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode($"Items ({sheet.Items.Count})"))
        {
            _itemChoice = Math.Clamp(_itemChoice, 0, ItemLabels.Length - 1);
            ImGui.SetNextItemWidth(220);
            ImGui.Combo("##giveItem", ref _itemChoice, ItemLabels, ItemLabels.Length);
            if (Button("Give", "Puts the chosen item in the hero's pack (free). Use it with the Skill action."))
                hub.HeroGiveItem(hero.ID, QuestItems.All[_itemChoice].Name);
            if (sheet.Items.Count == 0) UiText.Disabled("The pack is empty.");
            int i = 0;
            foreach (string item in sheet.Items)
            {
                UiText.Bullet(item);
                if (SmallButton($"Remove##item{i++}")) hub.HeroRemoveItem(hero.ID, item);
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNode($"Conditions ({sheet.Conditions.Count})"))
        {
            foreach (string condition in Conditions)
            {
                bool on = sheet.Conditions.Contains(condition);
                if (ImGui.Checkbox(condition, ref on)) hub.HeroToggleCondition(hero.ID, condition);
            }
            Tip("Checked when the hero activates: Afflicted (Str), Crippled (half move), Diseased (power costs wounds), Impaired (-1 to stat tests).");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode($"Injuries ({sheet.Injuries.Count})"))
        {
            foreach (string injury in StarQuestMissionRules.Injuries.Skip(1))
            {
                bool on = sheet.Injuries.Contains(injury);
                if (ImGui.Checkbox(injury, ref on)) hub.HeroToggleInjury(hero.ID, injury);
            }
            Tip("Chest Wound -1 Def, Blinded Eye -1 Qua, Smashed Leg Slow, Broken Arm / Spinal Injury / Crushed Spirit -1 to Str / Dex / Wil.");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode($"Skills ({sheet.Skills.Count})"))
        {
            UiText.Disabled("Teach any skill the game can play, to try it without levelling up.");
            _skillChoice = Math.Clamp(_skillChoice, 0, Math.Max(0, SkillNames.Length - 1));
            ImGui.SetNextItemWidth(220);
            ImGui.Combo("##teach", ref _skillChoice, SkillNames, SkillNames.Length);
            if (SkillNames.Length > 0)
            {
                string pick = SkillNames[_skillChoice];
                bool known = sheet.Skills.Contains(pick);
                if (Button(known ? "Forget" : "Teach", QuestSkill.Find(pick)?.Description ?? pick))
                    hub.HeroToggleSkill(hero.ID, pick);
            }
            int i = 0;
            foreach (string skill in sheet.Skills)
            {
                bool playable = QuestSkill.Find(skill) != null;
                UiText.Bullet(playable ? skill : $"{skill} (not in the game yet)");
                if (SmallButton($"Forget##skill{i++}")) hub.HeroToggleSkill(hero.ID, skill);
            }
            ImGui.TreePop();
        }
    }

    private Position ToTable(Vector2 mouse) =>
        new((mouse.X - _originX) / _scale, 0f, _tableH - (mouse.Y - _originY) / _scale);

    private Vector2 ToPixel(Position p) =>
        new(_originX + p.x * _scale, _originY + (_tableH - p.z) * _scale);
}
