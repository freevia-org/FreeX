using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

public enum DefinedNameUseInFormulaMode
{
    DirectMenu,
    PasteNamesDialog,
}

/// <summary>
/// Intentional renderer differences for the defined-name surface. The profile contains policy only;
/// native controls, dialogs, focus, and command execution remain renderer-owned.
/// </summary>
public sealed record DefinedNameUiProfile(
    DefinedNameUseInFormulaMode UseInFormulaMode,
    bool IncludeAllDefinitionsInUseInFormula,
    string PasteNamesNoNamesResourceKey,
    string PasteNamesNotEnoughColumnsResourceKey,
    string PasteNamesNotEnoughRowsResourceKey,
    bool ClearManagerRefersToOnDeselection,
    NamedRangeMetadata? NameBoxMetadata)
{
    public static DefinedNameUiProfile Wpf { get; } = new(
        DefinedNameUseInFormulaMode.DirectMenu,
        IncludeAllDefinitionsInUseInFormula: false,
        PasteNamesNoNamesResourceKey: "PasteNames_NoNamesMessage",
        PasteNamesNotEnoughColumnsResourceKey: "PasteNames_NotEnoughColumnsMessage",
        PasteNamesNotEnoughRowsResourceKey: "PasteNames_NotEnoughRowsMessage",
        ClearManagerRefersToOnDeselection: false,
        NameBoxMetadata: null);

    public static DefinedNameUiProfile Avalonia { get; } = new(
        DefinedNameUseInFormulaMode.PasteNamesDialog,
        IncludeAllDefinitionsInUseInFormula: true,
        PasteNamesNoNamesResourceKey: "PasteNames_NoNames",
        PasteNamesNotEnoughColumnsResourceKey: "PasteNames_NotEnoughColumns",
        PasteNamesNotEnoughRowsResourceKey: "PasteNames_NotEnoughRows",
        ClearManagerRefersToOnDeselection: true,
        NameBoxMetadata: NamedRangeMetadata.WorkbookScope);
}

public sealed record DefinedNameFilterDescriptor(
    DefinedNameFilter Filter,
    string LabelResourceKey);

/// <summary>A scope combo item with display text and non-collidable scope identity.</summary>
public readonly record struct DefinedNameScopeOption(DefinedNameScope Scope)
{
    public DefinedNameScopeOption(string label, SheetId? sheetId)
        : this(sheetId is { } id
            ? DefinedNameScope.ForSheet(id, label)
            : DefinedNameScope.Workbook)
    {
    }

    public string Label => Scope.Label;

    public SheetId? SheetId => Scope.SheetId;

    public override string ToString() => Label;

    public static implicit operator DefinedNameScopeOption(string label) => new(label, null);
}

public sealed record DefinedNameManagerSelectionPlan(
    DefinedNameRow? SelectedRow,
    bool CanEdit,
    bool CanDelete,
    bool CanSelectRefersTo,
    string RefersToText,
    bool ShouldUpdateRefersTo)
{
    public bool HasSelection => SelectedRow is not null;
}

public sealed record PasteNamesSelectionPlan(
    PasteNamesItem? SelectedItem,
    bool CanInsertName,
    bool CanPasteList);

public sealed record DefinedNameUseInFormulaPlan(
    DefinedNameUseInFormulaMode Mode,
    IReadOnlyList<PasteNamesItem> Items)
{
    public bool HasItems => Items.Count > 0;
}

public enum NameBoxDefinitionRejection
{
    None,
    BlankSelection,
    InvalidIdentifier,
    ExistingTable,
    ExistingFormula,
}

public sealed record NameBoxDefinitionPlan(
    string Name,
    NameBoxDefinitionRejection Rejection,
    DefineNamedRangeCommand? Command)
{
    public bool CanDefine => Rejection == NameBoxDefinitionRejection.None && Command is not null;
}

public enum NamedRangeSelectionTarget
{
    SelectedNameRefersTo,
    DefinitionRefersTo,
}

