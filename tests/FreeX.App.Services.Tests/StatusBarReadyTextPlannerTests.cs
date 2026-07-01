using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarReadyTextPlannerTests
{
    [Theory]
    [InlineData(null, "Ready")]
    [InlineData("", "Ready")]
    [InlineData("   ", "Ready")]
    [InlineData("Showing gridlines", "Ready")]
    [InlineData("Hiding headings", "Ready")]
    [InlineData("showing formulas", "Ready")]
    [InlineData("hiding formulas", "Ready")]
    [InlineData("Edit", "Edit")]
    [InlineData("Input: Use a number", "Input: Use a number")]
    public void NormalizeTransientReadyText_FoldsRendererToggleMessagesToFallback(
        string? status,
        string expected)
    {
        var text = StatusBarReadyTextPlanner.NormalizeTransientReadyText(status, "Ready");

        text.Should().Be(expected);
    }

    [Fact]
    public void BuildReadyText_ReturnsFallback_WhenActiveCellHasNoInputPrompt()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        var text = StatusBarReadyTextPlanner.BuildReadyText(
            sheet,
            new CellAddress(sheet.Id, 1, 1),
            "Ready");

        text.Should().Be("Ready");
    }

    [Fact]
    public void BuildReadyText_CombinesPromptTitleAndMessage()
    {
        var (sheet, address) = CreateSheetWithPrompt("Input", "Use a number");

        var text = StatusBarReadyTextPlanner.BuildReadyText(sheet, address, "Ready");

        text.Should().Be("Input: Use a number");
    }

    [Theory]
    [InlineData("", "Use a number", "Use a number")]
    [InlineData("Input", "", "Input")]
    public void BuildReadyText_UsesSinglePromptPart_WhenOnlyOnePartIsPresent(
        string title,
        string message,
        string expected)
    {
        var (sheet, address) = CreateSheetWithPrompt(title, message);

        var text = StatusBarReadyTextPlanner.BuildReadyText(sheet, address, "Ready");

        text.Should().Be(expected);
    }

    private static (Sheet Sheet, CellAddress Address) CreateSheetWithPrompt(
        string title,
        string message)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            ShowInputMessage = true,
            PromptTitle = title,
            PromptMessage = message
        });
        return (sheet, address);
    }
}
