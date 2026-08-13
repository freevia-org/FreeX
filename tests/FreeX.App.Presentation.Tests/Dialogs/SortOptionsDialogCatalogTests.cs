using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class SortOptionsDialogCatalogTests
{
    [Fact]
    public void Create_PreservesLocalizedLabelsAndExcelFirstKeyOrder()
    {
        var presentation = SortOptionsDialogCatalog.Create(key => $"localized:{key}");

        presentation.Title.Should().Be("localized:SortOptions_SortOptions");
        presentation.CaseSensitive.Should().Be("localized:SortOptions_CaseSensitive");
        presentation.FirstKeySortOrderLabel.Should().Be("localized:SortOptions_FirstKeySortOrderLabel");
        presentation.SortTopToBottom.Should().Be("localized:SortOptions_SortTopToBottom");
        presentation.SortLeftToRight.Should().Be("localized:SortOptions_SortLeftToRight");
        presentation.Orientation.Should().Be("localized:SortOptions_Orientation");
        presentation.FirstKeySortOrders.Should().Equal(
            new SortOptionsFirstKeyOrderChoice("localized:SortOptions_FirstKeyNormal", "Normal"),
            new SortOptionsFirstKeyOrderChoice("localized:SortOptions_FirstKeySunToSatShort", "Sun, Mon, Tue, Wed, Thu, Fri, Sat"),
            new SortOptionsFirstKeyOrderChoice("localized:SortOptions_FirstKeySundayToSaturday", "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday"),
            new SortOptionsFirstKeyOrderChoice("localized:SortOptions_FirstKeyJanToDecShort", "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec"),
            new SortOptionsFirstKeyOrderChoice("localized:SortOptions_FirstKeyJanuaryToDecember", "January, February, March, April, May, June, July, August, September, October, November, December"));
    }
}
