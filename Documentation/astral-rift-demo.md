# Astral Rift — native Unity demonstration (v2.1.0)

Astral Rift is a 240-card digital CCG demo authored by CardForgeStudio and executed by the Unity Dedicated Server. The set has four affinities—Solar, Tidal, Verdant and Umbral—and 240 card-specific authored abilities, ranging from resource generation and damage to conditional combinations, removal, milling and recursion.

The current distributable game archive is `GamePackages/astral-rift-demo-2.1.0.cardforge`. Its contents include a full 240-card ruleset, 240 card illustrations, four starter lists and the game-wide deck and board rules. The package manifest has 785 SHA-256-verified entries.

## Four starter decks

All decks are governed by **the same global construction and board rules**.

| Starter | Total | Main draw deck | Special cards | Strategy |
|---|---:|---:|---|---|
| Solar Vanguard | 42 | 40 | Solar Commander + Solar Mana Source | Fast deployment, direct damage, momentum and renown |
| Tidal Archive | 42 | 40 | Tidal Commander + Tidal Mana Source | Card advantage, milling, recursion and control |
| Verdant Sanctuary | 42 | 40 | Verdant Commander + Verdant Mana Source | Energy growth, shields, board scaling and sustain |
| Umbral Erosion | 42 | 40 | Umbral Commander + Umbral Mana Source | Resource denial, life pressure, attrition and removal |

Each starter contains 20 selected ordinary card types at two copies each (40), one distinct Commander and one distinct Mana Source. The four starter lists together use only 88 different card definitions, leaving 152 cards outside the starter choices. **Special cards appear in the 42-card deck list but do not count toward the 40-card main-deck minimum or enter the draw pile.**

## Deck construction

The game-wide rule set requires **at least 40 main-deck cards**, no more than **three copies of any individual card**, **exactly one Commander**, and **exactly one Mana Source**. The current authored maximum is 120 main-deck cards; the four included starters each have exactly 40 main cards plus 2 special cards.

These restrictions are defined in the game's `deckRules` and special `zones` definitions. The UI Toolkit deck builder shows the main-card count, Commander count, Mana Source count, total, and legality messages while the player edits. It blocks additions that exceed caps and disables saving an illegal deck. The native Unity server validates the same constraints independently on deck saving and match creation.

## Shared board zones

| Zone | Allowed card types | Capacity | Starting state |
|---|---|---:|---|
| Frontline (`battlefield`) | Units or cast Commanders | 6 per player | Empty |
| Relic Bay (`relic-bay`) | Relics | 2 per player | Empty |
| Command (`command`) | Commander only | 1 per player | Selected Commander from the deck |
| Mana Source (`mana-source`) | ManaSource only | 1 per player | Selected Mana Source from the deck |
| Shared Rift (`rift`) | Events | 3 shared | Preset Rift Event |
| Hand (`hand`) | Drawn cards | 9 per player | Five drawn from the 40-card main deck |
| Draw, discard, void | As defined by rules | No fixed limit | Main deck starts with 40, before opening draw |

The Commander **does not start on the frontline**. It starts in Command and can be moved to the frontline only by the **Cast commander** action after paying three energy. Removal or exile from the battlefield sends the same Commander instance **back to the Command zone**, not to discard or hand. The Command zone has only one slot. The Mana Source is **immovable**, cannot be drawn or discarded, and provides energy on subsequent turns. Both zones use shared rules: different decks select different matching special cards, but no deck may change the zone restrictions.

Resource pools (energy, life, shield, renown, momentum) are non-physical zones and never count as cards.

## Generate, run and test

In the Unity Editor, use **CardForge > Generate Sample Game** to regenerate the authored 2.1.0 package, then **CardForge > Server > Build Native Windows Dedicated Server** to create the native server executable. Run `Demo/Start-CardForgeDedicatedServer.ps1 -NoClient` and enter Play in the UI Toolkit client scene. The startup script does not use the retired ASP.NET server.

The staged authoritative rules are at `Assets/StreamingAssets/CardForgeStudio/CardForge/Rules/astral-rift.json`. They can be inspected and tested independently:

```powershell
dotnet run --project Tests/CardForge.Simulation.Tests/CardForge.Simulation.Tests.csproj
dotnet run --project Tests/CardForge.NativeServer.Tests/CardForge.NativeServer.Tests.csproj
dotnet build Shared/CardForge.DedicatedServer.Validation/CardForge.DedicatedServer.Validation.csproj
```

The native API tests use an isolated in-memory database adapter for repeatability. Actual MongoDB installation/startup, Unity Editor compilation, native player building, and live multiplayer deployment require separate validation. Do not interpret passing portable tests as proof that a production server is ready.
