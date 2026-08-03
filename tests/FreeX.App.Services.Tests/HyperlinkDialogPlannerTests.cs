using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class HyperlinkDialogPlannerTests
{
    [Fact]
    public void DialogSize_MatchesSharedWpfLogicalEvidenceTarget()
    {
        HyperlinkDialogPlanner.Width.Should().Be(560);
        HyperlinkDialogPlanner.Height.Should().Be(300);
        HyperlinkDialogPlanner.MinWidth.Should().Be(520);
        HyperlinkDialogPlanner.MinHeight.Should().Be(300);
    }

    [Fact]
    public void PresentationMetrics_MatchWpfHyperlinkDialogConsumers()
    {
        HyperlinkDialogPlanner.DialogMargin.Should().Be(16);
        HyperlinkDialogPlanner.LinkTypeColumnWidth.Should().Be(170);
        HyperlinkDialogPlanner.LinkTypeColumnGap.Should().Be(12);
        HyperlinkDialogPlanner.LabelColumnWidth.Should().Be(110);
        HyperlinkDialogPlanner.FieldHeight.Should().Be(24);
        HyperlinkDialogPlanner.FieldBottomMargin.Should().Be(8);
        HyperlinkDialogPlanner.ButtonGap.Should().Be(8);
        HyperlinkDialogPlanner.SecondaryButtonWidth.Should().Be(96);
        HyperlinkDialogPlanner.ActionButtonWidth.Should().Be(72);
        HyperlinkDialogPlanner.LinkTypeListHeight.Should().Be(96);
    }

    [Fact]
    public void ParityFixture_SeedsTheAuthoritativeDialogContent()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var sheet = new Sheet(sheetId, "Sheet1");

        HyperlinkDialogParityFixture.Seed(sheet, address);

        sheet.GetCell(address)?.Value.Should().Be(new TextValue(HyperlinkDialogParityFixture.DisplayText));
        sheet.Hyperlinks[address].Should().Be(HyperlinkDialogParityFixture.Target);
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage));
    }

    [Fact]
    public void Plan_UsesTargetAsDisplayTextWhenLabelIsBlank()
    {
        var plan = HyperlinkDialogPlanner.Plan("https://example.test", " ");

        plan.Should().Be(new HyperlinkDialogPlan(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "https://example.test",
            "https://example.test",
            "",
            ""));
    }

    [Fact]
    public void Plan_TrimsTargetDisplayTextScreenTipAndBookmark()
    {
        var plan = HyperlinkDialogPlanner.Plan(
            " Sheet1!A1 ",
            " Jump ",
            HyperlinkTargetKind.PlaceInThisDocument,
            "  Open budget cell  ",
            "  BudgetAnchor  ");

        plan.Should().Be(new HyperlinkDialogPlan(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Sheet1!A1",
            "Jump",
            "Open budget cell",
            "BudgetAnchor"));
    }

    [Theory]
    [InlineData(HyperlinkTargetKind.ExistingFileOrWebPage, HyperlinkDialogValidationError.MissingAddress)]
    [InlineData(HyperlinkTargetKind.CreateNewDocument, HyperlinkDialogValidationError.MissingNewDocumentName)]
    [InlineData(HyperlinkTargetKind.PlaceInThisDocument, HyperlinkDialogValidationError.MissingDocumentLocation)]
    [InlineData(HyperlinkTargetKind.EmailAddress, HyperlinkDialogValidationError.MissingEmailAddress)]
    public void TryPlan_ReportsLinkTypeSpecificBlankTargetErrors(
        HyperlinkTargetKind linkType,
        HyperlinkDialogValidationError expectedError)
    {
        HyperlinkDialogPlanner.TryPlan(" ", "Label", linkType, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("review@")]
    [InlineData("@example.test")]
    [InlineData("review@example test")]
    public void TryPlan_RejectsInvalidEmailTarget(string target)
    {
        HyperlinkDialogPlanner.TryPlan(target, "Label", HyperlinkTargetKind.EmailAddress, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(HyperlinkDialogValidationError.InvalidEmailAddress);
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test")]
    [InlineData("mailto:review@example.test", "mailto:review@example.test")]
    public void TryPlan_AcceptsAndNormalizesEmailTarget(string target, string expectedTarget)
    {
        HyperlinkDialogPlanner.TryPlan(target, "Label", HyperlinkTargetKind.EmailAddress, "", "", out var plan, out var error)
            .Should()
            .BeTrue();

        error.Should().Be(HyperlinkDialogValidationError.None);
        plan.Target.Should().Be(expectedTarget);
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test", "review@example.test")]
    [InlineData("mailto:review@example.test?subject=Budget", "mailto:review@example.test?subject=Budget", "review@example.test")]
    public void Plan_NormalizesEmailTargetWithoutLeakingMailtoIntoBlankDisplay(
        string target,
        string expectedTarget,
        string expectedDisplayText)
    {
        var plan = HyperlinkDialogPlanner.Plan(target, " ", HyperlinkTargetKind.EmailAddress);

        plan.Should().Be(new HyperlinkDialogPlan(
            HyperlinkTargetKind.EmailAddress,
            expectedTarget,
            expectedDisplayText,
            "",
            ""));
    }

    [Fact]
    public void Prefill_FromCellUsesExistingHyperlinkMetadataAndDisplayText()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 4, 2);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.SetCell(address, new TextValue("Quarterly report"));
        sheet.Hyperlinks[address] = " https://example.test/report ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open report",
            "ReportBookmark");

        HyperlinkDialogPrefill.FromCell(sheet, address).Should().Be(new HyperlinkDialogPrefill(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "https://example.test/report",
            "Quarterly report",
            "Open report",
            "ReportBookmark"));
    }

    [Fact]
    public void Prefill_FromBlankCellUsesDefaultWebTarget()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var sheet = new Sheet(sheetId, "Sheet1");

        HyperlinkDialogPrefill.FromCell(sheet, address).Should().Be(new HyperlinkDialogPrefill(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "https://",
            "",
            "",
            ""));
    }

    [Fact]
    public void Prefill_FromCellUsesExistingCellTextAsDisplayText()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 4, 2);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.SetCell(address, new TextValue("Quarterly report"));

        HyperlinkDialogPrefill.FromCell(sheet, address).Should().Be(new HyperlinkDialogPrefill(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "https://",
            "Quarterly report",
            "",
            ""));
    }
}
