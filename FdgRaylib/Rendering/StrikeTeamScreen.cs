using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// GF: Firefight Campaign (main menu > Firefight Campaign). Plain layout for now (the UI pass restyles it):
/// <list type="bullet">
/// <item>New strike team: pick a Firefight army of up to 200 pts with at most one hero, and name it. The team is
/// saved as its own army file in armies\GF Firefight, so it is picked in Play like any army.</item>
/// <item>A team's page: VP, saved points, every unit's XP, level, traits, injuries, talents and skills, the choices
/// still to make (Specialist, Fast Learner, a hero's skill set), and the match history.</item>
/// </list>
/// After each game the result is written to the team file automatically.
/// </summary>
public sealed class StrikeTeamScreen : IAppScreen
{
    public Action? OnBack;

    private enum EView { List, New }
    private EView _view = EView.List;

    private List<(string Path, BuiltArmyFile Army)> _all = new();
    private List<(string Path, BuiltArmyFile Army)> Teams => _all.Where(a => a.Army.StrikeTeam != null).ToList();
    private List<(string Path, BuiltArmyFile Army)> Armies => _all.Where(a => a.Army.StrikeTeam == null).ToList();
    private int _selected = -1;
    private bool _confirmDelete;
    private string _message = "";
    private bool _messageIsError;

    private string _newName = "";
    private int _newArmy = -1;

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public void OnShown()
    {
        Reload();
        _view = EView.List;
        _message = "";
    }

    private void Reload()
    {
        ArmyPaths.EnsureGameTypeFolders();
        _all = StrikeTeamFiles.LoadAll();
        int count = Teams.Count;
        if (_selected >= count) _selected = count - 1;
        if (_selected < 0 && count > 0) _selected = 0;
        _confirmDelete = false;
    }

