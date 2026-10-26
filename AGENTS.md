# Agent Git Policy

## Parallel Chat Sessions

- No one works in `main`: always create a separate branch and linked worktree for each task, do the work there, then merge, push, and clean up the temporary worktree/branch after the task is done.
- Expect multiple parallel agent sessions to be active at the same time.
- Use one isolated Git worktree per active chat session.
- Use one branch per session, preferably with the `codex/` prefix unless the user asks for another name.
- Do not run two chat sessions in the same working directory.
- Do not do implementation work directly in the primary `main` worktree. Treat `main` as an integration target only; if a session starts there, create or switch to an isolated linked worktree before editing files.
- Do not leave dirty working changes in `main`. If a session accidentally modifies `main`, stop and move that work onto an owned branch/worktree before continuing, or clearly report that `main` is dirty and blocked.
- Before starting code changes, run `git status --short --branch` and `git worktree list --porcelain`.
- If the current checkout is already a linked worktree, continue there and do not create a nested worktree.
- Prefer project-local worktrees under `.worktrees/`; this directory must remain ignored by Git.
- Keep changes scoped to the session's branch and avoid touching unrelated dirty files.
- Commit small, buildable units when the user asks for commits or when preparing integration.
- Always sync before starting work: fetch/pull the latest `main`, then merge or rebase it into the session branch before editing files, running UI tests, or resuming after any pause.
- Sync as often as possible while working, especially after pauses, before touching shared files, before running final verification, and whenever other sessions are actively merging.
- Always merge and sync after completing a task: once verification passes, integrate the finished work into `main`, then sync the session branch from the updated `main` so both are aligned before handing off.
- Keep long-running branches close to `main`: sync from `main` frequently, especially before editing shared files, running final verification, or asking for review.
- Merge completed, verified work back to `main` as often as practical. Prefer small, coherent integrations over letting many session branches drift.
- Merge as often as possible once work is verified. Do not let finished slices sit on session branches while other agents continue building on `main`.
- Integrate through `main` or a named integration branch only after build/tests pass, then sync other active session branches from the updated `main`.
- Before merging into `main`, verify the `main` worktree is clean or that dirty files are unrelated to the incoming changes. If dirty `main` files overlap with the merge, do not stash, overwrite, or work around them without explicit ownership; report the block and coordinate first.

## Execution

- Use subagents for independent work whenever the task can be split into non-overlapping scopes.
- Keep working until the assigned area is exhausted completely: implement the requested scope, close obvious follow-up gaps in that area, verify, document, commit/merge when appropriate, and report any remaining blockers explicitly.
- The local environment is configured for full access. Do not ask the user for permission prompts, do not request escalation, do not include `sandbox_permissions`, and do not use `require_escalated` in tool calls.
- If a command or tool is blocked by sandbox/setup/environment policy, report the exact blocker and stop that slice cleanly instead of waiting for approval.
- When launching or re-prompting subagents, explicitly tell them they inherit full access and must follow the same no-permission/no-escalation rule.

## Ownership

- Avoid assigning overlapping write scopes to parallel sessions.
- If overlap is unavoidable, name the shared files explicitly and coordinate which session owns the next edit.
- Treat unrelated modified or untracked files as user/session-owned and leave them untouched.

## Verification

