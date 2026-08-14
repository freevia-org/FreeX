using FluentAssertions;
using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidateDialogPlannerTests
{
    [Fact]
    public void SplitSourceRangeText_PreservesSeparatorsInsideQuotedSheetNames()
    {
        ConsolidateDialogPlanner.SplitSourceRangeText("'Q1, Actuals'!A1:B2; 'Bob''s; Sheet'!C3:D4")
            .Should().Equal("'Q1, Actuals'!A1:B2", "'Bob''s; Sheet'!C3:D4");
    }

    [Fact]
    public void SizeContract_MatchesCommittedWpfLogicalCapture()
    {
        ConsolidateDialogPlanner.WpfWindowWidth.Should().Be(420);
        ConsolidateDialogPlanner.CaptureWidth.Should().Be(380);
        ConsolidateDialogPlanner.CaptureHeight.Should().Be(420);
        ConsolidateDialogPlanner.CaptureContentWidth.Should().Be(341);
        ConsolidateDialogPlanner.CaptureContentHeight.Should().Be(361);
        ConsolidateDialogPlanner.MinWidth.Should().Be(360);
        ConsolidateDialogPlanner.ReferencesListHeight.Should().Be(72);
    }

    [Fact]
    public void ParityFixture_UsesTheSharedSourceAndDestinationState()
    {
        ConsolidateParityFixture.SourceReference.Should().Be("A1:C4");
        ConsolidateParityFixture.DestinationReference.Should().Be("H2");

        var sheetId = SheetId.New();
        ConsolidateParityFixture.CreateSourceRange(sheetId).Should().Be(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3)));
        ConsolidateParityFixture.CreateDestinationCell(sheetId).Should().Be(new CellAddress(sheetId, 2, 8));

        ConsolidateParityFixture.CreateDialogInitialState().Should().Be(
            new ConsolidateDialogInitialState("A1:C4", "H2"));
    }

    [Fact]
    public void TryAddReference_AcceptsDuplicatesWhenConfiguredLikeWpf()
    {
        var sheetId = SheetId.New();

        var added = ConsolidateDialogPlanner.TryAddReference(
            ["A1:B2"],
            "a1:b2",
            (string input, out IReadOnlyList<GridRange> ranges, out string? invalidPart) =>
                ConsolidateInputParser.TryParseSourceRanges(input, sheetId, out ranges, out invalidPart),
            rejectDuplicateReferences: false,
            out var updated,
            out var issue);

        added.Should().BeTrue();
        issue.HasIssue.Should().BeFalse();
        updated.Should().Equal("A1:B2", "a1:b2");
    }

    [Fact]
    public void TryAddReference_CanRejectDuplicateReferencesForShellsThatRequireUniqueListItems()
    {
        var sheetId = SheetId.New();

        var added = ConsolidateDialogPlanner.TryAddReference(
            ["A1:B2"],
            "a1:b2",
            (string input, out IReadOnlyList<GridRange> ranges, out string? invalidPart) =>
                ConsolidateInputParser.TryParseSourceRanges(input, sheetId, out ranges, out invalidPart),
            rejectDuplicateReferences: true,
            out var updated,
            out var issue);

        added.Should().BeFalse();
        updated.Should().Equal("A1:B2");
        issue.Kind.Should().Be(ConsolidateDialogIssueKind.DuplicateSourceReference);
        issue.InvalidPart.Should().Be("a1:b2");
    }

    [Fact]
    public void TryParse_BuildsSharedDialogResultAndRejectsMismatchedSourceSizes()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialogPlanner.TryParse(
            sheetId,
            sourceRangesText: "A1:B2; D1:E2",
            destinationCellText: "G5",
            function: ConsolidateFunction.Average,
            useTopRowLabels: true,
            useLeftColumnLabels: false,
            createLinksToSourceData: true,
            out var result,
            out var issue);

        parsed.Should().BeTrue();
        issue.HasIssue.Should().BeFalse();
        result.SourceRanges.Should().HaveCount(2);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 5, 7));
        result.Function.Should().Be(ConsolidateFunction.Average);
        result.UseTopRowLabels.Should().BeTrue();
        result.CreateLinksToSourceData.Should().BeTrue();

        ConsolidateDialogPlanner.TryParse(
                sheetId,
                sourceRangesText: "A1:B2; D1:F2",
                destinationCellText: "G5",
                out _,
                out issue)
            .Should()
            .BeFalse();
        issue.Kind.Should().Be(ConsolidateDialogIssueKind.MismatchedSourceSizes);
    }

    [Fact]
    public void TryPlanApply_PlansSharedEditsAndOverwriteTargets()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new TextValue("old"));

        var planned = ConsolidateDialogPlanner.TryPlanApply(
            workbook,
            ["A1:B1"],
            "D5",
            (string input, out GridRange range) =>
                WorkbookRangeTextCodec.TryParse(sheet.Id, input, _ => null, out range),
            new ConsolidateOptions { Function = ConsolidateFunction.Sum },
            out var plan,
            out var issue);

        planned.Should().BeTrue();
        issue.HasIssue.Should().BeFalse();
        plan.SourceRanges.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)));
        plan.DestinationCell.Should().Be(new CellAddress(sheet.Id, 5, 4));
        plan.Edits.Should().HaveCount(2);
        plan.OverwriteTargets.Should().Equal(new CellAddress(sheet.Id, 5, 4));
        plan.Edits.Select(edit => ((NumberValue)edit.NewCell.Value!).Value).Should().Equal(2, 3);
    }

    [Fact]
    public void TryPlanApply_RejectsMultiCellDestination()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));

        var planned = ConsolidateDialogPlanner.TryPlanApply(
            workbook,
            ["A1"],
            "D5:E5",
            (string input, out GridRange range) =>
                WorkbookRangeTextCodec.TryParse(sheet.Id, input, _ => null, out range),
            new ConsolidateOptions(),
            out _,
            out var issue);

        planned.Should().BeFalse();
        issue.Kind.Should().Be(ConsolidateDialogIssueKind.InvalidDestinationCell);
    }

    [Fact]
    public void ValidationMessages_UseOneWpfAuthorityContract()
    {
        var source = ConsolidateDialogPlanner.DescribeIssue(
            new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidSourceRange, "bad"),
            ConsolidateDialogMessageContext.FinalValidation);
        var destination = ConsolidateDialogPlanner.DescribeIssue(
            new ConsolidateDialogIssue(ConsolidateDialogIssueKind.InvalidDestinationCell),
            ConsolidateDialogMessageContext.FinalValidation);
        var duplicate = ConsolidateDialogPlanner.DescribeIssue(
            new ConsolidateDialogIssue(ConsolidateDialogIssueKind.DuplicateSourceReference, "A1:B2"),
            ConsolidateDialogMessageContext.AddReference);
        var pending = ConsolidateDialogPlanner.DescribePendingReference();

        source.Message.ResourceKey.Should().Be("Consolidate_EnterValidSourceRangeWithPart");
        source.Message.Arguments.Should().Equal("bad");
        source.FocusTarget.Should().Be(ConsolidateDialogFocusTarget.Reference);
        destination.Message.ResourceKey.Should().Be("Consolidate_EnterValidDestinationCell");
        destination.FocusTarget.Should().Be(ConsolidateDialogFocusTarget.Destination);
        duplicate.Message.ResourceKey.Should().Be("Consolidate_EnterValidSourceRange");
        pending.Message.ResourceKey.Should().Be("Consolidate_AddTheReferenceBeforeClickingOk");
        pending.FocusTarget.Should().Be(ConsolidateDialogFocusTarget.Reference);
    }
}
