using System.Globalization;
using System.Text;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record PageNumberFormatChoice(string Label, PageNumberFormat Format);
public sealed record PageNumberChapterStyleChoice(string Label, int Level);
public sealed record PageNumberChapterSeparatorChoice(
    string Label,
    PageNumberChapterSeparator Separator,
    string Text);

public sealed record PageNumberFormatDialogState(
    int FormatIndex,
    bool ContinueFromPreviousSection,
    string StartAtText,
    bool IncludeChapterNumber = false,
    int ChapterStyleIndex = 0,
    int ChapterSeparatorIndex = 0);

public sealed record PageNumberFormatDialogInput(
    int FormatIndex,
    bool ContinueFromPreviousSection,
    string? StartAtText,
    bool IncludeChapterNumber = false,
    int ChapterStyleIndex = 0,
    int ChapterSeparatorIndex = 0);

public sealed record PageNumberFormatDialogResult(
    PageNumberFormat Format,
    int? StartAt,
    int? ChapterStyleLevel = null,
    PageNumberChapterSeparator ChapterSeparator = PageNumberChapterSeparator.Hyphen);

public sealed record PageNumberDisplayPlan(
    int PageIndex,
    int SectionIndex,
    int LogicalPageNumber,
    string Text,
    string? ChapterNumber = null);

public sealed record PageNumberCitationReferencePlan(
    int BlockIndex,
    int RunIndex,
    Citation Citation,
    int PhysicalPageNumber,
    int SectionIndex,
    int SectionRelativePageNumber,
    int LogicalPageNumber,
    string DisplayText);

public static class PageNumberFormatDialogPlanner
{
    public const string Title = "Page Number Format";
    public const string NumberFormatLabel = "Number format:";
    public const string PageNumberingLabel = "Page numbering";
    public const string ContinueLabel = "Continue from previous section";
    public const string StartAtLabel = "Start at:";
    public const string IncludeChapterNumberLabel = "Include chapter number";
    public const string ChapterStartsWithStyleLabel = "Chapter starts with style:";
    public const string ChapterSeparatorLabel = "Use separator:";
    public const string InvalidStartAtMessage = "Start at must be a whole number of 1 or greater.";
    public const string InvalidFormatMessage = "Choose a supported page number format.";
    public const string InvalidChapterStyleMessage = "Choose a heading style for chapter numbering.";
    public const string InvalidChapterSeparatorMessage = "Choose a supported chapter separator.";

    public static readonly IReadOnlyList<PageNumberFormatChoice> FormatItems =
    [
        new("1, 2, 3, ...", PageNumberFormat.Decimal),
        new("i, ii, iii, ...", PageNumberFormat.LowerRoman),
        new("I, II, III, ...", PageNumberFormat.UpperRoman),
        new("a, b, c, ...", PageNumberFormat.LowerLetter),
        new("A, B, C, ...", PageNumberFormat.UpperLetter),
    ];

    public static readonly IReadOnlyList<PageNumberChapterStyleChoice> ChapterStyleItems =
        Enumerable.Range(1, 9)
            .Select(level => new PageNumberChapterStyleChoice(
                "Heading " + level.ToString(CultureInfo.InvariantCulture),
                level))
            .ToList();

    public static readonly IReadOnlyList<PageNumberChapterSeparatorChoice> ChapterSeparatorItems =
    [
        new("Hyphen", PageNumberChapterSeparator.Hyphen, "-"),
        new("Period", PageNumberChapterSeparator.Period, "."),
        new("Colon", PageNumberChapterSeparator.Colon, ":"),
        new("Em dash", PageNumberChapterSeparator.EmDash, "--"),
        new("En dash", PageNumberChapterSeparator.EnDash, "-")
    ];

