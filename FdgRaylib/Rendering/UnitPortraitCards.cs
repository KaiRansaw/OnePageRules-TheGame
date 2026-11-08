using System.Numerics;
using FDG.SaveLoad;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// An army list shown as a row of portrait unit cards - Kai's "Starting army" look (wish list,
/// "Everywhere", 2026-11-07): hover a card for the unit's stats, left-click it to see the unit's full
/// info in its own spot beside the cards.
///
/// <para>
/// One widget so every screen that previews a list shows it the same way: the lobby, Choose Army now, and
/// later the Army Forge, the in-game Army Lists and the Campaign. Give it an <see cref="ArmyListFile"/> and
/// a rectangle; it owns which card is clicked and nothing else. The picture is the unit's portrait from
/// UnitImages (the same files the 2D tokens use), falling back to the full standee picture, then to the
/// unit's initials - never to somebody else's picture.
/// </para>
///
/// <para>Rental drawing (plain ImGui). What is permanent is what a card and its hover say, which lives in
/// the pure <see cref="CardCaption"/> and <see cref="StatsTooltip"/> so tests pin it.</para>
///
/// <para>Render thread only - it loads textures.</para>
/// </summary>
internal sealed class UnitPortraitCards
{
    private readonly UnitImageLibrary _portraits = new(UnitImageLibrary.PortraitFolderName);
    private readonly UnitImageLibrary _pictures = new();

    private ArmyListFile? _army;
    private RuleGlossary _glossary = RuleGlossary.Empty;
    private int _selected = -1;

    /// <summary>The clicked card's index into the army's units, or -1.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Forget the clicked card (a different list is about to be shown).</summary>
    public void ClearSelection() => _selected = -1;

    /// <summary>
    /// Draws <paramref name="army"/> into <paramref name="size"/> at the cursor: cards on the left, the
    /// clicked unit's full info (the Army Forge's read-only unit view) on the right. A narrow area stacks
    /// the two instead. Null draws a line saying there is nothing to show.
    /// </summary>
    public void Draw(string id, ArmyListFile? army, Vector2 size, string emptyText = "No army to show.")
    {
        ImGui.PushID(id);
        if (!ReferenceEquals(army, _army))
        {
            _army = army;
            _selected = -1;
            // Built once per list, not per frame: Build walks the whole core rule catalog.
            _glossary = army == null ? RuleGlossary.Empty : RuleGlossary.Build(army.RuleDefinitions, army.GameSystem);
        }

        if (army == null || army.Units.Count == 0)
        {
            UiText.Disabled(army == null ? emptyText : "This list has no units.");
            ImGui.PopID();
            return;
        }

        float gap = 8f;
        bool beside = size.X >= ImGui.GetFontSize() * 30f;
        Vector2 cardsSize = beside ? new Vector2(size.X * 0.55f - gap / 2f, size.Y)
                                   : new Vector2(size.X, size.Y * 0.5f - gap / 2f);
        Vector2 detailSize = beside ? new Vector2(size.X - cardsSize.X - gap, size.Y)
                                    : new Vector2(size.X, size.Y - cardsSize.Y - gap);

        ImGui.BeginChild("##cards", cardsSize, ImGuiChildFlags.None);
        DrawCards(army);
        ImGui.EndChild();

        if (beside) ImGui.SameLine(0f, gap);
        ImGui.BeginChild("##unitinfo", detailSize, ImGuiChildFlags.Borders);
        if (_selected >= 0 && _selected < army.Units.Count)
            ForgeUnitDetail.DrawReadOnly(army.Units[_selected], _glossary);
        else
            UiText.Disabled("Click a unit card to see its full info here.");
        ImGui.EndChild();

        ImGui.PopID();
    }

    private void DrawCards(ArmyListFile army)
    {
        CardEvent hit = DrawCardGrid("##grid", army.Units, army.Faction, _selected);
        if (hit.Clicked >= 0) _selected = _selected == hit.Clicked ? -1 : hit.Clicked;
    }

    /// <summary>What the mouse did to a grid of cards this frame: the index clicked / double-clicked, or -1.</summary>
    internal readonly record struct CardEvent(int Clicked, int DoubleClicked);

    /// <summary>One card's size in the current font. Every grid uses it, so a strip can be sized in rows.</summary>
    internal static Vector2 CardSize
    {
        get
        {
            float cardW = ImGui.GetFontSize() * 6.2f;
            return new Vector2(cardW, cardW * 0.9f + ImGui.GetTextLineHeight() * 3f + 10f);
        }
    }

    /// <summary>The gap between cards, both ways.</summary>
    internal const float CardGap = 6f;

