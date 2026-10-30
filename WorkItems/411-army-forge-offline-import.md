# 411 - Importing the army books without network access

**Goal:** let the existing Army Forge importers run from saved responses, so the 87 bundled books can be
re-priced for OPR 3.6 from a machine that can reach onepagerules.com - Claude's cloud workspace cannot.

**The plan, in full:** the project doc `claude/army-forge-refresh.md`.

---

## 2026-10-30 - the groundwork

Solution builds clean. Verified end to end by hand (URL listing both passes, and the cache-only error).

### The finding that shaped this

The compile step is **already written**: `--import-book`, `--import-section-shapes`, `--import-spells`
and `--import-opr` all take Army Forge's own JSON and transfer it onto the bundled books, matched by
`(unit id, option id)`, preserving the curated rule definitions and effect sets. The only missing piece
was getting the responses onto a machine where the importer runs - **not** a way to read stats off a
page. Reading the builder unit by unit would be ~1,817 units of hand transcription AND would lose the
option ids the upgrade tree is keyed on, so it could not be matched back onto the existing books at all.

### What landed

All three in `ArmyForgeBookService.GetAsync`, which every importer already funnels through, so one
change serves all four modes:

- **`ArmyForgeBookService.CacheFolder`** / CLI `--book-cache <dir>` - read the folder first, and save
  anything fetched back into it. Doubles as a download cache.
- **`ArmyForgeBookService.CacheOnly`** / CLI `--book-cache-only` - never touch the network; a missing
  response is an error naming the URL and the exact file name to save it as.
- **CLI `--book-urls <books-dir>`** - prints every URL needed with its cache file name. Two passes: the
  two index URLs first, then one per book, because the book uids come out of the indexes.
- `CacheFileName`, `IndexUrls`, `BookUrl`, `IndexFromCache` are public so a future browser-driven run
  can ask for the same names rather than guessing them.

File names are deliberately printable and Windows-safe
(`army-books-<uid>-gameSystem-2.json`), because a person may be saving them by hand from a browser.

### Not done

- No run against the live site. Everything here is the plumbing for that evening.
- No browser automation yet. If Route B is needed, the 87 saves are the part worth automating with
  Claude in Chrome, and that needs Kai's machine linked.
- Quick Readjustment goes back on the list when the refreshed books carry it again (#409).
