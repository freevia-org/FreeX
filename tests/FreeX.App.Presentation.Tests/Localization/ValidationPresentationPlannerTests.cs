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
    public void GoalSeekValidation_PreservesRendererSpecificTextAndSharedFocus()
    {
        var result = GoalSeekRequestParseResult.Invalid(
            GoalSeekRequestParseError.InvalidSetCellAddress,
            "bad");

        var wpf = GoalSeekStatusDialogPlanner.DescribeValidationError(result, GoalSeekPresentationProfile.Wpf);
        var avalonia = GoalSeekStatusDialogPlanner.DescribeValidationError(result, GoalSeekPresentationProfile.Avalonia);

        wpf.Message.ResourceKey.Should().Be("GoalSeek_InvalidCellAddressMessage");
        wpf.Message.Arguments.Should().Equal("bad");
        avalonia.Message.LiteralText.Should().Be("Set cell 'bad' is not a valid cell reference.");
        wpf.FocusTarget.Should().Be(GoalSeekValidationFocusTarget.SetCell);
        avalonia.FocusTarget.Should().Be(GoalSeekValidationFocusTarget.SetCell);
    }

    [Fact]
    public void GoalSeekStatus_PreservesWpfAndAvaloniaFormattingProfiles()
    {
        var wpf = GoalSeekStatusDialogPlanner.DescribeStatus(true, 10, 9.5, 2, GoalSeekPresentationProfile.Wpf);
        var avalonia = GoalSeekStatusDialogPlanner.DescribeStatus(false, 10, 9.5, 2, GoalSeekPresentationProfile.Avalonia);

        wpf.ResourceKey.Should().Be("GoalSeekStatus_SuccessSummary");
        wpf.Arguments.Should().Equal("10", "9.5", "2");
        avalonia.LiteralText.Should().StartWith("Goal Seek could not find a solution.");
    }

    [Fact]
    public void DialogValidationDescriptors_CarryResourceAndFocusTogether()
    {
        var textToColumns = TextToColumnsDialogPlanner.DescribeValidationIssue(
            TextToColumnsDialogValidationIssue.InvalidThousandsSeparator);
        var comment = ThreadedCommentDialogPlanner.DescribeValidationError(
            ThreadedCommentDialogValidationError.EnterReply);
        var fill = FillSeriesPlanner.DescribeInputError(
            FillSeriesInputError.InvalidStop,
            FillSeriesValidationTextProfile.Avalonia);

        textToColumns.Message.ResourceKey.Should().Be("TextToColumns_EnterASingleThousandsSeparator");
        textToColumns.FocusTarget.Should().Be(TextToColumnsDialogFocusTarget.ThousandsSeparator);
        comment!.Message.ResourceKey.Should().Be("ThreadedComment_EnterReplyMessage");
        comment.FocusTarget.Should().Be(ThreadedCommentDialogFocusTarget.Reply);
        fill.Message.ResourceKey.Should().Be("FillSeries_InvalidStop");
        fill.FocusTarget.Should().Be(FillSeriesInputFocusTarget.StopValue);
    }

    [Fact]
    public void HyperlinkValidation_PreservesExactRendererProfiles()
    {
        var wpf = HyperlinkDialogPlanner.DescribeValidationError(
            HyperlinkDialogValidationError.MissingDocumentLocation,
            HyperlinkDialogTextProfile.Wpf);
        var avalonia = HyperlinkDialogPlanner.DescribeValidationError(
            HyperlinkDialogValidationError.MissingDocumentLocation,
            HyperlinkDialogTextProfile.Avalonia);

        wpf.Message.ResourceKey.Should().Be("Hyperlink_EnterValidCellReferenceOrDefinedName");
        avalonia.Message.LiteralText.Should().Be("Enter a cell reference or defined name.");
        wpf.FocusTarget.Should().Be(HyperlinkDialogFocusTarget.Target);
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
    public void PivotMessages_PreserveRendererProfilesAndTypedSuccess()
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

        PivotApplicationMessagePlanner.DescribeIssue(message, PivotMessageTextProfile.Wpf)
            .ResourceKey.Should().Be("MainWindowMessage_PivotTableSelectSourceRange");
        PivotApplicationMessagePlanner.DescribeIssue(message, PivotMessageTextProfile.Avalonia)
            .ResourceKey.Should().Be("PivotLoc_SelectRangeForPivot");
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
