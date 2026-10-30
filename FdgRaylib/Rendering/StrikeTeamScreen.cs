using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The campaign screen (main menu > Firefight Campaign). Kai's layout call, 2026-10-30: three columns
/// with nothing behind a tab - the roster, the selected unit, and the next mission - plus a card grid
/// for recruiting.
/// <list type="bullet">
/// <item><b>List</b> - the campaign armies on disk, and starting a new one.</item>
/// <item><b>Camp</b> - roster | selected unit (with its upgrades) | next mission. Everything visible.</item>
/// <item><b>Recruit</b> - one card per unit the book sells, with a cart along the bottom.</item>
/// </list>
/// Deliberately plain: under the working agreement the ImGui that draws a layout is throwaway, so this
/// is one pass to make the campaign playable and no more. The rules behind every button live in
/// <see cref="CampaignRules"/>, <see cref="CampaignEconomy"/> and <see cref="CampaignPreGame"/>, which
/// are the permanent half and are tested on their own.
/// </summary>
public sealed class StrikeTeamScreen : IAppScreen
{
    public Action? OnBack;

    private enum EView { List, New, Camp, Recruit }
    private EView _view = EView.List;

    private List<(string Path, BuiltArmyFile Army)> _all = new();
    private List<(string Path, BuiltArmyFile Army)> Teams => _all.Where(a => a.Army.StrikeTeam != null).ToList();
    private List<(string Path, BuiltArmyFile Army)> Armies => _all.Where(a => a.Army.StrikeTeam == null).ToList();
    private int _selected = -1;
    private int _unit;
    private bool _confirmDelete;
    private bool _confirmDisband;
    private string _message = "";
    private bool _messageIsError;

    private string _newName = "";
    private int _newArmy = -1;