    /// <summary>
    /// Draws <paramref name="units"/> as cards wrapping at the available width, at the cursor - the one card
    /// look every screen shares. The caller owns which card is selected (<paramref name="selected"/>, or -1)
    /// and decides what a click means; hover is the stats tooltip, always. <paramref name="tag"/> may put a
    /// short word over a card's picture ("Combined", "!", "pick a trait"); null or empty draws none.
    /// </summary>
    internal CardEvent DrawCardGrid(string id, IReadOnlyList<UnitFileEntry> units, string faction, int selected,
        Func<int, string?>? tag = null)
    {
        var cards = new List<Card>(units.Count);
        for (int i = 0; i < units.Count; i++)
        {
            UnitFileEntry unit = units[i];
            (string name, string facts) = CardCaption(unit);
            cards.Add(new Card(name, unit.Name, facts, () => StatsTooltip(unit), tag?.Invoke(i)));
        }
        return DrawCardGrid(id, cards, faction, selected);
    }

    /// <summary>
    /// One card, already worded: what it is called, which unit's picture it shows, the small line under the
    /// name, the hover text (built only while the card is hovered), an optional tag over the picture (and its colour), and whether the card is
    /// dimmed (a destroyed unit), and optionally the faction its picture is filed under when the cards
    /// come from different books (a Quest party). Lets a screen that holds live units rather than a saved list - the
    /// in-game Army Lists - draw the very same cards.
    /// </summary>
    internal readonly record struct Card(string Name, string PictureName, string Facts, Func<string> Tooltip,
        string? Tag = null, Vector4? TagColor = null, bool Dimmed = false, string? Faction = null);

