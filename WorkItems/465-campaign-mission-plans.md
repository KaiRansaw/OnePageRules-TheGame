# 465 — Campaign mission plans (generate or write; save and replay)

**Status:** done 2026-11-05 (build 1.05, B89).

Kai's design, 2026-11-05 (fdg-todo "Two ways to start a campaign"): add missions as you go; objectives
from the books plus your own notes; difficulty per mission; a campaign saves as a file you can replay
with fresh armies; replayed missions stay editable and re-rollable; the plan lives on Mission Setup.

## Built
- `Campaign/CampaignMissionPlan.cs`: `CampaignMissionPlan` (mission, title, terrain mode + map name,
  deployment style, mission type = core objectives or a Mission Cards game, difficulty; notes only inside
  a saved file), `CampaignMissionGenerator` (deployment: family on a D3 then the Advanced books' D6;
  mission type and difficulty odds are house rules), `CampaignScript` (.fdgcampaign JSON; From / ApplyTo),
  `CampaignPlans.Next/Set`. `StrikeTeamInfo.Plans`.
- Mission Setup: a "THE MISSION" column first (title, Generate all, deployment + Reroll, mission type +
  Reroll, difficulty + Reroll, notes = the mission's journal page, Save campaign as a file), the map under
  it, then the two forces. The next mission's plan is generated when first opened. Start Mission hands the
  deployment style and mission type to the lobby (`LobbyScreen.PendingDeploymentStyle/MissionCardGame`).
- New Campaign: "Missions" - plan as I go, or start from a saved campaign of this game.
- `FdgRaylib/SaveLoad/CampaignScriptPaths.cs` - a "Campaigns" folder beside Maps.
- Tests: `Tests/CampaignMissionPlanTests.cs`.

## Not done
- Quest campaigns keep their own Campaign Builder flow (out of scope, as the campaign spec says).
- A custom map named in a saved campaign that this computer doesn't have falls back to Auto From Layout.
