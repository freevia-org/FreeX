using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowInfoPanelTests
{
    [Fact]
    public void BackstageInfo_ExposesWorkbookStatisticAndSummaryFields()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("MainWindow.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var names = document
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        names.Should().Contain([
            "InfoStatisticsSummary",
            "InfoAccessibilitySummary",
            "InfoFormulaErrorSummary"
        ]);

        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .Contain(UiText.Get("MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings"));
    }

    [Fact]
    public void UpdateInfoView_RefreshesModelBackedStatisticsProtectionAndAccessibility()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        source.Should().Contain("BackstageInfoPlanner.Build(_workbook, _currentFilePath)");
        source.Should().Contain("InfoStatisticsSummary.Text");
        source.Should().Contain("InfoAccessibilitySummary.Text");
        source.Should().Contain("InfoFormulaErrorSummary.Text");
    }
}