    private (string Path, BuiltArmyFile Army)? Current
    {
        get { var t = Teams; return _selected >= 0 && _selected < t.Count ? t[_selected] : null; }
    }

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##StrikeTeams", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = screenW * 0.025f;
        float titlePx = screenH * 0.05f;
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.035f), U(TextBright),
            _view == EView.New ? "NEW STRIKE TEAM" : "FIREFIGHT CAMPAIGN");
        string sub = _view == EView.New
            ? "Pick a GF: Firefight army of up to 200 pts with at most one hero. Up to 40 unspent points are saved."
            : "Strike teams keep their units, XP, traits and injuries from game to game.";
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.035f + titlePx + 6f), U(TextDim), sub);

        float top = screenH * 0.14f;
        float bottomBarH = screenH * 0.09f;
        float bodyH = screenH - top - bottomBarH;
        float w = screenW - margin * 2f;
        if (_view == EView.New) DrawNew(margin, top, w, bodyH);
        else DrawList(margin, top, w, bodyH);
        DrawBottomBar(screenW, screenH, margin, bottomBarH);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    // ── List + details ──────────────────────────────────────────────────────────────────────────────

    private void DrawList(float x, float y, float w, float h)
    {
        var teams = Teams;
        float leftW = w * 0.26f;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##teamList", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        if (teams.Count == 0) UiText.Disabled("No strike teams yet.\nClick \"New Strike Team\".");
        for (int i = 0; i < teams.Count; i++)
        {
            StrikeTeamInfo info = teams[i].Army.StrikeTeam!;
            if (ImGui.Selectable($"{info.Name}\n   {info.Vp} VP - {info.Matches} match(es) - {teams[i].Army.TotalPoints} pts##t{i}",
                    i == _selected, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
            {
                _selected = i;
                _confirmDelete = false;
            }
        }
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + 14f, y));
        ImGui.BeginChild("##teamDetail", new Vector2(w - leftW - 14f, h), ImGuiChildFlags.Borders);
        if (Current is { } cur) DrawDetail(cur.Path, cur.Army);
        else UiText.Disabled("Pick a strike team on the left, or start a new one.");
        ImGui.EndChild();
    }

    private void DrawDetail(string path, BuiltArmyFile team)
    {
        StrikeTeamInfo info = team.StrikeTeam!;
        UiText.Colored(Accent, info.Name);
        ImGui.TextUnformatted($"{info.Vp} VP    {info.BankedPoints} pts saved    {info.Matches} match(es)    team value {team.TotalPoints} pts");
        UiText.Wrapped("To play: Play > Game Type GF: Firefight > pick this strike team as your army. Set Army Points to at least " +
                       $"{team.TotalPoints} (campaign games have no points limit). The result is recorded here after the game.");
        ImGui.Separator();

        foreach (UnitFileEntry unit in team.Units.ToList())
        {
            if (StrikeTeamRules.Sheet(unit) is not { } sheet) continue;
            ImGui.PushID(sheet.Id);
            bool open = ImGui.CollapsingHeader($"{unit.Name}   ({(sheet.IsHero ? "hero, " : "")}{unit.PointCost} pts, level {sheet.Level}, {sheet.Xp} XP)"
                + (sheet.Pending.Count > 0 ? "   - choice to make" : "") + "###u", ImGuiTreeNodeFlags.DefaultOpen);
            if (open)
            {
                Line("Traits", sheet.Traits.Select(t => t.Replace("Trait: ", "")));
                if (sheet.IsHero)
                {
                    Line("Skill sets", sheet.SkillSets);
                    Line("Skills", sheet.Skills);
                    Line("Talents", sheet.Talents);
                }
                Line("Injuries", sheet.Injuries);
                UiText.Disabled($"Games {sheet.Games}   cost change {sheet.CostChange:+0;-0;0} pts");
                foreach (string pending in sheet.Pending.ToList()) DrawChoice(path, team, unit, sheet, pending);
            }
            ImGui.PopID();
        }

        if (info.History.Count > 0 && ImGui.CollapsingHeader("Match history"))
            foreach (string line in Enumerable.Reverse(info.History)) UiText.Wrapped(line);
    }

    private static void Line(string label, IEnumerable<string> items)
    {
        string text = string.Join(", ", items);
        UiText.Colored(TextDim, label + ":");
        ImGui.SameLine();
        if (text.Length == 0) UiText.Disabled("none"); else UiText.Wrapped(text);
    }

    private void DrawChoice(string path, BuiltArmyFile team, UnitFileEntry unit, StrikeUnit sheet, string pending)
    {
        string what = pending switch
        {
            StrikeTeamRules.Specialist => "Specialist: +1 to hit in melee or when shooting?",
            StrikeTeamRules.FastLearner => "Fast Learner: pick any other trait.",
            _ => "Pick a skill set (its first skill is rolled straight away).",
        };
        UiText.Colored(Gold, what);
        foreach (string option in StrikeTeamRules.Options(sheet, pending))
        {
            ImGui.SameLine();
            if (UiButton.NavigateSmall(option.Replace("Trait: ", "") + "##" + pending))
            {
                var rng = new Random();
                string line = StrikeTeamRules.Choose(unit, pending, option, () => rng.Next(1, 7));
                team.StrikeTeam!.History.Add(line);
                if (StrikeTeamFiles.Save(path, team)) Say(line); else Say("Couldn't save the strike team file.", true);
                Reload();
            }
        }
    }

    // ── New strike team ─────────────────────────────────────────────────────────────────────────────

    private void DrawNew(float x, float y, float w, float h)
    {
        var armies = Armies;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##newTeam", new Vector2(w, h), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Team name");
        ImGui.SetNextItemWidth(Math.Min(420f, w * 0.5f));
        ImGui.InputText("##teamName", ref _newName, 48);
        ImGui.Spacing();
        ImGui.TextUnformatted("Starting army (from armies\\GF Firefight)");
        if (armies.Count == 0)
            UiText.Disabled("No Firefight armies yet - make one in Army Forge (Game Type GF: Firefight) or put a .fdgarmy file in armies\\GF Firefight.");
        for (int i = 0; i < armies.Count; i++)
        {
            BuiltArmyFile a = armies[i].Army;
            string? why = StrikeTeamRules.CannotStart(a);
            if (ImGui.Selectable($"{a.Name}   ({a.Faction}, {a.TotalPoints} pts, {a.Units.Count} units)##a{i}", i == _newArmy))
                _newArmy = i;
            if (why != null) { ImGui.SameLine(); UiText.Colored(Bad, why); }
        }
        ImGui.EndChild();
    }

    private string? NewProblem()
    {
        var armies = Armies;
        if (_newName.Trim().Length == 0) return "Name the team.";
        if (_newArmy < 0 || _newArmy >= armies.Count) return "Pick a starting army.";
        if (Teams.Any(t => string.Equals(t.Army.StrikeTeam!.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "A team with that name exists.";
        return StrikeTeamRules.CannotStart(armies[_newArmy].Army);
    }

    private void Create()
    {
        // A fresh copy of the army file, so the original list stays as it was.
        var source = Armies[_newArmy];
        if (StrikeTeamFiles.Load(source.Path) is not { } army) { Say("Couldn't read that army file.", true); return; }
        string name = _newName.Trim();
        string path = StrikeTeamFiles.NewPath(name);
        StrikeTeamRules.Start(army, name, path);
        // Not editable in Army Forge any more: re-compiling it there would wipe the campaign changes.
        army.Selections = null;
        if (!StrikeTeamFiles.Save(path, army)) { Say("Couldn't save the strike team file.", true); return; }
        _view = EView.List;
        Reload();
        _selected = Teams.FindIndex(t => t.Path == path);
        Say($"Strike team {name} created ({army.StrikeTeam!.BankedPoints} pts saved).");
    }

    private void Delete(string path)
    {
        try { File.Delete(path); Say("Strike team deleted."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say("Couldn't delete the file.", true); }
        Reload();
    }

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        Vector2 btn = new(screenW * 0.13f, barH * 0.55f);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput;
        float rx = screenW - margin - btn.X;

        ImGui.SetCursorPos(new Vector2(margin, by));
        if (_view == EView.List)
        {
            if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
            ImGui.SetCursorPos(new Vector2(rx, by));
            if (UiButton.Confirm("New Strike Team", btn))
            {
                _view = EView.New;
                _newName = "";
                _newArmy = -1;
                Reload();
                _message = "";
            }
            if (Current is { } cur)
            {
                ImGui.SetCursorPos(new Vector2(rx - btn.X - 10f, by));
                if (UiButton.Back(_confirmDelete ? "Really delete?" : "Delete", btn))
                {
                    if (_confirmDelete) Delete(cur.Path);
                    else _confirmDelete = true;
                }
            }
            if (_message.Length > 0)
            {
                Vector2 ts = ImGui.CalcTextSize(_message);
                ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
                UiText.Colored(_messageIsError ? Bad : Good, _message);
            }
        }
        else
        {
            if (UiButton.Back("Cancel", btn) || esc) _view = EView.List;
            ImGui.SetCursorPos(new Vector2(rx, by));
            string? problem = NewProblem();
            ImGui.BeginDisabled(problem != null);
            if (UiButton.Confirm("Create", btn)) Create();
            ImGui.EndDisabled();
            if (problem != null)
            {
                Vector2 ts = ImGui.CalcTextSize(problem);
                ImGui.SetCursorPos(new Vector2(rx - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
                UiText.Colored(TextDim, problem);
            }
        }
    }
}
