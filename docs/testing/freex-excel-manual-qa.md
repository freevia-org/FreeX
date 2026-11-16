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
| 5 | Grid navigation and formula editing: Ctrl+Home, arrows, Ctrl+Down/Right, Shift+Left selected matching cells/ranges in both apps. F2 showed `=SUM(D2:D3)` with D2:D3 references highlighted; Escape restored the D4 selection without changing its value (14). Mouse click selected B2 in both apps (value 4); FreeX status bar showed Average 4, Count 1, Numerical Count 1, Sum 4, Min 4, Max 4. Pass for this keyboard/mouse slice. | Paired live screenshots on separate monitors. Saved workbook copies remain unchanged and both show B2=4, D4=14 and `=SUM(D2:D3)`. Screenshots were inspected but not persisted. |
| 6 | Ribbon/key-tip surface: `Alt`, `N` selected Insert in both apps with B3 still selected and value 2. Excel's Insert ribbon exposes a substantially broader command set; FreeX visibly lacks several Excel controls and shows PivotChart and Insert Timeline disabled. A subsequent accelerator probe accidentally exited key-tip mode and typed `t` into Excel B3; Escape canceled the edit and the saved workbook remained unchanged. This probe is not a pass/fail result. | Paired live screenshots. Excel title reported Saved to this PC after cancellation. FreeX Insert key tips showed different command labels (for example, Table `TB` versus Excel `T`), but command activation parity was not established. |
| 7 | Mouse sheet-tab context menu: right-clicking Excel's Sheet1 tab opened Insert, Delete, Rename, Move or Copy, View Code, Protect Sheet, Tab Color, Hide, Unhide (disabled), Select All Sheets, and Link to this Sheet. Repeating right-click on FreeX's Sheet1 tab left it selected and opened no menu; Shift+F10 opened the cell context menu instead. Gap observed; no workbook changes. | Paired live UI screenshots. B3 remained 2 and Excel reported Saved to this PC. Excel and FreeX instances were closed after the check. |
| 8 | Mouse row-header menu: right-clicking row 3 selected the same full row in both apps. Both expose Insert, Delete, Row Height, Hide, Unhide, Format Cells, and Clear Contents. FreeX additionally exposes AutoFit Row Height and Group/Ungroup; Excel instead exposes Paste Options and Paste Special. No command was activated. | Paired live screenshots and accessibility state. Both showed the unchanged fixture values and Excel reported Saved to this PC. |
| 9 | Keyboard row-header context route was started in Excel (`Shift+Space`, then `Shift+F10` opened the row menu). The paired FreeX check was stopped before comparison: unrelated text appeared in FreeX's filename field during Open, so the dialog was canceled and no further keyboard input was sent. Inconclusive; not a product discrepancy. | Excel fixture remained saved; no workbook was opened or changed in FreeX. Desktop interaction is paused until the source of the unexpected input is cleared. |

## Discrepancies

