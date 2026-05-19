using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class StatusBarCalculatorTests
{
    [Theory]
    [InlineData(12.5, "12.5")]
    [InlineData(12.0000000001, "12")]
    [InlineData(123456789.1234, "123456789.1")]
    public void FormatNumber_UsesCompactExcelLikeStatusText(double value, string expected)
    {
        StatusBarCalculator.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void GetReadyStatusText_ReportsValidationInputPromptForActiveCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = "Use a number"
        });

        StatusBarCalculator.GetReadyStatusText(sheet, address).Should().Be("Input: Use a number");
    }

    [Fact]
    public void GetReadyStatusText_ReportsReadyWhenActiveCellHasNoError()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        StatusBarCalculator.GetReadyStatusText(sheet, new CellAddress(sheet.Id, 1, 1)).Should().Be("Ready");
    }
}