    public static PageNumberFormatDialogState BuildInitialState(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PageNumberFormatDialogState(
            FormatIndex: IndexOf(page.PageNumberFormat),
            ContinueFromPreviousSection: page.PageNumberStartAt is null,
            StartAtText: Math.Max(1, page.PageNumberStartAt ?? 1).ToString(CultureInfo.InvariantCulture),
            IncludeChapterNumber: page.PageNumberChapterStyleLevel is >= 1 and <= 9,
            ChapterStyleIndex: ChapterStyleIndexOf(page.PageNumberChapterStyleLevel),
            ChapterSeparatorIndex: ChapterSeparatorIndexOf(page.PageNumberChapterSeparator));
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
        int? chapterStyleLevel = null;
        var chapterSeparator = PageNumberChapterSeparator.Hyphen;
        if (input.IncludeChapterNumber)
        {
            if (input.ChapterStyleIndex < 0 || input.ChapterStyleIndex >= ChapterStyleItems.Count)
            {
                errorMessage = InvalidChapterStyleMessage;
                return false;
            }

            if (input.ChapterSeparatorIndex < 0 || input.ChapterSeparatorIndex >= ChapterSeparatorItems.Count)
            {
                errorMessage = InvalidChapterSeparatorMessage;
                return false;
            }

            chapterStyleLevel = ChapterStyleItems[input.ChapterStyleIndex].Level;
            chapterSeparator = ChapterSeparatorItems[input.ChapterSeparatorIndex].Separator;
        }

        if (input.ContinueFromPreviousSection)
        {
            result = new PageNumberFormatDialogResult(format, StartAt: null, chapterStyleLevel, chapterSeparator);
            return true;
        }

        if (!int.TryParse(input.StartAtText, NumberStyles.None, CultureInfo.InvariantCulture, out var startAt)
            || startAt < 1)
        {
            errorMessage = InvalidStartAtMessage;
            return false;
        }

        result = new PageNumberFormatDialogResult(format, startAt, chapterStyleLevel, chapterSeparator);
        return true;
    }

    public static void ApplyResult(PageSettings page, PageNumberFormatDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.PageNumberFormat = result.Format;
        page.PageNumberStartAt = result.StartAt is > 0 ? result.StartAt : null;
        page.PageNumberChapterStyleLevel = result.ChapterStyleLevel is >= 1 and <= 9
            ? result.ChapterStyleLevel
            : null;
        page.PageNumberChapterSeparator = result.ChapterSeparator;
    }

    public static string BuildCommandValue(
        PageNumberFormat format,
        int? startAt,
        int? chapterStyleLevel = null,
        PageNumberChapterSeparator chapterSeparator = PageNumberChapterSeparator.Hyphen)
    {
        var value = startAt is > 0
            ? $"{format}|start={startAt.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"{format}|continue";
        return chapterStyleLevel is >= 1 and <= 9
            ? value + $"|chapter={chapterStyleLevel.Value.ToString(CultureInfo.InvariantCulture)},sep={chapterSeparator}"
            : value;
    }

    public static bool TryBuildResultFromCommandValue(
        string? value,
        out PageNumberFormatDialogResult result)
    {
        result = new PageNumberFormatDialogResult(PageNumberFormat.Decimal, StartAt: null);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3 || !Enum.TryParse<PageNumberFormat>(parts[0], ignoreCase: true, out var format))
            return false;

        var chapterStyleLevel = (int?)null;
        var chapterSeparator = PageNumberChapterSeparator.Hyphen;
        if (parts.Length == 3
            && !TryParseChapterCommandPart(parts[2], out chapterStyleLevel, out chapterSeparator))
        {
            return false;
        }

        if (string.Equals(parts[1], "continue", StringComparison.OrdinalIgnoreCase))
        {
            result = new PageNumberFormatDialogResult(format, StartAt: null, chapterStyleLevel, chapterSeparator);
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

        result = new PageNumberFormatDialogResult(format, startAt, chapterStyleLevel, chapterSeparator);
        return true;
    }

