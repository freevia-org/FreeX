using System.Globalization;
using System.Text;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record PageNumberFormatChoice(string Label, PageNumberFormat Format);

public sealed record PageNumberFormatDialogState(
    int FormatIndex,
    bool ContinueFromPreviousSection,
    string StartAtText);

public sealed record PageNumberFormatDialogInput(
    int FormatIndex,
    bool ContinueFromPreviousSection,
    string? StartAtText);

public sealed record PageNumberFormatDialogResult(
    PageNumberFormat Format,
    int? StartAt);

public sealed record PageNumberDisplayPlan(
    int PageIndex,
    int SectionIndex,
    int LogicalPageNumber,
    string Text);

public static class PageNumberFormatDialogPlanner
{
    public const string Title = "Page Number Format";
    public const string NumberFormatLabel = "Number format:";
    public const string PageNumberingLabel = "Page numbering";
    public const string ContinueLabel = "Continue from previous section";
    public const string StartAtLabel = "Start at:";
    public const string ChapterNumberingDeferredLabel = "Chapter numbering is deferred for this pass.";
    public const string InvalidStartAtMessage = "Start at must be a whole number of 1 or greater.";
    public const string InvalidFormatMessage = "Choose a supported page number format.";

    public static readonly IReadOnlyList<PageNumberFormatChoice> FormatItems =
    [
        new("1, 2, 3, ...", PageNumberFormat.Decimal),
        new("i, ii, iii, ...", PageNumberFormat.LowerRoman),
        new("I, II, III, ...", PageNumberFormat.UpperRoman),
        new("a, b, c, ...", PageNumberFormat.LowerLetter),
        new("A, B, C, ...", PageNumberFormat.UpperLetter),
    ];

    public static PageNumberFormatDialogState BuildInitialState(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PageNumberFormatDialogState(
            FormatIndex: IndexOf(page.PageNumberFormat),
            ContinueFromPreviousSection: page.PageNumberStartAt is null,
            StartAtText: Math.Max(1, page.PageNumberStartAt ?? 1).ToString(CultureInfo.InvariantCulture));
    }

    public static bool TryBuildResult(
        PageNumberFormatDialogInput input,
        out PageNumberFormatDialogResult result,
        out string? errorMessage)
    {
        result = new PageNumberFormatDialogResult(PageNumberFormat.Decimal, StartAt: null);
        errorMessage = null;

        if (input.FormatIndex < 0 || input.FormatIndex >= FormatItems.Count)
        {
            errorMessage = InvalidFormatMessage;
            return false;
        }

        var format = FormatItems[input.FormatIndex].Format;
        if (input.ContinueFromPreviousSection)
        {
            result = new PageNumberFormatDialogResult(format, StartAt: null);
            return true;
        }

        if (!int.TryParse(input.StartAtText, NumberStyles.None, CultureInfo.InvariantCulture, out var startAt)
            || startAt < 1)
        {
            errorMessage = InvalidStartAtMessage;
            return false;
        }

        result = new PageNumberFormatDialogResult(format, startAt);
        return true;
    }

    public static void ApplyResult(PageSettings page, PageNumberFormatDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.PageNumberFormat = result.Format;
        page.PageNumberStartAt = result.StartAt is > 0 ? result.StartAt : null;
    }

    public static string BuildCommandValue(PageNumberFormat format, int? startAt) =>
        startAt is > 0
            ? $"{format}|start={startAt.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"{format}|continue";

    public static bool TryBuildResultFromCommandValue(
        string? value,
        out PageNumberFormatDialogResult result)
    {
        result = new PageNumberFormatDialogResult(PageNumberFormat.Decimal, StartAt: null);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Enum.TryParse<PageNumberFormat>(parts[0], ignoreCase: true, out var format))
            return false;

        if (string.Equals(parts[1], "continue", StringComparison.OrdinalIgnoreCase))
        {
            result = new PageNumberFormatDialogResult(format, StartAt: null);
            return true;
        }

        const string StartPrefix = "start=";
        if (!parts[1].StartsWith(StartPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rawStart = parts[1][StartPrefix.Length..];
        if (!int.TryParse(rawStart, NumberStyles.None, CultureInfo.InvariantCulture, out var startAt)
            || startAt < 1)
        {
            return false;
        }

        result = new PageNumberFormatDialogResult(format, startAt);
        return true;
    }

    public static IReadOnlyList<PageNumberDisplayPlan> BuildDisplayPlans(
        IReadOnlyList<HeaderFooterPageSectionPlan> pageSections)
    {
        ArgumentNullException.ThrowIfNull(pageSections);

        var result = new List<PageNumberDisplayPlan>(pageSections.Count);
        var currentSection = -1;
        var currentSectionStart = 1;
        var nextContinueValue = 1;

        for (var pageIndex = 0; pageIndex < pageSections.Count; pageIndex++)
        {
            var page = pageSections[pageIndex];
            if (page.SectionIndex != currentSection)
            {
                currentSection = page.SectionIndex;
                currentSectionStart = page.PageSettings.PageNumberStartAt ?? nextContinueValue;
                currentSectionStart = Math.Max(1, currentSectionStart);
            }

            var logical = currentSectionStart + Math.Max(1, page.SectionRelativePageNumber) - 1;
            nextContinueValue = logical + 1;
            result.Add(new PageNumberDisplayPlan(
                pageIndex,
                page.SectionIndex,
                logical,
                FormatPageNumber(logical, page.PageSettings.PageNumberFormat)));
        }

        return result;
    }

    public static string FormatPageNumber(int value, PageNumberFormat format)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        return format switch
        {
            PageNumberFormat.LowerRoman => ToRoman(value).ToLowerInvariant(),
            PageNumberFormat.UpperRoman => ToRoman(value),
            PageNumberFormat.LowerLetter => ToLetters(value, lower: true),
            PageNumberFormat.UpperLetter => ToLetters(value, lower: false),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static int IndexOf(PageNumberFormat format)
    {
        for (var i = 0; i < FormatItems.Count; i++)
            if (FormatItems[i].Format == format)
                return i;

        return 0;
    }

    private static string ToRoman(int value)
    {
        (int Value, string Token)[] symbols =
        [
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I"),
        ];

        var remaining = value;
        var sb = new StringBuilder();
        foreach (var (number, token) in symbols)
        {
            while (remaining >= number)
            {
                sb.Append(token);
                remaining -= number;
            }
        }

        return sb.ToString();
    }

    private static string ToLetters(int value, bool lower)
    {
        var n = value;
        var sb = new StringBuilder();
        var baseChar = lower ? 'a' : 'A';

        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)(baseChar + n % 26));
            n /= 26;
        }

        return sb.ToString();
    }
}
