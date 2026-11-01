# 425 — End-to-end networked playthrough coverage

**Goal.** Answer, with a test rather than a hand-verify, the question the whole multiplayer plan rests
on: **can two (and three) copies of the game play a whole game against each other over a real socket?**

**Status:** done 2026-11-01. Two tests, both green. Nothing had to be built in the engine.

---

## 2026-11-01 — the gap, and what it turned out to hide

### Why this was worth a test

The suite covered every networking slice and none of the whole:

| Fixture | Covers | Stops at |
|---|---|---|
| `NetworkedFullStateSyncTests` | join snapshot, forward refs, mid-game token stream | the data layer |
| `Lobby*Tests` (7 fixtures) | roster, teams, colours, join gates, bot naming, teardown | the lobby |
| `MultiClientLobbyTests` | 3+ players in a LOBBY | the lobby |
| `ResumeRejoinNetworkTests` | real sockets, saved-PlayerID adoption | the lobby — its own summary says "What they do NOT cover: launching the resumed game and playing on" |

So "networked play works" was a hand-verify from months ago with no regression net under it. A break in
the request/reply round trip, or in deployment with a remote seat, would have shipped silently.

### Shape of the tests

`FutureOfDarkGrimness/Tests/NetworkedGamePlaythroughTests.cs`, two cases:

1. `HostAndClient_PlayAWholeGame_ToAResult` — host + 1 client, 2 teams.
2. `HostAndTwoClients_PlayAWholeGame_ToAResult` — host + 2 clients, 3 teams (a free-for-all, which is
   the harder case for deployment zones and turn order than two clients sharing a team).

A real `FDGHost` on a loopback port and real `FDGClient`s, so framing, per-connection PlayerID
targeting, the catch-up sync and the request/reply round trip are genuinely in the path. **Both (all
three) seats are driven by the official AI**: the host's slot by a registry over its local game, each
client's slot by a registry standing on that client's own synced store inside its own
`FDGGame_AsClient`. That is the production shape of a human client — the GUI does the same thing with
ImGui resolvers in place of these — so every decision for a client slot crosses the wire as a request
and comes back as a reply.

The only doubles in the test are the dice.

### What it proved

**Networked multiplayer already works, for 2 AND 3 players.** Deployment, terrain placement,
activations, shooting, melee, morale and the end-of-game check all complete with one or two players
answering remotely, and `OnGameEnded` produces a result. Nothing in the engine needed changing, which
means what is left of multiplayer is the *convenience* layer, not the mechanism:

- a short join code / rendezvous instead of typing an IP and port,
- a lobby that offers more than two chairs rather than merely tolerating them,
- spectators,
- the live two-machine (real router / UPnP) verify, which a loopback test can never stand in for.

This also closes most of **#188**'s "in-game routing testable but uncovered" remainder: 3 players and
2 remote clients now play a full game in the suite. The real-machine verify and the #076 drop path
during play stay open there.

### Cost, and why it is accepted

~2 minutes of suite time for the two together. The host lobby hard-codes
`RealtimePresentationClock()`, which paces presentation beats in real time for the GUI's benefit, so a
networked game runs at watching speed even with no GUI attached. Not worth a settings knob to shave;
these are the only end-to-end multiplayer tests in the suite and a hang detector is what the timeouts
are for.

### Noted in passing, not a bug

The client discards the data messages the host sends *before* `LaunchGameMessage` (the slot infos and
the created world), because `FDGGame_AsClient` — and therefore its `GameDataUpdateReceiver` — is only
constructed when that message arrives. The `RequestAllCurrentData()` catch-up in its constructor then
pulls the whole world, which is exactly what `NetworkedFullStateSyncTests` covers. Harmless by
design; recorded here so the "Received unregistered message type AddSingleDataMessage. Discarding"
line in the test output is not mistaken for a fault later.

### Sabotage check

Removing the client's resolver registry (the one line that attaches its AI) fails the test at the
"never reached a result" assertion, as it must — the test genuinely depends on the remote seat
answering over the wire, not on the host playing both sides.

## Outcome

Done. Two green end-to-end tests; the multiplayer mechanism is proven in the suite, and the remaining
multiplayer work is scoped to the convenience layer above it.
