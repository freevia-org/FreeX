using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Shared display planning for generic Word complex fields. The hosts own live value resolution and
/// drawing, while this planner keeps the visible code/result shape and generated-region rules identical.
/// </summary>
public sealed record ComplexFieldDisplayPlan(string Text, bool IsFieldCode, bool SuppressedResult);

public static class ComplexFieldDisplayPlanner
{
    public const string FieldCodeColorHex = "#808080";

    public static RunFieldKind ResolveLiveKind(string keyword) => keyword switch
    {
        "PAGE" => RunFieldKind.PageNumber,
        "DATE" => RunFieldKind.Date,
        "TIME" => RunFieldKind.Time,
        "FILENAME" => RunFieldKind.FileName,
        "AUTHOR" => RunFieldKind.Author,
        "NUMPAGES" => RunFieldKind.NumPages,
        "TITLE" => RunFieldKind.Title,
        "SUBJECT" => RunFieldKind.Subject,
        "KEYWORDS" => RunFieldKind.Keywords,
        "COMMENTS" => RunFieldKind.DocComments,
        _ => RunFieldKind.None,
    };

    public static string FormatInvariantTemporalValue(RunFieldKind kind, DateTime value) => kind switch
    {
        RunFieldKind.Date => value.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        RunFieldKind.Time => value.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only DATE and TIME fields are temporal."),
    };

    public static ComplexFieldDisplayPlan Build(
        ComplexField field,
        string resolvedResult,
        TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(document);

        if (field.ShowCode)
            return new ComplexFieldDisplayPlan(
                "{" + field.Instruction.TrimEnd() + " }",
                IsFieldCode: true,
                SuppressedResult: false);

        return new ComplexFieldDisplayPlan(
            resolvedResult,
            IsFieldCode: false,
            SuppressedResult: false);
    }
}
