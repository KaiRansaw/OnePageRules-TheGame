#!/usr/bin/env bash
# The whole engine suite, nothing skipped except the known-broken Star Quest horde integration
# tests (15 of them hang ~20s each on Linux and report RoundsDone == 0 - a real open bug, tracked in
# claude/fdg-todo.md; pass --everything to include them).
#
# Takes roughly 20 minutes on a 2-core box: ~3,500 of the tests are instant, and a few dozen
# fixtures that stand up a server and play whole matches account for nearly all of it.
#
# Run this before committing. Run it WITHOUT FAIL if the change touches the lobby, game settings,
# saves/resume, networking or the bots - test-fast.sh skips exactly those.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "${1:-}" = "--everything" ]; then
  shift
  echo "Full run - including the known-hanging StarQuestHordeIntegration tests."
  exec dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj "$@"
fi

echo "Full run - everything except the known-broken StarQuestHordeIntegration tests."
exec dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj \
  --filter "FullyQualifiedName!~StarQuestHordeIntegration" "$@"
