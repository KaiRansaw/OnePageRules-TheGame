using System.Numerics;
using FDG;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Load Game screen (Load Game and Join Mockup, 2026-11-07): save tiles newest first with All /
/// Grimdark Future / Age of Fantasy filter buttons along the top; on the right a big picture of the selected
/// save's board and its details (game, mode, round, score, points, players, date) with LOAD and Delete.
/// Delete asks first. Player 1 (slot 0 - the host, i.e. you) is always listed first.
///
/// <para>Saves are read on a background task, one at a time, so the screen opens instantly and tiles
/// fill in as each file is read. Controls: click a tile to select it, double-click (or LOAD, or Enter)
/// to load it, arrow keys to move, mouse wheel to scroll, Esc / Back to return.</para>
///
/// <para>Rental layout (plain ImGui). What is permanent - what a save says about itself and which
/// filter it falls under - is in the pure <see cref="Matches"/> and the summary read.</para>
/// </summary>
public sealed class LoadGameScreen : IAppScreen
{
    /// <summary>Called with the full path of the save the player chose.</summary>
    public Action<string>? OnLoad;
    public Action? OnBack;

    /// <summary>The filter buttons along the top.</summary>
    internal enum EFilter { All, GrimdarkFuture, AgeOfFantasy }

    /// <summary>Whether a save of <paramref name="system"/> shows under <paramref name="filter"/>. A save whose
    /// game could not be read (made during setup, before the engine recorded the settings) shows under All only.</summary>
    internal static bool Matches(EFilter filter, EGameSystem? system) => filter switch
    {
        EFilter.All => true,
        EFilter.GrimdarkFuture => system is { } g && GameScreen.Games(GameScreen.EFamily.GrimdarkFuture).Contains(g),
        EFilter.AgeOfFantasy => system is { } a && GameScreen.Games(GameScreen.EFamily.AgeOfFantasy).Contains(a),
        _ => true,
    };

    // ── What a tile shows, pulled out of the save once and kept as plain data ────────────────────────
    private sealed record PreviewModel(Vector2 PosIn, IBaseShape Shape, Float2 Facing, int ColorIndex);
    private sealed record PreviewTerrain(IZone Shape, ETerrainType Type);
    private sealed record PreviewObjective(Vector2 PosIn, int? ColorIndex, int Number);

    private sealed class SaveSummary
    {
        public List<string> Factions = new();      // in slot order: Player 1 first
        public List<string> ArmyNames = new();
        public List<string> PlayerNames = new();
        public List<int> PointsLimits = new();
        public List<int> Scores = new();
        public List<int>? Vp;                      // mission-card games only
        public int ArmyPoints;
        public EGameSystem? GameSystem;            // null when the save has no settings (made during setup)
        public string GameLabel = "";
        public string ModeLabel = "";
        public string RoundLabel = "";
        public bool CanResume = true;   // false for a save made before round 1 (setup / deployment)
        public int? HordePoints;
        public float TableW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
        public float TableH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
        public TableBackgroundStyle Background = TableBackgrounds.For(ETableBackground.Forest);
        public List<PreviewTerrain> Terrain = new();
        public List<PreviewObjective> Objectives = new();
        public List<PreviewModel> Models = new();
    }

    private sealed class SaveEntry
    {
        public string Path = "";
        public string Title = "";
        public DateTime Saved;
        public volatile SaveSummary? Summary;
        public volatile string? Error;
    }

    private List<SaveEntry> _entries = new();
    private EFilter _filter = EFilter.All;
    private string? _selectedPath;
    private bool _scrollToSelected;
    private int _generation;   // bumped on every rescan so a stale background read stops early
    private string? _deleteAsked;   // the save the Delete popup is asking about

    private string _error = "";

    /// <summary>Shows a message along the bottom of the screen (e.g. why a save can't be loaded).</summary>
    public void ShowError(string message) => _error = message;

