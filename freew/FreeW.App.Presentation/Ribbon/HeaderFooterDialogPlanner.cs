using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum HeaderFooterSlotKind
{
    Header,
    Footer,
    EvenHeader,
    EvenFooter,
    FirstHeader,
    FirstFooter
}

public enum HeaderFooterSlotActivationKind
{
    Active,
    RequiresDifferentOddEvenPages,
    RequiresDifferentFirstPage
}

public sealed record HeaderFooterSlotActivationPlan(
    HeaderFooterSlotKind Slot,
    string SlotName,
    string Label,
    HeaderFooterSlotActivationKind Kind,
    string? Message);

public sealed record HeaderFooterSlotDialogState(
    string Text,
    bool HasPageNumber,
    bool HasComplexField,
    bool CanInsertPageNumber);

public static class HeaderFooterDialogPlanner
{
    public const string EditCaption = "Edit Header / Footer";
    public const string PageNumberPrefix = "Page ";
    public const string RunSeparator = "  ";

    public static HeaderFooterSlotKind ParseSlot(string slotName) =>
        slotName switch
        {
            "header" => HeaderFooterSlotKind.Header,
            "footer" => HeaderFooterSlotKind.Footer,
            "even-header" => HeaderFooterSlotKind.EvenHeader,
            "even-footer" => HeaderFooterSlotKind.EvenFooter,
            "first-header" => HeaderFooterSlotKind.FirstHeader,
            "first-footer" => HeaderFooterSlotKind.FirstFooter,
            _ => throw new ArgumentOutOfRangeException(nameof(slotName), slotName, "Unknown header/footer slot.")
        };

    public static string SlotNameFor(HeaderFooterSlotKind slot) =>
        slot switch
        {
            HeaderFooterSlotKind.Header => "header",
            HeaderFooterSlotKind.Footer => "footer",
            HeaderFooterSlotKind.EvenHeader => "even-header",
            HeaderFooterSlotKind.EvenFooter => "even-footer",
            HeaderFooterSlotKind.FirstHeader => "first-header",
            HeaderFooterSlotKind.FirstFooter => "first-footer",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };

    public static string LabelFor(HeaderFooterSlotKind slot) =>
        slot switch
        {
            HeaderFooterSlotKind.Header => "Default Header",
            HeaderFooterSlotKind.Footer => "Default Footer",
            HeaderFooterSlotKind.EvenHeader => "Even-Page Header",
            HeaderFooterSlotKind.EvenFooter => "Even-Page Footer",
            HeaderFooterSlotKind.FirstHeader => "First-Page Header",
            HeaderFooterSlotKind.FirstFooter => "First-Page Footer",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };

    public static HeaderFooterSlotActivationPlan PlanSlotActivation(
        string slotName,
        PageSettings pageSettings)
    {
        ArgumentNullException.ThrowIfNull(pageSettings);

        return PlanSlotActivation(
            ParseSlot(slotName),
            pageSettings.DifferentOddEvenPages,
            pageSettings.DifferentFirstPage);
    }

    public static HeaderFooterSlotActivationPlan PlanSlotActivation(
        HeaderFooterSlotKind slot,
        PageSettings pageSettings)
    {
        ArgumentNullException.ThrowIfNull(pageSettings);

        return PlanSlotActivation(
            slot,
            pageSettings.DifferentOddEvenPages,
            pageSettings.DifferentFirstPage);
    }

