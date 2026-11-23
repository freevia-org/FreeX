# App Platform Publish Lanes

`Full Signed Release` (`.github/workflows/full-release.yml`) is the canonical
release surface for FreeX, FreeW, FreeP, and Free Suite. It pins the dispatch
SHA, requires successful exact-SHA CI and CodeQL, runs the release-only matrix,
and publishes nothing until every selected package and trust gate passes.

## Release inventory

| App | Tag | Windows x64 | Linux x64/ARM64 | macOS Intel/Apple silicon |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| FreeW | `freew-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| FreeP | `freep-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| Free Suite | `free-suite-v<version>` | signed non-Inno suite bootstrapper | suite installer `.zip` | aggregate package containing the accepted app bundles |

Windows code signing uses Azure Artifact Signing with the Freevia public-trust
profile. The workflow signs app payloads at the Velopack packaging stage,
verifies every Authenticode signature, builds the suite from the final signed
per-app installers, and signs the outer suite bootstrapper. Checksums are
calculated only after the last signature is applied.

Linux has no platform code-signing identity in this repository. Its portable
and installer archives are integrity-protected with adjacent SHA-256 files,
SPDX 2.2 SBOMs, runtime manifests, and a final release manifest tied to the
complete source commit.

macOS uses a Developer ID Application certificate, hardened runtime signing,
Apple notarization, ticket stapling, and validation. The lane fails closed if
credentials are absent or any signing, notarization, stapling, or validation
operation fails. `Full Signed Release` never silently publishes an unsigned or
unnotarized macOS substitute.

Every version tag is immutable. An existing tag at another commit stops the
run before publication.

## Suite semantics

The suite release is created only for `app=all`. The Windows package is a
repository-owned bootstrapper, not Inno Setup. It invokes the same signed
Velopack installers published on the individual app releases and propagates a
child failure as an overall installation failure. Linux and macOS suite
packages reuse the corresponding finalized per-app packages, so suite and
individual installations retain the same app identities.

## Dispatch

| Requested work | Inputs |
| --- | --- |
| One app, one platform | `app=<FreeX|FreeW|FreeP>`, `platform=<windows|linux|macos>` |
| One app, all platforms | `app=<FreeX|FreeW|FreeP>`, `platform=all` |
| All apps, one platform | `app=all`, `platform=<windows|linux|macos>` |
| All apps, all platforms | `app=all`, `platform=all` |

Example full non-prerelease:

```powershell
gh workflow run full-release.yml --ref main `
  -f app=all `
  -f platform=all `
  -f release_version=0.8.185 `
  -f prerelease=false
```

Use `prerelease=true` for a signed test release. The same signing and integrity
requirements apply to both release states.

By default (empty `source_sha`, dispatched from `refs/heads/main`) the run releases the **newest
readiness-green ancestor of main**, not the main head, so the release tests a readiness run already
proved are reused. The first job (`resolve`) finds it with `tools/Get-LatestReadinessGreenSha.ps1` and
the job summary states the released commit, how many commits behind the head it is, the evidence run
and a loud warning when it is not the head. If no green commit is found the head is released and every
gate runs (the summary says so). To release the dispatched head itself and run every gate, add
`-f release_head=true`; to release one specific commit add `-f source_sha=<40-char lowercase sha>`
(an explicit SHA always wins). The commit must be an ancestor of the dispatched main head; see Release
Gate in `docs/testing/test-gates.md` for the exact rules and fail-closed behaviour.

## Installation verification

- Windows: run Authenticode verification and compare the adjacent SHA-256 file
  before launching the standalone executable or Velopack installer.
- Linux: run `sha256sum -c <download>.sha256`, extract the archive, and use its
  `install.sh` helper.
- macOS: run `shasum -a 256 -c <download>.sha256`; Gatekeeper validation should
  recognize the stapled notarization ticket before installation.

The older focused workflows remain diagnostic or preview lanes. They are not
the canonical all-app publication path and must not be used to claim a complete
signed release.

## Failing fast and recovering from infrastructure flakes

- `early_abort` (default on) starts a watcher job that cancels the whole run on the first
  real failure, because GitHub can cancel a run but not a single job. The run then ends as
  Cancelled, the failed job and its error stay visible, and nothing is published: the
  publication gate still requires every upstream job to have succeeded. Set `early_abort=false`
  to let every job finish.
- A failure is real unless `tools/Get-ReleaseRunFailureClass.ps1` can positively show the
  platform caused it (runner lost or never acquired, or only a job-setup or artifact-transfer
  step failed). Timeouts and anything uncertain are real.
- `release-auto-retry.yml` is dispatched by hand with the `run_id` of a finished run (it has no
  event trigger; `release-readiness.yml` also dispatches it for its own red main run) and re-runs the failed jobs once when that run concluded `failure`, was on
  its first attempt, and every failed job is infrastructure. It never retries a real failure,
  a cancelled run or a second attempt. A re-run still parks Windows signing jobs at the
  `public-preview` approval again.
- Publication resumes safely: `tools/Send-ReleaseAssets.ps1` uploads only assets that are
  missing, leaves identical ones, replaces only interrupted uploads, and refuses to overwrite a
  published asset whose content differs.

### Rehearsing the failure paths (fault injection)

`inject_fault` (default `none`, which changes nothing) lets a dry run exercise the paths above
live. It is honoured only with `dry_run=true` dispatched from `refs/heads/main`; the first step of
the `candidate` job fails the run for any other combination, and the value reaches scripts only
through environment variables. It adds one cheap job, `Inject a rehearsal fault`, that nothing
depends on.

- `real`: that job fails with an ordinary step error. With `early_abort` on, the watcher
  cancels the run and `release-auto-retry.yml` stays out (cancelled runs are never retried);
  with `early_abort=false` the run ends `failure` and the classifier reports `real`, so
  auto-retry does not retry.
- `infra`: on attempt 1 the job fails in a step named `Download injected rehearsal inputs`,
  which the classifier treats as infrastructure (a genuine runner annotation cannot be faked
  honestly). The watcher does not cancel, the run ends `failure`, a dispatched auto-retry re-runs the failed
  job once, and attempt 2 skips the injection and passes.
- `selftest`: the job runs `tools/Test-ReleaseResilienceSelfTest.ps1`: `Invoke-WithRetry` with a
  first attempt that fails and a second that succeeds, and `Send-ReleaseAssets.ps1 -PlanOnly`
  against a synthetic half-published release. Uploading to a live release is not rehearsed,
  because a draft release is invisible to the tag lookup and a real tag is public.
