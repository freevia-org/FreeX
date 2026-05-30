using FluentAssertions;
using System.IO;
using System.Text.RegularExpressions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCommandPresentationPlannerTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", RibbonCommandLayoutKind.Large)]
    [InlineData("Column Chart", "Column Chart", RibbonCommandLayoutKind.Small)]
    [InlineData("Bold", "Bold", RibbonCommandLayoutKind.Small)]
    [InlineData("Excluded Share", "Share", RibbonCommandLayoutKind.Large)]
    [InlineData("Get Add-ins", "Get Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("My Add-ins", "My Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("3D Map", "3D Map", RibbonCommandLayoutKind.Large)]
    [InlineData("Macros", "Macros", RibbonCommandLayoutKind.Large)]
    [InlineData("Contact Support", "Contact Support", RibbonCommandLayoutKind.Small)]
    [InlineData("Show Training", "Show Training", RibbonCommandLayoutKind.Small)]
    [InlineData("What's New", "What's New", RibbonCommandLayoutKind.Small)]
    public void GetLayoutKind_ClassifiesRibbonCommands(string commandName, string label, RibbonCommandLayoutKind expected)
    {
        RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label).Should().Be(expected);
    }

    [Theory]
    [InlineData(" PivotTable ", " PivotTable ", RibbonCommandLayoutKind.Large)]
    [InlineData(" Table ", " Table ", RibbonCommandLayoutKind.Large)]
    [InlineData(" Center ", " Center ", RibbonCommandLayoutKind.Small)]
    public void GetLayoutKind_NormalizesCommandAndLabelWhitespace(
        string commandName,
        string label,
        RibbonCommandLayoutKind expected)
    {
        RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label).Should().Be(expected);
    }

    [Theory]
    [InlineData("Axis Options", true)]
    [InlineData("Legend", true)]
    [InlineData("Column Chart", false)]
    [InlineData("Recommended Chart", false)]
    [InlineData("Sparkline", false)]
    [InlineData("Table", false)]
    public void ShouldHideFromInsertRibbon_HidesChartFormattingCommandsOnly(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("Column Chart", true)]
    [InlineData("Surface Chart", true)]
    [InlineData("3D Surface Chart", true)]
    [InlineData("Column", true)]
    [InlineData("Trend Order", false)]
    [InlineData("R-squared", false)]
    public void IsInsertRibbonChartCommand_AllowsOnlyInsertChartTypes(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("PivotTable", RibbonCommandIconKind.PivotTable)]
    [InlineData("Recommended PivotTables", RibbonCommandIconKind.PivotTable)]
    [InlineData("Table", RibbonCommandIconKind.Table)]
    [InlineData("Column Chart", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Recommended Charts", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Line Chart", RibbonCommandIconKind.ChartLine)]
    [InlineData("Get Data", RibbonCommandIconKind.GetData)]
    [InlineData("Refresh All", RibbonCommandIconKind.Refresh)]
    [InlineData("Reapply", RibbonCommandIconKind.Refresh)]
    [InlineData("Advanced", RibbonCommandIconKind.Filter)]
    [InlineData("Insert Function", RibbonCommandIconKind.Function)]
    [InlineData("Spelling", RibbonCommandIconKind.Spelling)]
    [InlineData("Check Accessibility", RibbonCommandIconKind.Accessibility)]
    [InlineData("Protect Sheet", RibbonCommandIconKind.Protect)]
    [InlineData("Allow Users to Edit Ranges", RibbonCommandIconKind.Protect)]
    [InlineData("Help Online", RibbonCommandIconKind.Help)]
    [InlineData("Report Issue", RibbonCommandIconKind.Feedback)]
    [InlineData("Copy Diagnostics", RibbonCommandIconKind.Info)]
    [InlineData("Get Add-ins", RibbonCommandIconKind.Insert)]
    [InlineData("Stocks", RibbonCommandIconKind.Table)]
    [InlineData("Geography", RibbonCommandIconKind.Table)]
    [InlineData("Page Orientation", RibbonCommandIconKind.Page)]
    [InlineData("Lasso Select", RibbonCommandIconKind.Target)]
    [InlineData("Macros", RibbonCommandIconKind.Function)]
    [InlineData("What's New", RibbonCommandIconKind.Info)]
    [InlineData("Text", RibbonCommandIconKind.TextBox)]
    [InlineData("Ungroup", RibbonCommandIconKind.Ungroup)]
    [InlineData("Shape Fill", RibbonCommandIconKind.Fill)]
    [InlineData("Shape Effects", RibbonCommandIconKind.Effects)]
    [InlineData("Quick Analysis", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Pick From Drop-down List...", RibbonCommandIconKind.List)]
    [InlineData("Unknown Command", RibbonCommandIconKind.Generic)]
    public void GetIcon_MapsKnownCommandsToSemanticVectorKinds(string commandName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);

        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(" PivotTable ", RibbonCommandIconKind.PivotTable)]
    [InlineData(" Table ", RibbonCommandIconKind.Table)]
    [InlineData(" Center ", RibbonCommandIconKind.Align)]
    public void GetIcon_NormalizesCommandWhitespaceBeforeExactMappings(
        string commandName,
        RibbonCommandIconKind expectedKind)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Column Chart", RibbonCommandIconAccent.Chart)]
    [InlineData("Get Data", RibbonCommandIconAccent.Data)]
    [InlineData("Theme Colors", RibbonCommandIconAccent.Theme)]
    [InlineData("Fill", RibbonCommandIconAccent.Fill)]
    [InlineData("Error Checking", RibbonCommandIconAccent.Warning)]
    [InlineData("Protect Workbook", RibbonCommandIconAccent.Protect)]
    [InlineData("Report Issue", RibbonCommandIconAccent.Help)]
    [InlineData("Copy Diagnostics", RibbonCommandIconAccent.Help)]
    [InlineData("Contact Support", RibbonCommandIconAccent.Help)]
    [InlineData("Show Training", RibbonCommandIconAccent.Help)]
    [InlineData("What's New", RibbonCommandIconAccent.Help)]
    public void GetIcon_AssignsExcelLikeAccentFamilies(string commandName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Accent.Should().Be(expectedAccent);
    }

    [Fact]
    public void GetIcon_DoesNotContainDuplicateContainsPredicates()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonCommandPresentationPlanner.Icons.cs"));
        var getIconSource = ExtractMethodSource(source, "public static RibbonCommandIcon GetIcon(");
        var duplicatePredicates = Regex
            .Matches(getIconSource, @"name\.Contains\(""(?<predicate>[^""]+)""\)")
            .Select(match => match.Groups["predicate"].Value)
            .GroupBy(predicate => predicate, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        duplicatePredicates.Should().BeEmpty("duplicate contains predicates create unreachable icon mapping rules");
    }

    private static string ExtractMethodSource(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        signatureIndex.Should().BeGreaterThanOrEqualTo(0, $"source should contain {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        bodyStart.Should().BeGreaterThanOrEqualTo(signatureIndex, $"source should contain a body for {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
                return source.Substring(signatureIndex, index - signatureIndex + 1);
        }

        throw new InvalidOperationException($"Could not find the end of {signature}.");
    }

    [Theory]
    [InlineData("Clipboard", RibbonCommandIconKind.Paste)]
    [InlineData("Font", RibbonCommandIconKind.Font)]
    [InlineData("Editing", RibbonCommandIconKind.Search)]
    [InlineData("Tools", RibbonCommandIconKind.Search)]
    [InlineData("Pens", RibbonCommandIconKind.Line)]
    [InlineData("Convert", RibbonCommandIconKind.Math)]
    [InlineData("Help", RibbonCommandIconKind.Help)]
    [InlineData("Unknown", RibbonCommandIconKind.Generic)]
    public void GetGroupIcon_MapsExcelRibbonGroupsToSemanticVectorKinds(string groupName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);

        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(" Tools ", RibbonCommandIconKind.Search)]
    [InlineData(" Pens ", RibbonCommandIconKind.Line)]
    [InlineData(" Show ", RibbonCommandIconKind.View)]
    [InlineData(" Layout ", RibbonCommandIconKind.Page)]
    public void GetGroupIcon_NormalizesGroupWhitespaceBeforeExactMappings(
        string groupName,
        RibbonCommandIconKind expectedKind)
    {
        RibbonCommandPresentationPlanner.GetGroupIcon(groupName).Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Charts", RibbonCommandIconAccent.Chart)]
    [InlineData("Get & Transform Data", RibbonCommandIconAccent.Data)]
    [InlineData("Themes", RibbonCommandIconAccent.Theme)]
    [InlineData("Protect", RibbonCommandIconAccent.Protect)]
    [InlineData("Help", RibbonCommandIconAccent.Help)]
    public void GetGroupIcon_AssignsExcelLikeAccentFamilies(string groupName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetGroupIcon(groupName).Accent.Should().Be(expectedAccent);
    }

    [Fact]
    public void MainRibbonGroupLabels_MapToSemanticIcons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var ribbonXaml = xaml[
            xaml.IndexOf("<TabControl x:Name=\"RibbonTabs\"", StringComparison.Ordinal)..xaml.IndexOf("<Grid Grid.Row=\"3\"", StringComparison.Ordinal)];
        var genericGroupLabels = Regex
            .Matches(ribbonXaml, "<TextBlock Text=\"(?<label>[^\"]+)\" Style=\"\\{StaticResource GroupLbl\\}\"")
            .Select(match => match.Groups["label"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(label => RibbonCommandPresentationPlanner.GetGroupIcon(label).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericGroupLabels.Should().BeEmpty("collapsed ribbon groups should use a semantic icon rather than the generic fallback");
    }

    [Fact]
    public void MainRibbonCommandTitles_MapToSemanticIcons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var ribbonXaml = xaml[
            xaml.IndexOf("<TabControl x:Name=\"RibbonTabs\"", StringComparison.Ordinal)..xaml.IndexOf("<Grid Grid.Row=\"3\"", StringComparison.Ordinal)];
        var genericTitles = Regex
            .Matches(ribbonXaml, "local:RibbonTooltip.Title=\"(?<title>[^\"]+)\"")
            .Select(match => match.Groups["title"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(title => !title.StartsWith("Excluded ", StringComparison.Ordinal))
            .Where(title => RibbonCommandPresentationPlanner.GetIcon(title).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericTitles.Should().BeEmpty("visible ribbon commands should use a specific semantic icon rather than the generic fallback");
    }
}
