# ADR-001: C# / .NET 10 and WPF for v1

**Date**: 2026-05-12  
**Status**: Accepted

## Context

We need to choose a technology stack for a native Windows spreadsheet application with a complex custom grid control, formula engine, and file I/O.

## Decision

- **Language**: C# 12+ / .NET 10 (LTS)
- **UI Framework**: WPF
- **Architecture**: Layered modular monolith, single process

## Rationale

- C# / .NET offers the best Windows-native fit with productive tooling and a mature ecosystem
- WPF is more stable and mature than WinUI 3 for complex custom controls like a virtualized grid
- Single-process monolith avoids unnecessary IPC complexity in v1
- The formula engine and core model are designed to be language-neutral at the interface level, preserving optionality for a future Rust port if profiling demands it

## Consequences

- Cross-platform is deferred (acceptable per scope)
- WPF is Windows-only (acceptable for v1)
- Must own the grid control — standard DataGrid will not scale
