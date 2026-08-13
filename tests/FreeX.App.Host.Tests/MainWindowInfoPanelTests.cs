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
            "InfoFormulaErrorSummary",
            "InfoFileSize",
            "InfoLastModified",
            "InfoShareStatus",
            "InfoWorkbookProtectionSummary",
            "InfoActiveSheetProtectionSummary"
        ]);

        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .Contain(UiText.Get("MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings"));
    }

    [Fact]
    public void UpdateInfoView_RefreshesModelBackedStatisticsProtectionAndAccessibility()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        source.Should().Contain("var activeSheet = _workbook.GetSheet(_currentSheetId);");
        source.Should().Contain("BackstageInfoPlanner.Build(");
        source.Should().Contain("_workbook,");
        source.Should().Contain("_currentFilePath,");
        source.Should().Contain("WpfResourceKeyTextResolver.Instance,");
        source.Should().Contain("activeSheet,");
        source.Should().Contain("hasSelection: SheetGrid.SelectedRange is not null");
        source.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        source.Should().Contain("BackstageInfoPlanner.CreatePaneRequest(info)");
        source.Should().NotContain("CreateBackstageInfoPaneRequest(");
        source.Should().Contain("ResolveBackstageInfoDetailTextBlock(detail.Id).Text = ResolveBackstageTextValue(detail.Value);");
        source.Should().Contain("FreeXBackstageInfoDetailId.Share => InfoShareStatus");
        source.Should().Contain("FreeXBackstageInfoDetailId.WorkbookProtection => InfoWorkbookProtectionSummary");
        source.Should().Contain("FreeXBackstageInfoDetailId.ActiveSheetProtection => InfoActiveSheetProtectionSummary");
        source.Should().Contain("FreeXBackstageInfoDetailId.WorkbookStatistics => InfoStatisticsSummary");
        source.Should().Contain("FreeXBackstageInfoDetailId.Accessibility => InfoAccessibilitySummary");
        source.Should().Contain("FreeXBackstageInfoDetailId.FormulaErrors => InfoFormulaErrorSummary");
    }
}
