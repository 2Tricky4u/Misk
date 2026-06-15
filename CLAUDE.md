# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Misk** — a 2D Risk-like turn-based strategy game built on **s&box** (Facepunch's Source 2 / C# engine). Dark medieval fantasy theme. The defining goals are: **host-authoritative networked multiplayer + local hotseat**, and a **data-driven, engine-agnostic core** so maps, factions, rules, and theme are swappable without touching game logic.

Read `docs/ARCHITECTURE.md` first — it is the authoritative map of the layers, the networking model, and how to add maps/factions/rules/themes. This file covers only the build model and the load-bearing invariants.

## Build / run / test

There is **no `dotnet build`/`dotnet test` workflow you should rely on**. The code compiles inside the s&box editor (hot-reload on save), and `Code/misk.csproj` references engine DLLs from a machine-specific Steam install path. Iterate through the editor, not the CLI.

- **Run:** open the project in the s&box editor and press **Play**. Startup scene is `Assets/scenes/misk.scene` (set in `misk.sbproj`).
- **Compile errors / logs:** the editor recompiles on file save and shows errors in its console.
- **MCP bridge:** the `mcp__sbox__*` tools (`get_bridge_status`, `console_run`, `execute_csharp`, `editor_play`, `editor_take_screenshot`, `editor_console_output`, `file_read/write`) talk to the **running editor** over `ws://localhost:29015`. **Most require the editor to be open** — check `get_bridge_status` first; if disconnected, ask the user to open the editor. Use these to verify compiles, drive playtests, and screenshot the board.
- **Tests:** there is no external (`dotnet test`) harness — verification runs **in-engine via dev console commands** (defined in `Code/Debug/`, dev-only, safe to delete). Press Play, then type into the game/editor console:
  - `misk_test` — the **domain test battery**: 37 deterministic checks with scripted dice covering combat/capture/reinforce/fortify/cards/setup/turn-loop/data-validation. Results render in `TestReport.razor` and the pass count logs to console. **Run this after any `Misk.Domain` change** — it's the de-facto unit suite and needs no second client.
  - `misk_hotseat [seats]` — start an offline match (2–6 seats); `misk_dump` — log mode/turn/phase + per-player lands/armies/cards; `misk_blitz [maxTurns]` — auto-play a full game (incl. manual draft, card trading, advance-after-capture) to victory via a trivial greedy AI; `misk_cardtest [n]` / `misk_host` — deal cards / start online hosting.
  - The `Misk.Domain` layer is engine-free (no `Sandbox` types), so it could also host a standalone xUnit project later; `misk_test` covers the same ground in-engine for now.
- **Fastest manual verification:** **hotseat** runs fully offline (no networking), so a single editor Play session + `misk_blitz` (or manual clicks) exercises the whole game loop. Online needs a second client.

## Architecture invariants (do not break these)

The codebase only stays maintainable if these hold:

1. **`Misk.Domain` must never reference `Sandbox`.** It is pure C# (rules, state, phases, combat, commands, events, victory). This is what keeps it testable and is the whole point of the layering. Engine code lives in `Misk.Presentation` (networked components) and `Misk.UI` (Razor).

2. **All game-state mutation goes through `GameContext`** (`Code/Domain/GameContext.cs`). Its `SetArmies/AddArmies/SetOwner/SetPhase/SetPendingReinforcements` helpers are the single choke point that raises the matching event on the `GameEventBus`. Never mutate `Territory.Armies`/`OwnerPlayerId`/`GameState.Phase` directly from commands — the event bus *is* the network/UI replication mechanism, so a silent mutation desyncs everything.

3. **Host authority.** Only the host simulates. `MiskGame` (`Code/Presentation/MiskGame.cs` + `MiskGame.Host.cs`) holds the domain (`GameState`/`TurnController`); clients render purely from `[Sync]` fields + their own read-only `StaticData`. Every host-only path is gated by `IsAuthority` (`IsHotseat || !Networking.IsActive || Networking.IsHost`). Player input arrives as `[Rpc.Host] Request*` calls that build a **Command** and run it through `TurnController` — clients must not call domain logic directly.

