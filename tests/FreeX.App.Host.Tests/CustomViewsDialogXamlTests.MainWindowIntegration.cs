using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    [Fact]
    public void MainWindow_CustomViewsApplyRefreshesViewportStatusAndWorksheetFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var methodStart = source.IndexOf("private void CustomViewsBtn_Click(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodStart = source.IndexOf("private void ArrangeAllPickerBtn_Click(", methodStart, StringComparison.Ordinal);
        nextMethodStart.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..nextMethodStart];

        method.Should().Contain("new CustomViewsDialog(_workbook, _commandBus) { Owner = this }");
        method.Should().Contain("dialog.ShowDialog();");
        method.Should().Contain("if (dialog.ViewApplied)");
        method.Should().Contain("UpdateViewport();");
        method.Should().Contain("RefreshStatusBar();");
        method.Should().Contain("FocusSheetGridIfNeeded();");
    }
}
