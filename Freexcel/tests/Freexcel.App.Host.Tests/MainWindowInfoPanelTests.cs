using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowInfoPanelTests
{
    [Fact]
    public void BackstageInfo_ExposesWorkbookStatisticAndSummaryFields()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var names = document
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        names.Should().Contain([
            "InfoCellsWithData",
            "InfoFormulaCount",
            "InfoCommentCount",
            "InfoChartCount",
            "InfoPictureCount",
            "InfoShapeCount",
            "InfoNamedRangeCount",
            "InfoWorkbookProtection",
            "InfoSheetProtection",
            "InfoAccessibilitySummary"
        ]);
    }

    [Fact]
    public void UpdateInfoView_RefreshesModelBackedStatisticsProtectionAndAccessibility()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("InfoPanelSummaryPlanner.Create(");
        source.Should().Contain("_workbook.GetSheet(_currentSheetId)");
        source.Should().Contain("InfoCellsWithData.Text");
        source.Should().Contain("InfoWorkbookProtection.Text");
        source.Should().Contain("InfoSheetProtection.Text");
        source.Should().Contain("InfoAccessibilitySummary.Text");
    }
}
