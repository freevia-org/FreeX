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
    public void TryCreatePlan_CreatesLocalFilePlanForLocalFileUriWithoutAllowingExternalLaunch()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " file:///Users/quinn/Work/Budget%202026.xlsx ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.LocalFile,
            "file:///Users/quinn/Work/Budget%202026.xlsx",
            null,
            "/Users/quinn/Work/Budget 2026.xlsx"));
        HyperlinkNavigationPlanner.IsAllowedScheme(plan!.Target).Should().BeFalse();
    }

    [Fact]
    public void TryCreatePlan_CreatesLocalFilePlanForMacOsAbsolutePath()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " /Users/quinn/Work/Budget.xlsx ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.LocalFile,
            "/Users/quinn/Work/Budget.xlsx",
            null,
            "/Users/quinn/Work/Budget.xlsx"));
    }

    [Fact]
    public void TryCreatePlan_ResolvesRelativeLocalFileAgainstWorkbookPathOnly()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        var workbookDirectory = Path.Combine(Path.GetTempPath(), "FreeXHyperlinkPlanner");
        var workbookPath = Path.Combine(workbookDirectory, "Book.fxl");
        var expectedLocalPath = Path.GetFullPath(Path.Combine(workbookDirectory, "Reports", "Budget.xlsx"));
        sheet.Hyperlinks[address] = " Reports/Budget.xlsx ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, workbookPath, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.LocalFile,
            "Reports/Budget.xlsx",
            null,
            expectedLocalPath));
    }

    [Fact]
    public void TryCreatePlan_LeavesRelativeLocalFileExternalWithoutWorkbookPath()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " Reports/Budget.xlsx ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "Reports/Budget.xlsx",
            null));
    }

    [Fact]
    public void TryCreatePlan_LeavesRemoteFileUriExternalForLauncherBlock()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.Hyperlinks[address] = " file://server/share/Budget.xlsx ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "file://server/share/Budget.xlsx",
            null));
        HyperlinkNavigationPlanner.IsAllowedScheme(plan!.Target).Should().BeFalse();
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

    [Fact]
    public void TryCreatePlan_CreatesExternalPlanForBareHyperlinkFormula()
    {
        // R27-lookup-reference-remaining-3: a cell whose sole hyperlink mechanism is a literal
        // =HYPERLINK(...) formula (no Insert-Hyperlink object) must still be click-navigable.
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.SetFormula(address, "HYPERLINK(\"https://example.com\",\"Click me\")");

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "https://example.com",
            null));
    }

    [Fact]
    public void TryCreatePlan_CreatesWorksheetPlanForHyperlinkFormulaWithHashTarget()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.SetFormula(address, "HYPERLINK(\"#'Data Sheet'!B2\",\"Go\")");

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.WorksheetCell,
            "'Data Sheet'!B2",
            null));
    }

    [Fact]
    public void TryCreatePlan_PrefersExplicitHyperlinkObjectOverHyperlinkFormulaOnSameCell()
    {
        // Sibling already-working case: an explicit Insert-Hyperlink object still wins even when the
        // cell also happens to contain a =HYPERLINK(...) formula (e.g. authored, then a link applied).
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.SetFormula(address, "HYPERLINK(\"https://formula.example\",\"Click me\")");
        sheet.Hyperlinks[address] = " https://explicit.example/report ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "https://explicit.example/report",
            null));
    }

    [Fact]
    public void TryCreatePlan_RejectsHyperlinkFormulaWithNonLiteralLinkArgument()
    {
        // Cell-reference link_location arguments are intentionally left unresolved (no dynamic
        // evaluation performed here) rather than misreporting a plan.
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.SetFormula(address, "HYPERLINK(A2,\"Click me\")");

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeFalse();
        plan.Should().BeNull();
    }

    [Fact]
    public void TryCreatePlan_RejectsNonHyperlinkFormulaCell()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1");
        var address = new CellAddress(sheetId, 1, 1);
        sheet.SetFormula(address, "SUM(A1:A2)");

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeFalse();
        plan.Should().BeNull();
    }
}
