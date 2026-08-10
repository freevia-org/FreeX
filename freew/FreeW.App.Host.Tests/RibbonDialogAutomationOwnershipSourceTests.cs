using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RibbonDialogAutomationOwnershipSourceTests
{
    [Fact]
    public void Ribbon_hosts_consume_shared_typed_numeric_parsers()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWRibbonNumericValueParser.TryParseFontSize(");
            source.Should().Contain("FreeWRibbonNumericValueParser.TryParseObjectPosition(");
            source.Should().Contain("FreeWRibbonNumericValueParser.TryParseObjectSize(");
            source.Should().Contain("FreeWRibbonNumericValueParser.TryParseChartSize(");
            source.Should().NotContain("private static bool TryParsePosition(");
            source.Should().NotContain("private static bool TryParseSize(");
            source.Should().NotContain("private static bool TryParseChartSize(");
        }

        wpf.Should().Contain("CultureInfo.CurrentCulture");
        avalonia.Should().Contain("CultureInfo.InvariantCulture");
    }

    [Fact]
    public void Dialog_and_backstage_hosts_do_not_redeclare_planned_automation_ids()
    {
        var dialogSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "CellShadingDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "CellShadingDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "PasswordPromptDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "PasswordPromptDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "ParagraphBreaksDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "ParagraphDialog.cs"),
        };
        var backstageSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "Backstage", "BackstageView.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs"),
        };

        foreach (var source in dialogSources)
        {
            source.Should().NotContain("\"CellShadingNoColorButton\"");
            source.Should().NotContain("$\"CellShadingSwatch{index}\"");
            source.Should().NotContain("\"PasswordPromptDialog\"");
            source.Should().NotContain("\"PasswordPromptPasswordBox\"");
            source.Should().NotContain("\"PasswordPromptOkButton\"");
            source.Should().NotContain("\"PasswordPromptCancelButton\"");
            source.Should().NotContain("\"paragraph-left-indent\"");
        }

        foreach (var source in backstageSources)
        {
            source.Should().Contain("BackstagePaneSurfacePlanner.WindowAutomationId");
            source.Should().Contain("surface.Search.AutomationId");
            source.Should().Contain("inline.FileNameAutomationId");
            source.Should().Contain("inline.FileTypeAutomationId");
            source.Should().NotContain("\"FreeWBackstageWindow\"");
            source.Should().NotContain("\"OpenSearchBox\"");
            source.Should().NotContain("\"SaveAsSuggestedFileName\"");
            source.Should().NotContain("\"SaveAsSelectedExtension\"");
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
