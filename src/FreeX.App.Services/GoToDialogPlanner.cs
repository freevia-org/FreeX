using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record GoToSpecialDialogText(
    string Blanks,
    string Constants,
    string Formulas,
    string Comments,
    string CurrentRegion,
    string RowDifferences,
    string ColumnDifferences,
    string LastCell,
    string ConditionalFormats,
    string Objects,
    string Precedents,
    string Dependents,
    string DataValidation,
    string VisibleCellsOnly)
{
    public static GoToSpecialDialogText Default { get; } = new(
        "Blanks",
        "Constants",
        "Formulas",
        "Comments",
        "Current region",
        "Row differences",
        "Column differences",
        "Last cell",
        "Conditional formats",
        "Objects",
        "Precedents",
        "Dependents",
        "Data validation",
        "Visible cells only");

    public string LabelFor(GoToSpecialKind kind) =>
        kind switch
        {
            GoToSpecialKind.Constants => Constants,
            GoToSpecialKind.Formulas => Formulas,
            GoToSpecialKind.Comments => Comments,
            GoToSpecialKind.CurrentRegion => CurrentRegion,
            GoToSpecialKind.RowDifferences => RowDifferences,
            GoToSpecialKind.ColumnDifferences => ColumnDifferences,
            GoToSpecialKind.LastCell => LastCell,
            GoToSpecialKind.ConditionalFormats => ConditionalFormats,
            GoToSpecialKind.Objects => Objects,
            GoToSpecialKind.Precedents => Precedents,
            GoToSpecialKind.Dependents => Dependents,
            GoToSpecialKind.DataValidation => DataValidation,
            GoToSpecialKind.VisibleCellsOnly => VisibleCellsOnly,
            _ => Blanks
        };
}

public static class GoToDialogPlanner
{
    public static IReadOnlyList<string> BuildReferenceChoices(
        string defaultAddress,
        IEnumerable<string>? recentReferences,
        IEnumerable<string>? definedNames) =>
        WorkbookReferenceNavigator.BuildReferenceChoices(defaultAddress, recentReferences, definedNames);

    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out GridRange range) =>
        WorkbookReferenceNavigator.TryParseReferenceRange(text, sheetId, definedNames, out range);

    /// <summary>
    /// Sheet-scope-aware overload for the Go To dialog, matching the precedence
    /// <see cref="Workbook.TryGetNamedRange(string,SheetId,out GridRange)"/> and
    /// <c>WorkbookSession.GoToReference</c> already use: a name scoped to the dialog's sheet beats a
    /// same-named workbook-global name, and <paramref name="resolveSheetId"/> lets a sheet-qualified
    /// reference (e.g. "Sheet2!A1" or "Sheet2!Rate") resolve against the qualifier's own sheet.
    /// </summary>
    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        Func<string, SheetId, GridRange?>? resolveScopedName,
        out GridRange range) =>
        WorkbookReferenceNavigator.TryParseReferenceRange(text, sheetId, resolveSheetId, definedNames, resolveScopedName, out range);

    /// <summary>
    /// Builds the defined-name set the Go To dialog should offer/resolve for <paramref name="sheetId"/>:
    /// every workbook-global name plus every name scoped to <paramref name="sheetId"/> itself (a
    /// scoped name shadows a same-named global one, matching <see cref="Workbook.TryGetNamedRange(string,SheetId,out GridRange)"/>'s
    /// precedence). Names scoped to a DIFFERENT sheet are excluded -- this mirrors Excel: the Name
    /// Box / Go To list only ever shows names visible from the active sheet (see also
    /// <c>NameBoxDropdownPlanner.Build</c>, which applies the identical <c>sheetId.Equals(activeSheetId)</c>
    /// filter for the formula bar's Name Box dropdown).
    /// </summary>
    public static IReadOnlyDictionary<string, GridRange> BuildDefinedNamesForSheet(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var combined = new Dictionary<string, GridRange>(workbook.NamedRanges, StringComparer.OrdinalIgnoreCase);
        foreach (var ((name, scopeSheetId), range) in workbook.ScopedNamedRanges)
        {
            if (scopeSheetId.Equals(sheetId))
                combined[name] = range;
        }

        return combined;
    }
}

public static class GoToSpecialDialogPlanner
{
    public const double Width = 430;
    public const double Height = 438;

