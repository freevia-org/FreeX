# ADR-008: Comprehensive Code Review Hardening — 2026-05-28

**Date**: 2026-05-28
**Status**: Accepted

## Context

A full architectural review of the 1,166-file FreeX source identified 17 prioritised findings spanning correctness bugs, security issues, performance regressions, and architectural inconsistencies. All 17 items were resolved in a single day via 12 PRs (#33–#44) merged to `main`.

## Decisions

### P0 — Correctness / Data Loss / Security

**CellStyle equality completeness (PR #33)**
`CellStyle.Equals` and `GetHashCode` excluded `NativeDifferentialAttributes`, `NativeDifferentialChildXmls`, and `NativeDifferentialElementXmls` from comparison. Two styles that differed only in XLSX dxf preservation metadata were treated as equal, silently discarding native metadata in the style registry. Fixed by adding all three fields to both methods using `SequenceEqual` helpers.

**NativeJsonAdapter: static options, no indentation, password hashing (PR #34)**
`JsonSerializerOptions` was allocated per save call (bypasses the .NET reflection cache) and had `WriteIndented = true` (doubles/triples file size). Fixed with a `static readonly SaveOptions`. Protection passwords were serialised as plaintext strings. Fixed with `NativePasswordHelper` that stores `"sha256:<HEX>"` and verifies with legacy-plaintext fallback for backward compatibility.

**XlsxFileAdapter: structured load diagnostics (PR #35)**
Five `catch (Exception ex) { Debug.WriteLine(...) }` blocks silently discarded feature-load failures in Release builds (Debug.WriteLine is stripped). Replaced with a `XlsxLoadResult` record that carries `IReadOnlyList<string> Warnings`; callers surface warnings via `MessageBox` after open.

### P1 — Performance / Stability

**GridView per-frame cache promotion (PR #36)**
`RenderCells` and `RenderSplitPaneCells` allocated fresh `Dictionary<CellColor, SolidColorBrush>`, `Dictionary<CellBorder, Pen>`, and `Dictionary<CellTypefaceKey, Typeface>` on every render frame. Promoted to `private readonly` class-level fields cleared with `.Clear()` at the start of each pass.

**CommandBus.Undo safety (PR #37)**
`PopUndo` moved the command to the redo stack before calling `Revert`. If `Revert` threw, the command was permanently lost. Added `RollbackPopUndo` and wrapped `Revert`/`Apply` in try/catch across both Undo and Redo paths.

**RecalcEngine and FormulaEvaluator hardening (PR #38)**
Full-column references (already handled by compact-range storage in the dependency graph) were validated with regression tests. Added a `[ThreadStatic] private static int _evalDepth` guard to `FormulaEvaluator.EvaluateNode` — returns `#NUM!` when depth exceeds 256 instead of crashing with a stack overflow.

**GetStyle: remove defensive Clone (PR #39)**
`Workbook.GetStyle` cloned the registered style on every read. Callers only use the style read-only (passing it to `StyleDiff.ApplyTo` which clones internally). Removed the clone; callers that previously mutated the returned style directly were fixed to use explicit `.Clone()` at their call sites (three sites in `HyperlinkCommands`, `PivotTableRefreshService`).

### P2 — Testability / Security

**Hyperlink scheme whitelist (PR #40)**
`HyperlinkNavigationPlanner` had no scheme guard before calling `Process.Start`. Added `IsAllowedScheme` with an allowlist of `http`, `https`, `mailto`, `ftp`; the navigation path returns early for any other scheme.

**IUserMessageService abstraction (PR #41)**
`MainWindow` partial classes called `MessageBox.Show` at ~55 call sites, making those paths untestable. Extracted `IUserMessageService` (in `App.UI`) with `ShowError`/`ShowWarning`/`ShowInfo`/`AskYesNo`, implemented by `WpfUserMessageService` (in `App.Host`), and migrated all `MainWindow`-owned call sites. Dialog classes retain direct calls pending per-dialog injection.

### P3 — Architecture / Memory

**XLSX stream load (PR #42)**
`OpenWorkbookLoader` read the entire XLSX file into a `byte[]` then created a `MemoryStream` for both the inspection and load passes — keeping two full copies in memory simultaneously. Changed to open a `FileStream` directly and reuse it for both passes (one copy instead of two).

**Undo stack byte-budget eviction (PR #43)**
The undo stack evicted by command count alone (`MaxUndoDepth = 100`). Added `IEstimatesMemory` interface, `MaxUndoByteBudget = 52_428_800` (50 MB), a running `_undoStackBytes` counter, and trim logic that evicts from the front when either limit is exceeded.

**NativeXmlPreserveBag consolidation (PR #44)**
12 `WorksheetXxxMetadataModel` classes each held a handful of nullable `string?` preservation fields with no behaviour. Replaced with `NativeXmlPreserveBag` (a `Dictionary<string, string>` with `Get`/`Set`/`Contains`/`All`) backed by `XmlNativeBagSerializer` in `Core.IO`. Three classes with structured behaviour (`WorksheetPageBreaksMetadataModel`, `WorksheetCellWatchesMetadataModel`, `WorksheetIgnoredErrorsMetadataModel`) were intentionally retained.

## Consequences

- Style registry collisions on dxf-preservation metadata are eliminated.
- `.fxl` files are smaller and faster to save; protection passwords are hashed on disk.
- XLSX partial-load failures are visible to users instead of silently discarded.
- GridView render loop allocations are reduced; cached objects are reused across frames.
- Undo chain corruption on exception is prevented; large snapshots are bounded by 50 MB.
- `javascript:` and similar URI schemes cannot be launched via hyperlink cells.
- `IUserMessageService` enables `MainWindow` path testing without blocking WPF dialogs.
- Peak XLSX load memory is halved for seekable file streams.
- 12 repetitive metadata model classes collapsed into one general-purpose bag; `Core.IO` owns the serialisation details.
