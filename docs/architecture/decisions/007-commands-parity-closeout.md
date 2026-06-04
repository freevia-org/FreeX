# ADR-007: Commands Parity Closeout Boundaries

**Date**: 2026-05-19
**Status**: Accepted

## Context

FreeX tracks visible Excel commands and many rows have reached Partial status. Some partial rows are small interaction
gaps over an existing model, while others require substantial renderers, package writers, locale engines, or proprietary
heuristics.

## Decision

Move model-backed command gaps to Implemented when they are undoable, tested, and visible in the UI. Keep advanced chart
families Deferred until each family has a dedicated data model, renderer, and package writer. Keep full locale/accounting
fidelity and full Excel PDF publish-option parity Partial while documenting the invariant/accounting subset and the
print-renderer-backed PDF/XPS export boundary. Custom number-format parity advances inside the invariant formatter rather than by adopting OS locale services:
conditional sections, named/default indexed color prefixes, escaped literals, comma scaling, visible LCID currency
symbols, and deterministic decimal/group/date separators for modeled LCIDs `409`, `407`, `40C`, and `422` are supported,
while full localized separators, currency names, workbook palette/theme overrides, and full
LCID/accounting semantics remain outside the closeout.

## Consequences

- Command parity is test-backed instead of checklist-driven.
- Clipboard, paste, Format Painter, alignment, AutoFit, Format Cells, and supported Flash Fill behavior can be presented
  as implemented where the model and UI genuinely support them.
- Advanced chart XML can be recognized without exposing non-working authoring commands or fallback rendering.
- Custom number formats are more Excel-like for common workbook-local patterns without making display output depend on
  the user's Windows locale. The supported subset now includes invariant conditional sections, named and
  default indexed `Color1`-through-`Color56` color prefixes for numeric, date/time, and text sections, invariant
  conditional section selection for numeric and date/time values, escaped literals including escaped layout directive
  characters, active percent scaling with token placement and quoted/escaped literal handling, variable decimals, variable and fixed-denominator fractions, scientific notation, elapsed time, comma scaling, date/time with long and compact AM/PM markers, contextual
  month/minute token handling across quoted literals, five-`m` month initials, and rounded clock/elapsed fractional-second display, elapsed-time, and text-section spacing/fill directive cleanup, visible currency symbols from LCID tokens, and deterministic
  decimal/group/date separators for modeled LCIDs `409`, `407`, `40C`, and `422`;
  exact locale services, localized currency names, workbook palette/theme overrides, and full accounting layout width
  fidelity remain documented partials.
- PDF files are now created directly and deterministically from the print renderer, but the first implementation is
  print-faithful raster output rather than full Excel PDF publish semantics. Export options now cover active-sheet and
  selected-range scopes, entire visible-workbook export, one-based page ranges with rendered page-count validation,
  extensionless `.pdf` path normalization, open-after-publish, and requested PDF Info document-property embedding for the
  current workbook name plus deterministic FreeX metadata. XPS export writes the same modeled metadata subset into package core properties.
- Insert PivotTable's new-worksheet destination is now an undoable model command that creates a PivotTable sheet and
  reuses the existing worksheet-range PivotTable materialization path.
- PivotTable value-field number formats are applied during materialization for supported built-in `numFmtId` values and
  custom workbook-catalog `numFmtId >= 164` values, then merged with PivotStyle visual formatting. Value Field Settings
  exposes a broader built-in preset catalog for common number, currency/accounting, date/time, percentage, fraction,
  scientific, and text formats; preserves raw `numFmtId` editing for advanced/loaded files; and can author custom format
  codes directly through the custom catalog path. Duplicate preset IDs keep compatibility aliases such as `Date`, while
  the first matching preset remains the canonical display label. Custom PivotTable format IDs are remapped on save when
  they collide with generated cell-style format IDs, and preserved source-package PivotTable XML is rewritten to reference the
  remapped IDs. A full Excel number-format picker/catalog remains outside the closeout.
- Full Excel locale matching, full PDF option parity, and lossless advanced chart package writing remain outside this
  closeout.
