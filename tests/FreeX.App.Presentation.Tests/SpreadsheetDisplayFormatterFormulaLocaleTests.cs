using System.Globalization;

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class SpreadsheetDisplayFormatterFormulaLocaleTests
{
    [Fact]
    public void FormatFormulaBarText_DisplaysFormulaUsingRegionalSeparators()
    {
        var cell = Cell.FromFormula("SUM(1.5,IF(A1=2.5,3.5,4.5),{6.5,7.5;8.5,9.5})");
        var address = new CellAddress(SheetId.New(), 0, 0);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(
            cell,
            address,
            useR1C1ReferenceStyle: false,
            sheet: null,
            workbook: null,
            culture: CultureInfo.GetCultureInfo("fr-FR"));

        text.Should().Be("=SUM(1,5;IF(A1=2,5;3,5;4,5);{6,5\\7,5;8,5\\9,5})");
    }
}
