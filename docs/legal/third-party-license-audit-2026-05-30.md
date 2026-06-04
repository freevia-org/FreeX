# Third-Party License Audit

**Date:** 2026-05-30

## Scope

This audit checked NuGet packages restored by:

```powershell
dotnet restore FreeX.slnx --disable-parallel -v:minimal
```

The scan covered 15 `project.assets.json` files under `src/` and `tests/`.

## Result

- 41 unique restored NuGet packages were found.
- Every restored package is listed in [../THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).
- Runtime packages use MIT, Apache-2.0, or BSD-3-Clause style licenses.
- A package-provided `NOTICE` file was found for Microsoft.NET.ILLink.Tasks
  and is now reflected in [../THIRD_PARTY_LICENSES.md](../../THIRD_PARTY_LICENSES.md).
- Package-provided license files were found for FluentAssertions,
  Newtonsoft.Json, SharpVectors.Wpf, and System.IO.Packaging and are now
  reflected in [../THIRD_PARTY_LICENSES.md](../../THIRD_PARTY_LICENSES.md).

## Open Compliance Watch Item

FluentAssertions 8.9.0 is a test/development dependency, not a runtime
dependency. Its package-provided Xceed Community License is limited to
non-commercial use unless a commercial license is obtained. If FreeX source and
tests are distributed for commercial use, replace FluentAssertions or confirm
the required Xceed commercial license.