    // These are the logical metrics shared by the WPF and Avalonia parity surfaces. Avalonia's
    // Fluent group template has different border insets and vertical chrome, so the explicit
    // Avalonia metrics keep its rendered bounds aligned with the WPF capture without changing
    // the shared dialog size or interaction contract.
    public const double ContentMargin = 12;
    public const double AvaloniaContentLeftMargin = 13;
    public const double AvaloniaContentTopMargin = 12;
    public const double AvaloniaContentRightMargin = 29;
    public const double ActionRowTopMargin = 10;
    public const double ActionRowRightMargin = 28;
    public const double ActionRowBottomMargin = 49;
    public const double ActionButtonHeight = 20;
    public const double AvaloniaChoiceGroupTopMargin = 3;
    public const double AvaloniaChoiceGroupBottomMargin = 13;
    public const double AvaloniaChoiceGroupHorizontalPadding = 8;
    public const double AvaloniaChoiceGroupBottomPadding = 9;
    public const double AvaloniaValueTypeGroupBottomPadding = 4;
    public const double AvaloniaValueTypeSpacing = 16;
    public const double AvaloniaChoiceButtonRightMargin = 12;
    public const double AvaloniaChoiceButtonBottomMargin = 1;

    public static IReadOnlyList<GoToSpecialKind> ChoiceOrder { get; } =
    [
        GoToSpecialKind.Blanks,
        GoToSpecialKind.Constants,
        GoToSpecialKind.Formulas,
        GoToSpecialKind.Comments,
        GoToSpecialKind.CurrentRegion,
        GoToSpecialKind.RowDifferences,
        GoToSpecialKind.ColumnDifferences,
        GoToSpecialKind.LastCell,
        GoToSpecialKind.ConditionalFormats,
        GoToSpecialKind.Objects,
        GoToSpecialKind.Precedents,
        GoToSpecialKind.Dependents,
        GoToSpecialKind.DataValidation,
        GoToSpecialKind.VisibleCellsOnly
    ];

    public static IReadOnlyList<GoToSpecialChoice> BuildChoices(GoToSpecialDialogText? text = null)
    {
        var resolvedText = text ?? GoToSpecialDialogText.Default;
        return ChoiceOrder
            .Select(kind => new GoToSpecialChoice(kind, resolvedText.LabelFor(kind)))
            .ToArray();
    }

    public static bool TryParseChoice(string text, out GoToSpecialKind kind, GoToSpecialDialogText? dialogText = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            kind = default;
            return false;
        }

        var normalized = Normalize(text);
        foreach (var choice in BuildChoices(dialogText))
        {
            if (string.Equals(Normalize(choice.Label), normalized, StringComparison.OrdinalIgnoreCase))
            {
                kind = choice.Kind;
                return true;
            }
        }

        kind = normalized switch
        {
            "constant" or "constants" => GoToSpecialKind.Constants,
            "formula" or "formulas" => GoToSpecialKind.Formulas,
            "comment" or "comments" => GoToSpecialKind.Comments,
            "validation" or "data validation" => GoToSpecialKind.DataValidation,
            "visible" or "visible cells" or "visible cells only" => GoToSpecialKind.VisibleCellsOnly,
            "row differences" or "row difference" => GoToSpecialKind.RowDifferences,
            "column differences" or "column difference" => GoToSpecialKind.ColumnDifferences,
            "current region" => GoToSpecialKind.CurrentRegion,
            "last cell" => GoToSpecialKind.LastCell,
            "conditional format" or "conditional formats" => GoToSpecialKind.ConditionalFormats,
            "object" or "objects" => GoToSpecialKind.Objects,
            "precedent" or "precedents" => GoToSpecialKind.Precedents,
            "dependent" or "dependents" => GoToSpecialKind.Dependents,
            "blank" or "blanks" => GoToSpecialKind.Blanks,
            _ => GoToSpecialKind.Blanks
        };

        return true;
    }

    public static bool UsesValueTypeOptions(GoToSpecialKind kind) =>
        kind is GoToSpecialKind.Constants or GoToSpecialKind.Formulas;

    public static GoToSpecialOptions BuildOptions(
        GoToSpecialKind kind,
        GoToSpecialValueTypes selectedValueTypes) =>
        UsesValueTypeOptions(kind)
            ? new GoToSpecialOptions(selectedValueTypes)
            : new GoToSpecialOptions();

    private static string Normalize(string text)
    {
        var withoutAccessKeys = text.Replace("_", "", StringComparison.Ordinal);
        return string.Join(' ', withoutAccessKeys.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
