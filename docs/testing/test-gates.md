# Test Gates

`eng/test-gates.json` is the single contract for automatic test execution. It names the app,
native platform, and gate ownership of every test project instead of relying on broad solution
names whose contents drift over time. `tools/Get-TestGateMatrix.ps1` converts that contract into
the GitHub Actions matrix, so adding or moving a test no longer requires a second hand-maintained
workflow matrix.

## Commit Gate

The commit gate proves source and deterministic desktop behavior. It runs the relevant app's
core, contract, and supported desktop test gates on the target runner. It excludes render evidence,
package smoke, external-workbook benchmarks, signing, and publication.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeX -Platform windows
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeW -Platform linux
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeP -Platform macos
```

Every app has a native commit lane on all platforms. The canonical `CI` workflow runs automatically
for `main` pushes and pull requests, remains manually dispatchable, and cancels superseded runs for
the same ref. Windows additionally runs the WPF desktop projects; Linux and macOS run only projects
assigned to those platforms. The three repository preflights and all test-gate entries run in
parallel. Only gates marked `requiresFullHistory` receive a full checkout.

## Release Gate

The release gate adds release-only render evidence. Canonical CI executes those entries alongside
the commit gates, so an exact-SHA successful CI run is the complete source-test attestation for a
tester release. Packaging, platform-native launch smoke, signing/notarization, and GitHub
publication remain release-workflow responsibilities and are not silently treated as unit tests.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate release -App all -Platform windows
```

Projects remain serial within a gate where process isolation matters, while independent gates and
platforms run concurrently. A project may belong to one gate only; the matrix includes commit and
release-only entries without constructing a second project inventory.

Hosted CI retains the first failed TRX and retries only that test project once. This bounded retry
absorbs intermittent desktop-event failures without repeating successful gates or platforms; a
second failure remains a hard gate failure. Local runs do not retry unless explicitly requested
with `-RetryFailedProjectCount 1`.

A gate may declare `buildProjects` for shipping assemblies that its tests inspect but do not
reference in their normal configuration. `Invoke-TestGate.ps1` builds each declared prerequisite
once before the gate's tests (unless `-NoBuild` is used because an earlier workflow build already
produced it). The FreeW and FreeP Windows desktop gates use this contract for both their WPF and
Avalonia shipping hosts, keeping commit and release execution consistent.

CI selects one manifest entry with `-GateId` on each hosted runner. The established `FreeX commit
gate` required-check name is retained for branch-protection compatibility, but its aggregate now
covers the generated matrix for all three apps plus all Windows, Linux, and macOS preflights.
`App Tester Release` does not repeat those tests: it requires successful `ci.yml` and
`codeql.yml` runs for its immutable `GITHUB_SHA`, then proceeds directly to native packaging and
installation tests.
