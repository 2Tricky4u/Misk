# Misk — Architecture

A 2D Risk-like strategy game for s&box. Dark medieval fantasy ("The Sundering Realms").
Built so the **map, factions, rules, and theme are data**, and the **game logic is
engine-agnostic and testable** — you can reskin or rebalance without touching core code.

## Layers (dependencies point inward)

```
UI (Razor)  ─▶  Presentation/Networking  ─▶  Domain (pure C#)
                         ▲
                       Data  ─▶  Domain
```

| Layer | Folder | Responsibility | Engine types? |
|-------|--------|----------------|---------------|
| Domain | `Code/Domain` | Rules, state, phases, combat, commands, events, victory | No (pure C#) |
| Data | `Code/Data` | DTOs + JSON loader + validator → domain models, theme | `FileSystem`, `Json` only |
| Presentation / Net | `Code/Presentation` | Host authority, `[Sync]` mirror, RPCs, lobby/seats | Yes (`Component`) |
| UI | `Code/UI` | Razor panels: menu, lobby, board, HUD, log, victory | Yes (`Panel`) |

The **Domain** layer never references `Sandbox`. That keeps the rules portable and unit-testable.

## Networking (host-authoritative, fine-grained `[Sync]`)

- The **host** is the only authority. It owns the domain simulation: `GameState`,
  `TurnController`, `DiceCombatResolver`, RNG. Only the host mutates it.
- **Clients never simulate.** They render from synced fields + their own read-only copy of the
  static data (`StaticData`, loaded from the same JSON on every machine).
- The bridge is the **domain event bus**: on the host, `MiskGame` subscribes to
  `ArmiesChanged/OwnerChanged/PhaseChanged/TurnChanged/ReinforcementsChanged/GameWon/CombatResolved`
  and writes each delta into networked state:
  - `NetDictionary<string,int> Armies`, `NetDictionary<string,string> Owners`
  - scalar `[Sync]` `PhaseIndex / CurrentPlayerIndex / TurnNumber / PendingReinforcements / WinnerPlayerId`
  - `NetList<SeatInfo> Seats`, `NetList<string> TurnOrder`
  - `StateVersion` (bumped every change; folded into Razor `BuildHash()` so all clients redraw)
- **Player actions** flow client → `[Rpc.Host] RequestDeploy/Attack/Fortify/EndPhase` → host builds
  the matching **Command** → `TurnController` validates + executes → events replicate the result.
  Combat dice are also sent via `[Rpc.Broadcast] BroadcastCombat` for the chronicle/log.
- **Lobby:** host `Networking.CreateLobby()`; joiners connect via the s&box front-end. Host
  `OnActive(connection)` adds a `SeatInfo`. Each player picks an available faction
  (`RequestSetFaction`, host-validated for uniqueness) and readies up; host starts.
- **Hotseat** is the same path with `IsHotseat = true`: all seats are local, any local input is
  allowed for the current seat, and no real connection is required (runs fully offline).

> Authority gate: everything host-only is guarded by `MiskGame.IsAuthority`
> (`IsHotseat || !Networking.IsActive || Networking.IsHost`).

## Key types

- `Code/Domain/Model/*` — `Territory`, `Region`, `Faction`, `Player`, `GameMap`, `GameState`.
- `Code/Domain/GameContext.cs` — the single mutation choke point; every change raises an event.
- `Code/Domain/Phases/*` — State pattern: `ReinforcePhaseState`, `AttackPhaseState`,
  `FortifyPhaseState`, driven by `TurnController`.
- `Code/Domain/Commands/*` — `DeployCommand`, `AttackCommand`, `FortifyCommand` (validate/execute).
- `Code/Domain/Rules/*` — `RulesConfig`, `DiceCombatResolver`, `StandardReinforcementCalculator`,
  `SystemDiceRoller` (seedable, injectable).
- `Code/Data/GameDataLoader.cs` + `MapValidator.cs` — JSON → validated domain.
- `Code/Presentation/MiskGame*.cs` — networked authority + view accessors.
- `Code/UI/**` — Razor; `MiskRoot` is the only `PanelComponent` (on the `ScreenPanel`), the rest
  inherit `Panel`.

## Rules systems (full Risk rulebook)

These close the gaps to the official rulebook and are all toggle/tune-able from `rules/classic.json`:

- **RISK cards** — self-contained `Code/Domain/Cards/` (`CardKind` Footmen/Riders/Siege/Banner-wild,
  `Card`, `CardDeck`, `CardSetEvaluator`). Earn a card per capturing turn (`TurnController.EndTurn`),
  trade sets for escalating armies + the +2 owned-territory bonus (`TradeCardsCommand`), mandatory
  trade at the hand limit (`ReinforcePhaseState.MustTrade`), steal cards on elimination
  (`AttackCommand`). Set `cardsEnabled:false` to disable the whole subsystem. Suit names/glyphs/colours
  are themed in `theme.cards`. Hands are hidden online: counts are public (`CardCounts`), contents are
  delivered only to the owner via a connection-filtered RPC (`MiskGame.DeliverHandTo`).
- **Attacker dice choice** — `AttackCommand.AttackerDice`; defender auto-rolls max (`defenderAutoMaxDice`).
- **Advance after capture** — `GameState.PendingAdvance` + `AdvanceArmiesCommand`; the attacker chooses
  how many armies to move in (auto-resolves when only one amount is legal).
- **Manual draft setup** — `SetupController` + `ClaimCommand`/`PlaceArmyCommand`; players claim then
  place. `manualSetup:false` falls back to `GameSetup.AssignInitialState` (instant auto-assign).

Debug drivers (`Code/Debug/MiskDebug.cs`, safe to delete): `misk_hotseat`, `misk_blitz` (auto-plays
setup+cards+advance to a winner), `misk_cardtest` (deals cards to inspect the tray), `misk_dump`.

## Extending the game

**Add / change a map** — edit or add a JSON under `Assets/data/maps/` (territories, positions,
adjacency, regions, bonuses). Point `GameDataLoader.DefaultMapPath` (or pass a path) at it. The
`MapValidator` will reject bad adjacency/region/duplicate-id data with a clear message.

**Add a faction** — append to `Assets/data/factions/factions.json` (id, name, color, accent, glyph,
optional sigil). No code changes; it appears in the lobby automatically (up to 6).

**Rebalance rules** — edit `Assets/data/rules/classic.json` (dice counts, min armies, reinforcement
formula, starting armies) or add another rules file and load it.

**Reskin** — edit `Assets/data/theme/dark_atlas.json`. Faction/region colours and the optional map
background image are data-driven. (UI chrome colours currently live in `MiskRoot.razor.scss`;
wiring them to the theme JSON via CSS variables is a planned polish step.)

**Real map art** — set `background` in the map JSON (or `mapBackground` in the theme) to an image
path; `MapView` already layers it under the hotspots. Hotspot `position` values are in canvas
coordinates (`canvasSize`), so they line up over the painted map.

## Deliberately deferred
Rulebook **variants** (2-player neutral armies, Capital RISK headquarters, Secret Mission), plus
AI opponents, save/load, advanced netcode (reconnect/spectator/turn timers), polygon territories,
and the deeper systems (economy, diplomacy, heroes, magic). Seams exist for all of them
(`Player.IsHuman`, the Command/phase API, serializable `GameState`, the `shape` field on territories).
The standard World-Domination game is now rulebook-complete.
