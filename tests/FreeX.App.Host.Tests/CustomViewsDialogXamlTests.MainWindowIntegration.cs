using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    [Fact]
    public void MainWindow_CustomViewsApplyRefreshesViewportStatusAndWorksheetFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");
        var methodStart = source.IndexOf("private void CustomViewsBtn_Click(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodStart = source.IndexOf("private void ArrangeAllPickerBtn_Click(", methodStart, StringComparison.Ordinal);
        nextMethodStart.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..nextMethodStart];

        method.Should().Contain("new CustomViewsDialog(_workbook, ExecuteCustomViewDialogCommand) { Owner = this }");
        method.Should().Contain("SyncWorkbookActiveSheetIndex();");
        method.Should().Contain("dialog.ShowDialog();");
        method.Should().Contain("if (dialog.ViewApplied)");
        method.Should().Contain("ApplyCustomViewWorkbookViewState();");
        method.Should().Contain("RefreshStatusBar();");
        method.Should().Contain("FocusSheetGridIfNeeded();");
    }
}
