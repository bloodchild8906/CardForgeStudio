# Editor to server workflow

1. Open `Editor` in Unity and use the existing CardForge designers.
2. Open **CardForge > Game Package Publisher**. Select a block, set its semantic package version, choose the turn flow, review gameplay defaults, and publish a `.cardforge` file.
3. Launch the native server dashboard and open **Game packages**.
4. Choose the package. The server imports it only after archive, manifest, hash, rule-pack, and victory validation succeeds.
5. Select an imported version and activate it. New games can then use the manifest `gameId` as their rule-pack ID.

Import does not implicitly replace the active definition unless **Activate after import** is selected. Package versions are immutable: importing different bytes under the same package ID and version is rejected.
