# 439 - Banner and Musician buff two picked friendly units in the four skirmish/adventure games

**Status:** done (B42, 2026-11-01).
**Came from:** `claude/rules-audit.md` finding 1, as corrected by #438 - it is **four systems, not one**.
**Kai's call, 2026-11-01:** the two units are picked **at deployment, each game**, from the units you
brought (not in the army builder, not auto-picked).

---

## Goal

| System | Banner / Musician |
|---|---|
| Grimdark Future, Age of Fantasy | *"This model and its unit get ..."* - **narrow, already correct** |
| GF: Firefight, AoF: Skirmish, GF: Star Quest, AoF: Quest | *"This model's unit **and up to 2 friendly units picked before the start of the game** get ... **If this model is killed, picked models that are deployed lose the rule.**"* |

Source: Army Forge's verbatim text for all six systems (`OfficialRuleText.json`, harvested 2026-10-31),
corroborated by the two core books that print a Command Groups section at all (AoF v3.6.0 narrow/per-unit,
AoFS v3.6.0 wide/per-army). The other four systems' core books have no Command Groups section, so Army
Forge is the only source and the Army-Forge-wins ruling applies - the same situation as Transport.

Second, separate half: **AoF: Skirmish caps Command Groups per ARMY** (*"each army may only have one
model with one of the following"*) where AoF is one of each **per unit**. The book data's `maxPicks: 1`
enforces the per-unit form only.

---

## Design

### No book-data change, and that is the point

The wide/narrow difference is **not** a different rule body - both are "+1 to combat morale" (Banner) and
"+1" on move actions" (Musician). The difference is *who else gets it*, which is a per-system engine rule
exactly like `MaxTargetsPerShootAction` and `StrikeBackIsMandatory` from #437. So this goes in
`CoreRulesProfile`, **not** in a new per-system supplement layer, and **no books need rebaking**.

That matters: the alternative was a fourth supplement layer keyed by system slug, wired into
`Program.cs`'s bake chain, plus a 114-book rebake - and it would have encoded in data something the
engine already knows from the game system.

### The grant mechanism already exists

`RuleEvaluator` resolves `TokenType.RuleGrant` tokens back to live rules for the holding unit, through
the same per-rule path as static attachments (`RuleEvaluator.cs` ~399-428). So a picked unit gets a
`RuleGrant` token naming `Banner` / `Musician` and benefits exactly as the bearer's own unit does. No new
effect kind needed.

### The one thing the engine cannot express: *"if this model is killed"*

**Banner and Musician are unit-scoped rules.** #197 established that a champion upgrade bought for one
model (`"affects": "One"`, `maxPicks: 1`) is scoped through "the aggregate format's only per-model
identity: the WEAPON COPY" - and that mechanism is for **weapon-scoped** rules (Sergeant, which modifies
that model's attacks). A unit-scoped rule from the same kind of section folds unit-wide, because its
effect *is* unit-wide ("this model **and its unit**"). So there is no record anywhere of which model
carries the banner.

Two honest options:

1. **Revoke when the bearer's UNIT is destroyed.** The picked units keep the buff while any model of the
   bearer unit lives. Slightly generous to the player; no new plumbing.
2. **Give unit-scoped champion upgrades a per-model home** so the bearer model is known. Real work on the
   army-compile path, and #197 deliberately did not build it (it had no need: weapon-scoped rules had the
   weapon copy).

**Taking option 1 now**, recorded in `claude/house-rules.md` as a judgement call, with option 2 noted as
the proper fix. Rationale: the buff is lost at the moment the unit stops existing, which is the common
case in the skirmish-scale games this rule belongs to (units are small; a banner-bearer's unit dying and
the banner model dying are usually the same event), and the alternative is a compile-path change for a
timing nuance. **Not** silently approximated - it is in the house-rules list and in the patch notes.

### Shape

- `CoreRulesProfile.CommandGroupExtraPicks` - 0 for GF/AoF, **2** for GFF/AoFS/GFSQ/AoFQ.
- `CoreRulesProfile.CommandGroupCapIsPerArmy` - false for AoF, **true** for AoFS (second half).
- `CommandGroupRules` - the rules that confer on picked units: `Banner`, `Musician`. **Not Sergeant**,
  which is self-only in every system (*"This model gets AP(+1) when attacking"*).
- A new stage inside `DeploymentStage`, after `RollForFirstDeploymentStage` and **before**
  `DeployAllUnitsStage` - "before the start of the game", and it keeps the book's *"picked models that
  are deployed"* meaningful, since a picked unit may still be in Ambush/Scout reserve.
