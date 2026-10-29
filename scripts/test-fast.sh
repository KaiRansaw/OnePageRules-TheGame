#!/usr/bin/env bash
# Fast engine test run - everything EXCEPT the fixtures that stand up a real server, play a whole
# match, or wait on sockets. Those are a handful of files but essentially all of the wall time: the
# full suite is ~20 minutes, this is seconds.
#
# Use this while iterating. Use test-full.sh before committing - ALWAYS before committing anything
# that touches the lobby, game settings, saves/resume, networking or the bots, because that is
# exactly what this script skips.
#
# Skipped here, and why:
#   Lobby* / MultiClientLobby / NetworkedFullStateSync / Disconnect* / FdgHostConnectionLimit
#   / PlayerReadiness / LobbyTeardown   - open sockets, connect clients, sync settings
#   Resume* / ArmyRuleDataResume / RuleRehydrationOnResume / StoreClone
#                                       - save, reload and re-hydrate a whole game
#   StarQuestHordeIntegration           - known to hang ~20s each on Linux and report 0 rounds
#   SearchTree / UctSearch / Tactician* / SimulationStop / Determinism
#                                       - run the bot's search over many simulated games
#   *RuleIntegration / VictoryCalculationStage / Transport* / TerrainPointsPlacementIntegration
#   / ScenarioCompiler / GameEndedMessage / RuleCatalogLint / EmbeddedRuleRegistration
#                                       - play real matches through the state machine
set -euo pipefail
cd "$(dirname "$0")/.."

SKIP=(
  Lobby MultiClientLobby NetworkedFullStateSync Disconnect FdgHostConnectionLimit PlayerReadiness
  Resume StoreClone RuleRehydration ArmyRuleData
  StarQuestHordeIntegration
  SearchTree UctSearch Tactician SimulationStop Determinism
  RuleIntegration VictoryCalculationStage Transport TerrainPointsPlacementIntegration
  ScenarioCompiler GameEndedMessage RuleCatalogLint EmbeddedRuleRegistration
)

FILTER=""
for name in "${SKIP[@]}"; do
  [ -n "$FILTER" ] && FILTER="$FILTER&"
  FILTER="${FILTER}FullyQualifiedName!~${name}"
done

echo "Fast run - skipping the slow integration fixtures (see test-full.sh for everything)."
exec dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj --filter "$FILTER" "$@"
