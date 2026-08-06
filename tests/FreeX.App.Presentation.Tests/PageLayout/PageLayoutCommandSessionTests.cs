using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutCommandSessionTests
{
    [Fact]
    public void PlanMarginsPreset_AppliesSharedPresetToEveryTargetSheet()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var expected = PageLayoutRibbonActionPlanner.PlanMarginsPreset(PageLayoutMarginPreset.Wide);
        var session = new PageLayoutCommandSession([first.Id, second.Id]);

        var plan = session.PlanMarginsPreset(PageLayoutMarginPreset.Wide);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.Command.Should().BeOfType<CompositeWorkbookCommand>();
        plan.CommandLabel.Should().Be(PageLayoutRibbonActionPlanner.PageMarginsCommandLabel);
        plan.Status.Should().Be(PageLayoutStatusPlanner.ForPreset(expected));
        foreach (var sheet in new[] { first, second })
        {
            sheet.PageMargins.Should().Be(expected.Value);
            sheet.HeaderMargin.Should().Be(expected.HeaderMargin);
            sheet.FooterMargin.Should().Be(expected.FooterMargin);
        }
    }

    [Fact]
    public void PlanSetPrintArea_RemapsSourceSelectionForEveryTargetSheet()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Source");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var selection = Range(source.Id, 2, 3, 8, 6);
        var session = new PageLayoutCommandSession([first.Id, second.Id]);

        var plan = session.PlanSetPrintArea(selection);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        first.PrintArea.Should().Be(Range(first.Id, 2, 3, 8, 6));
        second.PrintArea.Should().Be(Range(second.Id, 2, 3, 8, 6));
        plan.Status.Should().Be(PageLayoutStatusPlanner.PrintAreaSet);
    }

    [Fact]
    public void PlanPageBreakAction_PreservesPlannerStatusAndBatchesBreakSets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var session = new PageLayoutCommandSession([first.Id, second.Id]);

        var plan = session.PlanPageBreakAction(
            PageBreakMenuAction.Insert,
            Range(first.Id, 5, 4, 5, 4),
            currentRowBreaks: [2u],
            currentColumnBreaks: [3u]);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.SuccessStatusText.Should().Be("Inserted page breaks");
        foreach (var sheet in new[] { first, second })
        {
            sheet.RowPageBreaks.Should().Equal(2u, 5u);
            sheet.ColumnPageBreaks.Should().Equal(3u, 4u);
        }
    }

    [Fact]
    public void PlanHeaderFooter_BatchesPortableEditorResult()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var request = new PageSetupHeaderFooterRequest
        {
            Header = new WorksheetHeaderFooter("Left", "Center", "Right"),
            DifferentFirstPage = true,
            ScaleHeaderFooterWithDocument = false,
        };
        var session = new PageLayoutCommandSession([first.Id, second.Id]);

        var plan = session.PlanHeaderFooter(request);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        foreach (var sheet in new[] { first, second })
        {
            sheet.PageHeader.Should().Be(request.Header);
            sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
            sheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        }
    }

    [Fact]
    public void TryPlanPageSetup_ValidatesBuildsEveryTargetAndPreservesPrintPreviewFollowUp()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Source");
        var target = workbook.AddSheet("Target");
        var fields = PageSetupDialogModel.FromSheet(source) with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            PrintAreaText = "$B$2:$D$8",
        };
        var session = new PageLayoutCommandSession([source.Id, target.Id]);

        var build = session.TryPlanPageSetup(source, fields, PageSetupDialogAction.PrintPreview);
        var outcome = build.Plan!.Execution.Command.Apply(new TestCommandContext(workbook));

        build.Success.Should().BeTrue();
        build.Validation.Should().BeNull();
        build.Plan.FollowUpAction.Should().Be(PageSetupDialogFollowUpAction.PrintPreview);
        build.Plan.Execution.CommandLabel.Should().Be(PageSetupSubmissionPlanner.DefaultCommandLabel);
        build.Plan.Execution.Status.Should().Be(PageLayoutStatusPlanner.PageSetupSubmission);
        outcome.Success.Should().BeTrue();
        foreach (var sheet in new[] { source, target })
        {
            sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
            sheet.PrintArea.Should().Be(Range(sheet.Id, 2, 2, 8, 4));
        }
    }

    [Fact]
    public void TryPlanPageSetup_ReturnsPortableValidationWithoutBuildingACommand()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var fields = PageSetupDialogModel.FromSheet(sheet) with { PrintAreaText = "not a range" };
        var session = new PageLayoutCommandSession([sheet.Id]);

        var build = session.TryPlanPageSetup(sheet, fields);

        build.Success.Should().BeFalse();
        build.Plan.Should().BeNull();
        build.Validation!.Target.Should().Be(PageSetupValidationTarget.PrintArea);
    }

    [Fact]
    public void PlanMovePageBreak_UsesPortableBreakMutationForEveryTarget()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var session = new PageLayoutCommandSession([first.Id, second.Id]);

        var plan = session.PlanMovePageBreak(
            PageBreakAxis.Column,
            originalIndex: 3,
            newIndex: 6,
            currentRowBreaks: [2u],
            currentColumnBreaks: [3u, 7u]);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        foreach (var sheet in new[] { first, second })
        {
            sheet.RowPageBreaks.Should().Equal(2u);
            sheet.ColumnPageBreaks.Should().Equal(6u, 7u);
        }
    }

    [Fact]
    public void Constructor_DeduplicatesTargetsAndRejectsEmptyTargetSet()
    {
        var sheetId = SheetId.New();

        new PageLayoutCommandSession([sheetId, sheetId]).TargetSheetIds.Should().Equal(sheetId);
        var action = () => new PageLayoutCommandSession([]);
        action.Should().Throw<ArgumentException>();
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
