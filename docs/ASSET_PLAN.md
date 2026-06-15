# Misk — Art Direction & Asset Plan

**STATUS: generated and wired (HiggsField, Recraft 4.1).** A unified grimdark set was produced with
one shared palette (`#c9b28a #9c855f #2b2118 #c9a24a #6b6660 #7a1620`) so everything reads as one hand:
- **Map atlas** → `Assets/data/theme/maps/sundering_atlas.png` (referenced by `background` in the map JSON; `MapView` layers it under the hotspots with a dark scrim for readability).
- **6 faction sigils** → `Assets/data/theme/sigils/<id>.png` (referenced by `sigil` in `factions.json`; shown in the lobby chips, HUD and victory screen, with the glyph as fallback).
- **Gore decals** → `Assets/data/theme/fx/blood_splatter.svg` (seize) and `blood_slash.svg` (repelled), flashed on the last battle's territory by `MapView` (`MiskGame.LastCombatTerritoryId`).

To regenerate/replace any asset: re-run the matching prompt below (keep the shared palette), drop the
file at the same path — no code change needed. The prompts that produced the set are kept below for
reference and future maps.

---

## Original direction & prompts

## Direction
Dark medieval fantasy between LOTR and grim D&D: brutal, mature, atmospheric, **not cartoonish**.
Aged cartographer's atlas — ink, parchment, candlelit. Tasteful blood accents in combat feedback,
never gratuitous. Readability of gameplay information always wins over flourish.

Palette: parchment `#c9b28a`, ink `#2b2118`, gold `#c9a24a`, blood `#7a1620`, shadow `#120d0a`.

## 1. World map background (`Assets/data/theme/maps/sundering_atlas.png`, 1920×1080)
Generate one atmospheric atlas, then set the map JSON `background` (or theme `mapBackground`) to its
path. Hotspot positions already sit in 1920×1080 canvas space, so paint regions roughly where the
nodes are: Northern Holds (top), Blackwood (left), Crownlands (centre), Ashen Marches (right),
Red Wastes (bottom).

> Prompt: "Ancient hand-drawn fantasy world map on aged parchment, dark medieval atlas style,
> ink linework and watercolour washes, snowy northern holds at the top, haunted black forest to
> the west, a central kingdom of crownlands, volcanic ashen marches to the east, red desert wastes
> to the south, mountain ranges, coastlines, ruined fortresses, candlelit and weathered, muted
> earthy palette of parchment tan, ink brown, faded gold, subtle blood-red cartouches, no text,
> no labels, top-down cartographic view, highly detailed, grim and atmospheric."

Negative: bright saturated colours, cartoon, modern, text/letters, UI elements.

## 2. Faction sigils (`Assets/data/theme/sigils/<id>.png`, ~512×512, transparent)
One emblem per faction; set each faction's `sigil` field. Keep silhouettes bold and readable small.

| Faction | id | Motif prompt seed |
|---------|----|-------------------|
| Iron Covenant | `iron_covenant` | "grim iron hammer-and-anvil sigil bound in oath-chains, steel grey, riveted" |
| Bloodthorn Clans | `bloodthorn_clans` | "savage crossed cleavers wrapped in bleeding thorns, dark crimson, barbaric" |
| House Veyr | `house_veyr` | "fading royal crown over a cracked shield, deep blue and tarnished silver, regal decay" |
| Ashen Host | `ashen_host` | "skull crowned with ash and bone, grey and pale, undead empire heraldry" |
| Verdant Court | `verdant_court` | "antlered skull entwined with roots and leaves, deep green, fey and feral" |
| Obsidian Legion | `obsidian_legion` | "black hungry star over a jagged obsidian spire, violet glow, sorcerous" |

> Wrapper for each: "Medieval heraldic emblem, {motif}, centered sigil on transparent background,
> embossed metal and enamel, weathered, dark fantasy crest, clean bold silhouette, no text."

## 3. Optional later
- Region overlay textures (snow/ash/forest/sand/stone) tinted by `region.color`.
- Army-count medallion frame; faction banner ribbons for the HUD.
- Parchment panel/border frames to replace the CSS chrome.
- Dice / blood-splatter combat flourish for captures (keep subtle).

## Integration checklist
1. Generate via HiggsField → save under `Assets/data/theme/...`.
2. Reference by path in the relevant JSON (`background`, `mapBackground`, `sigil`).
3. Reload in editor; `MapView` and the lobby/HUD pick them up automatically.
