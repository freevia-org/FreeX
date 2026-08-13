namespace FreeX.App.Presentation.Dialogs;

public sealed record SortOptionsFirstKeyOrderChoice(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record SortOptionsDialogPresentation(
    string Title,
    string CaseSensitive,
    string FirstKeySortOrderLabel,
    string SortTopToBottom,
    string SortLeftToRight,
    string Orientation,
    IReadOnlyList<SortOptionsFirstKeyOrderChoice> FirstKeySortOrders);

/// <summary>Renderer-neutral localized text and ordered choices for the Sort Options dialog.</summary>
public static class SortOptionsDialogCatalog
{
    public const string NormalFirstKeySortOrder = "Normal";
    public const string ShortDayFirstKeySortOrder = "Sun, Mon, Tue, Wed, Thu, Fri, Sat";
    public const string LongDayFirstKeySortOrder = "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday";
    public const string ShortMonthFirstKeySortOrder = "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec";
    public const string LongMonthFirstKeySortOrder = "January, February, March, April, May, June, July, August, September, October, November, December";

    public const string TitleResourceKey = "SortOptions_SortOptions";
    public const string CaseSensitiveResourceKey = "SortOptions_CaseSensitive";
    public const string FirstKeySortOrderLabelResourceKey = "SortOptions_FirstKeySortOrderLabel";
    public const string SortTopToBottomResourceKey = "SortOptions_SortTopToBottom";
    public const string SortLeftToRightResourceKey = "SortOptions_SortLeftToRight";
    public const string OrientationResourceKey = "SortOptions_Orientation";
    public const string NormalFirstKeyResourceKey = "SortOptions_FirstKeyNormal";
    public const string ShortDayFirstKeyResourceKey = "SortOptions_FirstKeySunToSatShort";
    public const string LongDayFirstKeyResourceKey = "SortOptions_FirstKeySundayToSaturday";
    public const string ShortMonthFirstKeyResourceKey = "SortOptions_FirstKeyJanToDecShort";
    public const string LongMonthFirstKeyResourceKey = "SortOptions_FirstKeyJanuaryToDecember";

    public static SortOptionsDialogPresentation Create(Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return new SortOptionsDialogPresentation(
            localize(TitleResourceKey),
            localize(CaseSensitiveResourceKey),
            localize(FirstKeySortOrderLabelResourceKey),
            localize(SortTopToBottomResourceKey),
            localize(SortLeftToRightResourceKey),
            localize(OrientationResourceKey),
            [
                new(localize(NormalFirstKeyResourceKey), NormalFirstKeySortOrder),
                new(localize(ShortDayFirstKeyResourceKey), ShortDayFirstKeySortOrder),
                new(localize(LongDayFirstKeyResourceKey), LongDayFirstKeySortOrder),
                new(localize(ShortMonthFirstKeyResourceKey), ShortMonthFirstKeySortOrder),
                new(localize(LongMonthFirstKeyResourceKey), LongMonthFirstKeySortOrder)
            ]);
    }
}
