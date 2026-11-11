# Shared CardForge package contracts

Shared `.cardforge` manifest, schema and archive contract types remain in this portable package. The Unity editor uses them to validate and export authored games. The native Unity content publisher converts compiled rule entries to `CardForge.Simulation`, which is separately compiled under `Assets/CardForgeStudio/CardForgeDedicatedServer/Runtime/Simulation` with an asmdef.

This directory contains shared contract code, **not** a legacy server. All gameplay runs in the Unity Dedicated Server.
