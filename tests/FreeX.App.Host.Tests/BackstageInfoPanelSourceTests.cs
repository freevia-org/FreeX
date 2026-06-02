using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageInfoPanelSourceTests
{
    [Fact]
    public void InfoPanel_ExposesCommandActionButtonsWithStableNames()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var propertyFields = new[]
        {
            "InfoFileSize",
            "InfoLastModified",
            "InfoShareStatus",
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        source.Should().Contain("var activeSheet = _workbook.GetSheet(_currentSheetId);");
        source.Should().Contain("BackstageInfoPlanner.Build(_workbook, _currentFilePath, activeSheet)");
        source.Should().Contain("InfoFileSize.Text = plan.FileSize;");
        source.Should().Contain("InfoShareStatus.Text = plan.SharingStatus;");
        source.Should().Contain("InfoWorkbookProtectionSummary.Text = plan.Summary.WorkbookProtectionSummary;");
        source.Should().Contain("ProtectWorkbookBtn_Click(sender, e);");
    }

    [Fact]
    public void BackstageCodeBehind_WiresInfoPanelActionsWithAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var constructorSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs"));

        constructorSource.Should().Contain("ConfigureBackstageInfoActionButtons();");
        source.Should().Contain("InfoProtectWorkbookButton.Click += InfoProtectWorkbookBtn_Click;");
        source.Should().Contain("ConfigureBackstageInfoActionButton(");
        source.Should().Contain("\"BackstageInfoCheckAccessibilityButton\"");
        source.Should().Contain("\"BackstageInfoWorkbookStatisticsButton\"");
        source.Should().Contain("\"BackstageInfoErrorCheckingButton\"");
        source.Should().Contain("RibbonTooltip.SetKeyTip(button, keyTip);");
    }
}
