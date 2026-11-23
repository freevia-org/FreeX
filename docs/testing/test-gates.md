# Test Gates

`eng/test-gates.json` is the single contract for automatic test execution. It names the app,
native platform, and gate ownership of every test project instead of relying on broad solution
names whose contents drift over time. `tools/Get-TestGateMatrix.ps1` converts that contract into
the GitHub Actions matrix, so adding or moving a test no longer requires a second hand-maintained
workflow matrix.

## Main Integration Gate

The commit gate is the hosted integration tier, started manually (see Manual-Only Actions below). It runs the relevant app's
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
The two most expensive FreeX macOS legs (`freex-portable-unix` and `freex-avalonia` on macOS, about
17 of the 22 billed macOS minutes of a push and the longest macOS job) do not run on push: they are
the release-tier, macOS-only entries `freex-portable-unix-macos` and `freex-avalonia-macos`, which run
in `release-readiness.yml` (manual dispatch) and in `Full Signed Release`, so macOS coverage of the
same projects arrives before every release rather than being dropped. Windows and Linux keep all of
those suites in push CI, including the platform-agnostic managed-core ones (Formula, Model,
Presentation, Localization). `Test-TestGateContract.ps1` pins that every one of those projects stays
assigned on macOS and that the two legs stay release-tier and macOS-only. FreeW and FreeP keep their
small macOS commit legs: they own the macOS platform preflight and finish in 1.5 to 3 minutes.
`freex-portable-unix-macos` runs as two whole-project partitions: the macOS pool is five slots shared
with the six osx package legs, so in full release run 44885755819 its single 10.5-minute job (576 s of
test step, serial across nine projects) was granted a slot last (08:46:52) and alone set the 08:57
finish while the other macOS legs ended by 08:54. Halving it keeps every project on macOS (each runs in
exactly one partition) and makes the late-slot tail about 7 minutes; `freex-avalonia-macos` carries a
9-minute hint because it is the longest macOS leg (572 s / 442 s) and should be dispatched first.
The repository-automation tests (the PowerShell preflight and tool-script runners, the GitHub workflow
and release-automation source contracts, the macOS readiness/promotion preflights, and the resx
localization suites) live in `tests/FreeX.Tools.Tests`, run by the Windows-only `freex-tools` commit
gate. They used to sit inside `FreeX.App.Host.Tests`, a net10.0-windows/WPF assembly that disables
test parallelization process-wide, so about half of its serial time was non-WPF work that waited for
the release host batches. `FreeX.Tools.Tests` is a plain net10.0 project with xunit's class-level
parallelism on; only the timing-sensitive and culture-mutating classes join the `Tools serial`
collection (`DisableParallelization`). It is Windows-only because those tests launch `powershell.exe`
against the real repository. The classes kept their `FreeX.App.Host.Tests` namespace, so the fully
qualified test names are unchanged, and the move was proven lossless by diffing `dotnet test
--list-tests` before and after (5,421 tests before; 4,883 in the host plus 538 here). The two
resx classes that need WPF types (`LocalizationResourceTests`, `LocalizationUsageTests`) stay in the
host. `Test-TestGateContract.ps1` pins that the gate stays a Windows commit gate and that the project
stays a non-WPF project without an assembly-wide `DisableTestParallelization`. Measured on a shared
16-core developer machine at 20-28% background load (two runs each): the 538 tests take 389 and 398 seconds
with class-level parallelism and 528 and 635 seconds with parallelization forced off, which is how the
host assembly runs them. The gain is capped by the largest class (`GitHubWorkflowPreflightTests`, one
script launch per test); splitting that class is the next lever.
The canonical `CI` workflow is dispatched manually (`gh workflow run ci.yml -f full=true`), has no push or
schedule trigger, and cancels superseded runs for the same ref.
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
shipping source without compiling a reference to it. Ordinary branches cannot advance a `main` whose exact-SHA CI run is red or
still running; a commit with no run at all is allowed because CI is manual-only (the preflight then runs
in full, since there is no green attestation to scope it against); `-AllowRedMainFix` is reserved for a branch that repairs the observed failure.
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

## Measured Runner Time (2026-11-21)

Source: `gh run view <id> --json jobs` for the runs below, plus the per-leg job logs of CI run
44916523212. Job-minutes are `completedAt - startedAt` per job. The repository is public, so hosted
minutes are not billed; the scarce resources are the ~20 concurrent runner slots (windows ~10-11,
linux ~6, macOS 5) and wall-clock.

