using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaPrintPreviewSourceTests
{
    [Fact]
    public void PrintPreview_DelegatesSettingsSurfaceChoicesToSharedPresentationPlanners()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PrintPreview.cs"));

        source.Should().Contain("PrintPreviewSurfacePlanner.CreateTopToolbarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateDocumentToolbarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateFindBarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateSettingsRailPlan(");
        source.Should().Contain("PrintPreviewSettingsTextResolver");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.PrintWhatOptions, plan.Settings.PrintWhatSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.SidesOptions, plan.Settings.SidesSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.CollationOptions, plan.Settings.CollationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.OrientationOptions, plan.Settings.OrientationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.PaperSizeOptions, plan.Settings.PaperSizeSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.MarginOptions, plan.Settings.MarginsSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.ScalingOptions, plan.Settings.ScalingSelectedIndex)");
        source.Should().Contain("RenderPreviewInstructions(canvas, painting.Instructions)");
        source.Should().Contain("IsChecked = plan.Settings.PrintGridlines");
        source.Should().Contain("IsEnabled = plan.Settings.IgnorePrintAreaEnabled");

        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PrintWhatActiveSheets\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_SidesOneSided\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CollatedOption\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CopiesSectionLabel\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PageSetupButton\"");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Portrait\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"A4\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Narrow\")");
        source.Should().NotContain("CreatePreviewComboBox(82, \"100%\")");
        source.Should().NotContain("AlignCellTextLeft");
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
