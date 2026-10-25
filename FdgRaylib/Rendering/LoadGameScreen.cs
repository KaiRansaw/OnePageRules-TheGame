using System.Numerics;
using FDG;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Load Game screen: a grid of tiles, 5 per row, one per saved game, each showing a top-down
/// picture of the board as it was saved plus "Faction vs Faction", army points and the score. Replaces
/// the old native file dialog. Player 1 (slot 0 - the host, i.e. you) is always listed first.
///
/// <para>Saves are read on a background task, one at a time, so the screen opens instantly and tiles
/// fill in as each file is read. Controls: click a tile to select it, double-click (or Load, or Enter)
/// to load it, arrow keys to move, mouse wheel to scroll, Esc / Back to return.</para>
/// </summary>
public sealed class LoadGameScreen : IAppScreen
{
    /// <summary>Called with the full path of the save the player chose.</summary>
    public Action<string>? OnLoad;
    public Action? OnBack;

    // ── What a tile shows, pulled out of the save once and kept as plain data ────────────────────────
    private sealed record PreviewModel(Vector2 PosIn, IBaseShape Shape, Float2 Facing, int ColorIndex);
    private sealed record PreviewTerrain(IZone Shape, ETerrainType Type);
    private sealed record PreviewObjective(Vector2 PosIn, int? ColorIndex, int Number);

    private sealed class SaveSummary
    {
        public List<string> Factions = new();      // in slot order: Player 1 first
        public List<int> PointsLimits = new();
        public List<int> Scores = new();
        public int ArmyPoints;
        public string ModeLabel = "";
        public string RoundLabel = "";
        public bool CanResume = true;   // false for a save made before round 1 (setup / deployment)
        public int? HordePoints;
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
    private int _selected;
    private bool _scrollToSelected;
    private int _generation;   // bumped on every rescan so a stale background read stops early

    private string _error = "";

    /// <summary>Shows a message along the bottom of the screen (e.g. why a save can't be loaded).</summary>
    public void ShowError(string message) => _error = message;

    public void OnShown()
    {
        _error = "";
        int gen = ++_generation;
        _entries = SavePaths.ListSaves().Select(p => new SaveEntry
        {
            Path = p,
            Title = System.IO.Path.GetFileNameWithoutExtension(p),
            Saved = SavePaths.SafeWriteTime(p),
        }).ToList();
        _selected = 0;
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
            s.PointsLimits.Add(army?.PointsLimit ?? 0);
            s.Scores.Add(scores.TryGetValue(slot.PlayerID, out int sc) ? sc : 0);
        }

        GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
        GameSettings settings = progress?.Settings ?? GameSettings.GetDefault();
        s.ArmyPoints = progress != null ? settings.ArmyPoints : s.PointsLimits.DefaultIfEmpty(0).Max();
        s.Background = TableBackgrounds.For(settings.TableBackground);
        EGameMode mode = table.Progress.GameMode;
        s.ModeLabel = mode switch { EGameMode.Horde => "Horde Mode", EGameMode.StarQuestHorde => "Star Quest Horde", EGameMode.StarQuestMission => "Star Quest Mission", _ => "Standard" };
        s.HordePoints = table.Progress.HordePointsEarned;
        s.RoundLabel = table.Progress.RoundCount is int r ? $"Round {r}/{table.Progress.TotalRounds}" : "Deployment";
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
        float margin = screenW * 0.03f;

        // Title.
        ImDrawListPtr wdl = ImGui.GetWindowDrawList();
        float titlePx = screenH * 0.055f;
        wdl.AddText(headFont, titlePx, new Vector2(margin, screenH * 0.05f), TextBright, "LOAD GAME");
        wdl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(margin, screenH * 0.05f + titlePx + 6f), TextDim,
            $"{_entries.Count} saved game{(_entries.Count == 1 ? "" : "s")}  -  {SavePaths.FolderPath}");

        // Tile grid: 5 tiles per row, scrolling down for more.
        const int Columns = 5;
        float gridY = screenH * 0.17f;
        float bottomBarH = screenH * 0.10f;
        float gridW = screenW - margin * 2f;
        float gridH = screenH - gridY - bottomBarH;
        float gap = screenW * 0.010f;
        float scrollbarW = ImGui.GetStyle().ScrollbarSize + 4f;
        float tileW = (gridW - scrollbarW - gap * (Columns - 1)) / Columns;
        float pad = MathF.Max(6f, tileW * 0.03f);
        float previewH = (tileW - pad * 2f) / 1.5f + pad * 1.5f;   // 6' x 4' board = 3:2
        float textH = tileW * 0.42f;
        float tileH = previewH + textH;

