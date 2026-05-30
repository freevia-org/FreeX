# Agent Git Policy

## Parallel Chat Sessions

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

## Ownership

- Avoid assigning overlapping write scopes to parallel sessions.
- If overlap is unavoidable, name the shared files explicitly and coordinate which session owns the next edit.
- Treat unrelated modified or untracked files as user/session-owned and leave them untouched.

## Verification

- Before claiming a branch is ready, run the relevant build and tests from that branch's worktree.
- If a build fails because another process locks output files, identify and clear the stale process before rerunning.
- Report exact verification commands and outcomes in the final response.
