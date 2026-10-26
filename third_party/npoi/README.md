# NPOI.Core source build

FreeX uses `NPOI.Core.dll` for legacy BIFF8 (`.xls`) and OLE compound-file support.
The checked-in assembly is built from the Apache-2.0-licensed NPOI source rather than
the upstream NuGet binary, whose 2.8.x releases have a separate OSMF binary-release
agreement.

- Upstream: <https://github.com/nissl-lab/npoi>
- Source commit: `ab1aa0f7d9aaea06159eeb0311b7b74e752d819a`
- Upstream version: `2.8.1`
- Target framework: `net10.0`
- SDK: `10.0.400`
- Assembly SHA-256: `72B809E480E713F89D4BF4B49C427E16900F61F1A398CCD734B52E63EA97471E`
- License: Apache-2.0; the upstream license is preserved in `2.8.1/LICENSE`.

Regenerate the assembly with:

```powershell
pwsh -NoProfile -File tools/Build-NpoiCoreSourceDistribution.ps1
```

The script clones the exact source commit, builds only `NPOI.Core` in Release mode,
verifies the deterministic output hash, and replaces the tracked assembly.
