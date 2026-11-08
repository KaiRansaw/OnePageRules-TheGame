using System.Numerics;
using FDG;
using FDG.Quest;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Quest hero shop, drawn the same way by the Hero Builder and the Quest Campaigns screen (Batch E,
/// Kai's wish list: "Shop: a tab per hero; items for sale as cards with Buy; what the hero carries (Sell,
/// Give to); hand over credits", and the campaign's is "the same shop... limited to this campaign's
/// party"). All the arithmetic is <see cref="QuestShop"/>'s; this draws it and writes changed sheets back
/// through <paramref name="save"/>. Names and money are the system's own (AoF: Quest's potions and gold).
/// Rental layout.
/// </summary>
internal static class QuestShopPanel
{
    private static readonly Vector4 TextBright = new(0.95f, 0.95f, 0.93f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.63f, 0.66f, 1f);

    /// <summary>
    /// Draws the shop into a <paramref name="w"/> x <paramref name="h"/> area at the cursor. The hero tabs
    /// pick <paramref name="selected"/>. <paramref name="moneyHandOver"/> adds the "hand over credits" row.
    /// </summary>
    public static void Draw(EGameSystem system, IReadOnlyList<(string Path, QuestHero Hero)> heroes, ref int selected,
        bool moneyHandOver, Action<string, QuestHero> save, float w, float h)
    {
        if (heroes.Count == 0) { UiText.Disabled("No heroes."); return; }
        selected = Math.Clamp(selected, 0, heroes.Count - 1);
        string money = QuestItemSkin.CurrencySuffix(system);
        Vector2 origin = ImGui.GetCursorPos();

        // A tab per hero, each showing what it can spend.
        for (int i = 0; i < heroes.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool sel = i == selected;
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.4f, 0.65f, 1f));
            if (ImGui.Button($"{heroes[i].Hero.Name}  ({heroes[i].Hero.Credits}{money})##shoptab{i}")) selected = i;
            if (sel) ImGui.PopStyleColor();
        }
        (string path, QuestHero hero) = heroes[selected];
        float lh = ImGui.GetTextLineHeightWithSpacing();
        float top = origin.Y + lh * 1.8f;
        float buyW = w * 0.62f;

        ImGui.SetCursorPos(new Vector2(origin.X, top));
        ImGui.BeginChild("##shopbuy", new Vector2(buyW, h - lh * 1.8f), ImGuiChildFlags.Borders);
        UiText.Colored(TextDim, $"FOR SALE - LEVEL {hero.Level}, TIER {QuestProgression.TierUnlocked(hero.Level)} "
                                + "(higher tiers unlock at levels 2 / 5 / 9)");
        DrawItemCards(system, path, hero, save);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(origin.X + buyW + 12f, top));
        ImGui.BeginChild("##shopcarry", new Vector2(w - buyW - 12f, h - lh * 1.8f), ImGuiChildFlags.Borders);
        DrawCarried(system, heroes, path, hero, moneyHandOver, save);
        ImGui.EndChild();
    }

    private static void DrawItemCards(EGameSystem system, string path, QuestHero hero, Action<string, QuestHero> save)
    {
        float cardW = UiChrome.Em(11f);
        float avail = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((avail + 8f) / (cardW + 8f)));
        if (!ImGui.BeginTable("##shopcards", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV)) return;
        int n = 0;
        foreach ((QuestItemInfo item, string? whyNot) in QuestShop.Stock(hero, system))
        {
            if (n % columns == 0) ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushID(n++);
            string name = QuestItemSkin.Name(system, item.Name);
            ImGui.BeginGroup();
            UiText.Colored(whyNot != null ? TextDim : TextBright, name);
            UiText.Disabled($"Tier {item.Tier} - {QuestItemSkin.Money(system, item.Cost)}");
            ImGui.BeginDisabled(whyNot != null);
            if (UiButton.NavigateSmall($"Buy {QuestItemSkin.Money(system, item.Cost)}") && QuestShop.Buy(hero, item, system))
                save(path, hero);
            ImGui.EndDisabled();
            ImGui.EndGroup();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiText.TooltipWrapped(whyNot == null ? item.Description : whyNot + "\n\n" + item.Description);
            ImGui.Spacing();
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static void DrawCarried(EGameSystem system, IReadOnlyList<(string Path, QuestHero Hero)> heroes,
        string path, QuestHero hero, bool moneyHandOver, Action<string, QuestHero> save)
    {
        string money = QuestItemSkin.CurrencySuffix(system);
        UiText.Colored(TextDim, $"{hero.Name.ToUpperInvariant()} CARRIES ({hero.Credits}{money})");
        if (hero.Items.Count == 0) UiText.Disabled("Nothing.");
        var others = heroes.Where(e => e.Path != path).ToList();
        // Grouped so three potions are one row; ToList because the buttons below change the list.
        int row = 0;
        foreach (IGrouping<string, string> group in hero.Items.GroupBy(i => i).ToList())
        {
            ImGui.PushID(row++);
            QuestItemInfo? info = QuestItems.Find(group.Key);
            ImGui.TextUnformatted(group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key);
            if (info != null && ImGui.IsItemHovered()) UiText.TooltipWrapped(info.Description);
            ImGui.SameLine();
            // An item this build doesn't know has no price, so it can be handed over but not sold -
            // guessing a price would invent money, and calling it 0 would destroy the item.
            ImGui.BeginDisabled(info == null);
            if (UiButton.NavigateSmall($"Sell +{QuestItemSkin.Money(system, info?.SellValue ?? 0)}") && QuestShop.Sell(hero, group.Key))
                save(path, hero);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(others.Count == 0);
            if (UiButton.NavigateSmall("Give to...")) ImGui.OpenPopup("##giveto");
            ImGui.EndDisabled();
            if (others.Count == 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiText.Tooltip("There is no other hero to give it to.");
            if (ImGui.BeginPopup("##giveto"))
            {
                foreach ((string otherPath, QuestHero other) in others)
                {
                    if (ImGui.Selectable(other.Name) && QuestShop.Give(hero, other, group.Key))
                    {
                        save(path, hero);
                        save(otherPath, other);
                    }
                }
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }

        if (moneyHandOver && others.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.4f));
            UiText.Colored(TextDim, $"HAND OVER {QuestItemSkin.CurrencyWord(system).ToUpperInvariant()}");
            foreach ((string otherPath, QuestHero other) in others)
            {
                ImGui.PushID(otherPath);
                foreach (int amount in new[] { 5, 10 })
                {
                    ImGui.BeginDisabled(hero.Credits < amount);
                    if (UiButton.NavigateSmall($"{amount}{money} > {other.Name}##m{amount}")
                        && QuestShop.GiveCredits(hero, other, amount) > 0)
                    {
                        save(path, hero);
                        save(otherPath, other);
                    }
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                }
                ImGui.NewLine();
                ImGui.PopID();
            }
        }
    }
}
