# CardForge authored package format

A `.cardforge` file is a ZIP with `manifest.json`, content hashes, authoring documents, compiled rules and references to art. It is not an executable.

Important entries include:

| Path | Purpose |
|---|---|
| `manifest.json` | Archive version, game ID, package ID, SHA-256 file list |
| `rules/rule-pack.json` | Cards, zones, nonphysical resources, phases, actions, abilities and triggers |
| `rules/victory.json` | Game-end conditions and outcomes |
| `cards/`, `frames/`, `actions/`, `abilities/` | Designer source documents |
| `zones/game-zones.json` | Resource and card-zone definitions |
| `assets/` | Images and other referenced content |

The native content publisher converts the serialized rule pack into `SimRulePack`. Starter decks select card IDs but do not override shared board restrictions. Game-level zones define `allowedCardTypes`, `initialCardIds`, `capacity`, and optional `deckStartCardType`, `returnOnLeave`, and `immovable`. Astral Rift 2.1.0 requires 40 main-deck cards plus one Commander and one Mana Source; the special cards start in one-slot zones, outside the shuffled main deck. A cast Commander enters the battlefield and returns to Command on removal; the Mana Source cannot move. Action `costs` objects become lists of `{resourceId,amount}`. Effect `parameters` objects become lists of `{key,value}`; victory rules are attached to the pack. Unsupported designer effects must be implemented in the portable engine before those rules can run.

The server validates the resulting rules before accepting a new published version. Hot ruleset replacement is blocked while an unfinished match uses that ruleset. Card artwork is independently versioned through the asset-manifest's SHA-256 and can be downloaded on demand.

The archive format is retained for editor portability; the former ASP.NET package-import endpoint is **not used**. An Addressables scene catalog is a separate build artifact, not just another card image. Produce, upload and verify real remote bundles before advertising an updated scene.
