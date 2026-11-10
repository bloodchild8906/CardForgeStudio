# Architecture

CardForgeStudio keeps authoring and execution separated by a strict data boundary.

1. The Unity editor owns human-facing source documents: card data, visual frames and zones, gameplay zones and resources, actions, abilities, keywords, triggers, turn flow, and victory settings.
2. The package publisher validates those sources, compiles a schema-v2 authoritative rule pack, copies referenced assets, hashes every entry, and writes an immutable `.cardforge` ZIP archive.
3. The shared contract validates identifiers, semantic versions, safe paths, required content kinds, and manifest declarations in both Unity and .NET.
4. The server validates archive limits, entry paths, byte lengths, SHA-256 hashes, rule references, and victory definitions before recording a package version.
5. Activation is explicit. It publishes the compiled rule pack to the authoritative engine; old package versions remain available for rollback. Archives and active-version pointers are written atomically and restored on server startup. Activation is rejected while the same game definition is used by an unfinished match.

The existing raw rule-pack upload, content-pack store, asset service, state-machine runtime, social features, moderation, LiveOps, store, reporting, and WinUI dashboard remain available.