- Before integrating a committed branch, run `pwsh -NoProfile -File tools/Test-BranchForIntegration.ps1` from that branch's worktree. It verifies exact-base CI health, runs repository preflight and the full Release build, and executes only the manifest-defined Windows commit gates affected by the branch's paths and transitive project references.
- Do not merge ordinary work while the exact `origin/main` CI candidate is running, missing, cancelled, or failed. Only a branch whose purpose is to repair that failure may use `-AllowRedMainFix`; do not use `-SkipMainHealthCheck` for integration.
- The affected-gate runner favors the normal .NET restore/build cache, build servers, shared compilation, and parallelism. Run additional focused tests during development when useful, but do not make a complete local test lane a routine branch gate. GitHub runs the manifest-driven cross-platform integration suite after `main` is pushed, and the canonical Full Signed Release workflow completes the UI/render/release-only gates for the same immutable SHA before packaging.
- `Test-BranchForIntegration.ps1` only exercises the Windows commit gates. Behaviour that only diverges on Linux/macOS (for example, .NET mapping `ApplicationData` and `LocalApplicationData` to the same directory there, unlike Windows) will not fail locally unless it is also run there. Before pushing a branch that touches a gate whose `platforms` include `"linux"` in `eng/test-gates.json` (currently `freex-portable-unix`, `freex-avalonia`, `freew-core-portable`, `freep-core-portable`), run the Linux leg locally in Docker: `pwsh -NoProfile -File tools/Run-LinuxTestGate.ps1` (all linux commit gates) or `-GateId <id>` for just the affected one. It builds/reuses a lean container (`tools/LinuxTestGate/Dockerfile`, distinct from the interactive GUI/VNC image in `tools/LinuxInteractiveDocker`) and calls the same `tools/Invoke-TestGate.ps1` entry point CI uses, with `-Platform linux`. So the local pre-push sequence is: static preflight (`Test-RepositoryPreflight.ps1 -Mode Static`), the Windows branch-integration gate (`Test-BranchForIntegration.ps1`), then the Linux leg (`Run-LinuxTestGate.ps1`) when a linux-platform gate is affected.
- Do not run `dotnet test FreeX.slnx` or `dotnet test FreeX.UiTests.slnx` as routine/default verification.
- Run the UI lane (`dotnet test FreeX.UiTests.slnx --configuration Release --no-build --logger "trx;LogFileName=ui-tests.trx"`) locally only when the user explicitly requests it or there is a specific UI failure that cannot be diagnosed through focused tests. Tester-release candidates run the complete UI/release lane on GitHub; do not duplicate it locally without a concrete reason.
- For ribbon rendering/adaptive-layout/resize work, the focused ribbon lane is `dotnet test FreeX.RibbonTests.slnx --configuration Release --filter Category=RibbonUiLane` (see `docs/ribbon-ui-test-lane.md`).
- If a build fails because another process locks output files, identify and clear the stale process before rerunning.
- If a build or test command still fails because of stale build-server or shared-compiler state after clearing locks, rerun that command once with `--disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1` before treating it as a product failure.
- Report exact verification commands and outcomes in the final response.

## Process & Resource Hygiene

Every session must leave the machine as clean as it found it. Leftover Windows helper and build processes (`find.exe`, `grep.exe`, `head.exe`, `sed.exe`, `awk.exe`, `conhost.exe`, orphaned `dotnet`/`MSBuild`/`VBCSCompiler`/`testhost` nodes) accumulate across parallel sessions and thrash the box — several sessions building at once has caused MSB4166/OutOfMemory build crashes and stalled hangs.

- Prefer the harness's dedicated **Grep / Glob / Read** tools over shell `grep`/`find`/`head`/`cat`/`sed`/`awk`. On Windows/Git Bash each shell filter spawns a short-lived process wrapped in its own `conhost.exe`; a deep `... | grep | sort | head` pipeline spawns several per call and they pile up. Keep any unavoidable shell pipelines shallow (one filter, not a chain).
- **Do not orphan background builds/tests.** A backgrounded `dotnet build`/`dotnet test` that is still running when the session pauses or ends leaves MSBuild nodes + `testhost` + `VBCSCompiler` alive. Either run verification in the foreground, or if backgrounded, actively confirm it finished and reap it before moving on. Do not launch a replacement build while a prior one you started is still running — wait for or cancel the first.
- **Shut down build servers when done.** Before finishing a task, pausing, or handing off, run `dotnet build-server shutdown` to release this session's persistent MSBuild/Roslyn nodes.
- **Never kill processes machine-wide.** Do NOT run `taskkill /F /IM dotnet.exe` (or any `/IM`-by-image kill) or otherwise kill processes you did not start — it aborts every other session's builds and tests. If you must clear a stale process, target the specific PID your own session spawned.
- Kill stale `testhost.exe` from your own runs (they can hold `bin\...Tests.dll` and cause `MSB3027` file-locked failures) by PID, not by image name.
- Subagents/implementers that build or test inherit all of the above; every prompt that grants build/test access must restate: no machine-wide kills, no orphaned background builds, prefer dedicated search tools over shell filters, and `build-server shutdown` when done.
- Clean up temp/scratch files, throwaway probe projects, and stray app instances the session created, in addition to the branch/worktree cleanup covered above.
