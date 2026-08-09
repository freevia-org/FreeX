using System.Globalization;
using System.Security.Cryptography;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record FreeWVisualHeaderFooterExpectation(
    int SlotCount,
    int ImageCount,
    bool HasImages,
    IReadOnlyList<string> SlotNames,
    IReadOnlyList<string> ImageSignatures,
    IReadOnlyList<FreeWVisualHeaderFooterSlotPlan> Slots);

public sealed record FreeWVisualHeaderFooterSlotPlan(
    int PageNumber,
    int SectionOrdinal,
    int SectionRelativePageNumber,
    string SlotName,
    bool IsFooter,
    string Alignment,
    int ImageCount,
    IReadOnlyList<string> ImageSignatures,
    IReadOnlyList<FreeWVisualHeaderFooterLinePlan> Lines);

public sealed record FreeWVisualHeaderFooterLinePlan(
    int ParagraphIndex,
    int LineIndex,
    string Alignment,
    string Text,
    int ImageCount,
    IReadOnlyList<string> ImageSignatures,
    IReadOnlyList<FreeWVisualHeaderFooterRunPlan> Runs);

public sealed record FreeWVisualHeaderFooterRunPlan(
    string Kind,
    int ParagraphIndex,
    int RunIndex,
    int SegmentIndex,
    string Text,
    string? FieldKind,
    string? ImageSignature,
    double WidthDip,
    double HeightDip,
    string Alignment);

public static class HeaderFooterVisualPlanner
{
    public const string TextRunKind = "text";
    public const string FieldRunKind = "field";
    public const string TabRunKind = "tab";
    public const string ImageRunKind = "image";

    public static FreeWVisualHeaderFooterExpectation EmptyExpectation { get; } = new(
        SlotCount: 0,
        ImageCount: 0,
        HasImages: false,
        SlotNames: [],
        ImageSignatures: [],
        Slots: []);

