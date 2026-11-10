# CardForge.GamePackages

This is the shared, dependency-free package boundary.

- `Runtime` is compiled as the `CardForge.GamePackages` .NET Standard assembly and as a Unity asmdef through the local UPM package.
- `GamePackageContracts` defines manifest, content summary, file integrity, and victory contracts.
- `GamePackageManifestValidator` provides the host-neutral identifier, semantic-version, required-kind, declared-path, and archive-membership checks.
- `Schemas` documents the JSON wire format used by external tooling.

Archive I/O and executable rule validation remain server responsibilities so the shared assembly does not depend on ASP.NET or a JSON implementation unavailable to Unity.
