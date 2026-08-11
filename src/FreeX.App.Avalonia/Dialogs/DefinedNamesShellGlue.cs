using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// Portable (no Avalonia UI) glue backing the Avalonia Defined Names dialogs (Name Manager, Define Name,
/// Create Names from Selection). It projects the workbook's stored named ranges into the portable
/// <see cref="DefinedNameRow"/> rows (via <see cref="DefinedNameListProjector"/>), enumerates the scope
/// choices, and maps a validated <see cref="DefinedNameDraft"/> — or a <see cref="PlannedDefinedName"/>
/// from the create-from-selection planner, or a delete — onto the Core named-range commands the shell then
/// runs through its shared session command path. Kept UI-free so it is unit-testable without a window.
/// </summary>
internal static class DefinedNamesShellGlue
{
    /// <summary>A scope choice offered by the Define Name editor: the workbook, or a single worksheet.</summary>
    internal sealed record ScopeChoice(string Label, DefinedNameScope Scope);

    /// <summary>
    /// Builds the scope choices for the Define Name editor: the workbook scope first, then each sheet in
    /// workbook order (labelled by its display name).
    /// </summary>
    public static IReadOnlyList<ScopeChoice> BuildScopeChoices(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var choices = new List<ScopeChoice>
        {
            new(DefinedNameScope.WorkbookLabel, DefinedNameScope.Workbook),
        };
        foreach (var sheet in workbook.Sheets)
            choices.Add(new ScopeChoice(sheet.Name, DefinedNameScope.ForSheet(sheet.Id, sheet.Name)));
        return choices;
    }

    /// <summary>
    /// Projects the workbook's stored named ranges and named formulas into Name Manager rows: each row carries
    /// the name, its scope label (from the stored metadata, defaulting to the workbook scope), the refers-to
    /// text (sheet-qualified A1 text for range names, the raw formula/constant text for named formulas), and a
    /// value preview. The derived <see cref="DefinedNameKind"/> drives the kind/error filtering. Both
    /// range-valued names (<see cref="Workbook.NamedRanges"/>) and formula/constant-valued names
    /// (<see cref="Workbook.NamedFormulas"/>) — plus their sheet-scoped counterparts
    /// (<see cref="Workbook.ScopedNamedRanges"/> and <see cref="Workbook.ScopedNamedFormulas"/>) — must be
    /// listed here, or a scoped/formula-defined name is invisible and unmanageable through the Name Manager
    /// even though it loads, resolves, and saves correctly.
    /// </summary>
    public static IReadOnlyList<DefinedNameRow> BuildRows(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var rows = new List<DefinedNameRow>();
        foreach (var (name, range) in workbook.NamedRanges)
        {
            var scopeLabel = workbook.TryGetNamedRangeMetadata(name, out var metadata)
                ? metadata.Scope
                : DefinedNameScope.WorkbookLabel;
            var comment = metadata?.Comment ?? "";
            var refersTo = FormatRefersTo(range, workbook);
            // This name lives in the workbook-global NamedRanges dictionary, so its real scope
            // identity is the workbook (null) regardless of what display text scopeLabel carries --
            // see the ScopeSheetId doc comment on DefinedNameRow for why scopeLabel alone must never
            // be used to recover this identity (a sheet can legally be named "Workbook").
            rows.Add(DefinedNameListProjector.CreateRow(name, scopeLabel, refersTo, refersTo, comment, scopeSheetId: null));
        }

        // Formula/constant-valued names (e.g. "TaxRate" = "=1.05" or "Total" = "=SUM(Sheet1!A:A)") are stored
        // separately from NamedRanges — but (R123) can carry the same Comment metadata a plain named range
        // does, keyed by name in the same NamedRangeMetadataByName dictionary (see
        // DefineNamedFormulaCommand) — so this must read it the same way the range loop above does, or a
        // comment entered for a named formula/constant would round-trip to the file correctly but never be
        // visible again in this Name Manager.
        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            var comment = workbook.TryGetNamedRangeMetadata(name, out var formulaMetadata) ? formulaMetadata.Comment : "";
            var refersTo = "=" + formulaText;
            // Workbook-global NamedFormulas dictionary: real scope identity is the workbook (null).
            rows.Add(DefinedNameListProjector.CreateRow(name, DefinedNameScope.WorkbookLabel, refersTo, refersTo, comment, scopeSheetId: null));
        }

