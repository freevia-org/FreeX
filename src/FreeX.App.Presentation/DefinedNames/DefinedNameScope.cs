using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>
/// The visibility of a defined name: either the whole workbook, or a single worksheet.
/// A worksheet-scoped name is identified by its sheet; the workbook scope carries no sheet.
/// </summary>
public readonly record struct DefinedNameScope
{
    private DefinedNameScope(bool isWorkbook, SheetId? sheet, string label)
    {
        IsWorkbook = isWorkbook;
        Sheet = sheet;
        Label = label;
    }

    /// <summary>The shared workbook scope label, matching the desktop hosts' storage.</summary>
    public const string WorkbookLabel = "Workbook";

    /// <summary>True when the name is visible across the whole workbook.</summary>
    public bool IsWorkbook { get; }

    /// <summary>The owning sheet for a worksheet-scoped name; null for the workbook scope.</summary>
    public SheetId? Sheet { get; }

    /// <summary>The stable scope identity used by named-range commands (null means workbook-global).</summary>
    public SheetId? SheetId => Sheet;

    /// <summary>
    /// The display/storage label for this scope: <see cref="WorkbookLabel"/> for the workbook scope, or the
    /// worksheet's name for a sheet scope.
    /// </summary>
    public string Label { get; }

    /// <summary>The workbook (global) scope.</summary>
    public static DefinedNameScope Workbook { get; } =
        new(isWorkbook: true, sheet: null, label: WorkbookLabel);

    /// <summary>Build a worksheet scope from a sheet id and its display name.</summary>
    public static DefinedNameScope ForSheet(SheetId sheet, string sheetName) =>
        new(isWorkbook: false, sheet: sheet, label: sheetName);

    /// <summary>
    /// Compares scope identity without consulting display text. This deliberately distinguishes the workbook
    /// sentinel from a worksheet whose display name is also <c>Workbook</c>.
    /// </summary>
    public bool HasSameIdentity(DefinedNameScope other) =>
        IsWorkbook == other.IsWorkbook && Nullable.Equals(Sheet, other.Sheet);

    /// <summary>True when <paramref name="label"/> denotes the workbook scope (case-insensitively).</summary>
    public static bool IsWorkbookLabel(string? label) =>
        string.IsNullOrWhiteSpace(label)
        || string.Equals(label, WorkbookLabel, StringComparison.OrdinalIgnoreCase);
}
