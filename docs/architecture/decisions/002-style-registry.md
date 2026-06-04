# ADR-002: Style Registry — Identity by Value, Not by ID

**Date**: 2026-05-12  
**Status**: Accepted

## Context

Cell styling requires attaching font/fill/border/alignment metadata to cells. Excel uses a shared styles table keyed by integer index. We need an equivalent that supports structural equality (two cells with the same visual appearance should share one style entry) and is compatible with the "records and read-only structs" code style convention.

## Decision

- `CellStyle` is a class implementing `IEquatable<CellStyle>` with deep structural equality
- `CellBorder` is a `readonly record struct`
- `CellColor` is a `readonly record struct`
- `Workbook.RegisterStyle(CellStyle)` deduplicates by structural equality (linear scan; acceptable for v1 style counts)
- `Workbook.GetStyle(StyleId)` returns a clone (defensive copy prevents callers from mutating shared state)
- `StyleId` is an integer index; `StyleId 0` is always `CellStyle.Default` (zero-init means no style applied)

## Rationale

Structural equality deduplication mirrors how Excel's shared styles table compresses identical styles. Cloning on get is safe and simple — style objects are small. Linear scan is O(n) on style count, which stays small in v1 workbooks.

## Consequences

Style mutations always go through `RegisterStyle`, not direct mutation. The registry grows monotonically (no style GC in v1). A future optimization could use a `Dictionary<CellStyle, StyleId>` for O(1) lookup.
