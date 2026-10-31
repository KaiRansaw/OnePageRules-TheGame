using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Armor(X) - "Counts as having Defense X+ in place of the model's own Defense stat." #196 shipped a
// zero-hook marker so the name resolved and the description showed, but the mechanic was absent: every one
// of the 11 corpus sites is a paid upgrade (Heavy Armor, mounts, chariots) whose OTHER bundled rules
// (Tough, Fast, Strider) all worked, while the defense half silently did nothing. The 2026-07-29 slice
// authored the mechanic on Tough's creation seam - Lifecycle_OnUnitCreated -> setDefense(Arg(0)) - so
// UnitCreationRules writes the unit's Defense stat once and every save path (volleys, melee, impact,
// reflect, synthetic hits, the AI's CombatMath) reads it with no per-path folding. Owner-ruled: a literal
// SET, not a floor. The mechanism is pinned engine-side by ArmorRuleIntegrationTests; these pin the
// authored JSON, the embedded book copies, the corpus shape, and the numericValue -> argument flow.
[TestFixture]
public class ArmorShippedDataTests
{
    private const string RuleName = "Armor";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Armor() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Armor_IsACreationTimeDefenseSet_ReadingTheArgument()
    {
        SpecialRuleDefinition rule = Armor();
        HookEntry entry = rule.Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated),
            "authored at any other hook it would still validate, and UnitCreationRules would never fold it");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.Always>());
        Assert.That(entry.Effect, Is.InstanceOf<Effect.SetDefense>());
        Assert.That(((Effect.SetDefense)entry.Effect).Defense, Is.InstanceOf<ValueSource.Arg>(),
            "the X in Armor(X) is the rule's argument - a literal here would freeze every site to one value");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(1));
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
    }

    // ---- The corpus -----------------------------------------------------------------------------------

    private record Site(string Book, string Unit, int ArmorValue, bool FromUpgrade);

    // #412: this used to walk option.ItemsGained ONLY, because in the 3.5.1 corpus every Armor site was a
    // paid upgrade. 3.6.0 also gives units armour in their STARTING wargear (110 such sites), which the old
    // enumeration could not see - so those books were silently absent from the census, from the X-range
    // check, and from TheEmbeddedBookCopies_CarryTheMechanic below, which iterates the books this finds.
    // A book whose only Armor reference is starting wargear would therefore never have had its embedded
    // definition checked at all. Both site kinds are walked now, and FromUpgrade keeps them tellable apart.
    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (ItemEntry item in unit.Items)
                    foreach (SpecialRuleEntry rule in item.Rules)
                        if (IsArmor(rule, out int startingValue))
                            yield return new Site(bookName, unit.Name, startingValue, FromUpgrade: false);

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (ItemEntry item in option.ItemsGained)
                            foreach (SpecialRuleEntry rule in item.Rules)
                                if (IsArmor(rule, out int boughtValue))
                                    yield return new Site(bookName, unit.Name, boughtValue, FromUpgrade: true);
            }
        }
    }

    private static bool IsArmor(SpecialRuleEntry rule, out int armorValue)
    {
        if (rule is SpecialRuleEntry_CoreNumeric numeric
            && string.Equals(numeric.Name, RuleName, StringComparison.Ordinal))
        {
            armorValue = numeric.NumericValue;
            return true;
        }

        armorValue = 0;
        return false;
    }

    // Census re-taken for the OPR 3.6.0 import (2026-10-31, 188 books). Armor went from 11 references in
    // 7 books to 493 across 92, so the old explicit book list is gone: naming 92 books pins nothing a
    // reader can check, and the next import would only move it again. What the fixture still pins is the
    // part that cannot drift harmlessly - EVERY reference is a wargear grant (no Armor on a weapon, where
    // the creation-seam mechanic would never fold it) and every authored X is a plausible Defense value.
    // The count stays as the canary the comment always described: a change here means the corpus moved.
    [Test]
    public void EveryReference_IsAWargearGrant()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(493),
            "the 2026-10-31 census - a change here means the corpus moved, not the engine");
        Assert.That(sites.Count(s => s.FromUpgrade), Is.EqualTo(383),
            "paid upgrades, the only shape 3.5.1 had");
        Assert.That(sites.Count(s => !s.FromUpgrade), Is.EqualTo(110),
            "starting wargear, new in 3.6.0 - see the note on Sites()");
        Assert.That(sites.Select(s => s.ArmorValue), Is.All.InRange(2, 5),
            "every authored X is a plausible Defense value");
    }

    [Test]
    public void TheEmbeddedBookCopies_CarryTheMechanic()
    {
        // --apply-rules embeds the supplement's definition into each referencing book. A slice that edits
        // the supplement and forgets to re-embed ships a book whose Armor is still the #196 zero-hook
        // marker - name resolves, description shows, saves unchanged - and nothing else would notice.
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(ShippedBooks.Path(bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                resolver.RegisterOrReplace(definition);
            }

            Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Passive.Any(e => e.Effect is Effect.SetDefense), Is.True,
                $"{bookName}: the defense-set entry is missing - re-run --apply-rules");
        }
    }

    // ---- End to end: the shipped data through the real attach + creation path --------------------------

    [Test]
    public void HeavyArmor4_OnAShippedBookUnit_SetsDefenseTo4()
    {
        // The full flow a real army-load runs: the book's embedded definition registered on a resolver,
        // the item's coreNumeric entry resolved WITH its argument (numericValue 4 -> RuleArgument.Int(4)),
        // attached at unit scope, and UnitCreationRules folding the set onto the stat. HDF Veterans are
        // D5+ buying Heavy Armor (Armor(4)) for the whole unit.
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(ShippedBooks.Path("HumanDefenseForce" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
        {
            resolver.RegisterOrReplace(definition);
        }

        ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(resolver,
            new SpecialRuleEntry_CoreNumeric(RuleName, 4), ERuleScope.Unit, "Veterans (test)");
        Assert.That(resolved, Is.Not.Null, "Armor(4) resolves at unit scope against the shipped book");

        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Veterans", quality: 4, defense: 5,
            modelBindings: new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            });
        unit.AttachRuleDefinition(resolved!);

        UnitCreationRules.Apply(unit, new RuleEvaluator(new ProbabilisticDiceRoller()));

        Assert.That(unit.Defense, Is.EqualTo(4),
            "D5+ Veterans in shipped Heavy Armor save at 4+ - the paid upgrade finally does something");
    }
}
