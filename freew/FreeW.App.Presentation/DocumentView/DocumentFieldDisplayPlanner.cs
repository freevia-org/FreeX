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
}
