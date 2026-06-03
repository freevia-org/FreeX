using FreeX.Core.Commands;
using FluentAssertions;
using System.IO;

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
            NamedRangeCount: 7));

        message.Should().Contain("Sheets: 2").And.Contain("Named ranges: 7");
    }

    [Fact]
    public void WorkbookStatisticsDialogOpenedFromKeyboard_FocusesOkButton()
    {
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "StatusDialogKeyboardFocus.cs")));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
        source.Should().Contain("private static Button? FindDefaultButton");
        source.Should().Contain("button.Focus();");
        source.Should().Contain("Keyboard.Focus(button);");
    }

    [Fact]
    public void WorkbookStatisticsDialog_UsesSingleExcelLikeOkButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs"));

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly");
        source.Should().NotContain("DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true");
    }

    [Fact]
    public void WorkbookStatisticsDialog_StatisticsSummaryExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(statisticsBlock, UiText.Get(\"WorkbookStatistics_WorkbookStatistics\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(statisticsBlock, \"WorkbookStatisticsSummary\");");
        source.Should().Contain("AutomationProperties.SetHelpText(statisticsBlock, UiText.Get(\"WorkbookStatistics_SummarizesSheetCellFormulaCommentAndObjectCountsForTheWorkbook\"));");
    }
}
