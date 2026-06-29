using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaChartFormatDialogSourceTests
{
    [Fact]
    public void DataLabelsDialog_UsesSharedDescriptorAndFullPlannerSurface()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartDataLabelsPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartDataLabelsPlanner.GetLabelOptionsSection()");
        source.Should().Contain("ChartDataLabelsPlanner.GetStyleSection()");
        source.Should().Contain("ChartDataLabelsPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Callouts");
        source.Should().Contain("MakeDescriptorNumberBox(");
        source.Should().Contain("MakeColorButton(ChartDataLabelsDialogFieldId.FillColor");
        source.Should().NotContain("UiText.Get(\"ChartDataLabels_Show\")");
        source.Should().NotContain("UiText.Get(\"ChartDataLabels_ContainsLabel\")");
    }

    [Fact]
    public void AxisDialog_UsesSharedDescriptorAndPlannerParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"));

        source.Should().Contain("ChartAxisPlanner.GetDialogField(fieldId)");
        source.Should().Contain("ChartAxisPlanner.TryParseDialogInput(");
        source.Should().Contain("MakeAxisDescriptorCheck(ChartAxisDialogFieldId.LogScale");
        source.Should().Contain("MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Minimum");
        source.Should().Contain("MakeAxisDescriptorLabel(ChartAxisDialogFieldId.NumberFormat");
        source.Should().NotContain("AutomationProperties.SetName(minimumBox, \"Axis minimum\")");
        source.Should().NotContain("UiText.Get(\"ChartAxis_MinimumLabel\")");
        source.Should().NotContain("UiText.Get(\"ChartAxis_ShowMajorGridlines\")");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
