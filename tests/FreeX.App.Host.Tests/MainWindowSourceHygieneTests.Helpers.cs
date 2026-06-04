using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    private static string ReadEditingSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.Editing.cs",
                "MainWindow.EditingDropdowns.cs",
                "MainWindow.FormulaReferenceEditing.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static string ReadChartCommandSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.ChartCommands.cs",
                "MainWindow.ChartAxisCommands.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static void AssertChartButtonRoutesTo(string xaml, string content, string clickHandler, bool isDeferred)
    {
        var button = ExtractButtonElementByContent(xaml, content);

        button.Should().Contain($"Click=\"{clickHandler}\"");
        button.Should().Contain("Style=\"{StaticResource RibbonBtn}\"");
        button.Should().Contain("local:RibbonTooltip.Title=");

        if (isDeferred)
        {
            button.ShouldContainLocalizedAttribute(
                "local:RibbonTooltip.Description",
                "Deferred: retained from XLSX files; authoring and rendering need a dedicated data model.");
        }
        else
        {
            button.Should().NotContain("local:RibbonTooltip.Description=\"Deferred:");
        }
    }

    private static string ExtractButtonElementByContent(string xaml, string content)
    {
        var contentIndex = FindElementByLocalizedAttributeValue(xaml, "Button", "Content", content);

        var start = xaml.LastIndexOf("<Button", contentIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {content} chart button should have a Button start tag");

        var end = xaml.IndexOf("/>", contentIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(contentIndex, $"the {content} chart button should be self-closing");

        return xaml.Substring(start, end - start + 2);
    }

    private static int FindElementByLocalizedAttributeValue(string xaml, string elementName, string attributeName, string expectedValue)
    {
        var searchFrom = 0;
        while (searchFrom < xaml.Length)
        {
            var start = xaml.IndexOf($"<{elementName}", searchFrom, StringComparison.Ordinal);
            if (start < 0)
                break;

            var end = xaml.IndexOf(">", start, StringComparison.Ordinal);
            end.Should().BeGreaterThan(start, $"the {elementName} element should have a closing bracket");
            var element = xaml[start..(end + 1)];
            var match = System.Text.RegularExpressions.Regex.Match(
                element,
                $@"(?<![\w\.:]){System.Text.RegularExpressions.Regex.Escape(attributeName)}=""(?<value>[^""]*)""",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            if (match.Success && LocalizedXamlTestSupport.ResolveLocalizedValue(match.Groups["value"].Value) == expectedValue)
                return start + match.Index;

            searchFrom = end + 1;
        }

        searchFrom.Should().BeLessThan(0, $"the {expectedValue} {elementName} should be present");
        return -1;
    }

    private static string ReadPivotCommandSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.PivotCommands.cs",
                "MainWindow.PivotAdvancedCommands.cs",
                "MainWindow.PivotChartCommands.cs",
                "MainWindow.PivotDesignCommands.cs",
                "MainWindow.PivotSlicerTimeline.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static string ExtractMethodSource(string source, string signature)
        => SourceMethodExtractor.ExtractMethodSource(source, signature);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
