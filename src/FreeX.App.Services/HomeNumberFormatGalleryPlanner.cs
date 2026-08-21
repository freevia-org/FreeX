using Free.Shared.Ribbon;

namespace FreeX.App.Services;

/// <summary>
/// Presentation policy for the Home Number Format gallery. The underlying option order and values
/// remain owned by <see cref="HomeNumberFormatDropdownPlanner"/> because selection dispatch is index based.
/// </summary>
public static class HomeNumberFormatGalleryPlanner
{
    public static IReadOnlyList<RibbonComboBoxChoice> Choices { get; } =
    [
        Choice("General", "General", "No specific format", RibbonComboBoxGalleryPreviewKind.General),
        Choice("0.00", "Number", "1234.56", RibbonComboBoxGalleryPreviewKind.Number),
        Choice("$#,##0.00", "Currency", "$1,234.56", RibbonComboBoxGalleryPreviewKind.Currency),
        Choice(HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode, "Accounting", "$1,234.56", RibbonComboBoxGalleryPreviewKind.Accounting),
        Choice("m/d/yyyy", "Short Date", "5/18/1903", RibbonComboBoxGalleryPreviewKind.ShortDate),
        Choice("[$-F800]", "Long Date", "Sunday, May 18, 1903", RibbonComboBoxGalleryPreviewKind.LongDate),
        Choice("h:mm AM/PM", "Time", "1:26:24 PM", RibbonComboBoxGalleryPreviewKind.Time),
        Choice("0%", "Percentage", "123456.00%", RibbonComboBoxGalleryPreviewKind.Percentage),
        Choice("# ?/?", "Fraction", "1234 5/9", RibbonComboBoxGalleryPreviewKind.Fraction),
        Choice("0.00E+00", "Scientific", "1.23E+03", RibbonComboBoxGalleryPreviewKind.Scientific),
        Choice("@", "Text", "Text", RibbonComboBoxGalleryPreviewKind.Text),
        Choice("number-format.more", HomeNumberFormatDropdownPlanner.MoreNumberFormatsLabel, null, RibbonComboBoxGalleryPreviewKind.More),
    ];

    private static RibbonComboBoxChoice Choice(
        string value,
        string label,
        string? description,
        RibbonComboBoxGalleryPreviewKind previewKind) =>
        new(value, label, description, previewKind);
}