- One request per bearer unit, asking its owner for up to 2 friendly units; grants on answer.
- Resolvers: CLI, GUI, **and the AI** (bot armies carry Banners too, and no book gives the AI a clause
  for this - the bot's pick is a judgement call for the house-rules list).

---

## Notes

### 2026-11-01 - built

Shipped as designed above, with three things worth recording.

**1. No new resolver surface, and no new network message.** `PickCommandGroupUnitRequest` subclasses
`CancellableSelectionRequest<UnitData>`, so all three front ends adapt it onto the unit picker they
already have (`DerivedRequestAdapter`) - the `ChooseUnitToActivateRequest` pattern. Its own TYPE, though,
and that is the point: the official AI binds plain `SelectionRequest<UnitData>` to its **deployment-order**
resolver, so a bare selection here would have been answered by "which unit should I deploy next" logic.
The bot gets `AiPickCommandGroupUnitResolver`, which takes the first option (army order) - no book gives
the AI a clause for this, and "nearest unit" is not even answerable, since nothing is deployed yet.

**2. Revocation needed no new code at all.** `Token` already carries an `OwnerUnitID`, and
`TokenClearTrigger.OwnerDestroyed` already means "clear when the unit that PLACED this dies" -
`UnitDestructionNotifier` calls `TokenClearService.ClearForDestroyedOwner` on every alive-to-dead
transition. So granting with that trigger plus the bearer's ID gives per-bearer revocation for free. The
first design sketched a bearer-to-picks map in game state (new replicated field, networking cost); the
second sketched "revoke if no living unit in the army still carries the rule", which would have revoked
BOTH grants when one of two banners fell. The mechanism that was already there is better than either,
and `WithTwoBearers_OnlyTheDeadOnesPickLosesIt` is the test that would have caught the second.

**3. The per-army cap went in too**, rather than being left as a declared-but-unused flag:
`ForceOrgValidator` warns when an AoF: Skirmish list has more than one Command Group upgrade, counting
UPGRADES rather than units (each is an `Affects.One` section, so a Banner model and a Sergeant model in
one squad are two models with upgrades). Warning, not a block, like every other force-org check.

### What the played-game test caught

`CommandGroupPicksInAPlayedGameTests` plays real AI-vs-AI games and reads the picks out of the log. First
run: **no picks at all, in all four systems.** Not a wiring bug - `TestArmies.MakeShooterArmy` and
`MakeDefenderArmy` are **one unit each**, so the bearer was the only unit in its army and there was
correctly nobody to confer on. The engine was right and the test army was wrong; switched to
`MakeRichArmy`, which has two.

Worth keeping because of what it proves about the other fixture: the unit tests all passed while the
played game picked nothing, which is exactly the gap these end-to-end fixtures exist for. It also found
the log line reading "Warriors's Banner" - unit names are usually plural - now phrased "Banner on
Warriors" in both the log and the prompt.

### One gap found by reading my own code back, not by a test

`CarriesRule` originally matched `Condition.UnitHasRule` exactly - unit-level rules only. That is right
for a Command Group upgrade in the ordinary case, because the upgrade folds onto the unit (its effect is
unit-wide). But the **hero merge relocates a joined hero's own unit-scoped rules onto the hero model**
(#183/#197), so a Hero carrying a Banner into a squad would not have been recognised as a bearer at all,
and its banner would have conferred on nobody - a rule fully implemented and completely unreachable,
which is the exact shape of the Villain bug from B7. Now checks model-level rules too, the way
`Condition.MostModelsHaveThisRule` argues for, with a test attaching a Banner to a model.

No test caught this and none would have: every fixture here attaches Banner at unit level, because that
is what the books' own data does. It came from re-reading the finished stage - the same way the
melee/shooting token bug in #432 was found.

### Verification

- Engine and app suites green; build clean; headless smoke exits 0.
- **Sabotage-checked 6/6**, each reverted: skirmish picks 2 -> 0; the grant using `ManualOnly` so it is
  never revoked; the grant forgetting its `OwnerUnitID`; Sergeant added to the conferring list; the
  validator's cap check disabled; and the cap applied to every system.
- **34 tests**: 27 in `CommandGroupPickedUnitsTests` (allowance, grant, revocation, candidate filters,
  the cap, the bot) and 7 in `CommandGroupPicksInAPlayedGameTests` (four wide systems pick, two narrow
  systems do not, the game still reaches a result with a prompt in the deployment path).

### Left open

- **"If this model is killed" is read as the bearer UNIT dying**, because unit-scoped champion upgrades
  have no per-model identity (#197 gave that only to weapon-scoped ones, via the weapon copy). The one
  deliberate deviation here; in `claude/house-rules.md` with the proper fix named.
- Three smaller judgement calls, also in the house-rules list: "friendly" read as your own army rather
  than an ally's; declining the first pick ends that bearer's picking; the bot picks in army order.
- Kai's open question from the patch notes: whether two prompts in a row is worse than one prompt taking
  both picks, and whether declining should skip one pick rather than end the asking.