    public void OnShown()
    {
        _error = "";
        _deleteAsked = null;
        int gen = ++_generation;
        _entries = SavePaths.ListSaves().Select(p => new SaveEntry
        {
            Path = p,
            Title = System.IO.Path.GetFileNameWithoutExtension(p),
            Saved = SavePaths.SafeWriteTime(p),
        }).OrderByDescending(e => e.Saved).ToList();
        _selectedPath = _entries.FirstOrDefault()?.Path;
        _scrollToSelected = true;

        List<SaveEntry> toRead = _entries;
        Task.Run(() =>
        {
            foreach (SaveEntry e in toRead)
            {
                if (gen != _generation) return;
                try { e.Summary = ReadSummary(e.Path); }
                catch (Exception ex) { e.Error = ex.GetBaseException().Message; }
            }
        });
    }

    // ── Reading a save ───────────────────────────────────────────────────────────────────────────────
    private static SaveSummary ReadSummary(string path)
    {
        var store = GameSaveSerializer.Load(File.ReadAllText(path));
        var table = new TableState(store);
        var s = new SaveSummary();

        // Player order: slot 0 first (Player 1 - you), then the rest in slot order.
        List<IPlayerSlotInfo> slots = table.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
        var colorIndex = new Dictionary<PlayerID, int>();
        for (int i = 0; i < slots.Count; i++) colorIndex[slots[i].PlayerID] = i;

        Dictionary<PlayerID, ArmyData> armies = table.Armies.Objects.OfType<ArmyData>()
            .GroupBy(a => a.PlayerID).ToDictionary(g => g.Key, g => g.First());
        Dictionary<PlayerID, int> scores = table.Progress.Scores.ToDictionary(x => x.PlayerID, x => x.ObjectiveCount);

        foreach (IPlayerSlotInfo slot in slots)
        {
            armies.TryGetValue(slot.PlayerID, out ArmyData? army);
            string faction = !string.IsNullOrWhiteSpace(army?.Faction) ? army!.Faction
                : !string.IsNullOrWhiteSpace(army?.ArmyName) ? army!.ArmyName
                : slot.Name;
            s.Factions.Add(faction);
            s.ArmyNames.Add(!string.IsNullOrWhiteSpace(army?.ArmyName) ? army!.ArmyName : faction);
            s.PlayerNames.Add(slot.Name);
            s.PointsLimits.Add(army?.PointsLimit ?? 0);
            s.Scores.Add(scores.TryGetValue(slot.PlayerID, out int sc) ? sc : 0);
        }

        GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
        GameSettings settings = progress?.Settings ?? GameSettings.GetDefault();
        s.ArmyPoints = progress != null ? settings.ArmyPoints : s.PointsLimits.DefaultIfEmpty(0).Max();
        s.Background = TableBackgrounds.For(settings.TableBackground);
        if (progress != null)
        {
            s.GameSystem = settings.GameSystem;
            s.GameLabel = GameSystemInfo.DisplayName(settings.GameSystem);
            (s.TableW, s.TableH) = TableSizing.Resolve(settings);
        }
        if (table.Progress.MissionCards is { } cards)
            s.Vp = slots.Select(sl => cards.Vp.TryGetValue(sl.SlotID, out int v) ? v : 0).ToList();
        EGameMode mode = table.Progress.GameMode;
        s.ModeLabel = mode == EGameMode.Horde ? "Horde Mode" : GameSystemInfo.ModeLabel(mode, settings.GameSystem);
        s.HordePoints = table.Progress.HordePointsEarned;
        s.RoundLabel = table.Progress.RoundCount is int r ? (r <= table.Progress.TotalRounds ? $"Round {r}/{table.Progress.TotalRounds}" : $"Round {r} (extra)") : "Deployment";
        s.CanResume = progress != null;
        if (!s.CanResume) s.RoundLabel = "Saved during setup - can't be resumed";

        foreach (ITerrain t in table.Terrain.Objects)
            s.Terrain.Add(new PreviewTerrain(t.Shape, t.TerrainType));

        int n = 0;
        foreach (IObjective o in table.Objectives.Objects)
        {
            n++;
            int? owner = o.OwnerID is PlayerID pid && colorIndex.TryGetValue(pid, out int ci) ? ci : null;
            s.Objectives.Add(new PreviewObjective(new Vector2(o.Position.x, o.Position.z), owner, o.HordeNumber ?? n));
        }

        foreach (IUnit u in table.Units.Objects)
        {
            if (!u.GetIsOnBattlefield()) continue;
            int ci = colorIndex.TryGetValue(u.PlayerID, out int c) ? c : 0;
            foreach (IModel m in u.Models)
            {
                if (!m.GetIsAlive()) continue;
                s.Models.Add(new PreviewModel(new Vector2(m.Position.x, m.Position.z), m.BaseShape, m.Facing, ci));
            }
        }
        return s;
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────────
    private static uint U32(Color c, float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * alpha));

