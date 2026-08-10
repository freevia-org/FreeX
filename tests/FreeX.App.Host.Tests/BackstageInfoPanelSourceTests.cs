using System.IO;
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
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var serviceSource = DialogSourceTestSupport.ReadAppServicesSource("BackstageInfoPlanner.cs");

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "BackstageInfoPlanner.cs"))
            .Should()
            .BeFalse("Backstage Info plan construction is shared service logic; Host keeps only UI resources");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "InfoPanelSummaryPlanner.cs"))
            .Should()
            .BeFalse("Info panel summary computation is shared service logic");
        serviceSource.Should().Contain("public sealed record BackstageInfoPlan");
        serviceSource.Should().Contain("public static class BackstageInfoPlanner");
        source.Should().Contain("var activeSheet = _workbook.GetSheet(_currentSheetId);");
        source.Should().Contain("BackstageInfoPlanner.Build(");
        source.Should().Contain("BackstageInfoResources.Strings");
        source.Should().Contain("hasSelection: SheetGrid.SelectedRange is not null");
        source.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        source.Should().Contain("BackstageInfoPlanner.CreatePaneRequest(info)");
        source.Should().NotContain("CreateBackstageInfoPaneRequest(");
        source.Should().Contain("ResolveBackstageInfoDetailTextBlock(detail.Id).Text = ResolveBackstageTextValue(detail.Value);");
        source.Should().Contain("FreeXBackstageInfoDetailId.FileSize => InfoFileSize");
        source.Should().Contain("FreeXBackstageInfoDetailId.Share => InfoShareStatus");
        source.Should().Contain("FreeXBackstageInfoDetailId.Export => InfoExportStatus");
        source.Should().Contain("FreeXBackstageInfoDetailId.WorkbookProtection => InfoWorkbookProtectionSummary");
        source.Should().Contain("ProtectWorkbookBtn_Click(sender, e);");
    }

    [Fact]
    public void BackstageCodeBehind_WiresInfoPanelActionsWithAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var catalogSource = DialogSourceTestSupport.ReadPresentationSources("Backstage", "FreeXBackstagePaneCatalog.cs");
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        constructorSource.Should().Contain("ConfigureBackstageInfoActionButtons();");
        source.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        source.Should().Contain("FreeXBackstageInfoSurface.WpfInfoPane");
        source.Should().Contain("InfoProtectWorkbookButton.Click += InfoProtectWorkbookBtn_Click;");
        source.Should().Contain("ConfigureBackstageInfoActionButton(");
        source.Should().Contain("action.AutomationId");
        catalogSource.Should().Contain("\"BackstageInfoCheckAccessibilityButton\"");
        catalogSource.Should().Contain("\"BackstageInfoWorkbookStatisticsButton\"");
        catalogSource.Should().Contain("\"BackstageInfoErrorCheckingButton\"");
        source.Should().Contain("RibbonTooltip.SetKeyTip(button, keyTip);");
    }
}