    private static bool TryParseChapterCommandPart(
        string part,
        out int? chapterStyleLevel,
        out PageNumberChapterSeparator chapterSeparator)
    {
        chapterStyleLevel = null;
        chapterSeparator = PageNumberChapterSeparator.Hyphen;
        const string ChapterPrefix = "chapter=";
        const string SeparatorPrefix = "sep=";
        if (!part.StartsWith(ChapterPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var pieces = part.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length is < 1 or > 2)
            return false;

        var rawLevel = pieces[0][ChapterPrefix.Length..];
        if (!int.TryParse(rawLevel, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            || level is < 1 or > 9)
            return false;

        if (pieces.Length == 2)
        {
            if (!pieces[1].StartsWith(SeparatorPrefix, StringComparison.OrdinalIgnoreCase)
                || !Enum.TryParse<PageNumberChapterSeparator>(
                    pieces[1][SeparatorPrefix.Length..],
                    ignoreCase: true,
                    out chapterSeparator))
            {
                return false;
            }
        }

        chapterStyleLevel = level;
        return true;
    }

    public static IReadOnlyList<PageNumberDisplayPlan> BuildDisplayPlans(
        IReadOnlyList<HeaderFooterPageSectionPlan> pageSections,
        TextDocument? document = null,
        IReadOnlyList<int>? blockPageAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(pageSections);

        var result = new List<PageNumberDisplayPlan>(pageSections.Count);
        var chapterNumbers = BuildChapterNumbersByPage(document, blockPageAssignments, pageSections.Count);
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
            var pageNumberText = FormatPageNumber(logical, page.PageSettings.PageNumberFormat);
            var chapterNumber = ResolveChapterNumber(chapterNumbers, page.PageSettings, pageIndex);
            result.Add(new PageNumberDisplayPlan(
                pageIndex,
                page.SectionIndex,
                logical,
                chapterNumber is null
                    ? pageNumberText
                    : chapterNumber + SeparatorText(page.PageSettings.PageNumberChapterSeparator) + pageNumberText,
                chapterNumber));
        }

        return result;
    }

    /// <summary>
    /// Builds a stable block-to-page-label resolver from a host's physical page ownership. The returned
    /// labels use the same section restarts, continuation, number formats, and chapter prefixes as PAGE
    /// fields in headers and footers. Unplaced blocks remain unresolved.
    /// </summary>
    public static Func<int, string?> BuildBlockPageReferenceResolver(
        TextDocument document,
        Func<int, int?> physicalPageOf)
    {
        var addressResolver = BuildBlockPageReferenceAddressResolver(document, physicalPageOf);
        return blockIndex => addressResolver(blockIndex)?.DisplayText;
    }

    /// <summary>
    /// Builds a physical-page-to-display-label resolver using the same section restarts, formats, and
    /// chapter prefixes as block page references. <paramref name="minimumPageCount"/> lets a host retain
    /// later pages occupied by a multi-page block even when that block's start is on an earlier page.
    /// </summary>
    public static Func<int, string?> BuildPhysicalPageReferenceResolver(
        TextDocument document,
        Func<int, int?> physicalPageOf,
        int minimumPageCount = 1)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(physicalPageOf);

        var assignments = Enumerable
            .Repeat(HeaderFooterPagePlanner.UnassignedBlockPageIndex, document.Blocks.Count)
            .ToArray();
        var pageCount = Math.Max(1, minimumPageCount);
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var physicalPage = physicalPageOf(blockIndex)
                ?? CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex);
            if (physicalPage is not > 0)
                continue;