| Workflow / run | Jobs | Job-min | Wall | Gate step (build + test) | Setup, cache, upload, preflight |
| --- | --- | --- | --- | --- | --- |
| `ci.yml` push 44916523212 / 44913151638 / 44911864096 | 23 | 90.8 / 90.5 / 93.8 | 8.8 / 10.0 / 7.9 min | 68.9 / 68.4 / 71.1 | 18.6 / 18.5 / 18.9 (+2.6-3.1 platform preflight) |
| `release-readiness.yml` dispatch 44911915670 | 23 | 115.2 | 17.4 min | 95.6 | 18.8 |
| `full-release.yml` real 44885755819 (release tests ran) | 49 | 212.6 | 22.3 min | 100.7 (18 release-test legs) | 17.1 |
| `full-release.yml` dry run 44913445758 (release tests skipped) | 30 | 82.5 | 13.4 min | n/a | n/a |

Where the job-minutes go (CI run 44916523212, 19 matrix legs): windows 11 jobs 55.8 min, linux 6 jobs
27.4 min, macOS 2 jobs 6.8 min. In the 12 legs whose logs were read, the gate step is 52.6 min of which
test execution is 25.2 min (48%) and restore plus compile 27.4 min (52%); fixed per-job overhead
(provisioning, checkout 6-36 s, `setup-dotnet` 6-30 s, NuGet and obj cache restore, upload) is about
1 min per job. The full-release real run spends 117.8 min in release tests, 59.0 in the 15 app package
legs, 12.2 in the five suite package legs, and 19.4 in the early-abort watcher (one ubuntu slot held
for the whole run; 12.4 min in the dry run), which is the price of stopping a doomed release early.

Per gate, one SHA (test duration is the sum of the `Passed!` durations in the leg log; the rest of
the leg is restore, compile and runner startup):

| Gate (platform) | Wall per leg | Test time | Notes |
| --- | --- | --- | --- |
| `freex-avalonia` windows 1of2 / 2of2 | 481 / 413 s | 254 / 183 s | critical path of the push; includes full-history checkout (36 s) |
| `freex-avalonia` linux 1of2 / 2of2 | 339 / 355 s | 194 / ~190 s | |
| `freex-portable-unix` linux 1of2 / 2of2 | 347 / 252 s | 82 / 79 s | compile of the Core stack is >50% of the leg |
| `freex-core-portable` windows 1of2 / 2of2 | 220 / 310 s | 37 / 121 s | each leg rebuilds the Core stack |
| `freex-contract` windows | 196 s | 33 s | |
| `freex-tools` windows | 264 s | 132 s | |
| `freew-desktop` windows | 394-554 s | 204 s | serial: two builds, then Host and Avalonia tests; critical path in some runs |
| `freep-avalonia-desktop` / `freep-wpf-desktop` windows | 386 / 299 s | 101 / 93 s | |
| `freew-core-portable`, `freep-core-portable` x3 OS | 139-239 s, 172-210 s | n/a | one platform preflight owner per OS inside |
| release: `freex-wpf-host-batch1..7` windows | 223-452 s | n/a | seven batch assemblies |
| release: `freex-render-evidence-a/b` x3 OS | 294-520 s | n/a | seven capture assemblies, same ~22 shared files compiled once per assembly |
| release: `freex-portable-unix-macos` 1of2/2of2, `freex-avalonia-macos` 1of2/2of2 | 348/348, 370/295 s | n/a | macOS slots (5) are the binding limit |

Same project executed more than once per SHA. `Free.Shared.AppServices`, `FreeX.Core.Formula/IO/Model`,
`FreeX.App.Localization/Presentation/Services`, `FreeX.Integration` and `FreeX.ParityCompare` tests run
three times (windows in `freex-core-portable`/`freex-contract`, linux in `freex-portable-unix`, macOS in
the release-only `freex-portable-unix-macos`); `FreeX.App.Avalonia.Tests` and
`Free.Shared.Shell.Avalonia.Tests` run on windows and linux in the push gate and on macOS in
`freex-avalonia-macos`; the FreeW and FreeP core-portable projects run on all three OS in the push gate.
A script over `eng/test-gates.json` finds 93 (project, platform) executions of 49 projects and NO project
that runs twice on the same platform for one SHA, in the push gate or between the push gate and the
release gates. Every repeat above is therefore a different operating system. Across workflows,
`Full Signed Release` already reuses a readiness run on the exact SHA instead of re-running its release
tests (`Resolve-ReleaseGateEvidence.ps1`), and skips them for dry runs.