    public static HeaderFooterSlotActivationPlan PlanSlotActivation(
        HeaderFooterSlotKind slot,
        bool differentOddEvenPages,
        bool differentFirstPage)
    {
        var label = LabelFor(slot);
        var slotName = SlotNameFor(slot);

        if (slot is HeaderFooterSlotKind.EvenHeader or HeaderFooterSlotKind.EvenFooter
            && !differentOddEvenPages)
        {
            return new HeaderFooterSlotActivationPlan(
                slot,
                slotName,
                label,
                HeaderFooterSlotActivationKind.RequiresDifferentOddEvenPages,
                $"'{label}' is only active when 'Different Odd & Even Pages' is turned on.\n"
                    + "Enable that option in Header & Footer Design, then try again.");
        }

        if (slot is HeaderFooterSlotKind.FirstHeader or HeaderFooterSlotKind.FirstFooter
            && !differentFirstPage)
        {
            return new HeaderFooterSlotActivationPlan(
                slot,
                slotName,
                label,
                HeaderFooterSlotActivationKind.RequiresDifferentFirstPage,
                $"'{label}' is only active when 'Different First Page' is turned on.\n"
                    + "Enable that option in Header & Footer Design, then try again.");
        }

        return new HeaderFooterSlotActivationPlan(
            slot,
            slotName,
            label,
            HeaderFooterSlotActivationKind.Active,
            Message: null);
    }

    public static HeaderFooter? GetSlot(SectionHeadersFooters headersFooters, string slotName)
    {
        ArgumentNullException.ThrowIfNull(headersFooters);
        return GetSlot(headersFooters, ParseSlot(slotName));
    }

    public static HeaderFooter? GetSlot(SectionHeadersFooters headersFooters, HeaderFooterSlotKind slot)
    {
        ArgumentNullException.ThrowIfNull(headersFooters);

        return slot switch
        {
            HeaderFooterSlotKind.Header => headersFooters.Header,
            HeaderFooterSlotKind.Footer => headersFooters.Footer,
            HeaderFooterSlotKind.EvenHeader => headersFooters.EvenHeader,
            HeaderFooterSlotKind.EvenFooter => headersFooters.EvenFooter,
            HeaderFooterSlotKind.FirstHeader => headersFooters.FirstHeader,
            HeaderFooterSlotKind.FirstFooter => headersFooters.FirstFooter,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };
    }

    public static void SetSlot(SectionHeadersFooters headersFooters, string slotName, HeaderFooter? value)
    {
        ArgumentNullException.ThrowIfNull(headersFooters);
        SetSlot(headersFooters, ParseSlot(slotName), value);
    }

    public static void SetSlot(SectionHeadersFooters headersFooters, HeaderFooterSlotKind slot, HeaderFooter? value)
    {
        ArgumentNullException.ThrowIfNull(headersFooters);

        switch (slot)
        {
            case HeaderFooterSlotKind.Header:
                headersFooters.Header = value;
                break;
            case HeaderFooterSlotKind.Footer:
                headersFooters.Footer = value;
                break;
            case HeaderFooterSlotKind.EvenHeader:
                headersFooters.EvenHeader = value;
                break;
            case HeaderFooterSlotKind.EvenFooter:
                headersFooters.EvenFooter = value;
                break;
            case HeaderFooterSlotKind.FirstHeader:
                headersFooters.FirstHeader = value;
                break;
            case HeaderFooterSlotKind.FirstFooter:
                headersFooters.FirstFooter = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }
    }

    public static HeaderFooter? BuildPlainTextHeaderFooter(string text, HeaderFooter? existing)
    {
        var hasPageNumber = HasPageNumber(existing);
        if (text.Length == 0 && !hasPageNumber)
            return null;

        var headerFooter = new HeaderFooter();
        var paragraph = new Paragraph();
        if (text.Length > 0)
            paragraph.Runs.Add(new Run(text));

        if (hasPageNumber)
        {
            AppendSeparatorIfNeeded(paragraph);
            paragraph.Runs.Add(Run.PageNumberField());
        }

        headerFooter.Paragraphs.Add(paragraph);
        return headerFooter;
    }

