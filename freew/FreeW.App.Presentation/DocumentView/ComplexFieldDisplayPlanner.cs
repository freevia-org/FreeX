using System.Globalization;
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

    public static bool IsPageSectionField(string keyword) =>
        keyword is "SECTION" or "SECTIONPAGES";

    public static string ResolvePageSectionField(
        ComplexField field,
        string fallback,
        int sectionOrdinal,
        int sectionPageCount)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(fallback);

        var value = field.Keyword switch
        {
            "SECTION" when sectionOrdinal > 0 => sectionOrdinal,
            "SECTIONPAGES" when sectionPageCount > 0 => sectionPageCount,
            _ => 0,
        };
        return value > 0
            ? ComplexFieldEngine.FormatIntegerFieldValue(value, field.Instruction)
            : fallback;
    }

    public static string FormatInvariantTemporalValue(RunFieldKind kind, DateTime value) => kind switch
    {
        RunFieldKind.Date => value.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        RunFieldKind.Time => value.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only DATE and TIME fields are temporal."),
    };

    /// <summary>
    /// Resolves the live value shared by simple and complex fields. Pagination remains a renderer input;
    /// document properties, file name, time and fallback semantics are toolkit-neutral.
    /// </summary>
    public static string ResolveLiveValue(
        RunFieldKind kind,
        string fallback,
        TextDocument document,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(culture);

        return kind switch
        {
            RunFieldKind.Date => now.ToString("d", culture),
            RunFieldKind.Time => now.ToString("t", culture),
            RunFieldKind.Author => PreferLiveValue(document.Properties.Author, fallback),
            RunFieldKind.FileName => PreferLiveValue(fileName, fallback),
            RunFieldKind.Title => PreferLiveValue(document.Properties.Title, fallback),
            RunFieldKind.Subject => PreferLiveValue(document.Properties.Subject, fallback),
            RunFieldKind.Keywords => PreferLiveValue(document.Properties.Keywords, fallback),
            RunFieldKind.DocComments => PreferLiveValue(document.Properties.Comments, fallback),
            RunFieldKind.PageNumber => PreferLiveValue(pageNumberText, fallback),
            RunFieldKind.NumPages when pageCount is > 0 => pageCount.Value.ToString(CultureInfo.InvariantCulture),
            _ => fallback,
        };
    }

    public static string ResolveComplexFieldValue(
        Run run,
        TextDocument document,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        int sectionOrdinal = 0,
        int sectionPageCount = 0,
        string? cachedResult = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(document);
        var field = run.ComplexField ?? throw new ArgumentException("Run does not contain a complex field.", nameof(run));
        var fallback = ResolveLiveValue(
            ResolveLiveKind(field.Keyword),
            cachedResult ?? run.Text,
            document,
            fileName,
            now,
            culture,
            pageNumberText,
            pageCount);
        fallback = ResolvePageSectionField(field, fallback, sectionOrdinal, sectionPageCount);
        return ApplyTemporalPicture(
            field,
            now,
            (run.Formatting ?? document.DefaultRun).LanguageTag,
            culture,
            fallback);
    }

    private static string PreferLiveValue(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    public static string ApplyTemporalPicture(
        ComplexField field,
        DateTime value,
        string? languageTag,
        CultureInfo fallbackCulture,
        string fallback)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(fallbackCulture);
        ArgumentNullException.ThrowIfNull(fallback);

        if (field.Keyword is not ("DATE" or "TIME"))
            return fallback;

        var culture = ResolveCulture(languageTag, fallbackCulture);
        return WordFieldDateTimeFormatter.TryFormat(value, field.Instruction, culture, out var formatted)
            ? formatted
            : fallback;
    }

    private static CultureInfo ResolveCulture(string? languageTag, CultureInfo fallback)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageTag);
            }
            catch (CultureNotFoundException)
            {
                // Imported language tags can be malformed; retain the host's normal field culture.
            }
        }

        return fallback;
    }

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