        // Sheet-scoped named ranges (Excel "localSheetId") are stored separately from the workbook-global
        // NamedRanges dictionary and must also be listed, or they're invisible and unreachable through the
        // Name Manager's Edit/Delete actions, even though they load, resolve, and save correctly.
        foreach (var ((name, sheetId), range) in workbook.ScopedNamedRanges)
        {
            workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            var scopeLabel = workbook.GetSheet(sheetId)?.Name ?? metadata.Scope;
            var comment = metadata.Comment ?? "";
            var refersTo = FormatRefersTo(range, workbook);
            // sheetId here is the row's real scope identity -- carry it through even though
            // scopeLabel (the sheet's display name) may read "Workbook" and collide with the global
            // sentinel text; identity, not the label, is what any command routing must key off.
            rows.Add(DefinedNameListProjector.CreateRow(name, scopeLabel, refersTo, refersTo, comment, scopeSheetId: sheetId));
        }

        // Sheet-scoped named formulas (Excel "localSheetId") are stored separately from the workbook-global
        // NamedFormulas dictionary and must also be listed, or they're invisible and unreachable through the
        // Name Manager's Edit/Delete actions — the same requirement already applied to scoped named ranges.
        // Same R123 comment-metadata read as the workbook-global formula loop above.
        foreach (var ((name, sheetId), formulaText) in workbook.ScopedNamedFormulas)
        {
            workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var scopedFormulaMetadata);
            var scopeLabel = workbook.GetSheet(sheetId)?.Name ?? scopedFormulaMetadata.Scope;
            var refersTo = "=" + formulaText;
            // Same identity requirement as the scoped-ranges loop above.
            rows.Add(DefinedNameListProjector.CreateRow(name, scopeLabel, refersTo, refersTo, scopedFormulaMetadata.Comment, scopeSheetId: sheetId));
        }

        return rows;
    }

    /// <summary>
    /// Projects then filters/sorts the workbook's named-range rows for the Name Manager list — the typical
    /// Name Manager view (filter dropdown + name sort).
    /// </summary>
    public static IReadOnlyList<DefinedNameRow> ProjectRows(
        Workbook workbook,
        DefinedNameFilter filter = DefinedNameFilter.All,
        DefinedNameSortColumn sortColumn = DefinedNameSortColumn.Name,
        bool descending = false) =>
        DefinedNameListProjector.Project(BuildRows(workbook), filter, sortColumn, descending);

    /// <summary>
    /// Formats a named range as sheet-qualified A1 refers-to text (<c>Sheet1!A1:B2</c>), matching the desktop
    /// hosts. A single-cell range collapses to one address.
    /// </summary>
    public static string FormatRefersTo(GridRange range, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheetName = workbook.GetSheet(range.Start.Sheet)?.Name ?? "Sheet1";
        var start = range.Start.ToA1();
        var end = range.End.ToA1();
        var body = string.Equals(start, end, StringComparison.Ordinal) ? start : $"{start}:{end}";
        return $"{sheetName}!{body}";
    }

    /// <summary>
    /// Maps a validated Define-Name <paramref name="draft"/> plus the range its refers-to resolved to onto a
    /// <see cref="DefineNamedRangeCommand"/>. The scope label and comment are carried into the named-range
    /// metadata so a worksheet-scoped name round-trips, and <see cref="DefinedNameScope.Sheet"/> is passed
    /// through as the command's scope-sheet id so a sheet-scoped choice actually defines a sheet-scoped name
    /// (Excel "localSheetId") rather than a workbook-global one, matching the WPF host's NamedRangeDialog.
    /// Callers validate the draft (name + refers-to) and resolve the refers-to to a <see cref="GridRange"/>
    /// before calling this.
    /// </summary>
    public static DefineNamedRangeCommand BuildDefineCommand(DefinedNameDraft draft, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var metadata = new NamedRangeMetadata(draft.Scope.Label, draft.Comment ?? "");
        return new DefineNamedRangeCommand(draft.Name, range, metadata, draft.Scope.Sheet);
    }

    /// <summary>
    /// Maps a validated Define-Name <paramref name="draft"/> whose refers-to text did not resolve to a
    /// range/cell/existing-name reference, but did parse as a formula expression, onto a
    /// <see cref="DefineNamedFormulaCommand"/> — a named formula/constant (e.g. <c>=1.05</c> or
    /// <c>=SUM(Sheet1!A:A)</c>). The leading '=' is stripped since <see cref="Workbook.NamedFormulas"/> stores
    /// the raw formula text. <see cref="DefinedNameScope.Sheet"/> is passed through as the command's
    /// scope-sheet id so a sheet-scoped choice defines a sheet-scoped named formula rather than a
    /// workbook-global one, matching <see cref="BuildDefineCommand"/>. The scope label and comment are
    /// carried into the named-range metadata exactly like <see cref="BuildDefineCommand"/> does (R123:
    /// the Define Name editor's Comment field works identically for a range-backed or formula/constant-
    /// backed name, matching Excel's Name Manager). Callers try <see cref="BuildDefineCommand"/> first and
    /// fall back to this only when the refers-to text is not a resolvable range.
    /// </summary>
    public static DefineNamedFormulaCommand BuildDefineFormulaCommand(DefinedNameDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var text = draft.RefersTo.Trim();
        if (text.StartsWith('='))
            text = text[1..].Trim();
        var metadata = new NamedRangeMetadata(draft.Scope.Label, draft.Comment ?? "");
        return new DefineNamedFormulaCommand(draft.Name, text, draft.Scope.Sheet, metadata);
    }

    /// <summary>
    /// Maps a name deletion onto a <see cref="RemoveNamedRangeCommand"/>. <paramref name="scopeSheetId"/> must
    /// be passed for a sheet-scoped name (resolve it via <see cref="ResolveScopeSheetId"/>) so the command
    /// probes <see cref="Workbook.ScopedNamedRanges"/>/<see cref="Workbook.ScopedNamedFormulas"/> instead of
    /// the workbook-global dictionaries — otherwise a sheet-scoped name can never be found for deletion.
    /// </summary>
    public static RemoveNamedRangeCommand BuildDeleteCommand(string name, SheetId? scopeSheetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new RemoveNamedRangeCommand(name, scopeSheetId);
    }

    /// <summary>
    /// Resolves a bare Name Manager scope label (<see cref="DefinedNameScope.WorkbookLabel"/> or a sheet's
    /// display name) to a sheet-scope <see cref="SheetId"/> by looking the label up as a sheet name, or null
    /// for the workbook scope. LOSSY: nothing reserves "Workbook" as a sheet name, so a worksheet can legally
    /// be named exactly "Workbook" -- for a name actually scoped to that sheet, this method cannot tell that
    /// case apart from the true workbook-global scope and returns null either way. Do not use this to resolve
    /// a <see cref="DefinedNameRow"/>'s scope for command routing (delete/define) -- read the row's own
    /// <see cref="DefinedNameRow.ScopeSheetId"/> instead, which was captured from the real identity when the
    /// row was built and carries no such ambiguity. This helper only remains for callers that truly have
    /// nothing but a label in hand (e.g. resolving a freshly-typed sheet name against the workbook).
    /// </summary>
    public static SheetId? ResolveScopeSheetId(Workbook workbook, string? scopeLabel)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return DefinedNameScope.IsWorkbookLabel(scopeLabel) ? null : workbook.GetSheet(scopeLabel!)?.Id;
    }

    /// <summary>
    /// Maps each <see cref="PlannedDefinedName"/> produced by <see cref="CreateNamesFromSelectionPlanner"/>
    /// onto a workbook-scoped <see cref="DefineNamedRangeCommand"/> (the planner's names are sanitized and
    /// de-duplicated, so each is committed as-is).
    /// </summary>
    public static IReadOnlyList<DefineNamedRangeCommand> BuildCreateCommands(
        IEnumerable<PlannedDefinedName> plannedNames)
    {
        ArgumentNullException.ThrowIfNull(plannedNames);

        var commands = new List<DefineNamedRangeCommand>();
        foreach (var planned in plannedNames)
        {
            commands.Add(new DefineNamedRangeCommand(
                planned.Name,
                planned.Range,
                NamedRangeMetadata.WorkbookScope));
        }

        return commands;
    }
}
