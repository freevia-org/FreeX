using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class DialogSharedHelperSourceTests
{
    [Fact]
    public void FreePDialogs_RouteChromeThroughSharedDialogWindow()
    {
        var shellDialogWindow = ReadWorkspaceSource("shared", "Free.Shared.Shell.Wpf", "DialogWindow.cs");
        shellDialogWindow.Should().Contain("/Free.Shared.Shell.Wpf;component/DialogResources.xaml");

        var ribbonDialogWindow = ReadWorkspaceSource("shared", "Free.Shared.Ribbon.Wpf", "DialogWindow.cs");
        ribbonDialogWindow.Should().Contain("Free.Shared.Shell.Wpf.DialogWindow");
        ribbonDialogWindow.Should().NotContain("DialogResources.xaml");

        AssertUsesSharedDialogWindow("ChartDataDialog.cs", "ChartDataDialog");
        AssertUsesSharedDialogWindow("FindReplaceDialog.cs", "FindReplaceDialog");
        AssertUsesSharedDialogWindow("HyperlinkDialog.cs", "HyperlinkDialog");
        AssertUsesSharedDialogWindow("SlideSizeDialog.cs", "SlideSizeDialog");
    }

    [Fact]
    public void FreePModalDialogs_RouteOkCancelRowsThroughSharedFactory()
    {
        var slideSize = ReadHostSource("SlideSizeDialog.cs");
        slideSize.Should().Contain("DialogButtonRowFactory.Create(");
        slideSize.Should().Contain("buttonWidth: 80");
        slideSize.Should().Contain("DialogMessageHelper.ShowWarning(this, validation.Message, validation.Caption)");
        slideSize.Should().Contain("DialogFocus.FocusAndSelect(box)");
        slideSize.Should().NotContain("Content = \"OK\"");
        slideSize.Should().NotContain("Content = \"Cancel\"");
        slideSize.Should().NotContain("MessageBox.Show(");

        var hyperlink = ReadHostSource("HyperlinkDialog.cs");
        hyperlink.Should().Contain("DialogButtonRowFactory.Create(");
        hyperlink.Should().Contain("buttonWidth: 75");
        hyperlink.Should().Contain("acceptContent: Surface.AcceptLabel");
        hyperlink.Should().Contain("cancelContent: Surface.CancelLabel");
        hyperlink.Should().Contain("DialogMessageHelper.ShowWarning(this, validation.Message, validation.Caption)");
        hyperlink.Should().Contain("DialogFocus.FocusAndSelect(_urlBox)");
        hyperlink.Should().NotContain("Content = \"OK\"");
        hyperlink.Should().NotContain("Content = \"Cancel\"");
        hyperlink.Should().NotContain("MessageBox.Show(");

        var chartData = ReadHostSource("ChartDataDialog.cs");
        chartData.Should().Contain("DialogButtonRowFactory.Create(");
        chartData.Should().Contain("buttonWidth: 80");
        chartData.Should().NotContain("Content = \"OK\"");
        chartData.Should().NotContain("Content = \"Cancel\"");
    }

    private static void AssertUsesSharedDialogWindow(string fileName, string className)
    {
        var source = ReadHostSource(fileName);
        source.Should().Contain($"public sealed class {className} : Free.Shared.Ribbon.Wpf.DialogWindow");
        source.Should().NotContain($"public sealed class {className} : Window");
        source.Should().NotContain($"public sealed class {className} : System.Windows.Window");
    }

    private static string ReadHostSource(string fileName) =>
        ReadWorkspaceSource("freep", "FreeP.App.Host", fileName);

    private static string ReadWorkspaceSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        relativeParts.CopyTo(parts, 1);
        return File.ReadAllText(Path.Combine(parts));
    }

}
