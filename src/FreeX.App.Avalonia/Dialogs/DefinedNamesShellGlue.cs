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
    /// Projects the workbook's stored named ranges into Name Manager rows: each row carries the name, its
    /// scope label (from the stored metadata, defaulting to the workbook scope), the sheet-qualified refers-to
    /// text, and a value preview. The derived <see cref="DefinedNameKind"/> drives the kind/error filtering.
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
            rows.Add(DefinedNameListProjector.CreateRow(name, scopeLabel, refersTo, refersTo, comment));
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
    /// metadata so a worksheet-scoped name round-trips. Callers validate the draft (name + refers-to) and
    /// resolve the refers-to to a <see cref="GridRange"/> before calling this.
    /// </summary>
    public static DefineNamedRangeCommand BuildDefineCommand(DefinedNameDraft draft, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var metadata = new NamedRangeMetadata(draft.Scope.Label, draft.Comment ?? "");
        return new DefineNamedRangeCommand(draft.Name, range, metadata);
    }

    /// <summary>Maps a name deletion onto a <see cref="RemoveNamedRangeCommand"/>.</summary>
    public static RemoveNamedRangeCommand BuildDeleteCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new RemoveNamedRangeCommand(name);
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
