# ADR-004: Volatile Functions — Dirty-First Evaluation Order

**Date**: 2026-05-12  
**Status**: Accepted

## Context

Excel functions like `NOW()`, `TODAY()`, and `RAND()` must re-evaluate on every recalculation, regardless of whether their inputs changed. The existing `RecalcEngine` uses a topological-sort dependency graph that only re-evaluates cells whose inputs changed.

## Decision

- `BuiltInFunctions.IsVolatile(string name)` is the single source of truth for which functions are volatile
- `RecalcEngine` maintains a `HashSet<CellAddress> _volatileCells`
- `RegisterFormulaDependencies` detects volatile functions by walking the AST; if found, the cell is added to `_volatileCells`
- On every `Recalculate(workbook, changedCells)` call, volatile cells are prepended to the evaluation plan BEFORE all other changed cells
- The prepend uses `_volatileCells.Concat(plan.OrderedCells.Where(c => !_volatileCells.Contains(c)))` — the `.Where` exclusion prevents volatile cells from appearing twice

## Rationale

Volatile-first ordering ensures volatile cells always have up-to-date values when non-volatile dependents evaluate. A simple `.Concat(...).Distinct()` was rejected because `.Distinct()` uses hash ordering, which does not preserve the volatile-first guarantee.

## Consequences

Every recalc call re-evaluates all volatile cells, regardless of whether anything else changed. For v1 volatile function counts (typically 1-5 per sheet), this is negligible. A future optimization could skip volatile re-evaluation when the workbook is guaranteed read-only (e.g. during a print preview).
