using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Identifies why a manual-translation request could not be planned, so the UI shell can surface a
/// localized message without embedding validation logic.
/// </summary>
public enum TranslateDialogValidationError
{
    None,
    EmptyTranslation,
    MissingTargetReference,
    InvalidTargetReference,
    SameSourceAndTarget
}

/// <summary>
/// A language choice offered in the Translate dialog. <see cref="Code"/> is a stable BCP-47-ish tag
/// used for status text; <see cref="DisplayKey"/> is a localization key resolved by the UI shell.
/// </summary>
public sealed record TranslateLanguageOption(string Code, string DisplayKey);

/// <summary>
/// A planned manual translation: the resolved target range and the text to write into each cell of
/// that range, top-left to bottom-right, row by row. The shell commits these writes through the
/// normal cell-edit path so undo/redo and protection rules apply.
/// </summary>
public sealed record TranslateWritePlan(
    GridRange TargetRange,
    IReadOnlyList<TranslateCellWrite> Writes,
    string FromLanguageCode,
    string ToLanguageCode);

/// <summary>A single cell write produced by the translate planner.</summary>
public sealed record TranslateCellWrite(CellAddress Address, string Text);

/// <summary>
/// Portable planner for the Review ▸ Translate manual-translation helper. There is no offline
/// translation engine in this build, so this is an honest manual helper: it surfaces the selected
/// source text and the user types the translation, which the planner validates and routes to a
/// chosen target cell or range. All option/validation logic lives here so the macOS shell (and any
/// other UI) inherits it unchanged.
/// </summary>
public static class TranslateDialogPlanner
{
    /// <summary>
    /// Languages offered in the From/To pickers. Kept deliberately short and culture-neutral; the
    /// shell resolves <see cref="TranslateLanguageOption.DisplayKey"/> through its localization table.
    /// "auto" is offered only as a From option (detect source).
    /// </summary>
    public static IReadOnlyList<TranslateLanguageOption> Languages { get; } =
    [
        new("auto", "Translate_LangAuto"),
        new("en", "Translate_LangEnglish"),
        new("fr", "Translate_LangFrench"),
        new("es", "Translate_LangSpanish"),
        new("de", "Translate_LangGerman"),
        new("it", "Translate_LangItalian"),
        new("pt", "Translate_LangPortuguese"),
        new("nl", "Translate_LangDutch"),
        new("zh", "Translate_LangChinese"),
        new("ja", "Translate_LangJapanese"),
        new("ar", "Translate_LangArabic"),
        new("ru", "Translate_LangRussian"),
    ];

    /// <summary>The default From language code (detect source).</summary>
    public const string DefaultFromCode = "auto";

    /// <summary>The default To language code.</summary>
    public const string DefaultToCode = "en";

    /// <summary>
    /// Builds the default A1 target reference shown when the dialog opens: the cell immediately to
    /// the right of the source cell (a common "translation goes alongside" placement). Falls back to
    /// the source cell itself when the source is on the last column.
    /// </summary>
    public static string SuggestTargetReference(CellAddress source)
    {
        var col = source.Col + 1;
        if (col > CellAddress.MaxCol)
            col = source.Col;
        return new CellAddress(source.Sheet, source.Row, col).ToA1();
    }

    /// <summary>
    /// Validates a manual-translation request and, on success, produces a <see cref="TranslateWritePlan"/>.
    /// The translation text is split on newlines so a multi-line entry fills successive rows of the
    /// target range; a single-cell target receives the whole text (newlines preserved). The plan never
    /// writes outside the resolved target range.
    /// </summary>
    public static bool TryPlan(
        SheetId sheetId,
        CellAddress source,
        string? translation,
        string? targetReference,
        string fromLanguageCode,
        string toLanguageCode,
        out TranslateWritePlan plan,
        out TranslateDialogValidationError error)
    {
        plan = null!;

        if (string.IsNullOrWhiteSpace(translation))
        {
            error = TranslateDialogValidationError.EmptyTranslation;
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetReference))
        {
            error = TranslateDialogValidationError.MissingTargetReference;
            return false;
        }

        GridRange target;
        try
        {
            target = GridRange.ParseCellOrRange(targetReference.Trim(), sheetId);
        }
        catch (FormatException)
        {
            error = TranslateDialogValidationError.InvalidTargetReference;
            return false;
        }
        catch (ArgumentException)
        {
            error = TranslateDialogValidationError.InvalidTargetReference;
            return false;
        }

        if (target.RowCount == 1 && target.ColCount == 1 && target.Start.Equals(source))
        {
            error = TranslateDialogValidationError.SameSourceAndTarget;
            return false;
        }

        var writes = BuildWrites(target, translation);
        plan = new TranslateWritePlan(
            target,
            writes,
            NormalizeLanguageCode(fromLanguageCode, DefaultFromCode),
            NormalizeLanguageCode(toLanguageCode, DefaultToCode));
        error = TranslateDialogValidationError.None;
        return true;
    }

    public static IWorkbookCommand BuildCommand(TranslateWritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new EditCellsCommand(
            plan.TargetRange.Start.Sheet,
            plan.Writes
                .Select(write => (
                    write.Address,
                    Cell.FromValue(new TextValue(write.Text))))
                .ToList());
    }

    private static IReadOnlyList<TranslateCellWrite> BuildWrites(GridRange target, string translation)
    {
        // Single-cell target: write the whole translation (including any newlines) into that cell.
        if (target.RowCount == 1 && target.ColCount == 1)
            return [new TranslateCellWrite(target.Start, translation)];

        // Multi-cell target: map each non-final newline-delimited line to a successive cell, row by
        // row, capped at the range's capacity. Any overflow lines are appended to the last cell so no
        // text is silently lost and nothing is written outside the chosen range.
        var lines = translation.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cells = target.AllCells().ToList();
        var writes = new List<TranslateCellWrite>(cells.Count);
        var capacity = cells.Count;

        for (var i = 0; i < capacity && i < lines.Length; i++)
        {
            var text = i == capacity - 1 && lines.Length > capacity
                ? string.Join("\n", lines[i..])
                : lines[i];
            writes.Add(new TranslateCellWrite(cells[i], text));
        }

        return writes;
    }

    private static string NormalizeLanguageCode(string? code, string fallback) =>
        string.IsNullOrWhiteSpace(code) ? fallback : code.Trim();
}
