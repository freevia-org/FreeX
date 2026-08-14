using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.FillSeries;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.TextToColumns;
using FreeX.App.Services;

namespace FreeX.App.Presentation.Tests.Localization;

public sealed class ValidationPresentationPlannerTests
{
    [Fact]
    public void GoalSeekValidation_UsesOneSharedMessageAndFocusContract()
    {
        var result = GoalSeekRequestParseResult.Invalid(
            GoalSeekRequestParseError.InvalidSetCellAddress,
            "bad");

        var presentation = GoalSeekStatusDialogPlanner.DescribeValidationError(result);

        presentation.Message.ResourceKey.Should().Be("GoalSeek_InvalidCellAddressMessage");
        presentation.Message.Arguments.Should().Equal("bad");
        presentation.FocusTarget.Should().Be(GoalSeekValidationFocusTarget.SetCell);
    }

    [Fact]
    public void GoalSeekStatus_UsesWpfAuthorityPrecisionAndInvariantFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var target = 1.234567890123;
            var actual = 9.876543210987;
            var found = 2.345678901234;
            var presentation = GoalSeekStatusDialogPlanner.DescribeStatus(true, target, actual, found);

            presentation.ResourceKey.Should().Be("GoalSeekStatus_SuccessSummary");
            presentation.Arguments.Should().Equal(
                target.ToString("G10", CultureInfo.InvariantCulture),
                actual.ToString("G10", CultureInfo.InvariantCulture),
                found.ToString("G10", CultureInfo.InvariantCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void GoalSeekExecutionFailure_PreservesServiceErrorsAndExactFallbacks()
    {
        GoalSeekStatusDialogPlanner.DescribeExecutionFailure(
                WorkbookGoalSeekStatus.InvalidRequest,
                null,
                "B2",
                "A1")
            .Should().BeEquivalentTo(new
            {
                ResourceKey = "GoalSeek_InvalidRequestFormat",
                Arguments = new object?[] { "B2" }
            });
        GoalSeekStatusDialogPlanner.DescribeExecutionFailure(
                WorkbookGoalSeekStatus.ApplyFailed,
                null,
                "B2",
                "A1")
            .Should().BeEquivalentTo(new
            {
                ResourceKey = "GoalSeek_ResultCouldNotBeAppliedFormat",
                Arguments = new object?[] { "A1" }
            });
        GoalSeekStatusDialogPlanner.DescribeExecutionFailure(
                WorkbookGoalSeekStatus.ApplyFailed,
                "Core failure",
                "B2",
                "A1")
            .LiteralText.Should().Be("Core failure");
    }

    [Fact]
    public void DialogValidationDescriptors_CarryResourceAndFocusTogether()
    {
        var textToColumns = TextToColumnsDialogPlanner.DescribeValidationIssue(
            TextToColumnsDialogValidationIssue.InvalidThousandsSeparator);
        var comment = ThreadedCommentDialogPlanner.DescribeValidationError(
            ThreadedCommentDialogValidationError.EnterReply);
        var fill = FillSeriesPlanner.DescribeInputError(FillSeriesInputError.InvalidStop);

        textToColumns.Message.ResourceKey.Should().Be("TextToColumns_EnterASingleThousandsSeparator");
        textToColumns.FocusTarget.Should().Be(TextToColumnsDialogFocusTarget.ThousandsSeparator);
        comment!.Message.ResourceKey.Should().Be("ThreadedComment_EnterReplyMessage");
        comment.FocusTarget.Should().Be(ThreadedCommentDialogFocusTarget.Reply);
        fill.Message.ResourceKey.Should().Be("FillSeriesStep_InvalidStopMessage");
        fill.FocusTarget.Should().Be(FillSeriesInputFocusTarget.StopValue);
    }

    [Fact]
    public void FillSeriesCompletionDescriptors_PreserveResourceKeysAndCoreErrors()
    {
        FillSeriesPlanner.DescribeNoSeed().ResourceKey.Should().Be("FillSeries_NoSeed");
        FillSeriesPlanner.DescribeCommandFailure(null).ResourceKey.Should().Be("FillSeries_Failed");
        FillSeriesPlanner.DescribeCommandFailure("Core failure").LiteralText.Should().Be("Core failure");

        var success = FillSeriesPlanner.DescribeSuccess("A1:A5");
        success.ResourceKey.Should().Be("FillSeries_Filled");
        success.Arguments.Should().Equal("A1:A5");
    }

    [Fact]
    public void HyperlinkValidation_UsesSharedLocalizedWpfAuthorityMessage()
    {
        var presentation = HyperlinkDialogPlanner.DescribeValidationError(
            HyperlinkDialogValidationError.MissingDocumentLocation);

        presentation.Message.ResourceKey.Should().Be("Hyperlink_EnterValidCellReferenceOrDefinedName");
        presentation.Message.LiteralText.Should().BeNull();
        presentation.FocusTarget.Should().Be(HyperlinkDialogFocusTarget.Target);
    }

    [Fact]
    public void ChartValidation_ProjectsMessageAndFieldFromOneOwner()
    {
        var axis = ChartValidationPresentationPlanner.Describe(ChartAxisFormatParseIssue.LabelAngle);
        var area = ChartValidationPresentationPlanner.Describe(ChartAreaFormatParseIssue.LegendFontSize);
        var series = ChartValidationPresentationPlanner.Describe(ChartSeriesFormatParseIssue.MarkerSize);

        axis.Message.ResourceKey.Should().Be("ChartAxisFormat_InvalidLabelAngleMessage");
        axis.FocusTarget.Should().Be(ChartAxisDialogFieldId.LabelAngle);
        area.Message.ResourceKey.Should().Be("ChartAreaLegend_InvalidLegendFontSizeMessage");
        area.FocusTarget.Should().Be(ChartAreaFormatDialogFieldId.LegendFontSize);
        series.Message.ResourceKey.Should().Be("ChartSeriesFormat_InvalidMarkerSizeMessage");
        series.FocusTarget.Should().Be(ChartSeriesFormatDialogFieldId.MarkerSize);
    }

    [Fact]
    public void ChartCommandResult_PreservesLocalizedSuccessFallbackAndCoreError()
    {
        var success = ChartWorkflowCommandCatalog.DescribeCommandResult(true, "Legend");
        var fallback = ChartWorkflowCommandCatalog.DescribeCommandResult(false, "Legend");
        var coreError = ChartWorkflowCommandCatalog.DescribeCommandResult(false, "Legend", "Core failure");

        success.ResourceKey.Should().Be(ChartWorkflowCommandCatalog.CommandAppliedStatusResourceKey);
        success.Arguments.Should().Equal("Legend");
        fallback.ResourceKey.Should().Be(ChartWorkflowCommandCatalog.CommandFailedStatusResourceKey);
        fallback.Arguments.Should().Equal("Legend");
        coreError.LiteralText.Should().Be("Core failure");
    }

    [Fact]
    public void PivotMessages_UseSharedWpfAuthorityResourcesAndTypedSuccess()
    {
        var message = new PivotMessageModel(
            PivotApplicationIssue.MissingSource,
            PivotMessageSeverity.Information);
        var outcome = new PivotApplicationOutcome(
            Action: PivotApplicationAction.Refresh,
            Success: true,
            Executed: true,
            IsNoOp: false,
            Transition: PivotDisplayTransition.None,
            Message: null,
            StatusArgument: "Pivot1");

        PivotApplicationMessagePlanner.DescribeIssue(message)
            .ResourceKey.Should().Be("MainWindowMessage_PivotTableSelectSourceRange");
        PivotApplicationMessagePlanner.DescribeSuccess(outcome).Arguments.Should().Equal("Pivot1");
    }

    [Fact]
    public void PrintPreviewValidation_UsesTypedFocusTargets()
    {
        var page = PrintPreviewDialogPlanner.DescribeInvalidPageNumber(7);
        var range = PrintPreviewDialogPlanner.DescribeInvalidPageRange(
            "From page must be less than or equal to To page.",
            PrintPreviewValidationFocusTarget.ToPage);

        page.Message.ResourceKey.Should().Be("PrintPreview_InvalidPageNumberMessage");
        page.Message.Arguments.Should().Equal(7);
        page.FocusTarget.Should().Be(PrintPreviewValidationFocusTarget.PageNumber);
        range.FocusTarget.Should().Be(PrintPreviewValidationFocusTarget.ToPage);
        PrintPreviewDialogPlanner.InitialFocusCommand.Should().Be(PrintPreviewToolbarCommand.Print);
        PrintSettingsPlanner.InitialDialogFocusTarget.Should().Be(PrintDialogFocusTarget.ConfirmAction);
    }

    [Fact]
    public void AdvancedFilterInlineValidation_PreservesExactInvalidText()
    {
        var result = AdvancedFilterPlanResult.Invalid(
            AdvancedFilterPlanError.InvalidListRange,
            "bad range");

        var presentation = AdvancedFilterPlanner.DescribeError(
            result,
            AdvancedFilterErrorPresentationKind.InlineValidation);

        presentation.Message.LiteralText.Should().Be("Enter a valid list range. (bad range)");
        presentation.FocusTarget.Should().Be(AdvancedFilterErrorFocusTarget.ListRange);
    }
}