public sealed record NamedRangeSelectionRequest(
    NamedRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

/// <summary>Workbook identifiers that participate in defined-name validation and creation policy.</summary>
public static class DefinedNameIdentifierCatalog
{
    public static IReadOnlyList<string> GetTableNames(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return workbook.Sheets
            .SelectMany(sheet => sheet.StructuredTables)
            .SelectMany(table => new[] { table.Name, table.DisplayName })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool ContainsTableName(Workbook workbook, string name) =>
        GetTableNames(workbook).Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool ContainsFormulaName(Workbook workbook, SheetId activeSheetId, string name)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return workbook.NamedFormulas.ContainsKey(name) ||
               workbook.ScopedNamedFormulas.ContainsKey((name, activeSheetId));
    }
}

/// <summary>UI-free descriptors and interaction policy shared by the WPF and Avalonia name surfaces.</summary>
public static class DefinedNameUiPolicy
{
    public static IReadOnlyList<DefinedNameFilterDescriptor> Filters { get; } =
    [
        new(DefinedNameFilter.All, "NamedRange_AllNames"),
        new(DefinedNameFilter.Workbook, "NamedRange_NamesScopedToWorkbook"),
        new(DefinedNameFilter.Worksheet, "NamedRange_NamesScopedToWorksheet"),
        new(DefinedNameFilter.Errors, "NamedRange_NamesWithErrors"),
        new(DefinedNameFilter.NoErrors, "NamedRange_NamesWithoutErrors"),
    ];

    public static DefinedNameFilter ResolveFilter(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Filters.Count
            ? Filters[selectedIndex].Filter
            : DefinedNameFilter.All;

    public static IReadOnlyList<DefinedNameScopeOption> BuildScopeOptions(
        IEnumerable<DefinedNameScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var options = scopes.Select(scope => new DefinedNameScopeOption(scope)).ToArray();
        return options.Length > 0 ? options : [new DefinedNameScopeOption(DefinedNameScope.Workbook)];
    }

    public static DefinedNameScopeOption ResolveScopeOption(
        IReadOnlyList<DefinedNameScopeOption> options,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(options);
        return selectedIndex >= 0 && selectedIndex < options.Count
            ? options[selectedIndex]
            : options.FirstOrDefault(new DefinedNameScopeOption(DefinedNameScope.Workbook));
    }

    public static DefinedNameScopeOption FindScopeOption(
        IReadOnlyList<DefinedNameScopeOption> options,
        string? scopeLabel,
        SheetId? scopeSheetId)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var option in options)
        {
            if (Nullable.Equals(option.SheetId, scopeSheetId))
                return option;
        }

        foreach (var option in options)
        {
            if (string.Equals(option.Label, scopeLabel, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        return options.FirstOrDefault(new DefinedNameScopeOption(DefinedNameScope.Workbook));
    }

    public static DefinedNameDraft CreateDraft(
        string? name,
        DefinedNameScope scope,
        string? refersTo,
        string? comment) =>
        new(
            name?.Trim() ?? string.Empty,
            scope,
            refersTo?.Trim() ?? string.Empty,
            comment?.Trim() ?? string.Empty);

    public static DefinedNameDraft CreateDraft(
        string? name,
        IReadOnlyList<DefinedNameScopeOption> options,
        int selectedIndex,
        string? refersTo,
        string? comment) =>
        CreateDraft(name, ResolveScopeOption(options, selectedIndex).Scope, refersTo, comment);

    public static DefinedNameManagerSelectionPlan PlanManagerSelection(
        DefinedNameRow? selectedRow,
        DefinedNameUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var hasSelection = selectedRow is not null;
        return new(
            selectedRow,
            CanEdit: hasSelection,
            CanDelete: hasSelection,
            CanSelectRefersTo: hasSelection,
            RefersToText: selectedRow?.RefersTo ?? string.Empty,
            ShouldUpdateRefersTo: hasSelection || profile.ClearManagerRefersToOnDeselection);
    }

    public static DefinedNameManagerSelectionPlan PlanManagerSelection(
        IReadOnlyList<DefinedNameRow> rows,
        int selectedIndex,
        DefinedNameUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return PlanManagerSelection(
            selectedIndex >= 0 && selectedIndex < rows.Count ? rows[selectedIndex] : null,
            profile);
    }

    public static DefinedNameRow? FindRow(
        IEnumerable<DefinedNameRow> rows,
        string name,
        DefinedNameScope scope)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.FirstOrDefault(row =>
            string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
            row.Scope.HasSameIdentity(scope));
    }

    public static PasteNamesSelectionPlan PlanPasteNamesSelection(
        IReadOnlyList<PasteNamesItem> items,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        var selected = selectedIndex >= 0 && selectedIndex < items.Count
            ? items[selectedIndex]
            : null;
        return new(selected, CanInsertName: selected is not null, CanPasteList: items.Count > 0);
    }

    public static DefinedNameUseInFormulaPlan PlanUseInFormula(
        Workbook workbook,
        Func<GridRange, string> formatRange,
        DefinedNameUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(formatRange);
        ArgumentNullException.ThrowIfNull(profile);

        IReadOnlyList<PasteNamesItem> items = profile.IncludeAllDefinitionsInUseInFormula
            ? PasteNamesPlanner.BuildItems(workbook, formatRange)
            : workbook.NamedRanges
                .Select(entry => new PasteNamesItem(entry.Key, string.Empty))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        return new(profile.UseInFormulaMode, items);
    }

    public static string FormatNameManagerRow(DefinedNameRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return $"{row.Name}    [{row.ScopeLabel}]    {row.RefersTo}    {row.Value}";
    }

    public static string FormatPasteNamesRow(PasteNamesItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return $"{item.Name}    {item.RefersTo}";
    }

    public static string GetPasteNamesListErrorResourceKey(
        PasteNamesListError error,
        DefinedNameUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return error switch
        {
            PasteNamesListError.NotEnoughColumns => profile.PasteNamesNotEnoughColumnsResourceKey,
            PasteNamesListError.NotEnoughRows => profile.PasteNamesNotEnoughRowsResourceKey,
            _ => profile.PasteNamesNoNamesResourceKey,
        };
    }

    public static NameBoxDefinitionPlan PlanNameBoxDefinition(
        Workbook workbook,
        SheetId activeSheetId,
        GridRange? selection,
        string? text,
        DefinedNameUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(profile);

        var name = text?.Trim() ?? string.Empty;
        if (selection is null)
            return new(name, NameBoxDefinitionRejection.BlankSelection, null);

        if (workbook.ValidateNamedRangeName(name) is not null)
            return new(name, NameBoxDefinitionRejection.InvalidIdentifier, null);

        if (DefinedNameIdentifierCatalog.ContainsTableName(workbook, name))
            return new(name, NameBoxDefinitionRejection.ExistingTable, null);

        if (DefinedNameIdentifierCatalog.ContainsFormulaName(workbook, activeSheetId, name))
            return new(name, NameBoxDefinitionRejection.ExistingFormula, null);

        return new(
            name,
            NameBoxDefinitionRejection.None,
            new DefineNamedRangeCommand(name, selection.Value, profile.NameBoxMetadata));
    }

    public static NamedRangeSelectionRequest CreateRangeSelectionRequest(
        NamedRangeSelectionTarget target,
        string? currentText) =>
        new(target, currentText?.Trim() ?? string.Empty, CollapseDialog: true);
}