Rejected as not provably safe or not worth it, with the evidence:

- Dropping a repeated cross-OS execution: the suites read paths, culture, line endings and process
  behaviour (the `.NET` `ApplicationData`/`LocalApplicationData` split already surfaced a Linux-only
  failure), so a Windows pass does not prove the Linux or macOS result. The pure-calculation suites
  are already single-platform (`platformProjects.linux`).
- Merging `freex-core-portable` windows 2 partitions into 1: 530/500/565 s of leg time becomes about
  400-440 s (saves ~2 job-min and one windows slot per push) but that equals the windows
  critical path (400-480 s), so wall could grow; the 3 to 2 retune in `a24671e64f` measured the same
  trade-off. Not taken.
- Rebalancing `freex-avalonia` windows partitions (254 s vs 183 s of test time): the split is by static
  test count, not time; a better cut needs per-class timings that move between runs. At most ~30 s of
  wall, not taken.
- One shared build artifact for the legs of a gate: `bin/` is ~2 GB per leg against a 10 GB repository
  cache, so transfer costs more than the ~1 min recompile (`obj/` is already cached per gate, and the
  timestamps script keeps that cache honest).
- One compile for the seven capture or host batch assemblies: each is a separate assembly by design
  (per-process isolation of Avalonia headless state); sharing needs a test-structure change with no
  proof of equivalence.
- Trimming `setup-dotnet` or the full-history checkout: the SDK pin cannot be proven preinstalled on
  every image, and the history-dependent tests are not enumerated.
- Skipping the daily scheduled `ci.yml` run when a push already ran full on the head SHA (3 of the 3
  sampled days): saves at most ~90 job-min per day (about 1% of push CI) and needs a new coverage mode
  that the release attestation reader would have to understand. Not taken.
- Replacing the early-abort watcher (one ubuntu slot for the release duration) by per-job failure
  steps: the classifier needs the whole run state, and the path cannot be exercised without dispatching
  a release.

Change made from this data: `release-readiness.yml` no longer re-executes an unchanged commit that
already failed its full gates twice. Measured 2026-11-12 to 2026-11-21: nine of about 25 scheduled
readiness runs executed an already-red SHA (`c1af1fb4` six times, `54e0cdc7` three times), each
about 100 job-minutes including six macOS jobs. The guard keeps one retry (so a flake still gets a
second look) and skips later runs for seven days; the summary job still fails with the reason, so the
red signal is unchanged and only the re-execution is saved. Failures whose README-freshness check
failed do not count, because that check reads the newest published release and can flip without a
commit. That rule would have skipped five of those nine runs: about 500 job-minutes, of which about
190 are macOS minutes, in ten days. `ReleaseAutomationWorkflowTests` pins the guard and the
still-red summary.

## Manual-Only Actions

Every GitHub Actions workflow is started by hand with `workflow_dispatch` (for example
`gh workflow run ci.yml -f full=true`). No workflow declares a `push`, `schedule` or `pull_request`
trigger, so pushing to `main` starts nothing and the local gate (`tools/Test-BranchForIntegration.ps1`)
is the pre-merge check. There are no exceptions: `release-auto-retry.yml` is also
dispatch-only (an operator passes the `run_id` of a failed Full Signed Release run to re-run its
infrastructure failures once), and no workflow declares `workflow_run`. `tools/Test-GitHubWorkflows.ps1` enforces the
contract. Consequences: a commit may have no `ci.yml` run, and `Full Signed Release` still requires
exact-SHA successful CI and CodeQL runs, so dispatch both for the release SHA first.

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

A manual `ci.yml` run with `full: true` (the default) always runs the full matrix, which is what a release
attestation needs. Manual runs use their own concurrency group.

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

## Cross-Session Throttle

Many agent sessions share one machine, so per-session parallelism cannot bound the aggregate load: a
Release build that takes minutes on a quiet box has taken tens of minutes under load, and repository
preflight and focused tests have hit their timeouts waiting on CPU and memory, not on a defect.
`tools/Invoke-Throttled.ps1` makes heavy verification queue instead of thrash:

