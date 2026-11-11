# CardForge exported game packages

The `.cardforge` archives in this directory are authoring/distribution artifacts, not separate server binaries. Generate a current sample through **CardForge > Generate Sample Game**. The native publisher extracts compiled rules and stages images to `Assets/StreamingAssets/CardForge` for the Unity Dedicated Server. When playing against a local native demo host, **CardForge > Play Sample Game** can upload compatible rule and artwork changes directly.

Do not launch or import packages through an old ASP.NET API; the native Unity process is the sole runtime server.
