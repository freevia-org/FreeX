# ADR-006: Find & Replace — Service in Core.Commands, Func<Workbook> in Dialog

**Date**: 2026-05-12  
**Status**: Accepted

## Context

Find and Replace is a workbook-level search feature. It needs to be testable without a UI and must integrate with the undo/redo system for Replace operations.

## Decision

- `FindReplaceService` is a static class in `Core.Commands` (no UI dependency)
- `Find` returns `IReadOnlyList<FindResult>` — pure read, no command bus needed
- `ReplaceAll` groups edits per sheet and issues one `EditCellsCommand` per sheet through `ICommandBus` for undo support
- `ReplaceAll` returns the count of cells actually written (not the count of cells matched), which is lower when some matches are formula cells (formula cells are skipped on replace)
- `FindReplaceDialog` in `App.Host` accepts `Func<Workbook>` (not `Workbook`) so that if the user opens a new file while the dialog is open, subsequent searches use the new workbook rather than the stale snapshot

## Rationale

Keeping Find logic in `Core.Commands` makes it unit-testable without WPF. The `Func<Workbook>` pattern is the minimal fix for the stale-reference problem without introducing a full observer/event pattern. Returning actual-writes-count rather than match-count gives the user an accurate replacement report.

## Consequences

Replace does not support formula cells (skipped with no error). Replace operates on display text (numbers stored as text will be re-parsed as numbers after replacement). A future "Replace in formulas" feature would require a separate command.
