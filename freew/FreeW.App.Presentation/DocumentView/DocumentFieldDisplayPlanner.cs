using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public readonly record struct DocumentFieldDisplayContext(
    DateTime EvaluatedAt,
    string? FileName = null,
    string? PageNumberText = null,
    int? PageCount = null);

/// <summary>
/// Resolves the live display text for simple document fields. Renderers provide pagination and file
/// context; the shared planner owns fallback, metadata, and culture policy.
/// </summary>
public static class DocumentFieldDisplayPlanner
{
    public static string Resolve(
        RunFieldKind kind,
        string fallback,
        TextDocument document,
        DocumentFieldDisplayContext context)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(document);
        var liveValue = kind switch
        {
            RunFieldKind.Date or RunFieldKind.Time =>
                FormatTemporalValue(kind, context.EvaluatedAt),
            RunFieldKind.Author => document.Properties.Author,
            RunFieldKind.FileName => context.FileName,
            RunFieldKind.Title => document.Properties.Title,
            RunFieldKind.Subject => document.Properties.Subject,
            RunFieldKind.Keywords => document.Properties.Keywords,
            RunFieldKind.DocComments => document.Properties.Comments,
            RunFieldKind.PageNumber => string.IsNullOrEmpty(context.PageNumberText)
                ? ResolveFirstPageNumberText(document)
                : context.PageNumberText,
            RunFieldKind.NumPages when context.PageCount is > 0 =>
                context.PageCount.Value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        return string.IsNullOrEmpty(liveValue) ? fallback : liveValue;
    }

    public static string FormatTemporalValue(RunFieldKind kind, DateTime value) => kind switch
    {
        RunFieldKind.Date => value.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
        RunFieldKind.Time => value.ToString("h:mm tt", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Only DATE and TIME fields are temporal."),
    };

    public static string ResolveFirstPageNumberText(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var firstValue = Math.Max(1, document.Page.PageNumberStartAt ?? 1);
        return PageNumberFormatDialogPlanner.FormatPageNumber(
            firstValue,
            document.Page.PageNumberFormat);
    }

    /// <summary>
    /// The <c>w:fldSimple/@w:instr</c> keyword a <see cref="RunFieldKind"/> serialises as (matching
    /// <see cref="RunFieldKind"/>'s own doc comment, e.g. <see cref="RunFieldKind.PageNumber"/> is
    /// "PAGE", <see cref="RunFieldKind.Date"/> is "DATE"). <see cref="RunFieldKind.None"/> has no
    /// keyword and returns "".
    /// </summary>
    public static string FieldCodeKeyword(RunFieldKind kind) => kind switch
    {
        RunFieldKind.PageNumber => "PAGE",
        RunFieldKind.Date => "DATE",
        RunFieldKind.Time => "TIME",
        RunFieldKind.FileName => "FILENAME",
        RunFieldKind.Author => "AUTHOR",
        RunFieldKind.NumPages => "NUMPAGES",
        RunFieldKind.Title => "TITLE",
        RunFieldKind.Subject => "SUBJECT",
        RunFieldKind.Keywords => "KEYWORDS",
        RunFieldKind.DocComments => "COMMENTS",
        _ => string.Empty,
    };

    /// <summary>
    /// The field-code text Shift+F9 / Alt+F9 shows in place of the result for a <see cref="RunFieldKind"/>
    /// field (e.g. <c>{ PAGE }</c>), matching the brace format
    /// <see cref="ComplexFieldDisplayPlanner.Build"/> uses for the <see cref="ComplexField"/> form: "{" +
    /// the instruction + " }". Returns "" for <see cref="RunFieldKind.None"/> (nothing to show a code for).
    /// </summary>
    public static string ResolveCode(RunFieldKind kind)
    {
        var keyword = FieldCodeKeyword(kind);
        return keyword.Length == 0 ? string.Empty : "{ " + keyword + " }";
    }
}
