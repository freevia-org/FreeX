using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ViewCommandSourceTests
{
    [Theory]
    [InlineData("Zoom", "Q", "ZoomPickerBtn_Click")]
    [InlineData("100%", "Z1", "Zoom100Btn_Click")]
    [InlineData("Zoom to Selection", "ZS", "ZoomSelectionBtn_Click")]
    public void ViewZoomCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("200%", "2", "200")]
    [InlineData("100%", "1", "100")]
    [InlineData("75%", "7", "75")]
    [InlineData("50%", "5", "50")]
    [InlineData("25%", "3", "25")]
    public void ViewZoomPresetMenuItems_ExposeExpectedKeyTipsTagsAndSharedHandler(
        string header,
        string keyTip,
        string tag)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header);

        item.Should().Contain($"Header=\"{header}\"");
        item.Should().Contain($"Tag=\"{tag}\"");
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain("Click=\"ZoomPresetMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomCustomMenuItem_OpensZoomDialog()
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), "Custom...");

        item.Should().Contain("local:RibbonTooltip.KeyTip=\"C\"");
        item.Should().Contain("Click=\"ZoomCustomMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomHandlers_RouteThroughZoomMapperDialogAndSelectionPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        source.Should().Contain("OpenRibbonContextMenu(btn, cm)");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent)");
        source.Should().Contain("new ZoomDialog(current) { Owner = this }");
        source.Should().Contain("ZoomSelectionPlanner.CalculateDialogZoomPercent(");
        source.Should().Contain("private void Zoom100Btn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ZoomSlider.Value = 100;");
        source.Should().Contain("ZoomSelectionPlanner.CalculateFitPercent(");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct)");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} View zoom command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} View zoom command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} View zoom button should have a closing marker");
        return xaml[start..end];
    }

    private static string ExtractMenuItemElementByHeader(string xaml, string header)
    {
        var headerIndex = xaml.IndexOf($"Header=\"{header}\"", StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"the {header} zoom preset should be present");

        var start = xaml.LastIndexOf("<MenuItem", headerIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {header} zoom preset should be a MenuItem");

        var end = xaml.IndexOf("/>", headerIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThan(headerIndex, $"the {header} zoom preset should be self-closing");
        return xaml[start..(end + 2)];
    }
}
