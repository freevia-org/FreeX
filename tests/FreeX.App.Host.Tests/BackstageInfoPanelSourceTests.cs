using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageInfoPanelSourceTests
{
    [Fact]
    public void InfoPanel_ExposesCommandActionButtonsWithStableNames()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var expectedNames = new[]
        {
            "InfoProtectWorkbookButton",
            "InfoCheckAccessibilityButton",
            "InfoWorkbookStatisticsButton",
            "InfoErrorCheckingButton"
        };

        foreach (var name in expectedNames)
        {
            var button = document
                .Descendants()
                .Single(element => element.Attribute(x + "Name")?.Value == name);

            button.Attribute("Click").Should().BeNull("Backstage Info action buttons are wired in code so XAML inventory counts stay stable");
            button.Attribute("AutomationProperties.AutomationId").Should().BeNull();
        }
    }

    [Fact]
    public void InfoPanel_ExposesRicherFileAndProtectionProperties()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var propertyFields = new[]
        {
            "InfoFileSize",
            "InfoLastModified",
            "InfoShareStatus",
            "InfoExportStatus",
            "InfoWorkbookProtectionSummary",
            "InfoActiveSheetProtectionSummary"
        };

        foreach (var field in propertyFields)
        {
            document
                .Descendants()
                .Where(element => element.Attribute(x + "Name")?.Value == field)
                .Should()
                .ContainSingle();
        }
    }

    [Fact]
    public void BackstageCodeBehind_PopulatesInfoPanelFromActiveSheetAwarePlan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        source.Should().Contain("var activeSheet = _workbook.GetSheet(_currentSheetId);");
        source.Should().Contain("BackstageInfoPlanner.Build(");
        source.Should().Contain("hasSelection: SheetGrid.SelectedRange is not null");
        source.Should().Contain("InfoFileSize.Text = plan.FileSize;");
        source.Should().Contain("InfoShareStatus.Text = plan.SharingStatus;");
        source.Should().Contain("InfoExportStatus.Text = plan.ExportStatus;");
        source.Should().Contain("InfoWorkbookProtectionSummary.Text = plan.Summary.WorkbookProtectionSummary;");
        source.Should().Contain("ProtectWorkbookBtn_Click(sender, e);");
    }

    [Fact]
    public void BackstageCodeBehind_WiresInfoPanelActionsWithAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var catalogSource = DialogSourceTestSupport.ReadAppServicesSource("FreeXBackstagePaneCatalog.cs");
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        constructorSource.Should().Contain("ConfigureBackstageInfoActionButtons();");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.WpfInfoPane)");
        source.Should().Contain("InfoProtectWorkbookButton.Click += InfoProtectWorkbookBtn_Click;");
        source.Should().Contain("ConfigureBackstageInfoActionButton(");
        source.Should().Contain("action.AutomationId");
        catalogSource.Should().Contain("\"BackstageInfoCheckAccessibilityButton\"");
        catalogSource.Should().Contain("\"BackstageInfoWorkbookStatisticsButton\"");
        catalogSource.Should().Contain("\"BackstageInfoErrorCheckingButton\"");
        source.Should().Contain("RibbonTooltip.SetKeyTip(button, keyTip);");
    }
}
