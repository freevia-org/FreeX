# FreeX / Excel Manual UX Parity

**Platform:** Windows, WPF FreeX host versus Microsoft Excel installed on this machine  
**FreeX source:** `origin/main` at `224915d89128d51fc93a93748f2088ace1db04ed`, Release build  
**Run date:** 2026-11-16

This is a sequential, paired manual run. Each scenario uses the same clean workbook state in Excel and FreeX, exercises the same mouse/keyboard route, compares visible behavior, and checks the saved XLSX package. A pass only applies to the named scenario; it does not imply full UI parity.

## Coverage Plan

| Area | Keyboard and mouse surfaces |
| --- | --- |
| App chrome and files | Start/recent, title bar, quick access, File/Backstage, New/Open/Save/Save As/Print/Export/Close, Options, window controls |
| Ribbon and menus | Every tab, groups, split buttons, galleries, key tips, contextual tabs, menus and dialogs |
| Grid and editing | Cell entry/edit/cancel/commit, clipboard, selection, navigation, range/name box, fill, insert/delete, row/column sizing, undo/redo |
| Formulas | Formula bar, function entry, recalculation, references, autocomplete, error handling and formula navigation |
| Sheets and views | Add/rename/move/copy/hide/unhide/protect, sheet menus, zoom, freeze/split, normal/page layout/page break views |
| Data and analysis | Sort/filter, tables, validation, text-to-columns, remove duplicates, pivots and slicers |
| Insert and review | Charts, shapes, images, hyperlinks, comments/notes, spelling, accessibility and selection pane |
| Workbook output | Save/reopen, formula/value/style comparison, print preview and export formats |

## Executed Iterations

| Iteration | Scenario and result | Evidence |
| --- | --- | --- |
| 1 | New workbook, paste a 4x4 table with multiplication and SUM formulas, inspect visible results, Save As in both apps. Pass for values and formulas. | `artifacts/manual-qa/iteration-01/Excel-UX-Iteration01.xlsx`; `artifacts/manual-qa/iteration-01/FreeX-UX-Iteration01.xlsx` |
| 2 | Keyboard navigation from A1 to B2, replace 3 with 4, commit with Enter, save. Both recalculate D2=5, B4=6 and D4=14. Pass. | Same saved workbooks after edit; parsed with OpenPyXL, including formula text and cached values. |
| 3 | Open Find using Ctrl+F. Functional command opens in both. Initial visual parity failed: Excel starts at about 440x194 px with Options/results hidden; FreeX started at about 706x424 px with format controls and an empty results grid visible. | Live paired UI inspection. Repair source: `codex/ux-qa-iter25-find-replace-20260831`, commit `12a6da5b6584f31432b61207de0b8e7d141d3b02`. |
| 4 | Reopen Find after porting the compact-dialog change. FreeX now opens at about 528x160 px; format tools and results are absent initially. The large-results-pane defect is fixed, but the dialog remains 88 px wider and 34 px shorter than Excel's 440x194 px window. | Paired live UI, Ctrl+F in Excel and FreeX. Focused `FindReplaceDialogXamlTests`: 23 passed. |

## Discrepancies

| ID | Severity | Observation | State / next action |
| --- | --- | --- | --- |
| UXQA-20261116-01 | P2 | FreeX Find still differs in initial dimensions (about 528x160 px versus Excel's 440x194 px), though the oversized results area and advanced controls are now hidden by default. | Compact-dialog repair is ported and the 23 focused tests pass. Further size/layout tuning remains; confirm measurements at matched window scaling before changing dimensions. |
| UXQA-20261116-02 | P2 | A new Excel sheet defaults to Aptos Narrow 11; FreeX defaults to Calibri 11. | Investigate workbook theme/default-font behavior; verify visual and saved-style parity in a later formatting iteration. |
| UXQA-20261116-03 | P3 | The blank C4 serializes as null in Excel's XLSX and as an empty string in FreeX's XLSX. Formulas and all displayed/cached values match. | Determine whether this is a meaningful package-semantic difference; retain as an open structural observation. |

## Current Status

Four narrow scenarios have been exercised; the initial workbook/editing scenarios passed and the Find/Replace visual defect was substantially reduced, with a smaller dimension mismatch still open. This is not a full keyboard, mouse, or command-surface pass. Continue with navigation, selection, editing and common Home/Data commands, one paired scenario at a time.

Screenshots from this run were inspected live but not persisted as files. Capture paired before/after screenshots in future iterations when recording visual outcomes.

## Integration Handoff

- The verified repair is commit `e4ef06c3d053d116762d408dfeca258cd7b0e779` on `codex/ux-parity-current-20261116`, based on `origin/main` `224915d89128d51fc93a93748f2088ace1db04ed`.
- `FindReplaceDialogXamlTests`: 23 passed, 0 failed. Release host build completed with 0 warnings and 0 errors before the live comparison.
- During reconciliation, `origin/main` advanced to `c728e62648bfed8527c5eefaabd20195881d87ca`. CI failed in repository preflight because `Test-MacOsAppReadiness.ps1` expects the previous `actions/download-artifact` commit marker; CodeQL passed. This UX branch does not repair that workflow failure, so the integration script, push, and merge are intentionally deferred until the exact base candidate is green.
- The newer upstream commits also modify the same Find/Replace XAML, code-behind, planner and tests. Coordinate with the active shared-dialog owner and rebase/resolve against a green main before proposing integration; do not overwrite the newer shared-dialog work without review.
- Local worktree audit: `ux-qa-iter25-find-replace-integration` is clean and contains the repair source commit; `ux-qa-iter27-backstage-visual-20260831` has unresolved rebase conflicts; `freex-number-format-gallery-20260821` contains untracked screenshot evidence. Preserve the latter two. Other clean pre-rewrite branches have not been validated against current main and are not yet safe to archive as integrated.
