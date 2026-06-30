using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class DialogSharedHelperSourceTests
{
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
        hyperlink.Should().Contain("DialogButtonRowFactory.Create(OnOk, buttonWidth: 75)");
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

    private static string ReadHostSource(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "freep", "FreeP.App.Host", fileName));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
