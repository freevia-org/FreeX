# Test Gates

`eng/test-gates.json` is the single contract for automatic test execution. It names the app,
native platform, and gate ownership of every test project instead of relying on broad solution
names whose contents drift over time.

## Commit Gate

The commit gate proves source and deterministic desktop behavior. It runs the relevant app's
core, contract, and supported desktop test gates on the target runner. It excludes render evidence,
package smoke, external-workbook benchmarks, signing, and publication.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeX -Platform windows
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeW -Platform linux
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeP -Platform macos
```

Every app has a native commit lane on all platforms. The all platforms contract uses Windows, Linux,
and macOS native runners. Windows additionally runs the WPF desktop
projects; Linux and macOS run the portable core, contract, and Avalonia projects assigned to their
platform. This keeps platform-specific UI tests off unsupported runners.

## Release Gate

The release gate includes every applicable commit gate plus release-only render evidence. Packaging,
platform-native launch smoke, signing/notarization, and GitHub publication remain release workflow
responsibilities and are not silently treated as unit tests.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate release -App all -Platform windows
```

The runner is serial by project. Existing explicit batch projects remain separate entries where
they provide process isolation for UI or renderer resources. A project may belong to one gate only;
the release gate inherits commit coverage rather than duplicating a second project list.
