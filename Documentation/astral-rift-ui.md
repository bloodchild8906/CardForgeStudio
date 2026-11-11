# Astral Rift — Runtime UI Toolkit

## Updated visual experience

CardForgeStudio now uses a dark astral tabletop treatment with gold primary actions, quieter navigation, accessible focus targets and shared card styling.

- **Play**: illustrated hero, three starter strategy selections, AI match creation for 1–4 opponents, match-ID entry and shortcuts to social/news. The first saved deck for the active rule pack is selected after sign-in.
- **Deck Workshop**: two-column workspace with live 40 + 1 + 1 legality, saved deck selection, three starter templates, visual draft entries, five-type filters, search, 24-card incremental loading, card-art hover descriptions and authoritative save validation.
- **Collection**: art-first library, 36-card incremental loading, category filtering, search, copy counts and a full read-only card inspector.
- **Match**: always-visible opponent and player battlefield seats, Commander, Mana Source, Library (concealed), frontline, Discard, Exile, opponent hand (concealed), own hand, selected-card inspector and a phase/action strip. Discard and Exile show the top face-up card plus zone count to avoid overflowing the screen.
- **Inputs**: click or keyboard Enter/Space to select visible cards; Escape clears target/card selection. Server-provided legal actions remain authoritative; Next Phase and End Turn are visually primary. Off-turn Quickspell windows remain accessible.
- **Card design**: distinct Creature, Spell, Quickspell, Mana Source, Commander front compositions. Every card type uses exactly the same hidden card back. Card pictures reuse the existing 240 imported illustrations.

## Files

- `Assets/CardForgeStudio/CardForgeClient/Runtime/UI/CardForgeToolkitClient.cs`
- `Assets/CardForgeStudio/CardForgeClient/Runtime/UI/CardForgeArenaPresentation.cs`
- `Assets/CardForgeStudio/CardForgeClient/Runtime/UI/CardForgeArenaPolish.cs`
- `Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/CardForgeArena.uss`

The new stylesheet is loaded through `Resources.Load<StyleSheet>("AstralRift/CardForgeArena")`. The runtime falls back to programmatic base styling if this optional asset has not yet been imported.

## Validation and limitations

- Run `powershell -NoProfile -ExecutionPolicy Bypass -File Demo/ValidateRuntimeUiPolish.ps1` to compile **both UI partial classes** without permanently changing Unity's generated C# project.
- Run `dotnet run --project Tests/CardForge.Simulation.Tests/CardForge.Simulation.Tests.csproj` to test 3.0 rules, three 42-card unique starters, Commander tax, mana levels and fast spells.
- Run `dotnet build CardForge.Editor.csproj` for editor-side C# compilation.
- Unity Editor asset import / compilation, Game View visual fit at multiple sizes, live mouse/keyboard and Windows standalone build **still require validation in Unity**. The generated C# project may not list new scripts until Unity reimports project assets.
- Only display code changed: no edits to deck rules, server authority or the illustrations.