4. **Fine-grained `[Sync]`, driven by events.** On the host, `WireHostEvents()` subscribes to the domain event bus and writes each delta into networked state (`NetDictionary` armies/owners, `NetList` seats/turn-order, scalar `[Sync]` phase/turn/pending/winner). `StateVersion` bumps on every change; Razor panels fold `Game.ViewHash` into `BuildHash()` so all clients redraw. Don't add a parallel sync path or a JSON-snapshot blob — extend the event→`[Sync]` bridge.

5. **Everything player-facing is data, not code.** Fantasy names, colors, adjacency, region bonuses, dice counts, starting armies, and theme all live in `Assets/data/**.json`, loaded and validated by `Code/Data/GameDataLoader.cs` + `MapValidator.cs`. Never hardcode a territory/faction/region name or a balance number in logic. New map/faction/rule = new/edited JSON; `MapValidator` fail-fasts on bad adjacency/refs/duplicates.

6. **UI rule:** `MiskRoot.razor` is the **only** `PanelComponent` (it sits on the `ScreenPanel` in the scene). Every other `.razor` inherits `Panel` and is used as a child tag. UI reads synced state and raises intents; it contains **no game rules**. Full-screen overlay panels (HUD, combat log) set `pointer-events: none` and re-enable it on their interactive controls, or they swallow board clicks.

## Conventions

- **s&box C# style:** Allman braces; spaces inside parentheses — `if ( x )`, `Foo( a, b )`. Match the surrounding files.
- **Globals:** project-wide usings come from `Code/Assembly.cs` (`Sandbox`, `System.Collections.Generic`, `System.Linq`) plus `Sandbox.Internal.GlobalGameNamespace` (so `Json`, `FileSystem`, `Log`, `Game` are available unqualified). `Nullable` is **disabled** project-wide — don't rely on nullable-reference annotations.
- **JSON ↔ DTO:** deserialization is case-insensitive, so camelCase JSON keys map to PascalCase DTO properties without attributes. DTOs (`Code/Data/Dto`) are dumb; the loader converts them to domain models.
- **Randomness:** combat/setup use the injected, seedable `IDiceRoller` — never `Game.Random`/`System.Random` directly in the domain, so games stay deterministic and testable.

## Known caveats

- **TODO — online two-client play is NOT yet verified.** Everything else (full rules, manual draft, cards, combat, victory) is verified in-engine via hotseat + a 37/37 domain test suite (`misk_test`) + the `misk_blitz` driver. But **host↔client networking has never been run across two real instances**: lobby host/join, `[Sync]` replication, hidden-hand delivery (`MiskGame.DeliverHandTo` via `Rpc.FilterInclude`), and `Rpc.Caller` ownership checks. An in-game **Join a War** button exists (`MiskGame.JoinAny` → `Networking.QueryLobbies("local.misk")` + `Connect`). To test: editor hosts (`misk_host`), launch a second standalone client, Join, then confirm seats/board replicate and turn-ownership is enforced. If `[Sync]` doesn't replicate, mark the `Game` GameObject **Networked** in `misk.scene`.
- **Editor recompiles only when focused.** The s&box editor's file watcher ignores external file edits while its window is unfocused (and suppresses code hot-reload during Play). After editing from a headless/MCP session, the editor window must be clicked/focused (and Play stopped) before it rebuilds. Compiler errors are NOT surfaced by the MCP console tool — read them from `<steam>/sbox/logs/sbox-dev.log` instead.
- **Online sync needs the `Game` GameObject networked.** `misk.scene` sets `NetworkMode` on it; if `[Sync]` doesn't replicate online, that object must be marked Networked. Hotseat is unaffected (all local).
