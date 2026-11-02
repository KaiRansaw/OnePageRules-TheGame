#!/usr/bin/env bash
# Compare every bundled army book against the LIVE official Army Forge, unit by unit (#443).
#
# This is the only check that answers "is our army data right", because it tests against the source
# OPR actually serves rather than against the PDFs (which lag it) or against our own snapshot (which is
# what made the Lustbound numbers look verified for weeks while they were wrong in 14 books).
#
#   scripts/audit-against-army-forge.sh [workdir]
#
# Needs outbound access to army-forge.onepagerules.com. That was blocked from Claude's workspace until
# 2026-11-01; if it is blocked again, run this on a machine that can reach it - nothing here is
# workspace-specific.
#
# Reads nothing but the bundled books; writes nothing outside <workdir>. Safe to run any time.
set -euo pipefail

cd "$(dirname "$0")/.."
WORK="${1:-/tmp/af-audit}"
BOOKS="FdgRaylib/Assets/Books"
CLI=(dotnet run --project FdgRaylib/FdgRaylib.csproj --no-build --)

mkdir -p "$WORK/cache"
echo "== 1. asking the rebuild what it needs (two passes: indexes name the books)"
for pass in 1 2; do
  "${CLI[@]}" --rebuild-books "$WORK/cache" "$BOOKS" --dry-run 2>&1 \
    | grep -v '^INFO\|^WARNING' > "$WORK/dryrun$pass.txt"
  need=$(grep -cP '^[^\t]+\t+https://' "$WORK/dryrun$pass.txt" || true)
  echo "   pass $pass: $need response(s) to fetch"
  [ "$need" -eq 0 ] && break
  : > "$WORK/fetch-failures.txt"
  grep -P '^[^\t]+\t+https://' "$WORK/dryrun$pass.txt" | while IFS=$'\t' read -r name url _rest; do
    [ -s "$WORK/cache/$name" ] && continue
    code=$(curl -s -o "$WORK/cache/$name" -w '%{http_code}' --max-time 60 "$url")
    size=$(stat -c%s "$WORK/cache/$name" 2>/dev/null || echo 0)
    # A short or non-200 body is REPORTED, never left to be silently imported as an empty book.
    if [ "$code" != "200" ] || [ "$size" -le 500 ]; then
      printf '%s\t%s\t%s\t%s\n' "$code" "$size" "$name" "$url" >> "$WORK/fetch-failures.txt"
      rm -f "$WORK/cache/$name"
    fi
    sleep 0.2
  done
  if [ -s "$WORK/fetch-failures.txt" ]; then
    echo "   FETCH FAILURES (fix these before trusting the report):"
    cat "$WORK/fetch-failures.txt"
  fi
done

echo "== 2. rebuilding into a scratch copy (the shipped books are never touched)"
rm -rf "$WORK/fresh"
cp -r "$BOOKS" "$WORK/fresh"
"${CLI[@]}" --rebuild-books "$WORK/cache" "$WORK/fresh" 2>&1 \
  | grep -v '^INFO\|^WARNING' | tail -3

# Per SYSTEM FOLDER, never the whole tree at once: OPR publishes the same faction name once per game
# mode, so a whole-tree diff pairs Grimdark Future's "Blood Brothers" against Firefight's and reports
# every upgrade in both as changed. Each mode's book must be compared only against itself.
echo "== 3. diffing each game system against itself"
dirty=0
for sys in "Grimdark Future" "GF Firefight" "GF Star Quest" "Age of Fantasy" "AoF Skirmish" "AoF Quest"; do
  out="$WORK/report-${sys// /-}.md"
  line=$("${CLI[@]}" --book-diff "$BOOKS/$sys" "$WORK/fresh/$sys" "$out" 2>&1 \
    | grep -v '^INFO\|^WARNING' | grep -E 'compared' | head -1)
  printf '   %-17s %s\n' "$sys" "$line"
  case "$line" in *", 0 differ."*) ;; *) dirty=1;; esac
done

echo
if [ "$dirty" -eq 0 ]; then
  echo "Our data matches the official Army Forge in every system."
else
  echo "Differences found. Reports: $WORK/report-*.md"
  echo "To adopt them, copy the affected books from $WORK/fresh over the bundled ones and re-run."
  echo "A saved army embeds its own book snapshot, so replacing a bundled book does not disturb"
  echo "armies already saved (BuiltArmyFile.Book)."
fi