    public static FreeWVisualHeaderFooterExpectation BuildExpectation(
        TextDocument? document,
        int pageNumber,
        int pageCount,
        IReadOnlyList<int>? blockPageAssignments = null)
    {
        if (document is null || pageCount <= 0)
            return EmptyExpectation;

        var safePageCount = Math.Max(1, pageCount);
        var safePageNumber = Math.Clamp(pageNumber, 1, safePageCount);
        var assignments = blockPageAssignments ?? BuildSectionBreakPageAssignments(document, safePageCount);
        var pageToSection = HeaderFooterPagePlanner.MapPagesToSections(document, assignments, safePageCount);
        if (pageToSection.Count == 0)
            return EmptyExpectation;

        var pageSection = pageToSection[safePageNumber - 1];
        var displayPlan = PageNumberFormatDialogPlanner.BuildDisplayPlans(
                pageToSection,
                document,
                assignments)
            .ElementAtOrDefault(safePageNumber - 1);
        var diffOddEven = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(document);
        var slots = HeaderFooterPagePlanner.ResolveSlots(
            pageSection.HeadersFooters,
            pageSection.SectionRelativePageNumber,
            pageSection.PageSettings,
            diffOddEven,
            displayPlan?.LogicalPageNumber);

        var slotPlans = new List<FreeWVisualHeaderFooterSlotPlan>();
        AddSlot(slotPlans, slots.Header, slots.HeaderSlotName, isFooter: false, safePageNumber, safePageCount, pageSection, displayPlan);
        AddSlot(slotPlans, slots.Footer, slots.FooterSlotName, isFooter: true, safePageNumber, safePageCount, pageSection, displayPlan);

        var imageSignatures = slotPlans
            .SelectMany(slot => slot.ImageSignatures)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

        return new FreeWVisualHeaderFooterExpectation(
            SlotCount: slotPlans.Count,
            ImageCount: imageSignatures.Count,
            HasImages: imageSignatures.Count > 0,
            SlotNames: slotPlans
                .Select(slot => slot.SlotName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ImageSignatures: imageSignatures,
            Slots: slotPlans);
    }

    public static string BuildImageSignature(
        string slotName,
        int pageNumber,
        int sectionOrdinal,
        int sectionRelativePageNumber,
        int paragraphIndex,
        int runIndex,
        InlineImage image,
        TextAlignment alignment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(image);

        var widthDip = RoundDip(PageLayout.PointsToDip(Math.Max(0, image.WidthPt)));
        var heightDip = RoundDip(PageLayout.PointsToDip(Math.Max(0, image.HeightPt)));
        var hash = image.Bytes.Length == 0
            ? "empty"
            : Convert.ToHexString(SHA256.HashData(image.Bytes)).ToLowerInvariant()[..16];
        var alt = NormalizeSignaturePart(image.AltText);

        return string.Join(
            "|",
            $"slot={slotName}",
            $"section={Math.Max(1, sectionOrdinal).ToString(CultureInfo.InvariantCulture)}",
            $"sectionPage={Math.Max(1, sectionRelativePageNumber).ToString(CultureInfo.InvariantCulture)}",
            $"page={Math.Max(1, pageNumber).ToString(CultureInfo.InvariantCulture)}",
            $"para={Math.Max(0, paragraphIndex).ToString(CultureInfo.InvariantCulture)}",
            $"run={Math.Max(0, runIndex).ToString(CultureInfo.InvariantCulture)}",
            $"format={image.Format}",
            $"bytes={image.Bytes.Length.ToString(CultureInfo.InvariantCulture)}",
            $"sha16={hash}",
            $"sizePt={FormatDouble(image.WidthPt)}x{FormatDouble(image.HeightPt)}",
            $"sizeDip={FormatDouble(widthDip)}x{FormatDouble(heightDip)}",
            $"align={alignment}",
            $"wrap={image.Wrapping}",
            $"alt={alt}");
    }

    private static void AddSlot(
        List<FreeWVisualHeaderFooterSlotPlan> slotPlans,
        HeaderFooter? slot,
        string slotName,
        bool isFooter,
        int pageNumber,
        int pageCount,
        HeaderFooterPageSectionPlan pageSection,
        PageNumberDisplayPlan? displayPlan)
    {
        if (slot is null || slot.IsEmpty)
            return;

        var sectionOrdinal = pageSection.SectionIndex + 1;
        var lines = BuildLines(
            slot,
            slotName,
            pageNumber,
            pageCount,
            sectionOrdinal,
            pageSection.SectionRelativePageNumber,
            pageSection.SectionPageCount,
            displayPlan);
        var imageSignatures = lines
            .SelectMany(line => line.ImageSignatures)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

        slotPlans.Add(new FreeWVisualHeaderFooterSlotPlan(
            PageNumber: pageNumber,
            SectionOrdinal: sectionOrdinal,
            SectionRelativePageNumber: pageSection.SectionRelativePageNumber,
            SlotName: slotName,
            IsFooter: isFooter,
            Alignment: DominantAlignment(lines),
            ImageCount: imageSignatures.Count,
            ImageSignatures: imageSignatures,
            Lines: lines));
    }

    private static IReadOnlyList<FreeWVisualHeaderFooterLinePlan> BuildLines(
        HeaderFooter slot,
        string slotName,
        int pageNumber,
        int pageCount,
        int sectionOrdinal,
        int sectionRelativePageNumber,
        int sectionPageCount,
        PageNumberDisplayPlan? displayPlan = null)
    {
        var lines = new List<FreeWVisualHeaderFooterLinePlan>();
        for (var paragraphIndex = 0; paragraphIndex < slot.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = slot.Paragraphs[paragraphIndex];
            var alignment = paragraph.Formatting.Alignment;
            var runs = BuildRuns(
                paragraph,
                slotName,
                pageNumber,
                pageCount,
                sectionOrdinal,
                sectionRelativePageNumber,
                sectionPageCount,
                paragraphIndex,
                alignment,
                displayPlan);
            var imageSignatures = runs
                .Where(run => string.Equals(run.Kind, ImageRunKind, StringComparison.Ordinal))
                .Select(run => run.ImageSignature)
                .OfType<string>()
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToList();

            lines.Add(new FreeWVisualHeaderFooterLinePlan(
                ParagraphIndex: paragraphIndex,
                LineIndex: paragraphIndex,
                Alignment: alignment.ToString(),
                Text: string.Concat(runs.Where(run => run.Kind is TextRunKind or FieldRunKind).Select(run => run.Text)),
                ImageCount: imageSignatures.Count,
                ImageSignatures: imageSignatures,
                Runs: runs));
        }

        return lines;
    }

    private static IReadOnlyList<FreeWVisualHeaderFooterRunPlan> BuildRuns(
        Paragraph paragraph,
        string slotName,
        int pageNumber,
        int pageCount,
        int sectionOrdinal,
        int sectionRelativePageNumber,
        int sectionPageCount,
        int paragraphIndex,
        TextAlignment alignment,
        PageNumberDisplayPlan? displayPlan)
    {
        var runs = new List<FreeWVisualHeaderFooterRunPlan>();
        for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            if (run.Image is { } image)
            {
                var signature = BuildImageSignature(
                    slotName,
                    pageNumber,
                    sectionOrdinal,
                    sectionRelativePageNumber,
                    paragraphIndex,
                    runIndex,
                    image,
                    alignment);
                runs.Add(new FreeWVisualHeaderFooterRunPlan(
                    ImageRunKind,
                    paragraphIndex,
                    runIndex,
                    SegmentIndex: 0,
                    Text: string.Empty,
                    FieldKind: null,
                    ImageSignature: signature,
                    WidthDip: RoundDip(PageLayout.PointsToDip(Math.Max(0, image.WidthPt))),
                    HeightDip: RoundDip(PageLayout.PointsToDip(Math.Max(0, image.HeightPt))),
                    Alignment: alignment.ToString()));
                continue;
            }

            var fieldKind = FieldKindFor(run);
            if (!string.IsNullOrEmpty(fieldKind))
            {
                var text = ResolveHeaderFooterFieldText(
                    run,
                    fieldKind,
                    pageCount,
                    sectionOrdinal,
                    sectionPageCount,
                    displayPlan);
                runs.Add(new FreeWVisualHeaderFooterRunPlan(
                    FieldRunKind,
                    paragraphIndex,
                    runIndex,
                    SegmentIndex: 0,
                    Text: text,
                    FieldKind: fieldKind,
                    ImageSignature: null,
                    WidthDip: 0,
                    HeightDip: 0,
                    Alignment: alignment.ToString()));
                continue;
            }

            AddTextAndTabSegments(runs, run.Text, paragraphIndex, runIndex, alignment);
        }

        return runs;
    }