    public static HeaderFooter AddPageNumberToSlot(HeaderFooter? current)
    {
        if (HasPageNumber(current))
            return current!;

        var result = current ?? new HeaderFooter();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Center
            }
        };
        paragraph.Runs.Add(new Run(PageNumberPrefix));
        paragraph.Runs.Add(Run.PageNumberField());
        result.Paragraphs.Add(paragraph);
        return result;
    }

    public static HeaderFooter AppendPlainDateTimeToSlot(HeaderFooter? current, string text)
    {
        var result = current ?? new HeaderFooter();
        var paragraph = EnsureDefaultParagraph(result);
        if (!string.IsNullOrEmpty(text))
            paragraph.Runs.Add(new Run(text));
        return result;
    }

    public static HeaderFooter AppendFieldDateTimeToSlot(HeaderFooter? current, string instruction)
    {
        var result = current ?? new HeaderFooter();
        var paragraph = EnsureDefaultParagraph(result);
        if (!string.IsNullOrWhiteSpace(instruction))
            paragraph.Runs.Add(Run.ComplexFieldRun(" " + instruction.Trim() + " "));
        return result;
    }

    public static HeaderFooter AppendComplexFieldToSlot(HeaderFooter? current, string instruction)
    {
        var result = current ?? new HeaderFooter();
        var paragraph = EnsureDefaultParagraph(result);
        paragraph.Runs.Add(Run.ComplexFieldRun(instruction));
        return result;
    }

    public static HeaderFooterSlotDialogState BuildSlotDialogState(HeaderFooter? current) =>
        new(
            current?.PlainText ?? string.Empty,
            HasPageNumber(current),
            HasComplexField(current),
            CanInsertPageNumber: !HasPageNumber(current));

    public static HeaderFooter? BuildSlotDialogResult(
        string text,
        bool appendPageNumber,
        string? appendDateTimeText,
        string? appendFieldInstruction)
    {
        if (text.Length == 0
            && !appendPageNumber
            && appendDateTimeText is null
            && appendFieldInstruction is null)
        {
            return null;
        }

        var headerFooter = new HeaderFooter();
        var paragraph = new Paragraph();

        if (text.Length > 0)
            paragraph.Runs.Add(new Run(text));

        if (appendDateTimeText is { } dateTime)
        {
            AppendSeparatorIfNeeded(paragraph);
            paragraph.Runs.Add(new Run(dateTime));
        }

        if (appendFieldInstruction is { } instruction)
        {
            AppendSeparatorIfNeeded(paragraph);
            paragraph.Runs.Add(Run.ComplexFieldRun(instruction));
        }

        if (appendPageNumber)
        {
            AppendSeparatorIfNeeded(paragraph);
            paragraph.Runs.Add(Run.PageNumberField());
        }

        headerFooter.Paragraphs.Add(paragraph);
        return headerFooter;
    }

    public static bool TryParseDistance(string? value, out double points) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out points)
        && points >= 0;

    public static string FormatDistance(double points) =>
        points.ToString("0.##", CultureInfo.InvariantCulture);

    public static bool HasPageNumber(HeaderFooter? headerFooter) =>
        headerFooter?.Paragraphs.SelectMany(p => p.Runs)
            .Any(r => r.FieldKind == RunFieldKind.PageNumber) ?? false;

    public static bool HasComplexField(HeaderFooter? headerFooter) =>
        headerFooter?.Paragraphs.SelectMany(p => p.Runs)
            .Any(r => r.ComplexField is not null) ?? false;

    public static bool IsFooterSlot(HeaderFooterSlotKind slot) =>
        slot is HeaderFooterSlotKind.Footer
            or HeaderFooterSlotKind.EvenFooter
            or HeaderFooterSlotKind.FirstFooter;

    public static int CommandSlotIndexFor(HeaderFooterSlotKind slot) => slot switch
    {
        HeaderFooterSlotKind.Header => 0,
        HeaderFooterSlotKind.Footer => 1,
        HeaderFooterSlotKind.FirstHeader => 2,
        HeaderFooterSlotKind.FirstFooter => 3,
        HeaderFooterSlotKind.EvenHeader => 4,
        HeaderFooterSlotKind.EvenFooter => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    private static Paragraph EnsureDefaultParagraph(HeaderFooter headerFooter)
    {
        if (headerFooter.Paragraphs.Count == 0)
            headerFooter.Paragraphs.Add(new Paragraph());
        return headerFooter.Paragraphs[^1];
    }

    private static void AppendSeparatorIfNeeded(Paragraph paragraph)
    {
        if (paragraph.Runs.Count > 0)
            paragraph.Runs.Add(new Run(RunSeparator));
    }
}
