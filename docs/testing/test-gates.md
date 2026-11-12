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
validators and rescanning every tracked path three times. All integration entries run in parallel.

Before integration, `tools/Test-BranchForIntegration.ps1` verifies the exact `origin/main` CI result,
runs preflight and the Release build, and then executes only the affected Windows commit gates.
`tools/Get-ImpactedTestGates.ps1` derives those gates from transitive `ProjectReference` consumers
plus each gate's `impactPaths`. The latter covers source-contract tests that intentionally inspect
shipping source without compiling a reference to it. Ordinary branches cannot advance a red or
still-running `main`; `-AllowRedMainFix` is reserved for a branch that repairs the observed failure.
FreeX keeps core and contract suites isolated on Windows, where they have distinct runtime and
failure characteristics, but coalesces them on Linux and macOS. This removes two checkout/setup/
restore jobs and keeps CI plus CodeQL within the repository's observed 20-runner concurrency ceiling.
Only gates marked `requiresFullHistory` receive a full checkout. The local command defaults to
`-Mode All` and therefore remains the complete preflight.

Repository-wide static checks batch tracked-file work through Git instead of opening each file from
PowerShell, build case/Unicode path indexes in one pass, and preselect managed-source candidates
before detailed path analysis. Generated-document helper projects use the normal incremental,
parallel .NET build path first and retain the isolated serialized build only as a one-time fallback.
These are execution optimizations only: conflict markers, line endings, path collisions, Unicode
normalization, script syntax, platform-specific APIs, generated freshness, and project references
remain covered.

## Impact-Based CI Coverage

A push to `main` pays for what it can affect, and nothing is guessed. `tools/Select-CiCoverage.ps1`
(the `prepare` job) picks one of two coverage modes:

- `full` runs the whole commit matrix. It is the default and the answer to every doubt.
- `inert` runs only the legs that own a repository preflight (`runGate=false`: static preflight on
  Linux, platform preflight on Windows, Linux and macOS) and skips the test gates. The preflight is
  never skipped.

`inert` is granted only when the commit differs from its **anchor** (the nearest ancestor with a
successful, completed, full-coverage `ci.yml` run) exclusively inside the documentation directories
listed in `eng/test-gates.json` `ciImpact.inertDocsDirectories`. Everything else is `full`:
source, tests, `tools/`, `eng/`, `.github/`, `Directory.Build.*`, `Directory.Packages.props`,
`global.json`, solutions, root and project files, other `docs/` trees, odd path shapes (traversal,
case differences, quoting, absolute paths), a missing or unprovable anchor, force pushes and first
pushes without history, API or git errors in the selector, the daily scheduled run, and
`workflow_dispatch` with `full` (the default). Selection is by tree comparison against the anchor,
so merge commits, rebases and force pushes need no special handling.

Why no per-project gate selection: nearly every commit-gate test project reads repository files at
runtime (source-contract tests over `src/`, `docs/`, `tools/`, `.github/`, and other apps' sources),
so a `ProjectReference` closure does not bound what a test can observe and cannot be proven sound.
`tools/Get-ImpactedTestGates.ps1` therefore keeps its project-closure logic for the advisory local
Windows gate (`-Scope Local`), while `-Scope Ci` applies exactly the rule above.

The inert list is mechanically true. `tools/Test-CiImpactSelection.ps1` (run by
`Test-TestGateContract.ps1` in the static preflight) fails when any tracked source, script, project,
workflow or manifest outside release-only test projects refers to a listed directory. Comments do not
count; two documentation generators are named exemptions that must stay unreferenced by any gate. It
also holds the path-to-mode table, the decision table (every failure to establish a fact selects
`full`), the reduced-matrix invariants (all preflight owners kept) and the release-attestation probes.

Each `ci.yml` run records its coverage in an evidence job named `CI coverage: full` or
`CI coverage: inert anchor=<sha>`, and the `FreeX commit gate` aggregate also requires that job.
`tools/Test-GitHubReleaseCandidate.ps1` (used by `Resolve-GitHubReleaseCandidate.ps1`, and so by
`Full Signed Release`) reads it back: a run before this feature (no evidence job) or a `full` run
attests its commit; an `inert` run attests its commit only if its anchor has a successful full run
and is an ancestor of the commit; an unreadable record attests nothing. A reduced run therefore
cannot make a release candidate falsely green; at worst the resolver walks to an older candidate.

A daily scheduled `ci.yml` run (`workflow_dispatch` with `full: true` does the same on demand) always
runs the full matrix so a full attestation exists near every commit and drift cannot hide behind
reduced pushes. Scheduled and manual runs use their own concurrency group, so they neither cancel nor
are cancelled by a push run.

## Diff-Scoped Local Preflight

The local integration gate (`tools/Test-BranchForIntegration.ps1`) does not re-run preflight checks
whose inputs the branch cannot have changed. `eng/preflight-scopes.json` maps every preflight check
(by its label in `tools/Test-RepositoryPreflight.ps1`) to the path globs that can change its outcome.
The gate passes `-ScopeBaseRef <green origin/main SHA>` and a check runs only when the branch diff
against that commit (committed, uncommitted and untracked; added, deleted and either side of a
rename) touches its triggers, or an added/deleted path matches its `structuralTriggers` (for
example the path-collision scan in cross-platform portability). Scoping fails safe and runs EVERY
check when the base commit is not contained in `HEAD`, the diff or manifest cannot be read, a
global trigger changes (`Test-RepositoryPreflight.ps1`, `ToolScriptSupport.ps1`, the scopes file,
`global.json`, `Directory.*`, `.gitattributes`, ...), or a changed path matches no check and is not
declared ignorable. A check with no manifest entry always runs, and the gate contract
(`tools/Test-PreflightScopes.ps1`) fails until it is mapped.

`-Full` runs every check locally:

```powershell
pwsh -NoProfile -File tools/Test-BranchForIntegration.ps1 -Full
```

Hosted CI never scopes: `ci.yml` still runs `Test-RepositoryPreflight.ps1 -Mode Static` once and
`-Mode Platform` on all three operating systems in full, and the contract test fails if any workflow
mentions `-ScopeBaseRef`. A bare `Test-RepositoryPreflight.ps1` is also always full. Scoping therefore
only shortens the pre-merge wait; a missed trigger is caught by the first CI run after the push, and
`-Full` is the escape hatch before it.

## Release Gate

The release gate adds WPF UI-host batches and release-only render evidence. `Full Signed Release`
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
belong to one gate per platform only; platform-specific grouping can differ when it removes runner
overhead without changing coverage.

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
`Full Signed Release` does not repeat those tests: it requires successful `ci.yml` and
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
