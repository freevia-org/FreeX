using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class DialogRangeSelectionFormatterTests
{
    [Theory]
    [InlineData(DialogRangeSelectionFormat.Range, "B2:D4")]
    [InlineData(DialogRangeSelectionFormat.StartCell, "B2")]
    [InlineData(DialogRangeSelectionFormat.PageSetupPrintArea, "$B$2:$D$4")]
    [InlineData(DialogRangeSelectionFormat.PageSetupRepeatRows, "$2:$4")]
    [InlineData(DialogRangeSelectionFormat.PageSetupRepeatColumns, "$B:$D")]
    public void Format_UsesSharedDialogRangeFormattingDispatch(
        DialogRangeSelectionFormat format,
        string expected)
    {
        var range = CreateRange();

        var result = DialogRangeSelectionFormatter.Format(
            range,
            format,
            new DialogRangeSelectionFormatContext("Source", "Source", UseR1C1ReferenceStyle: false));

        result.Should().Be(expected);
    }

    [Fact]
    public void Format_DataValidationFormula_QualifiesOnlyCrossSheetSources()
    {
        var range = CreateRange();

        DialogRangeSelectionFormatter.Format(
                range,
                DialogRangeSelectionFormat.DataValidationFormula,
                new DialogRangeSelectionFormatContext("Source Sheet", "Host", false))
            .Should().Be("='Source Sheet'!$B$2:$D$4");
        DialogRangeSelectionFormatter.Format(
                range,
                DialogRangeSelectionFormat.DataValidationFormula,
                new DialogRangeSelectionFormatContext("Host", "Host", false))
            .Should().Be("=$B$2:$D$4");
    }

    [Fact]
    public void Format_PageSetupHonorsR1C1ReferenceStyle()
    {
        DialogRangeSelectionFormatter.Format(
                CreateRange(),
                DialogRangeSelectionFormat.PageSetupPrintArea,
                new DialogRangeSelectionFormatContext(null, null, UseR1C1ReferenceStyle: true))
            .Should().Be("R2C2:R4C4");
    }

    private static GridRange CreateRange()
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 4, 4));
    }
}
