using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaPrintPreviewSourceTests
{
    [Fact]
    public void PrintPreview_DelegatesSettingsSurfaceChoicesToSharedPresentationPlanners()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PrintPreview.cs"));

        source.Should().Contain("PrintPreviewSettingsPanelPlanner.Build(");
        source.Should().Contain("PrintPreviewSettingsTextResolver");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.PrintWhatOptions, panelPlan.PrintWhatSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.SidesOptions, panelPlan.SidesSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.CollationOptions, panelPlan.CollationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.OrientationOptions, panelPlan.OrientationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.PaperSizeOptions, panelPlan.PaperSizeSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.MarginOptions, panelPlan.MarginsSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(183, panelPlan.ScalingOptions, panelPlan.ScalingSelectedIndex)");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateToolbarCollatedText(PrintPreviewSettingsTextResolver)");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateSidesOptions(PrintPreviewSettingsTextResolver)");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateZoomOptions(PrintPreviewSettingsTextResolver)");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreatePageRangeToolbarPlan(");
        source.Should().Contain("IsChecked = panelPlan.PrintGridlines");
        source.Should().Contain("IsEnabled = panelPlan.IgnorePrintAreaEnabled");

        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PrintWhatActiveSheets\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_SidesOneSided\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CollatedOption\"");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Portrait\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"A4\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Narrow\")");
        source.Should().NotContain("CreatePreviewComboBox(82, \"100%\")");
    }

    private static string RepoFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
