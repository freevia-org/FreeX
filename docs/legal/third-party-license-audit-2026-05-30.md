# Third-Party License Audit

**Date:** 2026-06-06

<!-- VERIFY: this audit predates several package additions to Directory.Packages.props,
     including Velopack 1.2.0 (added 2026-06-16, for Windows installer/self-update
     packaging) and packages added for FreeP media/PDF/legacy-doc import work. Velopack
     is already listed in ../../THIRD_PARTY_NOTICES.md (MIT), but the packages added
     since this audit's date have not been re-verified against this audit's own
     restore-and-scan methodology (project.assets.json count, package-provided
     NOTICE/license file discovery). Re-run the scope command below and refresh this
     audit's package count/date to confirm no new commercial-license or attribution
     obligations were introduced. -->

## Scope

This audit checked NuGet packages restored by:

```powershell
dotnet restore FreeX.slnx --disable-parallel -v:minimal
```

The scan covered 18 `project.assets.json` files under `src/` and `tests/`.

## Result

- 66 unique restored NuGet packages were found.
- Every restored package is listed in [../THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).
- Runtime packages use MIT, Apache-2.0, BSD-3-Clause, or BSD-style package
  licenses.
- A package-provided `NOTICE` file was found for Microsoft.NET.ILLink.Tasks
  and is now reflected in [../THIRD_PARTY_LICENSES.md](../../THIRD_PARTY_LICENSES.md).
- Package-provided license files were found for Avalonia.Angle.Windows.Natives,
  FluentAssertions, Newtonsoft.Json, SharpVectors.Wpf, and
  System.IO.Packaging and are now reflected in
  [../THIRD_PARTY_LICENSES.md](../../THIRD_PARTY_LICENSES.md).

## Open Compliance Watch Item

FluentAssertions 8.9.0 is a test/development dependency, not a runtime
dependency. Its package-provided Xceed Community License is limited to
non-commercial use unless a commercial license is obtained. If FreeX source and
tests are distributed for commercial use, replace FluentAssertions or confirm
the required Xceed commercial license.