    /// <summary>Recruits waiting to be confirmed: the roster unit id, and whether it is the 5-XP one.</summary>
    private readonly List<string> _cart = new();
    private bool _cartVeteran;
    /// <summary>One new Hero per mission: set when a Hero is recruited, cleared after a mission.</summary>
    private bool _heroRecruited;

    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.88f, 0.45f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.86f, 0.5f, 1f);

    private static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    private static readonly Random Rng = new();
    private static int D6() => Rng.Next(1, 7);

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
        _confirmDisband = false;
    }

    private (string Path, BuiltArmyFile Army)? Current
    {
        get { var t = Teams; return _selected >= 0 && _selected < t.Count ? t[_selected] : null; }
    }

    private void Say(string text, bool error = false) { _message = text; _messageIsError = error; }

    private void SaveCurrent(string path, BuiltArmyFile army)
    {
        if (!StrikeTeamFiles.Save(path, army)) Say("Couldn't save the campaign army file.", true);
    }

    private static CampaignNumbers NumbersFor(BuiltArmyFile army) =>
        CampaignNumbers.For(army.StrikeTeam?.GameSystem ?? EGameSystem.GFFirefight);

    // -- Frame -------------------------------------------------------------------------------------

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##Campaign", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = screenW * 0.02f;
        float titlePx = screenH * 0.045f;
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();

        (string title, string sub) = HeaderText();
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.03f), U(TextBright), title);
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.03f + titlePx + 4f), U(TextDim), sub);

        float top = screenH * 0.125f;
        float bottomBarH = screenH * 0.085f;
        float bodyH = screenH - top - bottomBarH;
        float w = screenW - margin * 2f;

        switch (_view)
        {
            case EView.New: DrawNew(margin, top, w, bodyH); break;
            case EView.Camp: DrawCamp(margin, top, w, bodyH); break;
            case EView.Recruit: DrawRecruit(margin, top, w, bodyH); break;
            default: DrawList(margin, top, w, bodyH); break;
        }
        DrawBottomBar(screenW, screenH, margin, bottomBarH);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private (string, string) HeaderText()
    {
        if (_view == EView.New)
            return ("NEW CAMPAIGN ARMY",
                "Pick an army inside the treasury, and name it. Whatever is left over becomes points you can spend between missions.");
        if (Current is { } cur && _view is EView.Camp or EView.Recruit)
        {
            StrikeTeamInfo info = cur.Army.StrikeTeam!;
            string head = _view == EView.Recruit ? "RECRUIT - " + info.Name.ToUpperInvariant() : info.Name.ToUpperInvariant();
            return (head,
                $"{GameSystemInfo.DisplayName(info.GameSystem)}  -  {info.CampaignType} campaign  -  " +
                $"{info.Matches} mission(s) played  -  treasury {info.BankedPoints} pts  -  {info.Vp} VP  -  army {cur.Army.TotalPoints} pts");
        }
        return ("CAMPAIGN", "A campaign army keeps its units, XP, traits and injuries from mission to mission.");
    }

    // -- The list of campaign armies ----------------------------------------------------------------

    private void DrawList(float x, float y, float w, float h)
    {
        var teams = Teams;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##teamList", new Vector2(w, h), ImGuiChildFlags.Borders);
        if (teams.Count == 0) UiText.Disabled("No campaign armies yet.\nClick \"New Campaign Army\".");
        for (int i = 0; i < teams.Count; i++)
        {
            StrikeTeamInfo info = teams[i].Army.StrikeTeam!;
            bool pending = teams[i].Army.Units.Any(u => CampaignRules.Sheet(u)?.Pending.Count > 0);
            string line = $"{info.Name}\n   {info.Vp} VP - {info.Matches} mission(s) - army {teams[i].Army.TotalPoints} pts - treasury {info.BankedPoints} pts"
                + (pending ? "   (choices waiting)" : "");
            if (ImGui.Selectable(line + $"##t{i}", i == _selected, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
            {
                _selected = i;
                _unit = 0;
                _confirmDelete = false;
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenCamp(i);
        }
        ImGui.EndChild();
    }

    private void OpenCamp(int index)
    {
        _selected = index;
        _unit = 0;
        _cart.Clear();
        _cartVeteran = false;
        _view = EView.Camp;
        _message = "";
    }

    // -- Camp: three columns ------------------------------------------------------------------------

    private void DrawCamp(float x, float y, float w, float h)
    {
        if (Current is not { } cur) { _view = EView.List; return; }
        (string path, BuiltArmyFile army) = cur;
        CampaignNumbers n = NumbersFor(army);

        float gap = 12f;
        float leftW = w * 0.25f;
        float rightW = w * 0.29f;
        float midW = w - leftW - rightW - gap * 2f;

        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##roster", new Vector2(leftW, h), ImGuiChildFlags.Borders);
        DrawRoster(path, army);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + gap, y));
        ImGui.BeginChild("##unit", new Vector2(midW, h), ImGuiChildFlags.Borders);
        DrawUnitDetail(path, army, n);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x + leftW + midW + gap * 2f, y));
        ImGui.BeginChild("##mission", new Vector2(rightW, h), ImGuiChildFlags.Borders);
        DrawNextMission(path, army, n);
        ImGui.EndChild();
    }

    // Column 1 ---------------------------------------------------------------------------------------

    private void DrawRoster(string path, BuiltArmyFile army)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        UiText.Colored(TextDim, $"YOUR ARMY - {army.Units.Count} UNIT(S), {CampaignEconomy.HeroCount(army)} HERO(ES)");
        ImGui.Separator();

        for (int i = 0; i < army.Units.Count; i++)
        {
            UnitFileEntry unit = army.Units[i];
            StrikeUnit? sheet = CampaignRules.Sheet(unit);
            string tag = sheet?.IsHero == true ? " [hero]" : "";
            string second = sheet == null
                ? "no campaign sheet"
                : $"   Lv {sheet.Level} - {sheet.Xp} XP - {unit.ModelCount} model(s) - {unit.PointCost} pts";
            if (sheet?.Pending.Count > 0) second = "   WAITING ON YOU: pick a trait";

            if (ImGui.Selectable($"{unit.Name}{tag}\n{second}##u{i}", i == _unit, ImGuiSelectableFlags.None,
                    new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 2f)))
                _unit = i;
        }

        ImGui.Separator();
        if (UiButton.Navigate("+ Recruit a unit"))
        {
            if (army.Book == null) Say("This army has no book attached, so there is nothing to recruit from.", true);
            else { _cart.Clear(); _cartVeteran = false; _view = EView.Recruit; _message = ""; }
        }
        UiText.Disabled($"Treasury {info.BankedPoints} pts");
    }

    // Column 2 ---------------------------------------------------------------------------------------

    private void DrawUnitDetail(string path, BuiltArmyFile army, CampaignNumbers n)
    {
        if (_unit < 0 || _unit >= army.Units.Count) { UiText.Disabled("Pick a unit on the left."); return; }
        UnitFileEntry unit = army.Units[_unit];
        if (CampaignRules.Sheet(unit) is not { } sheet) { UiText.Disabled("This unit has no campaign sheet."); return; }

        UiText.Colored(Accent, unit.Name + (sheet.IsHero ? "   [hero]" : ""));
        ImGui.TextUnformatted($"{unit.ModelCount} model(s)   Quality {unit.Quality}+   Defense {unit.Defense}+   " +
                              $"{unit.PointCost} pts (book {CampaignEconomy.BookCost(unit)}, campaign {sheet.CostChange:+0;-0;0})");

        int next = Math.Min(CampaignNumbers.MaxXp, (sheet.Level + 1) * CampaignNumbers.XpPerLevel);
        ImGui.TextUnformatted($"Level {sheet.Level}   {sheet.Xp} / {CampaignNumbers.MaxXp} XP" +
                              (sheet.Xp >= CampaignNumbers.MaxXp ? "   (fully levelled)" : $"   next level at {next}")
                              + $"   {sheet.Games} game(s)");
        ImGui.Separator();

        Line("Traits", sheet.Traits.Select(t => t.Replace("Trait: ", "")));
        if (sheet.IsHero)
        {
            Line("Skill sets", sheet.SkillSets);
            Line("Skills", sheet.Skills);
            Line("Talents", sheet.Talents);
        }
        Line("Injuries", sheet.Injuries);
        Line("Weapons", unit.Weapons.Select(Describe));

        foreach (string pending in sheet.Pending.ToList()) DrawChoice(path, army, unit, sheet, pending);

        ImGui.Separator();
        DrawUpgrades(path, army, unit, n);

        ImGui.Separator();
        if (UiButton.Back(_confirmDisband ? "Really disband?" : "Disband unit"))
        {
            if (_confirmDisband)
            {
                CampaignPurchase result = CampaignEconomy.Disband(army, unit);
                army.StrikeTeam!.History.Add(result.Message);
                SaveCurrent(path, army);
                Say(result.Message);
                _confirmDisband = false;
                _unit = Math.Max(0, _unit - 1);
                Reload();
            }
            else _confirmDisband = true;
        }
        if (_confirmDisband) { ImGui.SameLine(); UiText.Colored(Bad, "Its XP and upgrades are gone, and nothing comes back."); }
    }

    private static string Describe(WeaponFileEntry w)
    {
        string range = w.RangeInches > 0 ? $"{w.RangeInches}\", " : "";
        return $"{w.Name} ({range}A{w.Attacks})";
    }

    private static void Line(string label, IEnumerable<string> items)
    {
        string text = string.Join(", ", items);
        UiText.Colored(TextDim, label + ":");
        ImGui.SameLine();
        if (text.Length == 0) UiText.Disabled("none"); else UiText.Wrapped(text);
    }

    private void DrawChoice(string path, BuiltArmyFile army, UnitFileEntry unit, StrikeUnit sheet, string pending)
    {
        string what = pending switch
        {
            CampaignTables.Specialist => "Specialist: +1 to hit in melee, or when shooting?",
            CampaignTables.FastLearner => "Fast Learner: pick any other trait.",
            CampaignTables.LuckyBreak => "Lucky Break: pick any other injury.",
            CampaignTables.LearnedLesson => "Learned Lesson: pick any other talent.",
            _ => "Pick a skill set (its first skill is rolled straight away).",
        };
        UiText.Colored(Gold, what);
        foreach (string option in CampaignRules.Options(sheet, pending))
        {
            ImGui.SameLine();
            if (UiButton.NavigateSmall(option.Replace("Trait: ", "") + "##" + pending))
            {
                string line = CampaignRules.Choose(unit, pending, option, D6);
                army.StrikeTeam!.History.Add(line);
                SaveCurrent(path, army);
                Say(line);
            }
        }
    }

    /// <summary>
    /// The unit's upgrade sections, one row per option, priced by what the change would cost. Every
    /// price and every purchase goes through <see cref="CampaignEconomy"/> - this only draws it.
    /// </summary>
    private void DrawUpgrades(string path, BuiltArmyFile army, UnitFileEntry unit, CampaignNumbers n)
    {
        UiText.Colored(TextDim, "UPGRADES");
        if (army.Book == null) { UiText.Disabled("No book attached to this army."); return; }
        if (CampaignEconomy.Loadout(unit) is not { } loadout)
        {
            UiText.Disabled("This unit wasn't built from a book, so its upgrades can't be changed.");
            return;
        }
        RosterUnit? roster = army.Book.Units.FirstOrDefault(r => r.Id == loadout.RosterUnitId);
        if (roster == null) { UiText.Disabled("The book no longer has this unit."); return; }
        if (roster.Sections.Count == 0) { UiText.Disabled("Nothing to buy for this unit."); return; }

        int treasury = army.StrikeTeam!.BankedPoints;
        foreach (UpgradeSection section in roster.Sections)
        {
            ImGui.PushID(section.Id);
            UiText.Disabled(section.Label);
            foreach (UpgradeOption option in section.Options)
            {
                bool owned = loadout.Choices.Any(c => c.SectionId == section.Id && c.OptionId == option.Id);
                BuilderUnit wanted = Toggled(loadout, section.Id, option.Id, !owned);
                int price = CampaignEconomy.PriceOfChange(army.Book, unit, wanted);
                bool affordable = price <= treasury;

                string label = owned
                    ? (price >= 0 ? $"Drop {option.Label} ({price:+0;-0;0} pts)" : $"Sell {option.Label} (+{-price} pts)")
                    : (price >= 0 ? $"Buy {option.Label} (-{price} pts)" : $"Take {option.Label} (+{-price} pts)");

                ImGui.BeginDisabled(!owned && !affordable);
                if (UiButton.NavigateSmall(label + "##" + option.Id))
                {
                    CampaignPurchase result = CampaignEconomy.ChangeUpgrades(army, unit, army.Book, wanted);
                    if (result.Ok) { army.StrikeTeam.History.Add(result.Message); SaveCurrent(path, army); }
                    Say(result.Message, !result.Ok);
                }
                ImGui.EndDisabled();
                if (!owned && !affordable)
                {
                    ImGui.SameLine();
                    UiText.Disabled($"needs {price - treasury} more");
                }
                if (option.CostUnpriced)
                {
                    ImGui.SameLine();
                    UiText.Disabled("(OPR publishes no price for this one)");
                }
            }
            ImGui.PopID();
        }
    }

    /// <summary>A copy of the loadout with one option switched on or off. One pick per section.</summary>
    private static BuilderUnit Toggled(BuilderUnit from, string sectionId, string optionId, bool on)
    {
        var copy = new BuilderUnit
        {
            RosterUnitId = from.RosterUnitId,
            ModelCount = from.ModelCount,
            Id = from.Id,
            JoinsUnitId = from.JoinsUnitId,
            CombinedWithId = from.CombinedWithId,
            Choices = from.Choices.Where(c => c.SectionId != sectionId)
                .Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count })
                .ToList(),
        };
        if (on) copy.Choices.Add(new UpgradeChoice { SectionId = sectionId, OptionId = optionId, Count = 1 });
        return copy;
    }

    // Column 3 ---------------------------------------------------------------------------------------

    private void DrawNextMission(string path, BuiltArmyFile army, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        UiText.Colored(TextDim, "NEXT MISSION");
        ImGui.Separator();

        // Special objective
        UiText.Colored(TextDim, "Your special objective");
        if (info.SpecialObjective.Length == 0)
        {
            UiText.Disabled("Not rolled yet.");
            if (UiButton.NavigateSmall("Roll it"))
            {
                bool caster = army.Units.Any(u => u.SpecialRules.Any(r => r.PrintableName.Contains("Caster", StringComparison.OrdinalIgnoreCase)));
                info.SpecialObjective = CampaignPreGame.RollSpecialObjective(
                    new CampaignEntrant(info.Name, army.TotalPoints, caster), D6);
                SaveCurrent(path, army);
                Say($"Special objective: {info.SpecialObjective}.");
            }
        }
        else
        {
            UiText.Colored(Gold, info.SpecialObjective);
            UiText.Wrapped(CampaignPreGame.ObjectiveText(info.SpecialObjective));
            UiText.Disabled($"Worth +{n.SpecialObjectiveBonus} pts into your treasury.");
        }

        ImGui.Separator();

        // Who is coming
        var others = Teams.Where(t => t.Path != path).Select(t =>
            new CampaignEntrant(t.Army.StrikeTeam!.Name, t.Army.TotalPoints, false)).ToList();
        int bringing = 0;
        UiText.Colored(TextDim, "Who is coming");
        foreach (UnitFileEntry unit in army.Units)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) continue;
            bool coming = info.Bringing.Count == 0 || info.Bringing.Contains(sheet.Id);
            bool was = coming;
            ImGui.PushID(sheet.Id);
            if (UiButton.Checkbox($"{unit.Name}  ({unit.PointCost} pts)", ref coming) && coming != was)
            {
                if (info.Bringing.Count == 0)
                    info.Bringing = army.Units.Select(u => CampaignRules.Sheet(u)?.Id ?? "").Where(s => s.Length > 0).ToList();
                if (coming) { if (!info.Bringing.Contains(sheet.Id)) info.Bringing.Add(sheet.Id); }
                else info.Bringing.Remove(sheet.Id);
                SaveCurrent(path, army);
            }
            ImGui.PopID();
            if (coming) bringing += unit.PointCost;
        }

        int comingCount = info.Bringing.Count == 0 ? army.Units.Count : info.Bringing.Count;
        if (others.Count == 0)
        {
            ImGui.TextUnformatted($"Bringing {bringing} pts in {comingCount} unit(s).");
            UiText.Disabled("No other campaign army on this computer, so there is no cap to measure against yet.");
        }
        else
        {
            int cap = CampaignPreGame.MaxPointsForMission(others, n);
            ImGui.TextUnformatted($"Bringing {bringing} / {cap} pts in {comingCount} unit(s).");
            string? why = CampaignPreGame.CannotBring(bringing, others, n, comingCount);
            if (why != null) UiText.Colored(Bad, why);
            else UiText.Disabled($"The cap is {n.ArmyCapOverLowest} pts over the smallest army.");
        }

        ImGui.Separator();
        UiText.Colored(TextDim, "How to play it");
        UiText.Wrapped($"Play > Game Type {GameSystemInfo.DisplayName(info.GameSystem)} > pick \"{info.Name}\" as your army. " +
                       $"Campaign games have no points limit, so set Army Points to at least {army.TotalPoints}. " +
                       "The result is written back here after the game.");

        if (info.History.Count > 0)
        {
            ImGui.Separator();
            UiText.Colored(TextDim, "Last mission");
            foreach (string line in Enumerable.Reverse(info.History).Take(6)) UiText.Wrapped(line);
        }
    }

    // -- Recruit: the card grid ---------------------------------------------------------------------

    private void DrawRecruit(float x, float y, float w, float h)
    {
        if (Current is not { } cur || cur.Army.Book is not { } book) { _view = EView.Camp; return; }
        (string path, BuiltArmyFile army) = cur;
        CampaignNumbers n = NumbersFor(army);
        StrikeTeamInfo info = army.StrikeTeam!;

        float cartH = ImGui.GetTextLineHeightWithSpacing() * 4f;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##cards", new Vector2(w, h - cartH - 10f), ImGuiChildFlags.Borders);

        int columns = Math.Max(1, (int)(w / 330f));
        float cardW = (w - 24f) / columns - 10f;
        float cardH = ImGui.GetTextLineHeightWithSpacing() * 9f;

        int drawn = 0;
        foreach (RosterUnit roster in book.Units)
        {
            if (drawn % columns != 0) ImGui.SameLine();
            drawn++;
            ImGui.PushID(roster.Id);
            ImGui.BeginChild("##card" + roster.Id, new Vector2(cardW, cardH), ImGuiChildFlags.Borders);
            DrawCard(path, army, book, roster, n, info);
            ImGui.EndChild();
            ImGui.PopID();
        }
        if (drawn == 0) UiText.Disabled("This book has no units.");
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(x, y + h - cartH));
        ImGui.BeginChild("##cart", new Vector2(w, cartH), ImGuiChildFlags.Borders);
        DrawCart(path, army, book, n);
        ImGui.EndChild();
    }

    private void DrawCard(string path, BuiltArmyFile army, BookFile book, RosterUnit roster,
                          CampaignNumbers n, StrikeTeamInfo info)
    {
        bool isHero = roster.Rules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });
        UiText.Colored(Accent, roster.Name + (isHero ? "   [hero]" : ""));
        ImGui.TextUnformatted($"Quality {roster.Quality}+   Defense {roster.Defense}+   {roster.BaseModelCount} model(s)");

        WeaponFileEntry? ranged = roster.Weapons.Where(wp => wp.RangeInches > 0).OrderByDescending(wp => wp.RangeInches).FirstOrDefault();
        WeaponFileEntry? melee = roster.Weapons.Where(wp => wp.RangeInches == 0).OrderByDescending(wp => wp.Attacks).FirstOrDefault();
        UiText.Disabled(ranged != null ? "Ranged: " + Describe(ranged) : "Ranged: none");
        UiText.Disabled(melee != null ? "Melee: " + Describe(melee) : "Melee: none");
        string rules = string.Join(", ", roster.Rules.Select(r => r.PrintableName).Take(4));
        UiText.Wrapped(rules.Length > 0 ? rules : "No special rules");

        ImGui.TextUnformatted($"{roster.BasePointCost} pts");

        var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        string? why = CampaignEconomy.CannotRecruit(army, book, wanted, isHero && _heroRecruited);
        int inCart = _cart.Count(c => c == roster.Id);

        ImGui.BeginDisabled(why != null);
        if (UiButton.NavigateSmall(inCart > 0 ? $"In cart x{inCart} - add another" : "Recruit"))
            _cart.Add(roster.Id);
        ImGui.EndDisabled();
        if (why != null) UiText.Colored(Bad, why);
        if (inCart > 0)
        {
            ImGui.SameLine();
            if (UiButton.NavigateSmall("-##rm" + roster.Id)) _cart.Remove(roster.Id);
        }
    }

    private void DrawCart(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        StrikeTeamInfo info = army.StrikeTeam!;
        int total = 0;
        foreach (string id in _cart)
            total += book.Units.FirstOrDefault(r => r.Id == id)?.BasePointCost ?? 0;
        if (_cartVeteran) total += n.VeteranRecruitCost;

        if (_cart.Count == 0) UiText.Disabled("Nothing in the cart yet - pick a card above.");
        else UiText.Wrapped("In the cart: " + string.Join(", ",
            _cart.Select(id => book.Units.FirstOrDefault(r => r.Id == id)?.Name ?? id)));

        bool veteran = _cartVeteran;
        if (UiButton.Checkbox($"One recruit starts at 5 XP (+{n.VeteranRecruitCost} pts)", ref veteran)) _cartVeteran = veteran;

        ImGui.TextUnformatted($"Total {total} pts   -   treasury {info.BankedPoints} pts   -   leaves {info.BankedPoints - total} pts");
        if (total > info.BankedPoints) UiText.Colored(Bad, "That is more than the treasury holds.");

        ImGui.BeginDisabled(_cart.Count == 0 || total > info.BankedPoints);
        if (UiButton.Confirm("Confirm recruits")) ConfirmRecruits(path, army, book, n);
        ImGui.EndDisabled();
    }

    private void ConfirmRecruits(string path, BuiltArmyFile army, BookFile book, CampaignNumbers n)
    {
        bool veteranLeft = _cartVeteran;
        var lines = new List<string>();
        foreach (string id in _cart.ToList())
        {
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == id);
            if (roster == null) continue;
            bool isHero = roster.Rules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });
            var wanted = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
            CampaignPurchase result = CampaignEconomy.Recruit(army, book, wanted, n,
                veteran: veteranLeft, heroAlreadyRecruitedThisMission: isHero && _heroRecruited, d6: D6);
            lines.Add(result.Message);
            if (!result.Ok) continue;
            if (veteranLeft) veteranLeft = false;
            if (isHero) _heroRecruited = true;
        }

        army.StrikeTeam!.History.AddRange(lines);
        SaveCurrent(path, army);
        _cart.Clear();
        _cartVeteran = false;
        Reload();
        _view = EView.Camp;
        Say(string.Join("  ", lines));
    }

    // -- New campaign army --------------------------------------------------------------------------

    private void DrawNew(float x, float y, float w, float h)
    {
        var armies = Armies;
        CampaignNumbers n = CampaignNumbers.Firefight;
        ImGui.SetCursorPos(new Vector2(x, y));
        ImGui.BeginChild("##newTeam", new Vector2(w, h), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Army name");
        ImGui.SetNextItemWidth(Math.Min(420f, w * 0.5f));
        ImGui.InputText("##teamName", ref _newName, 48);
        ImGui.Spacing();
        UiText.Wrapped($"Starting army (from armies\\GF Firefight). The treasury is {n.Treasury} pts and you must spend " +
                       $"at least {n.MinimumSpend} of it, with at most one hero. Whatever is left over you keep to spend later.");
        if (armies.Count == 0)
            UiText.Disabled("No Firefight armies yet - make one in Army Forge (Game Type GF: Firefight) or put a .fdgarmy file in armies\\GF Firefight.");
        for (int i = 0; i < armies.Count; i++)
        {
            BuiltArmyFile a = armies[i].Army;
            string? why = CampaignRules.CannotStart(a, n);
            if (ImGui.Selectable($"{a.Name}   ({a.Faction}, {a.TotalPoints} pts, {a.Units.Count} units)##a{i}", i == _newArmy))
                _newArmy = i;
            if (why != null) { ImGui.SameLine(); UiText.Colored(Bad, why); }
        }
        ImGui.EndChild();
    }

    private string? NewProblem()
    {
        var armies = Armies;
        if (_newName.Trim().Length == 0) return "Name the army.";
        if (_newArmy < 0 || _newArmy >= armies.Count) return "Pick a starting army.";
        if (Teams.Any(t => string.Equals(t.Army.StrikeTeam!.Name, _newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "A campaign army with that name exists.";
        return CampaignRules.CannotStart(armies[_newArmy].Army, CampaignNumbers.Firefight);
    }

    private void Create()
    {
        // A fresh copy of the army file, so the original list stays as it was.
        var source = Armies[_newArmy];
        if (StrikeTeamFiles.Load(source.Path) is not { } army) { Say("Couldn't read that army file.", true); return; }
        string name = _newName.Trim();
        string path = StrikeTeamFiles.NewPath(name);
        CampaignRules.Start(army, name, path, CampaignNumbers.Firefight, EGameSystem.GFFirefight);
        // Not editable in Army Forge any more: re-compiling it there would wipe the campaign changes.
        // Each unit's own selections were copied onto its sheet by Start, which is what the campaign
        // screen upgrades against, and the book stays attached so there is something to buy from.
        army.Selections = null;
        if (!StrikeTeamFiles.Save(path, army)) { Say("Couldn't save the campaign army file.", true); return; }
        Reload();
        _selected = Teams.FindIndex(t => t.Path == path);
        OpenCamp(_selected);
        Say($"{name} created, with {army.StrikeTeam!.BankedPoints} pts left in the treasury.");
    }

    private void Delete(string path)
    {
        try { File.Delete(path); Say("Campaign army deleted."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say("Couldn't delete the file.", true); }
        Reload();
    }

    // -- Bottom bar ---------------------------------------------------------------------------------

    private void DrawBottomBar(int screenW, int screenH, float margin, float barH)
    {
        Vector2 btn = new(screenW * 0.13f, barH * 0.55f);
        float by = screenH - barH + (barH - btn.Y) * 0.5f;
        bool esc = ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput;
        float rx = screenW - margin - btn.X;

        ImGui.SetCursorPos(new Vector2(margin, by));
        switch (_view)
        {
            case EView.New:
                if (UiButton.Back("Cancel", btn) || esc) _view = EView.List;
                ImGui.SetCursorPos(new Vector2(rx, by));
                string? problem = NewProblem();
                ImGui.BeginDisabled(problem != null);
                if (UiButton.Confirm("Create", btn)) Create();
                ImGui.EndDisabled();
                if (problem != null) RightLabel(rx, by, btn, problem, TextDim);
                break;

            case EView.Recruit:
                if (UiButton.Back("Back to camp", btn) || esc) { _cart.Clear(); _view = EView.Camp; }
                break;

            case EView.Camp:
                if (UiButton.Back("Campaign armies", btn) || esc) _view = EView.List;
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Confirm("Play next mission", btn)) OnBack?.Invoke();
                RightLabel(rx, by, btn, "Pick it in the lobby as your army", TextDim);
                DrawMessage(margin, by, btn);
                break;

            default:
                if (UiButton.Back("Back", btn) || esc) OnBack?.Invoke();
                ImGui.SetCursorPos(new Vector2(rx, by));
                if (UiButton.Confirm("New Campaign Army", btn))
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
                    if (UiButton.Navigate("Open", btn)) OpenCamp(_selected);
                    ImGui.SetCursorPos(new Vector2(rx - btn.X * 2f - 20f, by));
                    if (UiButton.Back(_confirmDelete ? "Really delete?" : "Delete", btn))
                    {
                        if (_confirmDelete) Delete(cur.Path);
                        else _confirmDelete = true;
                    }
                }
                DrawMessage(margin, by, btn);
                break;
        }
    }

    private void DrawMessage(float margin, float by, Vector2 btn)
    {
        if (_message.Length == 0) return;
        Vector2 ts = ImGui.CalcTextSize(_message);
        ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(_messageIsError ? Bad : Good, _message);
    }

    private static void RightLabel(float rx, float by, Vector2 btn, string text, Vector4 color)
    {
        Vector2 ts = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos(new Vector2(rx - 16f - ts.X, by + (btn.Y - ts.Y) / 2f));
        UiText.Colored(color, text);
    }
}
