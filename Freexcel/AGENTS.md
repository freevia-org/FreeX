# Agent Git Policy

## Parallel Chat Sessions

- Use one isolated Git worktree per active chat session.
- Use one branch per session, preferably with the `codex/` prefix unless the user asks for another name.
- Do not run two chat sessions in the same working directory.
- Before starting code changes, run `git status --short --branch` and `git worktree list --porcelain`.
- If the current checkout is already a linked worktree, continue there and do not create a nested worktree.
- Prefer project-local worktrees under `.worktrees/`; this directory must remain ignored by Git.
- Keep changes scoped to the session's branch and avoid touching unrelated dirty files.
- Commit small, buildable units when the user asks for commits or when preparing integration.
- Always sync before starting work: fetch/pull the latest `main`, then merge or rebase it into the session branch before editing files, running UI tests, or resuming after any pause.
- Always merge and sync after completing a task: once verification passes, integrate the finished work into `main`, then sync the session branch from the updated `main` so both are aligned before handing off.
- Keep long-running branches close to `main`: sync from `main` frequently, especially before editing shared files, running final verification, or asking for review.
- Merge completed, verified work back to `main` as often as practical. Prefer small, coherent integrations over letting many session branches drift.
- Integrate through `main` or a named integration branch only after build/tests pass, then sync other active session branches from the updated `main`.

## Ownership

- Avoid assigning overlapping write scopes to parallel sessions.
- If overlap is unavoidable, name the shared files explicitly and coordinate which session owns the next edit.
- Treat unrelated modified or untracked files as user/session-owned and leave them untouched.

## Verification

- Before claiming a branch is ready, run the relevant build and tests from that branch's worktree.
- If a build fails because another process locks output files, identify and clear the stale process before rerunning.
- Report exact verification commands and outcomes in the final response.
