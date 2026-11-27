using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Army Forge 3.6.0: "Enhanced Watchborn / Enhanced Vinci Tech / Enhanced Exotic Gear - If this model has Versatile
// Attack, it always gets either AP(+1) or +1 to hit rolls from Versatile Attack (instead of only when shooting/charging
// enemies over 9"/6" away)." OPR's sentence names the generic rule; the factions' units carry Watchborn / Vinci Tech /
// Exotic Gear. Enhanced Watchborn and Enhanced Vinci Tech were gated on a rule named "Versatile Attack" that no unit has,
// so they never offered anything (Lane 1 deep hunt, 2026-11-27). Now each is gated on its own faction rule, and - since
// the Enhanced rule REPLACES the 9"/6" condition rather than adding a second pick - the faction rule's own (distance-gated)
// pick steps aside while the unit has the Enhanced rule. Enhanced Exotic Gear was already gated right; it gets the same
// step-aside.
[TestFixture]
public class EnhancedVersatileShippedDataTests
{
    private static readonly (string Base, string Enhanced)[] Families =
    {
        ("Watchborn", "Enhanced Watchborn"), ("Vinci Tech", "Enhanced Vinci Tech"), ("Exotic Gear", "Enhanced Exotic Gear"),
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static UnitData UnitWith(params SpecialRuleDefinition[] rules)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Probe", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) });
        foreach (SpecialRuleDefinition rule in rules) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
        return store.GetDataBinding<UnitData>(store.Create(unit)).GetValue();
    }

    private static bool Offers(SpecialRuleDefinition rule, UnitData unit) =>
        rule.Activated.Count > 0 && rule.Activated.All(a => a.AvailableWhen.Evaluate(
            new RuleInvocation(Hook: null, Bearer: unit, Arguments: Array.Empty<RuleArgument>(), Definition: rule)));

    [Test]
    public void EveryShippedBook_TheEnhancedPickReplacesTheFactionPick()
    {
        int checkedBooks = 0;
        var wrong = new List<string>();
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach ((string baseName, string enhancedName) in Families)
            {
                SpecialRuleDefinition? enhanced = book.RuleDefinitions.FirstOrDefault(r => r.Name == enhancedName);
                SpecialRuleDefinition? faction = book.RuleDefinitions.FirstOrDefault(r => r.Name == baseName);
                if (enhanced == null || faction == null) continue;
                checkedBooks++;
                string where = $"{Path.GetFileName(path)}: {enhancedName}";

                UnitData plain = UnitWith(faction);
                UnitData boosted = UnitWith(faction, enhanced);
                UnitData enhancedOnly = UnitWith(enhanced);
                if (!Offers(faction, plain)) wrong.Add($"{where} - {baseName} alone offers nothing");
                if (Offers(enhanced, enhancedOnly)) wrong.Add($"{where} - offered to a unit without {baseName}");
                if (!Offers(enhanced, boosted)) wrong.Add($"{where} - never offered to a {baseName} unit");
                if (Offers(faction, boosted)) wrong.Add($"{where} - the 9\"/6\" pick is still offered too");
            }
        }

        Assert.That(checkedBooks, Is.GreaterThanOrEqualTo(11), "Watch (Prime) Brothers x3 games, Duchies of Vinci x3, Trade Federations x2");
        Assert.That(wrong, Is.Empty, string.Join("; ", wrong));
    }
}
