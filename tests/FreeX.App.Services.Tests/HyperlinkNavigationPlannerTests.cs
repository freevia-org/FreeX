using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class HyperlinkNavigationPlannerTests
{
    [Theory]
    [InlineData("http://example.test")]
    [InlineData("https://example.test")]
    [InlineData("mailto:user@example.test")]
    [InlineData("ftp://example.test/file.txt")]
    public void IsAllowedScheme_AcceptsKnownExternalSchemes(string target) =>
        HyperlinkNavigationPlanner.IsAllowedScheme(target).Should().BeTrue();

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file:///tmp/book.xlsx")]
    [InlineData("relative/path/file.xlsx")]
    [InlineData("")]
    public void IsAllowedScheme_RejectsBlockedAndRelativeTargets(string target) =>
        HyperlinkNavigationPlanner.IsAllowedScheme(target).Should().BeFalse();

    [Fact]
    public void TryCreatePlan_CreatesWorksheetPlanForDocumentLink()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " 'Data Sheet'!B2 ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.WorksheetCell,
            "'Data Sheet'!B2",
            null));
    }

    [Fact]
    public void TryCreatePlan_CreatesExternalPlanForWebLink()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " https://example.test/report ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "https://example.test/report",
            null));
    }

    [Fact]
    public void TryCreatePlan_RejectsMissingOrBlankHyperlink()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " ";

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var blankPlan).Should().BeFalse();
        blankPlan.Should().BeNull();

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, new CellAddress(sheetId, 2, 1), out var missingPlan)
            .Should()
            .BeFalse();
        missingPlan.Should().BeNull();
    }
}