    /// <summary>The same grid as above, for cards the caller has worded itself.</summary>
    internal CardEvent DrawCardGrid(string id, IReadOnlyList<Card> cards, string faction, int selected)
    {
        Vector2 size = CardSize;
        float cardW = size.X, cardH = size.Y, picH = cardW * 0.9f;
        float lineH = ImGui.GetTextLineHeight();
        int columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + CardGap) / (cardW + CardGap)));
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        int clicked = -1, doubleClicked = -1;

        ImGui.PushID(id);
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            if (i % columns != 0) ImGui.SameLine(0f, CardGap);
            ImGui.PushID(i);
            Vector2 p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##card", new Vector2(cardW, cardH));
            bool hovered = ImGui.IsItemHovered();
            // On the PRESS, not the release: a double-click's second press then reports the click and the
            // double-click in the same frame, and nothing arrives afterwards to undo what the double did.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) clicked = i;
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) doubleClicked = i;
            Vector2 p1 = p0 + new Vector2(cardW, cardH);

            dl.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1f)), 4f);
            Vector2 i0 = p0 + new Vector2(3f, 3f), i1 = new(p1.X - 3f, p0.Y + 3f + picH);
            // A card may name its own faction (a party of heroes from different books); else the grid's.
            DrawPicture(dl, i0, i1, card.PictureName, card.Faction ?? faction);

            dl.PushClipRect(p0, p1, true);
            float y = i1.Y + 3f;
            foreach (string line in WrapTwoLines(card.Name, cardW - 8f))
            {
                dl.AddText(new Vector2(p0.X + 4f, y), ImGui.GetColorU32(ImGuiCol.Text), line);
                y += lineH;
            }
            dl.AddText(new Vector2(p0.X + 4f, p0.Y + cardH - lineH - 3f), ImGui.GetColorU32(ImGuiCol.TextDisabled), card.Facts);
            // A destroyed unit stays on the list but reads as spent.
            if (card.Dimmed) dl.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), 4f);
            // The tag last, so it stays readable over a dimmed card.
            if (card.Tag is { Length: > 0 } word)
            {
                Vector2 ts = ImGui.CalcTextSize(word);
                dl.AddRectFilled(i0, i0 + ts + new Vector2(6f, 2f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f)));
                dl.AddText(i0 + new Vector2(3f, 1f), ImGui.GetColorU32(card.TagColor ?? ImGuiTheme.HeaderAccent), word);
            }
            dl.PopClipRect();

            uint border = i == selected ? ImGui.GetColorU32(ImGuiTheme.HeaderAccent)
                : hovered ? ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.85f, 0.9f))
                : ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.34f, 1f));
            dl.AddRect(p0, p1, border, 4f, ImDrawFlags.None, i == selected ? 3f : 1.5f);

            if (hovered) UiText.TooltipWrapped(card.Tooltip(), 440f);
            ImGui.PopID();
        }
        ImGui.PopID();
        return new CardEvent(clicked, doubleClicked);
    }

    /// <summary>How many rows of cards <paramref name="count"/> cards make at <paramref name="width"/>.</summary>
    internal static int RowsFor(int count, float width, float cardWidth)
    {
        if (count <= 0) return 0;
        int columns = Math.Max(1, (int)((width + CardGap) / (cardWidth + CardGap)));
        return (count + columns - 1) / columns;
    }

    /// <summary>The unit's picture alone, filling <paramref name="size"/> at the cursor (the unit panel's
    /// portrait). Same picture, same fallbacks as the cards.</summary>
    internal void DrawPortrait(string unitName, string faction, Vector2 size)
    {
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        DrawPicture(dl, p0, p0 + size, unitName, faction);
        dl.AddRect(p0, p0 + size, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.34f, 1f)), 3f);
    }

    private void DrawPicture(ImDrawListPtr dl, Vector2 i0, Vector2 i1, string unitName, string faction)
    {
        Texture2D? tex = _portraits.Get(unitName, faction) ?? _pictures.Get(unitName, faction);
        if (tex is Texture2D t && t.Width > 0 && t.Height > 0)
        {
            // Cropped to fill, centred side to side and from the top for a tall picture (where the face is).
            float boxAspect = (i1.X - i0.X) / MathF.Max(1f, i1.Y - i0.Y);
            float texAspect = t.Width / (float)t.Height;
            Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
            if (texAspect > boxAspect) { float w = boxAspect / texAspect; uv0.X = (1f - w) / 2f; uv1.X = uv0.X + w; }
            else { uv1.Y = texAspect / boxAspect; }
            dl.AddImage((IntPtr)t.Id, i0, i1, uv0, uv1);
            return;
        }

        dl.AddRectFilled(i0, i1, ImGui.GetColorU32(new Vector4(0.10f, 0.22f, 0.30f, 1f)), 3f);
        string initials = Initials(unitName);
        float px = ImGui.GetFontSize() * 1.8f;
        Vector2 ts = ImGui.CalcTextSize(initials) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, (i0 + i1 - ts) / 2f,
            ImGui.GetColorU32(new Vector4(0.75f, 0.90f, 1f, 0.85f)), initials);
    }

    private static IEnumerable<string> WrapTwoLines(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) { yield return text; yield break; }
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string first = "";
        int k = 0;
        for (; k < words.Length; k++)
        {
            string next = first.Length == 0 ? words[k] : first + " " + words[k];
            if (first.Length > 0 && ImGui.CalcTextSize(next).X > width) break;
            first = next;
        }
        yield return first;
        if (k < words.Length) yield return LobbyScreen.FitWithEllipsis(string.Join(' ', words[k..]), width);
    }

    // ── What a card says (pure, tested) ─────────────────────────────────────────────────────────────

    /// <summary>A card's two captions: the unit's name, and "[models] - points".</summary>
    internal static (string Name, string Facts) CardCaption(UnitFileEntry unit) =>
        (unit.Name, unit.ModelCount > 1 ? $"[{unit.ModelCount}] {unit.PointCost} pts" : $"{unit.PointCost} pts");

    /// <summary>
    /// The hover: name, size and points, Quality / Defense (and Tough when the unit has it), every weapon
    /// as the army list prints it, and the unit's special rules. Straight from the saved unit - nothing
    /// derived, nothing guessed.
    /// </summary>
    internal static string StatsTooltip(UnitFileEntry unit)
    {
        var lines = new List<string>
        {
            $"{unit.Name} [{unit.ModelCount}] - {unit.PointCost} pts",
            $"Quality {unit.Quality}+   Defense {unit.Defense}+"
                + (ForgeUnitDetail.ToughValue(unit) is { } tough ? $"   Tough {tough}" : ""),
        };
        if (unit.Weapons.Count > 0)
        {
            lines.Add("");
            foreach (WeaponFileEntry weapon in unit.Weapons)
                lines.Add(RuleTextFlow.Flatten(RuleTextFlow.WeaponLine(weapon)));
        }
        if (unit.SpecialRules.Count > 0)
        {
            lines.Add("");
            lines.Add(RuleTextFlow.Flatten(RuleTextFlow.RuleList(unit.SpecialRules)));
        }
        lines.Add("");
        lines.Add("Click for the full unit.");
        return string.Join("\n", lines);
    }

    /// <summary>Up to three initials from the name's words, ignoring bracketed parts.</summary>
    internal static string Initials(string name)
    {
        string bare = System.Text.RegularExpressions.Regex.Replace(name, @"\([^)]*\)|\[[^\]]*\]", " ");
        string initials = string.Concat(bare.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).Take(3).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length > 0 ? initials : "?";
    }
}