    private static void AddTextAndTabSegments(
        List<FreeWVisualHeaderFooterRunPlan> runs,
        string text,
        int paragraphIndex,
        int runIndex,
        TextAlignment alignment)
    {
        if (string.IsNullOrEmpty(text))
        {
            runs.Add(new FreeWVisualHeaderFooterRunPlan(
                TextRunKind,
                paragraphIndex,
                runIndex,
                SegmentIndex: 0,
                Text: string.Empty,
                FieldKind: null,
                ImageSignature: null,
                WidthDip: 0,
                HeightDip: 0,
                Alignment: alignment.ToString()));
            return;
        }

        var segmentIndex = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\t')
                continue;

            if (i > start)
            {
                AddTextRun(runs, text[start..i], paragraphIndex, runIndex, segmentIndex, alignment);
                segmentIndex++;
            }

            runs.Add(new FreeWVisualHeaderFooterRunPlan(
                TabRunKind,
                paragraphIndex,
                runIndex,
                segmentIndex++,
                Text: "\t",
                FieldKind: null,
                ImageSignature: null,
                WidthDip: 0,
                HeightDip: 0,
                Alignment: alignment.ToString()));
            start = i + 1;
        }

        if (start < text.Length)
            AddTextRun(runs, text[start..], paragraphIndex, runIndex, segmentIndex, alignment);
    }

    private static void AddTextRun(
        List<FreeWVisualHeaderFooterRunPlan> runs,
        string text,
        int paragraphIndex,
        int runIndex,
        int segmentIndex,
        TextAlignment alignment)
    {
        runs.Add(new FreeWVisualHeaderFooterRunPlan(
            TextRunKind,
            paragraphIndex,
            runIndex,
            segmentIndex,
            text,
            FieldKind: null,
            ImageSignature: null,
            WidthDip: 0,
            HeightDip: 0,
            alignment.ToString()));
    }

    private static string? FieldKindFor(Run run)
    {
        if (run.FieldKind != RunFieldKind.None)
            return run.FieldKind.ToString();
        return run.ComplexField?.Keyword.Length > 0 ? run.ComplexField.Keyword : null;
    }

    private static string ResolveHeaderFooterFieldText(
        Run run,
        string fieldKind,
        int pageCount,
        int sectionOrdinal,
        int sectionPageCount,
        PageNumberDisplayPlan? displayPlan)
    {
        if (IsPageNumberField(fieldKind))
            return displayPlan?.Text ?? run.Text;

        if (IsNumPagesField(fieldKind))
            return Math.Max(1, pageCount).ToString(CultureInfo.InvariantCulture);

        if (run.ComplexField is { } field && ComplexFieldDisplayPlanner.IsPageSectionField(field.Keyword))
            return ComplexFieldDisplayPlanner.ResolvePageSectionField(
                field,
                run.Text,
                sectionOrdinal,
                sectionPageCount);

        return run.Text;
    }

    private static bool IsPageNumberField(string fieldKind) =>
        string.Equals(fieldKind, nameof(RunFieldKind.PageNumber), StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKind, "PAGE", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumPagesField(string fieldKind) =>
        string.Equals(fieldKind, nameof(RunFieldKind.NumPages), StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKind, "NUMPAGES", StringComparison.OrdinalIgnoreCase);

    private static string DominantAlignment(IReadOnlyList<FreeWVisualHeaderFooterLinePlan> lines) =>
        lines
            .GroupBy(line => line.Alignment)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? TextAlignment.Left.ToString();

    private static int[] BuildSectionBreakPageAssignments(TextDocument document, int pageCount)
    {
        var assignments = new int[document.Blocks.Count];
        var pageIndex = 0;
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            assignments[blockIndex] = Math.Clamp(pageIndex, 0, Math.Max(0, pageCount - 1));
            if (document.Blocks[blockIndex] is Paragraph { SectionBreak: { } section }
                && section.BreakKind is SectionBreakKind.NextPage
                    or SectionBreakKind.EvenPage
                    or SectionBreakKind.OddPage)
            {
                pageIndex++;
            }
        }

        return assignments;
    }

    private static string NormalizeSignaturePart(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace('|', '/')
                .Replace('\r', ' ')
                .Replace('\n', ' ');

    private static double RoundDip(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatDouble(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
}