            assignments[blockIndex] = physicalPage.Value - 1;
            pageCount = Math.Max(pageCount, physicalPage.Value);
        }

        var pageSections = HeaderFooterPagePlanner.MapPagesToSections(document, assignments, pageCount);
        var displayPlans = BuildDisplayPlans(pageSections, document, assignments);
        return physicalPage => physicalPage > 0 && physicalPage <= displayPlans.Count
            ? displayPlans[physicalPage - 1].Text
            : null;
    }

    /// <summary>
    /// Builds the index-specific form of <see cref="BuildBlockPageReferenceResolver"/>, retaining the
    /// zero-based physical page identity so equal labels from restarted sections are not deduplicated.
    /// </summary>
    public static Func<int, IndexPageReferenceAddress?> BuildBlockPageReferenceAddressResolver(
        TextDocument document,
        Func<int, int?> physicalPageOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(physicalPageOf);

        var assignments = Enumerable
            .Repeat(HeaderFooterPagePlanner.UnassignedBlockPageIndex, document.Blocks.Count)
            .ToArray();
        var pageCount = 1;
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var physicalPage = physicalPageOf(blockIndex)
                ?? CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex);
            if (physicalPage is not > 0)
                continue;

            assignments[blockIndex] = physicalPage.Value - 1;
            pageCount = Math.Max(pageCount, physicalPage.Value);
        }

        var pageSections = HeaderFooterPagePlanner.MapPagesToSections(document, assignments, pageCount);
        var displayPlans = BuildDisplayPlans(pageSections, document, assignments);
        var addressByBlock = new IndexPageReferenceAddress?[document.Blocks.Count];
        for (var blockIndex = 0; blockIndex < assignments.Length; blockIndex++)
        {
            var pageIndex = assignments[blockIndex];
            if (pageIndex >= 0 && pageIndex < displayPlans.Count)
                addressByBlock[blockIndex] = new IndexPageReferenceAddress(pageIndex, displayPlans[pageIndex].Text);
        }

        return blockIndex => blockIndex >= 0 && blockIndex < addressByBlock.Length
            ? addressByBlock[blockIndex]
            : null;
    }

    public static IReadOnlyList<PageNumberCitationReferencePlan> BuildCitationPageReferencePlans(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sections = document.Sections;
        var plans = new List<PageNumberCitationReferencePlan>();
        var sectionIndex = 0;
        var sectionRelativePageNumber = 1;
        var physicalPageNumber = 1;
        var nextContinueValue = 1;
        var currentSectionStart = ResolveSectionStart(sections, sectionIndex, nextContinueValue);

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block is not Paragraph paragraph)
                continue;

            if (paragraph.Formatting.PageBreakBefore)
                AdvanceWithinSection();

            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var run = paragraph.Runs[runIndex];
                if (run.IsPageBreak)
                {
                    AdvanceWithinSection();
                    continue;
                }

                if (run.Citation is not { } citation)
                    continue;

                var logicalPageNumber = CurrentLogicalPageNumber();
                plans.Add(new PageNumberCitationReferencePlan(
                    blockIndex,
                    runIndex,
                    citation,
                    physicalPageNumber,
                    sectionIndex,
                    sectionRelativePageNumber,
                    logicalPageNumber,
                    FormatPageNumber(logicalPageNumber, CurrentPageSettings().PageNumberFormat)));
            }

            if (paragraph.SectionBreak is { } sectionBreak)
            {
                nextContinueValue = CurrentLogicalPageNumber() + 1;
                physicalPageNumber = AdvanceForSectionBreak(physicalPageNumber, sectionBreak.BreakKind);
                sectionIndex = Math.Min(sectionIndex + 1, Math.Max(0, sections.Count - 1));
                sectionRelativePageNumber = 1;
                currentSectionStart = ResolveSectionStart(sections, sectionIndex, nextContinueValue);
            }
        }

        return plans;

        PageSettings CurrentPageSettings()
        {
            if (sections.Count == 0)
                return document.Page;

            var safeSectionIndex = Math.Clamp(sectionIndex, 0, sections.Count - 1);
            return sections[safeSectionIndex].Page;
        }

        int CurrentLogicalPageNumber() => currentSectionStart + Math.Max(1, sectionRelativePageNumber) - 1;

        void AdvanceWithinSection()
        {
            nextContinueValue = CurrentLogicalPageNumber() + 1;
            physicalPageNumber++;
            sectionRelativePageNumber++;
        }
    }

    public static ToaCitationPageResolver BuildCitationPageReferenceResolver(TextDocument document)
    {
        var referenceByRun = BuildCitationPageReferencePlans(document)
            .ToDictionary(
                plan => (plan.BlockIndex, plan.RunIndex),
                plan => new ToaCitationPageReference(plan.PhysicalPageNumber, plan.DisplayText));

        return (_, blockIndex, runIndex, _) =>
            referenceByRun.TryGetValue((blockIndex, runIndex), out var reference)
                ? reference
                : null;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<string?>> BuildChapterNumbersByPage(
        TextDocument? document,
        IReadOnlyList<int>? blockPageAssignments,
        int pageCount)
    {
        if (document is null || blockPageAssignments is null || pageCount <= 0)
            return new Dictionary<int, IReadOnlyList<string?>>();

        var headingEntries = DocumentOutline.Of(document)
            .Where(entry => entry.Level is >= 1 and <= 9
                && entry.BlockIndex >= 0
                && entry.BlockIndex < blockPageAssignments.Count
                && blockPageAssignments[entry.BlockIndex] >= 0)
            .Select(entry => (
                entry.Level,
                PageIndex: Math.Clamp(blockPageAssignments[entry.BlockIndex], 0, pageCount - 1),
                entry.BlockIndex))
            .OrderBy(entry => entry.PageIndex)
            .ThenBy(entry => entry.BlockIndex)
            .ToList();

        if (headingEntries.Count == 0)
            return new Dictionary<int, IReadOnlyList<string?>>();

        var perLevel = Enumerable.Range(1, 9)
            .ToDictionary(
                level => level,
                _ => (IReadOnlyList<string?>)new string?[pageCount]);
        var counters = new List<int>();
        var currentByLevel = new string?[10];
        var headingIndex = 0;
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            while (headingIndex < headingEntries.Count
                && headingEntries[headingIndex].PageIndex <= pageIndex)
            {
                var level = headingEntries[headingIndex].Level;
                while (counters.Count < level)
                    counters.Add(0);
                while (counters.Count > level)
                    counters.RemoveAt(counters.Count - 1);
                counters[level - 1]++;
                for (var reset = level + 1; reset < currentByLevel.Length; reset++)
                    currentByLevel[reset] = null;
                currentByLevel[level] = string.Join('.', counters);
                headingIndex++;
            }

            for (var level = 1; level <= 9; level++)
                ((string?[])perLevel[level])[pageIndex] = currentByLevel[level];
        }

        return perLevel;
    }

    private static string? ResolveChapterNumber(
        IReadOnlyDictionary<int, IReadOnlyList<string?>> chapterNumbers,
        PageSettings page,
        int pageIndex)
    {
        if (page.PageNumberChapterStyleLevel is not { } level
            || level is < 1 or > 9
            || !chapterNumbers.TryGetValue(level, out var numbers)
            || pageIndex < 0
            || pageIndex >= numbers.Count)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(numbers[pageIndex])
            ? null
            : numbers[pageIndex];
    }

    public static string SeparatorText(PageNumberChapterSeparator separator) => separator switch
    {
        PageNumberChapterSeparator.Period => ".",
        PageNumberChapterSeparator.Colon => ":",
        PageNumberChapterSeparator.EmDash => "--",
        PageNumberChapterSeparator.EnDash => "-",
        _ => "-"
    };

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

    private static int ResolveSectionStart(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        int nextContinueValue)
    {
        if (sections.Count == 0)
            return Math.Max(1, nextContinueValue);

        var safeSectionIndex = Math.Clamp(sectionIndex, 0, sections.Count - 1);
        return Math.Max(1, sections[safeSectionIndex].Page.PageNumberStartAt ?? nextContinueValue);
    }

    private static int AdvanceForSectionBreak(int physicalPageNumber, SectionBreakKind breakKind) =>
        breakKind switch
        {
            SectionBreakKind.NextPage => physicalPageNumber + 1,
            SectionBreakKind.EvenPage => physicalPageNumber % 2 == 0 ? physicalPageNumber + 2 : physicalPageNumber + 1,
            SectionBreakKind.OddPage => physicalPageNumber % 2 == 0 ? physicalPageNumber + 1 : physicalPageNumber + 2,
            _ => physicalPageNumber
        };

    private static int IndexOf(PageNumberFormat format)
    {
        for (var i = 0; i < FormatItems.Count; i++)
            if (FormatItems[i].Format == format)
                return i;

        return 0;
    }

    private static int ChapterStyleIndexOf(int? level)
    {
        if (level is null)
            return 0;

        for (var i = 0; i < ChapterStyleItems.Count; i++)
            if (ChapterStyleItems[i].Level == level)
                return i;

        return 0;
    }

    private static int ChapterSeparatorIndexOf(PageNumberChapterSeparator separator)
    {
        for (var i = 0; i < ChapterSeparatorItems.Count; i++)
            if (ChapterSeparatorItems[i].Separator == separator)
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