    private static uint U32(int r, int g, int b, int a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

    private static Color PlayerColor(int index) =>
        PlayerColorOptions.Options[((index % PlayerColorOptions.Count) + PlayerColorOptions.Count) % PlayerColorOptions.Count].Color;

    // Halo-style accent: pale steel blue frames, brighter when selected.
    private static readonly uint FrameColor     = U32(120, 170, 210, 140);
    private static readonly uint FrameSelected  = U32(170, 220, 255, 255);
    private static readonly uint TileBg         = U32(12, 16, 22, 215);
    private static readonly uint TileBgHover    = U32(22, 30, 40, 230);
    private static readonly uint TextBright     = U32(225, 235, 245);
    private static readonly uint TextDim        = U32(150, 165, 180);

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##LoadGame", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                  ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar |
                                  ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        ImFontPtr headFont = useMenuFont ? RaylibRenderer.MenuFont : ImGui.GetFont();
        float margin = MathF.Max(16f, screenW * 0.02f);

        List<SaveEntry> shown = _entries.Where(e => Matches(_filter, e.Summary?.GameSystem)).ToList();
        int selected = shown.FindIndex(e => e.Path == _selectedPath);
        if (selected < 0 && shown.Count > 0) { selected = 0; _selectedPath = shown[0].Path; }

        // ── Header: title, count, filter buttons ──
        float headerH = MathF.Max(56f, screenH * 0.075f);
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();
        float titlePx = headerH * 0.5f;
        wdl.AddText(headFont, titlePx, new Vector2(margin, (headerH - titlePx) / 2f), TextBright, "LOAD GAME");
        float titleW = ImGui.CalcTextSize("LOAD GAME").X * (titlePx / ImGui.GetFontSize()) * 1.1f;
        string count = $"{shown.Count} saved game{(shown.Count == 1 ? "" : "s")} - newest first";
        wdl.AddText(new Vector2(margin + titleW + 24f, (headerH - ImGui.GetFontSize()) / 2f), TextDim, count);

        (EFilter Filter, string Label, string Hover)[] filters =
        {
            (EFilter.All, "All", "Every saved game."),
            (EFilter.GrimdarkFuture, "Grimdark Future", "Grimdark Future, GF: Firefight and GF: Star Quest saves."),
            (EFilter.AgeOfFantasy, "Age of Fantasy", "Age of Fantasy, AoF: Skirmish and AoF: Quest saves."),
        };
        float fx = screenW - margin;
        float fh = headerH * 0.6f;
        for (int k = filters.Length - 1; k >= 0; k--)
        {
            float fw = ImGui.CalcTextSize(filters[k].Label).X + 32f;
            fx -= fw;
            ImGui.SetCursorPos(new Vector2(fx, (headerH - fh) / 2f));
            bool on = _filter == filters[k].Filter;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.70f, 1f));
            if (ImGui.Button(filters[k].Label + "##filter", new Vector2(fw, fh)))
            {
                _filter = filters[k].Filter;
                _scrollToSelected = true;
            }
            if (on) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) UiText.Tooltip(filters[k].Hover);
            fx -= 8f;
        }
        wdl.AddLine(new Vector2(0, headerH), new Vector2(screenW, headerH), U32(74, 74, 74));

        // ── Body: tile grid on the left, the selected save on the right ──
        float bottomBarH = MathF.Max(60f, screenH * 0.08f);
        float bodyY = headerH + 14f;
        float bodyH = screenH - bodyY - bottomBarH - 6f;
        float detailW = Math.Clamp(screenW * 0.30f, 340f, 560f);
        float gridW = screenW - margin * 2f - detailW - 16f;

        const int Columns = 3;
        float gap = 12f;
        float scrollbarW = ImGui.GetStyle().ScrollbarSize + 4f;
        float tileW = (gridW - scrollbarW - gap * (Columns - 1)) / Columns;
        float pad = MathF.Max(6f, tileW * 0.03f);
        float previewH = (tileW - pad * 2f) / 1.5f + pad * 1.5f;   // 6' x 4' board = 3:2
        float textH = MathF.Max(ImGui.GetFontSize() * 4.2f, tileW * 0.30f);
        float tileH = previewH + textH;

        ImGui.SetCursorPos(new Vector2(margin, bodyY));
        ImGui.BeginChild("##saveGrid", new Vector2(gridW, bodyH), ImGuiChildFlags.None, ImGuiWindowFlags.None);

        if (shown.Count == 0)
        {
            ImGui.SetCursorPos(new Vector2(0, bodyH * 0.4f));
            UiText.Disabled(_entries.Count == 0
                ? "No saved games yet. Save a game from the in-game menu and it will appear here."
                : "No saved games for this game family. Pick All to see every save.");
        }

        bool loadNow = false;
        for (int i = 0; i < shown.Count; i++)
        {
            int col = i % Columns, row = i / Columns;
            ImGui.SetCursorPos(new Vector2(col * (tileW + gap), row * (tileH + gap)));
            if (DrawTile(shown[i], i == selected, tileW, tileH, previewH, pad, headFont, out bool doubleClicked))
            {
                selected = i;
                _selectedPath = shown[i].Path;
            }
            if (doubleClicked) loadNow = true;
            if (i == selected && _scrollToSelected)
            {
                ImGui.SetScrollHereY(0.5f);
                _scrollToSelected = false;
            }
        }
        int rows = (shown.Count + Columns - 1) / Columns;
        ImGui.SetCursorPos(new Vector2(0, rows * (tileH + gap)));
        ImGui.Dummy(new Vector2(1, 1));
        ImGui.EndChild();

        // Keyboard: arrows move one tile sideways or one row up/down (not while the Delete box is up).
        bool popupOpen = _deleteAsked != null;
        if (shown.Count > 0 && !popupOpen)
        {
            int before = selected;
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) selected = Math.Min(shown.Count - 1, selected + 1);
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))  selected = Math.Max(0, selected - 1);
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))  selected = Math.Min(shown.Count - 1, selected + Columns);
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))    selected = Math.Max(0, selected - Columns);
            if (selected != before) { _scrollToSelected = true; _selectedPath = shown[selected].Path; }
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) loadNow = true;
        }

        SaveEntry? current = selected >= 0 && selected < shown.Count ? shown[selected] : null;
        ImGui.SetCursorPos(new Vector2(screenW - margin - detailW, bodyY));
        ImGui.BeginChild("##saveDetail", new Vector2(detailW, bodyH), ImGuiChildFlags.Borders);
        if (DrawDetail(current, detailW, headFont, out bool deleteClicked)) loadNow = true;
        if (deleteClicked && current != null)
        {
            _deleteAsked = current.Path;
            ImGui.OpenPopup(DeletePopup);
        }
        DrawDeletePopup();
        ImGui.EndChild();

        // ── Bottom bar: Back, and the note from the mockup ──
        Vector2 btn = new(MathF.Max(140f, screenW * 0.09f), bottomBarH * 0.66f);
        float btnY = screenH - bottomBarH + (bottomBarH - btn.Y) * 0.5f;
        ImGui.SetCursorPos(new Vector2(margin, btnY));
        if (UiButton.Back("Back", btn) || (!popupOpen && ImGui.IsKeyPressed(ImGuiKey.Escape, false)))
        {
            _generation++;
            OnBack?.Invoke();
        }
        ImGui.SameLine(0f, 16f);
        ImGui.SetCursorPosY(btnY + (btn.Y - ImGui.GetTextLineHeight()) / 2f);
        UiText.Disabled("Saves made before round 1 show \"can't be resumed\". Campaign and Quest campaign "
            + "progress lives in their own screens, not here.", wrap: false);

        ImGui.End();
        ImGui.PopStyleColor();

        if (_error.Length > 0)
        {
            Vector2 es = ImGui.CalcTextSize(_error);
            ImGui.GetForegroundDrawList().AddText(new Vector2((screenW - es.X) / 2f, bodyY + bodyH - es.Y - 4f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.55f, 0.45f, 1f)), _error);
        }

        if (loadNow && current?.Summary is { CanResume: false })
        {
            ShowError("That game was saved before round 1 started, so it can't be resumed.");
            loadNow = false;
        }
        if (loadNow) _error = "";
        if (loadNow && current != null && current.Error == null)
        {
            _generation++;
            OnLoad?.Invoke(current.Path);
        }
    }

    private const string DeletePopup = "Delete this saved game?";

    /// <summary>The selected save in full: big board, "Army vs Army", its details, Delete and LOAD.
    /// Returns true when LOAD was pressed; <paramref name="deleteClicked"/> when Delete was.</summary>
    private static bool DrawDetail(SaveEntry? e, float width, ImFontPtr headFont, out bool deleteClicked)
    {
        deleteClicked = false;
        if (e == null)
        {
            UiText.Disabled("Pick a saved game on the left.");
            return false;
        }
        SaveSummary? s = e.Summary;
        float inner = ImGui.GetContentRegionAvail().X;

        // Big board picture, shaped like the save's own table.
        float tw = s?.TableW ?? GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
        float th = s?.TableH ?? GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
        float boardH = MathF.Min(inner * th / tw, ImGui.GetContentRegionAvail().Y * 0.45f);
        Vector2 b0 = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(inner, boardH));
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        if (s != null) DrawBoard(dl, s, b0, b0 + new Vector2(inner, boardH));
        else
        {
            string msg = e.Error != null ? "Could not read this save" : "Reading...";
            Vector2 ts = ImGui.CalcTextSize(msg);
            dl.AddText(b0 + new Vector2(inner, boardH) / 2f - ts / 2f, TextDim, msg);
        }

        ImGui.Spacing();
        ImGui.PushFont(headFont);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(s != null && s.ArmyNames.Count > 0 ? string.Join(" vs ", s.ArmyNames) : e.Title);
        ImGui.PopTextWrapPos();
        ImGui.PopFont();
        ImGui.Spacing();

        if (s != null && ImGui.BeginTable("##savefacts", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Objectives").X + 16f);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);
            void Row(string k, string v)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); UiText.Disabled(k, wrap: false);
                ImGui.TableSetColumnIndex(1); ImGui.PushTextWrapPos(0f); ImGui.TextUnformatted(v); ImGui.PopTextWrapPos();
            }
            Row("Game", s.GameLabel.Length > 0 ? $"{s.GameLabel} - {s.ModeLabel}" : s.ModeLabel);
            Row("Round", s.RoundLabel);
            Row("Score", "Objectives held " + string.Join(" vs ", s.Scores)
                + (s.HordePoints is int hp ? $" (Horde {hp} pts earned)" : ""));
            if (s.Vp != null) Row("VP", string.Join(" vs ", s.Vp));
            bool samePoints = s.PointsLimits.Count == 0 || s.PointsLimits.All(pl => pl == s.PointsLimits[0] || pl == 0);
            Row("Points", samePoints ? $"{s.ArmyPoints} pts" : string.Join(" vs ", s.PointsLimits) + " pts");
            Row("Players", string.Join(", ", s.PlayerNames.Select((n, i) =>
                i < s.Factions.Count ? $"{n} ({s.Factions[i]})" : n)));
            Row("Saved", e.Saved.ToString("MMM d, yyyy h:mm tt"));
            Row("File", e.Title);
            ImGui.EndTable();
        }
        else if (e.Error != null)
        {
            UiText.Colored(new Vector4(1f, 0.55f, 0.45f, 1f), e.Error);
        }

        // Delete and LOAD pinned to the bottom of the panel.
        float rowH = MathF.Max(44f, ImGui.GetFrameHeight() * 1.8f);
        float y = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - rowH;
        if (ImGui.GetCursorPosY() < y) ImGui.SetCursorPosY(y);
        float delW = MathF.Max(110f, inner * 0.28f);
        if (UiButton.Back("Delete", new Vector2(delW, rowH))) deleteClicked = true;
        if (ImGui.IsItemHovered()) UiText.Tooltip("Delete this saved game from disk. Asks first.");
        ImGui.SameLine();
        ImGui.BeginDisabled(s is { CanResume: false } || e.Error != null);
        bool load = UiButton.Confirm("LOAD", new Vector2(ImGui.GetContentRegionAvail().X, rowH));
        ImGui.EndDisabled();
        return load;
    }

    private void DrawDeletePopup()
    {
        if (!ImGui.BeginPopupModal(DeletePopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }
        SaveEntry? target = _entries.FirstOrDefault(x => x.Path == _deleteAsked);
        ImGui.TextUnformatted($"Delete \"{target?.Title ?? "this save"}\"?");
        UiText.Disabled("The file is removed from disk. This can't be undone.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Keep it", new Vector2(140, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            _deleteAsked = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (UiButton.Confirm("Delete", new Vector2(140, 0)) && target != null)
        {
            try
            {
                File.Delete(target.Path);
                int at = _entries.IndexOf(target);
                _entries.Remove(target);
                // Select the next save along, so a run of deletes doesn't jump back to the top.
                _selectedPath = _entries.Count == 0 ? null : _entries[Math.Min(at, _entries.Count - 1)].Path;
                _error = "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _error = "Couldn't delete that save: " + ex.Message;
            }
            _deleteAsked = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>Draws one tile at the current cursor. Returns true when it was clicked.</summary>
    private static bool DrawTile(SaveEntry e, bool selected, float w, float h, float previewH, float pad,
        ImFontPtr headFont, out bool doubleClicked)
    {
        Vector2 p0 = ImGui.GetCursorScreenPos();
        Vector2 p1 = p0 + new Vector2(w, h);
        bool clicked = ImGui.InvisibleButton($"##tile{e.Path}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        doubleClicked = hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(p0, p1, true);
        dl.AddRectFilled(p0, p1, hovered ? TileBgHover : TileBg);

        // Board preview.
        Vector2 boardMin = p0 + new Vector2(pad, pad);
        Vector2 boardMax = new(p1.X - pad, p0.Y + previewH - pad * 0.5f);
        SaveSummary? s = e.Summary;
        if (s != null) DrawBoard(dl, s, boardMin, boardMax);
        else
        {
            string msg = e.Error != null ? "Could not read this save" : "Reading...";
            Vector2 ts = ImGui.CalcTextSize(msg);
            dl.AddText((boardMin + boardMax) / 2f - ts / 2f, TextDim, msg);
        }

        // Text block under the board: title, game - mode - round, date.
        float big = ImGui.GetFontSize() * 1.1f;
        float small = ImGui.GetFontSize();
        ImFontPtr body = ImGui.GetFont();
        float x = p0.X + pad;
        float y = p0.Y + previewH + 2f;
        float maxW = w - pad * 2f;

        if (s != null)
        {
            string title = string.Join(" vs ", s.Factions.Select(Abbreviate));
            y = DrawWrapped(dl, headFont, big, x, y, maxW, TextBright, title);
            string game = s.GameLabel.Length > 0 ? s.GameLabel + " - " : "";
            y = DrawWrapped(dl, body, small, x, y, maxW, TextBright, $"{game}{s.ModeLabel} - {s.RoundLabel}");
            DrawWrapped(dl, body, small, x, y, maxW, TextDim, e.Saved.ToString("MMM d, yyyy h:mm tt"));
        }
        else
        {
            y = DrawWrapped(dl, headFont, big, x, y, maxW, TextBright, e.Title);
            if (e.Error != null) DrawWrapped(dl, body, small, x, y, maxW, TextDim, e.Error);
        }
        dl.PopClipRect();

        // Frame: thin when idle, thick and bright when selected.
        dl.AddRect(p0, p1, selected ? FrameSelected : FrameColor, 2f, ImDrawFlags.None, selected ? 4f : 1.5f);
        return clicked;
    }

    /// <summary>Short faction name: the first letter of each word ("Blood Brothers" -> "BB",
    /// "Human Defense Force" -> "HDF", "Soul-Snatcher Cults" -> "SSC"). A one-word name stays whole
    /// ("Orks"), since a single letter wouldn't tell you anything.</summary>
    internal static string Abbreviate(string name)
    {
        string[] words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return name.Trim();
        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0])));
    }

    /// <summary>Draws <paramref name="text"/> word-wrapped to <paramref name="maxW"/> pixels and returns the
    /// y just below the last line. A single word wider than the line is kept whole (the tile clips it).</summary>
    private static float DrawWrapped(ImDrawListPtr dl, ImFontPtr font, float size, float x, float y, float maxW,
        uint color, string text)
    {
        foreach (string line in WrapLines(font, size, text, maxW))
        {
            dl.AddText(font, size, new Vector2(x, y), color, line);
            y += size + 2f;
        }
        return y + 3f;
    }

    private static List<string> WrapLines(ImFontPtr font, float size, string text, float maxW)
    {
        ImGui.PushFont(font);
        float k = size / ImGui.GetFontSize();
        float Width(string t) => ImGui.CalcTextSize(t).X * k;

        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string attempt = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Width(attempt) > maxW)
            {
                lines.Add(current);
                current = word;
            }
            else current = attempt;
        }
        if (current.Length > 0) lines.Add(current);
        ImGui.PopFont();
        return lines;
    }

    /// <summary>A flat top-down picture of the saved board, fitted into the given rectangle.</summary>
    private static void DrawBoard(ImDrawListPtr dl, SaveSummary s, Vector2 min, Vector2 max)
    {
        float tw = s.TableW;
        float th = s.TableH;
        Vector2 avail = max - min;
        float scale = MathF.Max(0.1f, MathF.Min(avail.X / tw, avail.Y / th));
        int ox = (int)(min.X + (avail.X - tw * scale) / 2f);
        int oy = (int)(min.Y + (avail.Y - th * scale) / 2f);
        Vector2 P(float xIn, float zIn) => new(ox + xIn * scale, oy + (th - zIn) * scale);

        Vector2 tl = P(0, th), br = P(tw, 0);
        dl.AddRectFilled(tl, br, U32(s.Background.Surface));
        for (float gx = 6; gx < tw; gx += 6) dl.AddLine(P(gx, 0), P(gx, th), U32(s.Background.GridMinor));
        for (float gz = 6; gz < th; gz += 6) dl.AddLine(P(0, gz), P(tw, gz), U32(s.Background.GridMinor));

        dl.PushClipRect(tl, br, true);
        foreach (PreviewTerrain t in s.Terrain)
        {
            (Color fill, Color outline) = TerrainColors.For(t.Type);
            ZoneRenderer.DrawFilled(t.Shape, dl, scale, ox, oy, th, U32(fill), U32(outline), 1f);
        }

        foreach (PreviewObjective o in s.Objectives)
        {
            Color c = o.ColorIndex is int ci ? PlayerColor(ci) : new Color(160, 160, 160, 255);
            Vector2 center = P(o.PosIn.X, o.PosIn.Y);
            dl.AddCircleFilled(center, 3f * scale, U32(c, 0.18f), 32);
            dl.AddCircle(center, 3f * scale, U32(c, 0.7f), 32, 1f);
            float r = MathF.Max(4f, 0.6f * scale);
            dl.AddCircleFilled(center, r, U32(c), 16);
            dl.AddCircle(center, r, U32(0, 0, 0), 16, 1f);
        }

        foreach (PreviewModel m in s.Models)
        {
            Color c = PlayerColor(m.ColorIndex);
            ModelBaseRenderer.DrawFilledImGui(dl, m.Shape, P(m.PosIn.X, m.PosIn.Y), scale,
                U32(c), U32(0, 0, 0), 1f, m.Facing);
        }
        dl.PopClipRect();

        dl.AddRect(tl, br, U32(s.Background.Border), 0f, ImDrawFlags.None, 2f);
    }
}