```powershell
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build -- dotnet build FreeX.slnx --configuration Release
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Test -- dotnet test <project> --configuration Release --no-build
```

- `-Kind Build` and `-Kind Preflight` share the heavy pool (`FREEX_THROTTLE_BUILD_SLOTS`, default 2);
  `-Kind Test` has its own pool (`FREEX_THROTTLE_TEST_SLOTS`, default 2). The wrapper waits for a slot
  (FIFO among live waiters, a "waiting for a ... slot (queue position ...)" line on stderr every 30 s),
  runs the command in the foreground with output and the exact exit code passed through, and always
  releases the slot. Everything after `--` reaches the command verbatim; the wrapper never changes
  build or test flags and never kills any process.
- Each slot is a named OS mutex (`Global\FreeXThrottle-<pool>-<n>`), which Windows releases when the
  owning process dies. A killed or crashed holder therefore cannot deadlock a slot; the next waiter
  receives an abandoned mutex and treats it as acquired. (A command orphaned by killing only its
  wrapper keeps running without a slot until it exits.)
- `-MaxWaitMinutes` (default 360) is the escape hatch: when the budget is spent the command runs
  without a slot after a warning. `FREEX_THROTTLE_DISABLE=1` bypasses the throttle entirely, and it is
  bypassed automatically when `GITHUB_ACTIONS=true` (hosted runners are not shared) and for any
  descendant of a throttled command (`FREEX_THROTTLE_HELD`), so parallel preflight children and nested
  wrappers never take a second slot.
- The repository entry points use it themselves: `Test-RepositoryPreflight.ps1` re-launches itself
  through one `Preflight` slot before any check starts (the wait is outside the per-check watchdog),
  and `Test-BranchForIntegration.ps1` takes a `Build` slot around the Release build and a `Test` slot
  around each affected gate. `tools/Test-InvokeThrottled.ps1` (run by `Test-ToolScripts.ps1`) pins
  the contract: exit-code pass-through, release on failure and on a killed holder, the slot limit and
  both bypasses. `FREEX_THROTTLE_NAMESPACE` selects a private pool (used by that self-test).

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

### Releasing from the newest readiness-green commit

When main moved since the last fully green `release-readiness.yml` run, re-running every release gate
on the head repeats work a readiness run already proved. So a `full-release.yml` dispatch from
`refs/heads/main` with an EMPTY `source_sha` releases the **newest readiness-green ancestor of main**
by default (real and dry-run alike). The first job, `resolve`, finds it with
`tools/Get-LatestReadinessGreenSha.ps1`, which judges each commit with
`tools/Resolve-ReleaseGateEvidence.ps1` (the same fail-closed script the candidate job uses, against
the dispatched head's gate manifest; the candidate job re-checks the chosen commit against its OWN
manifest). The workflow then builds, packages, signs and tags exactly that commit and skips the release
tests only when a fresh (7 days) readiness run proved every expected gate on exactly that SHA.

- Fail closed: no green commit, a helper error or any unparseable answer releases the dispatched head
  and runs every gate; the job summary says "FALLBACK TO THE HEAD" loudly.
- The job summary always prints the released commit, how many commits it is behind the head and the
  evidence run, and warns when an ancestor is released (later commits are not in the release).
- `release_head=true` (default false) releases the dispatched head and runs every gate, as before the
  default changed. The weekly packaging rehearsal sets it so packaging stays rehearsed at the head.
- An explicit `source_sha` (40-character lowercase) always wins; `release_head` is then ignored.
- Tag continuations are unchanged: they release the tagged commit, and `source_sha` is rejected there.
- The resolved SHA is validated again by the candidate job (40-character lowercase, ancestor of the
  dispatched head, head fetched) and reaches every job only through the `RELEASE_SHA` environment
  variable from the `resolve` job output; no job uses `github.sha` for checkout or tagging. The CI/CodeQL
  attestation and immutable-tag checks apply to the resolved SHA as before.

Preview the default choice locally with `pwsh -NoProfile -File tools/Get-LatestReadinessGreenSha.ps1`
(exits 1 when none exists, in which case the workflow releases the head).
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
18 release-test entries run immediately while speculative packaging is capped at five concurrent
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
