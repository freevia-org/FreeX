# Test Gates

`eng/test-gates.json` is the single contract for automatic test execution. It names the app,
native platform, and gate ownership of every test project instead of relying on broad solution
names whose contents drift over time. `tools/Get-TestGateMatrix.ps1` converts that contract into
the GitHub Actions matrix, so adding or moving a test no longer requires a second hand-maintained
workflow matrix.

## Main Integration Gate

The commit gate is the hosted integration tier for a push to `main`. It runs the relevant app's
core, contract, integration, Avalonia, and supported sister-app desktop projects on the target
runner. It excludes WPF UI-host batches, render evidence, package smoke, external-workbook
benchmarks, signing, and publication.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeX -Platform windows
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeW -Platform linux
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeP -Platform macos
```

Every app has a native commit lane on all platforms. Tests with observable filesystem, runtime,
globalization, process, or OS behavior stay in `*-portable` gates on Windows, Linux, and macOS.
Pure calculation/model/localization/definition suites with no platform behavior run once through
the `platformProjects.linux` portion of an existing underloaded Linux gate rather than repeating
identical assertions on three operating systems or paying for a separate runner checkout and setup.
The canonical `CI` workflow runs automatically
for `main` pushes, remains manually dispatchable, and cancels superseded runs for the same ref.
Branches are integrated after repository preflight and a successful Release build; this repository
does not use pull-request workflows. Hosted CI runs repository-static validation once, while small
platform-behavior preflights exercise process, shell, path, macOS-readiness, and Linux-packaging
behavior on Windows, Linux, and macOS. The manifest assigns these checks through `preflightModes`
to short existing integration entries, so they reuse the same checkout, SDK setup, NuGet cache,
and runner slot instead of occupying four additional jobs. The gate contract requires exactly one
static owner and one platform owner per OS. This also avoids rebuilding generated-document
validators and rescanning every tracked path three times. All integration entries run in parallel,
and only gates marked `requiresFullHistory` receive a full checkout. The local command defaults to
`-Mode All` and therefore remains the complete preflight.

## Release Gate

The release gate adds WPF UI-host batches and release-only render evidence. `App Tester Release`
requires exact-SHA successful CI and CodeQL runs, then executes only these release entries. The
integration attestation and release-only results together are the complete test suite for the
immutable release candidate without rerunning integration projects. Packaging, platform-native
launch smoke, signing/notarization, and GitHub publication remain release-workflow responsibilities.

```powershell
pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate release -App all -Platform windows
```

Projects remain serial within a gate where process isolation matters, while independent gates and
platforms run concurrently. A gate can declare `partitions` and `partitionProjects` when one safe,
isolated test assembly dominates the critical path. The runner deterministically balances source
files by statically discoverable test-case count (facts plus inline theory rows), combines the
generated partition with the project's existing VSTest exclusions, and runs every non-partitioned
sibling only in partition one. The FreeX Avalonia
commit gate uses two processes per OS for its 2,000+ test assembly; folding the former neutral jobs
into existing Linux lanes makes that expansion job-count neutral. FreeP adds one purposeful Windows
job to separate its independent WPF and Avalonia stacks, replacing the former nine-minute serial
critical path. The seven WPF host batches are separate release gates so each receives
an isolated Windows runner and no single batch determines a 25-minute serial critical path. The
seven render-evidence assemblies are divided between two gates per operating system, cutting their
serial critical path roughly in half without launching an unbounded renderer fan-out. A project may
belong to one gate only.

Hosted CI and release gates retain TRX and hang-diagnostic output and use a 15-minute test-host hang
timeout. They do not silently retry a failed project; a failure remains visible and the individual
matrix job can be rerun without repeating successful jobs. Local runs may explicitly request the
legacy bounded project retry with `-RetryFailedProjectCount 1` when diagnosing a suspected flake.

A gate may declare `buildProjects` for shipping assemblies that its tests inspect but do not
reference in their normal configuration. `Invoke-TestGate.ps1` builds each declared prerequisite
once before the gate's tests (unless `-NoBuild` is used because an earlier workflow build already
produced it). The FreeW Windows desktop gate and the isolated FreeP WPF/Avalonia desktop gates use
this contract for their shipping hosts. FreeP's two independent UI stacks run concurrently rather
than serializing more than 4,000 tests and both application builds on one critical-path runner.

CI selects one manifest entry with `-GateId` on each hosted runner. The established `FreeX commit
gate` required-check name is retained for branch-protection compatibility, but its aggregate now
covers the generated matrix for all three apps, including its embedded static and Windows, Linux,
and macOS platform preflights.
`App Tester Release` does not repeat those tests: it requires successful `ci.yml` and
`codeql.yml` runs for its immutable `GITHUB_SHA`, runs the release-only matrix, and starts immutable
native packaging and installation work in parallel. App publication still waits for the complete
release-only test gate, every selected package, and the
all-app workflow also waits for every suite package before creating or updating any release.
The all-platform release matrix deliberately budgets the observed 20 concurrent GitHub runner slots:
15 release-test entries run immediately while speculative packaging is capped at five concurrent
jobs. This prevents package builds from queueing the tests that decide whether publication is allowed;
remaining package jobs still overlap the longer release-only UI and render lanes.
Release packaging and publication checkouts are shallow because they only need the immutable checked-out
SHA and query remote tags through GitHub or `git ls-remote`; no release script reads historical objects.
This avoids fetching the complete repository history independently in every packaging job.
Platform-specific suite dispatches also generate only the requested suite-runtime matrix entries;
they no longer allocate five jobs and skip most of their steps at runtime.

NuGet caches are keyed from the SDK, central package versions, and repository-wide build props and
targets. Individual project files are intentionally excluded from the key: adding or moving a project
does not change restored package content, while central dependency changes still invalidate the cache.
CodeQL uses no-build C# extraction over shipped production sources and excludes tests, build output,
and repository tooling because those are already compiled and exercised by CI/preflight gates.
