using FreeX.App.Services;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void WorkbookStatisticsDialog_CreateMessage_UsesWorkbookStatisticsFormatter()
    {
        var message = WorkbookStatisticsDialog.CreateMessage(new(
            WorksheetCount: 2,
            CellCount: 12,
            FormulaCount: 3,
            CommentCount: 1,
            ChartCount: 4,
            PictureCount: 5,
            ShapeCount: 6,
            NamedRangeCount: 7,
            UsedWorksheetCount: 1,
            TableCount: 8,
            HyperlinkCount: 9));

        message.Should()
            .Contain("Sheets: 2")
            .And.Contain("Used sheets: 1")
            .And.Contain("Tables: 8")
            .And.Contain("Hyperlinks: 9")
            .And.Contain("Named ranges: 7");
    }

    [Fact]
    public void WorkbookStatisticsDialogOpenedFromKeyboard_FocusesOkButton()
    {
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookStatisticsDialog.cs")
            + Environment.NewLine
            // StatusDialogKeyboardFocus was extracted into the shared shell helpers project.
            + DialogSourceTestSupport.ReadShellSources("StatusDialogKeyboardFocus.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
        source.Should().Contain("private static Button? FindDefaultButton");
        source.Should().Contain("button.Focus();");
        source.Should().Contain("Keyboard.Focus(button);");
    }

    [Fact]
    public void WorkbookStatisticsDialog_ProvidesCopyButtonAndDefaultOkButton()
    {
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookStatisticsDialog.cs");

        source.Should().Contain("var copyContent = UiText.Get(\"WorkbookStatistics_CopyToClipboard\");");
        source.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsCopyButton");
        source.Should().Contain("copy.Click += (_, _) => CopyMessageToClipboard(message);");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
    }

    [Fact]
    public void WorkbookStatisticsDialog_UsesSharedDialogSizePlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookStatisticsDialog.cs");
        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("WorkbookStatisticsDialogPlanner.cs");

        source.Should().Contain("Width = WorkbookStatisticsDialogPlanner.Width;");
        source.Should().Contain("Height = WorkbookStatisticsDialogPlanner.Height;");
        source.Should().Contain("MinWidth = WorkbookStatisticsDialogPlanner.MinWidth;");
        source.Should().Contain("MinHeight = WorkbookStatisticsDialogPlanner.MinHeight;");
        WorkbookStatisticsDialogPlanner.Width.Should().Be(500);
        WorkbookStatisticsDialogPlanner.Height.Should().Be(560);
        WorkbookStatisticsDialogPlanner.MinWidth.Should().Be(420);
        WorkbookStatisticsDialogPlanner.MinHeight.Should().Be(420);
        plannerSource.Should().Contain("public static class WorkbookStatisticsDialogPlanner");
    }

    [Fact]
    public void WorkbookStatisticsDialog_StatisticsSummaryIsSelectableAndExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookStatisticsDialog.cs");

        source.Should().Contain("var statisticsBlock = new TextBox");
        source.Should().Contain("IsReadOnly = true");
        source.Should().Contain("AcceptsReturn = true");
        source.Should().Contain("AutomationProperties.SetName(statisticsBlock, UiText.Get(\"WorkbookStatistics_WorkbookStatistics\"));");
        source.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsSummary");
        source.Should().Contain("AutomationProperties.SetHelpText(statisticsBlock, UiText.Get(\"WorkbookStatistics_SummarizesSheetCellFormulaCommentAndObjectCountsForTheWorkbook\"));");
    }

    [Fact]
    public void WorkbookStatisticsDialog_CopyButtonUsesSharedPlatformClipboard()
    {
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookStatisticsDialog.cs");

        source.Should().Contain("_platformClipboard.WriteAsync(new PlatformClipboardContent(Text: message))");
        source.Should().Contain(".AsTask()");
        source.Should().Contain("IsClipboardUnavailableException");
    }
}
