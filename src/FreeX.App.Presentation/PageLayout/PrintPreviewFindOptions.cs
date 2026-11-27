namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// The five checkable entries of the WPF DocumentViewer find box's drop-down menu, in menu order.
/// </summary>
public enum PrintPreviewFindOption
{
    WholeWord,
    Case,
    Diacritic,
    Kashida,
    AlefHamza,
}

/// <summary>
/// The find box option state. Every option starts off, as in the WPF DocumentViewer: a default search
/// is case-insensitive, ignores diacritics, kashida and alef-hamza variants, and matches inside words.
/// </summary>
public readonly record struct PrintPreviewFindOptions(
    bool MatchWholeWord = false,
    bool MatchCase = false,
    bool MatchDiacritic = false,
    bool MatchKashida = false,
    bool MatchAlefHamza = false)
{
    public bool Get(PrintPreviewFindOption option) => option switch
    {
        PrintPreviewFindOption.WholeWord => MatchWholeWord,
        PrintPreviewFindOption.Case => MatchCase,
        PrintPreviewFindOption.Diacritic => MatchDiacritic,
        PrintPreviewFindOption.Kashida => MatchKashida,
        PrintPreviewFindOption.AlefHamza => MatchAlefHamza,
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    public PrintPreviewFindOptions With(PrintPreviewFindOption option, bool value) => option switch
    {
        PrintPreviewFindOption.WholeWord => this with { MatchWholeWord = value },
        PrintPreviewFindOption.Case => this with { MatchCase = value },
        PrintPreviewFindOption.Diacritic => this with { MatchDiacritic = value },
        PrintPreviewFindOption.Kashida => this with { MatchKashida = value },
        PrintPreviewFindOption.AlefHamza => this with { MatchAlefHamza = value },
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };
}

/// <summary>One drop-down menu entry: the option it toggles, its caption (access key marked with an underscore) and automation id.</summary>
public sealed record PrintPreviewFindOptionPlan(PrintPreviewFindOption Option, string Text, string AutomationId);