| ID | Severity | Observation | State / next action |
| --- | --- | --- | --- |
| UXQA-20261116-01 | P2 | FreeX Find still differs in initial dimensions (about 528x160 px versus Excel's 440x194 px), though the oversized results area and advanced controls are now hidden by default. | Compact-dialog repair is ported and the 23 focused tests pass. Further size/layout tuning remains; confirm measurements at matched window scaling before changing dimensions. |
| UXQA-20261116-02 | P2 | A new Excel sheet defaults to Aptos Narrow 11; FreeX defaults to Calibri 11. | Investigate workbook theme/default-font behavior; verify visual and saved-style parity in a later formatting iteration. |
| UXQA-20261116-03 | P3 | The blank C4 serializes as null in Excel's XLSX and as an empty string in FreeX's XLSX. Formulas and all displayed/cached values match. | Determine whether this is a meaningful package-semantic difference; retain as an open structural observation. |
| UXQA-20261116-04 | P2 | Insert command surface differs: Excel exposes controls not present in FreeX (including Forms, Pictures, Shapes, Icons, 3D Models, SmartArt, Screenshot, Checkbox, Maps, Slicer, WordArt, Signature Line, Object, and Equation); FreeX shows PivotChart and Insert Timeline disabled. | Compare against the explicitly implemented FreeX command inventory before classifying omissions as defects. Route actionable implemented-command mismatches to the UI owner; retain intentional scope exclusions as coverage notes. |
| UXQA-20261116-05 | P3 | Insert key-tip labels differ for shared commands (Table displayed `T` in Excel and `TB` in FreeX). | Keyboard route to the tab matches (`Alt`, `N`); command activation was not validated, so treat as an observed accessibility/usability difference pending a safe repeat. |
| UXQA-20261116-06 | P2 | Right-clicking a sheet tab opens Excel's sheet-management context menu, but right-clicking FreeX's Sheet1 tab opens no menu. `Shift+F10` in FreeX opens the cell context menu instead. | Confirm expected FreeX scope and the implemented route for rename/delete/move/hide before handoff. The mouse-context affordance is absent in the current UI build; prioritize parity if these sheet commands are intended to be supported. |
| UXQA-20261116-07 | P3 | Row-header menus share core row operations, but FreeX adds AutoFit Row Height and Group/Ungroup while Excel provides Paste Options and Paste Special in this context. The menu labels also vary slightly (`Insert Row Above` / `Delete Row(s)` versus `Insert` / `Delete`). | Determine whether the paste-specific and extra outline commands are intentionally context-specific. Shared operations appear present; follow up only if the differing placement, labels, or availability causes a user-visible workflow gap. |

## Current Status

Eight narrow scenarios have yielded observations; a ninth keyboard route was safely abandoned as inconclusive after unexpected text appeared in FreeX's file dialog. Workbook creation/editing, tested grid navigation, the Insert-tab keyboard route, and core row-header commands passed. Find/Replace still has a dimension mismatch. FreeX's worksheet-tab right-click did not expose the Excel sheet-management menu. This is not a full keyboard, mouse, or command-surface pass. Continue one paired scenario at a time only after desktop input is confirmed isolated; mouse double-click editing remains untested because an earlier attempt routed coordinates to the other monitor and did not activate the target cell. Insert command coverage still needs reconciliation against FreeX's implemented-command inventory.

Screenshots from this run were inspected live but not persisted as files. Capture paired before/after screenshots in future iterations when recording visual outcomes.

## Integration Handoff

- The QA branch is `codex/ux-parity-current-20261116`, currently at `fb965f522091ef352edb45508e7d1325cbf1a5b6`; fetched `origin/main` is `1d683ada415bf0099f2f3db97758e526e9b78494`. The branch is 7 commits ahead and 10 behind. That newer main and active dialog/Avalonia work overlap the branch's Find/Replace change, so do not merge this stale branch without reconciling those files and rerunning the mandatory integration gate.
- Exact-base CI `43856339630` passed for b82fc0c20. `tools/Test-BranchForIntegration.ps1` passed on commit `2e36570531` after a first attempt hit output locks from the QA-launched FreeX process; that process was closed and the same gate rerun. The Release build passed with 0 warnings and 0 errors. Scoped gates passed: localization 45/45; presentation 5716 passed, 2 skipped; services 3750 passed, 1 skipped; integration 978 passed, 1 skipped; parity comparison 49/49; Avalonia shell 139/139; Avalonia app 2429/2429; tools 518 passed, 1 skipped. Preflight checks also passed.
- New exact-base CI `43867864502` and CodeQL `43867864416` for `1d683ada41` completed successfully. The QA branch has not been pushed or merged. Rebase and rerun the integration gate only after the active shared-dialog work settles; verify latest main health again before integration.
- Active work remains in FreeX Dialogs, FreeX Avalonia Parity, FreeX macOS, and FX Refactor sessions. Their latest checkpoints show in-progress edits or verification; none is merge-ready yet. Shared-dialog changes overlap this branch. The FreeX Linux diagnostic is complete and made no product changes; it removed its detached gate worktree and run containers, but a temporary helper script remains because its host policy blocked deletion. No integration was needed for that read-only diagnosis.
- Local worktree audit: `ux-qa-iter25-find-replace-integration` is clean and contains the repair source commit; `ux-qa-iter27-backstage-visual-20260831` has unresolved rebase conflicts; `freex-number-format-gallery-20260821` contains untracked screenshot evidence. Preserve the latter two. Other clean pre-rewrite branches have not been validated against current main and are not yet safe to archive as integrated.