        ImGui.SetCursorPos(new Vector2(margin, gridY));
        ImGui.BeginChild("##saveGrid", new Vector2(gridW, gridH), ImGuiChildFlags.None, ImGuiWindowFlags.None);

        if (_entries.Count == 0)
        {
            ImGui.SetCursorPos(new Vector2(0, gridH * 0.4f));
            ImGui.TextDisabled("No saved games yet. Save a game from the in-game menu and it will appear here.");
        }

        bool loadNow = false;
        for (int i = 0; i < _entries.Count; i++)
        {
            int col = i % Columns, row = i / Columns;
            ImGui.SetCursorPos(new Vector2(col * (tileW + gap), row * (tileH + gap)));
            if (DrawTile(_entries[i], i == _selected, tileW, tileH, previewH, pad, headFont, out bool doubleClicked))
                _selected = i;
            if (doubleClicked) loadNow = true;
            if (i == _selected && _scrollToSelected)
            {
                ImGui.SetScrollHereY(0.5f);
                _scrollToSelected = false;
            }
        }
        // Bottom spacer so the last row can scroll fully into view.
        int rows = (_entries.Count + Columns - 1) / Columns;
        ImGui.SetCursorPos(new Vector2(0, rows * (tileH + gap)));
        ImGui.Dummy(new Vector2(1, 1));
        ImGui.EndChild();

        // Keyboard: arrows move one tile sideways or one row up/down.
        if (_entries.Count > 0)
        {
            int before = _selected;
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) _selected = Math.Min(_entries.Count - 1, _selected + 1);
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))  _selected = Math.Max(0, _selected - 1);
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))  _selected = Math.Min(_entries.Count - 1, _selected + Columns);
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))    _selected = Math.Max(0, _selected - Columns);
            if (_selected != before) _scrollToSelected = true;
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) loadNow = true;
        }

        // Bottom bar: Back on the left, Load on the right.
        Vector2 btn = new(screenW * 0.14f, bottomBarH * 0.55f);
        float btnY = screenH - bottomBarH + (bottomBarH - btn.Y) * 0.5f;
        ImGui.SetCursorPos(new Vector2(margin, btnY));
        if (UiButton.Back("Back", btn) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            _generation++;
            OnBack?.Invoke();
        }
        ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X, btnY));
        ImGui.BeginDisabled(_entries.Count == 0);
        if (UiButton.Confirm("Load", btn)) loadNow = true;
        ImGui.EndDisabled();

        ImGui.End();
        ImGui.PopStyleColor();

        if (_error.Length > 0)
        {
            Vector2 es = ImGui.CalcTextSize(_error);
            ImGui.GetForegroundDrawList().AddText(new Vector2((screenW - es.X) / 2f, screenH - bottomBarH / 2f - es.Y / 2f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.55f, 0.45f, 1f)), _error);
        }

        if (loadNow && _selected >= 0 && _selected < _entries.Count && _entries[_selected].Summary is { CanResume: false })
        {
            ShowError("That game was saved before round 1 started, so it can't be resumed.");
            loadNow = false;
        }
        if (loadNow) _error = "";
        if (loadNow && _selected >= 0 && _selected < _entries.Count && _entries[_selected].Error == null)
        {
            _generation++;
            OnLoad?.Invoke(_entries[_selected].Path);
        }
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

        // Text block under the board, word-wrapped to the tile width.
        float big = MathF.Max(15f, w * 0.075f);
        float mid = MathF.Max(13f, w * 0.052f);
        float small = MathF.Max(12f, w * 0.040f);
        ImFontPtr body = ImGui.GetFont();
        float x = p0.X + pad;
        float y = p0.Y + previewH + 2f;
        float maxW = w - pad * 2f;

        if (s != null)
        {
            string factions = string.Join(" vs ", s.Factions.Select(Abbreviate));
            y = DrawWrapped(dl, headFont, big, x, y, maxW, TextBright, factions);

            bool samePoints = s.PointsLimits.Count == 0 || s.PointsLimits.All(pl => pl == s.PointsLimits[0] || pl == 0);
            string points = samePoints ? $"{s.ArmyPoints} pts" : string.Join(" vs ", s.PointsLimits) + " pts";
            string score = "Score " + string.Join(" vs ", s.Scores);
            if (s.HordePoints is int hp) score += $" (Horde {hp})";
            y = DrawWrapped(dl, headFont, mid, x, y, maxW, TextBright, $"{points}  -  {score}");

            DrawWrapped(dl, body, small, x, y, maxW, TextDim,
                $"{s.ModeLabel} - {s.RoundLabel} - {e.Title} - {e.Saved:MMM d, yyyy h:mm tt}");
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
        float tw = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
        float th = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
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
