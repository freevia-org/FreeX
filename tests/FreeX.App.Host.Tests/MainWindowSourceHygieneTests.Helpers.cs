using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    private static string ReadEditingSource()
    {
        return DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "\n",
            "MainWindow.Editing.cs",
            "MainWindow.EditingDropdowns.cs",
            "MainWindow.FormulaReferenceEditing.cs");
    }

    private static string ReadChartCommandSource()
    {
        return DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "\n",
            "MainWindow.ChartCommands.cs",
            "MainWindow.ChartAxisCommands.cs");
    }

    private static string ReadPivotCommandSource()
    {
        return DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "\n",
            "MainWindow.PivotCommands.cs",
            "MainWindow.PivotAdvancedCommands.cs",
            "MainWindow.PivotChartCommands.cs",
            "MainWindow.PivotDesignCommands.cs",
            "MainWindow.PivotSlicerTimeline.cs",
            "MainWindow.TableDesignCommands.cs");
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
