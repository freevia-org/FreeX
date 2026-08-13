namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonComboChoiceSourceConvergenceTests
{
    [Fact]
    public void WpfAndAvaloniaRenderersBothUseTypedLabelsValuesAndStableStateMatching()
    {
        var wpf = Source("shared/Free.Shared.Ribbon.Wpf/RibbonWpfRenderer.cs");
        var avalonia = Source("shared/Free.Shared.Ribbon.Avalonia/AvaloniaRibbonRenderer.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            Assert.Contains("nameof(RibbonComboBoxChoice.Label)", renderer);
            Assert.Contains("box.SelectedItem is RibbonComboBoxChoice choice", renderer);
            Assert.Contains("return choice.Value;", renderer);
            Assert.Contains("string.Equals(choice.Value, value", renderer);
        }
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(relativePath)).ReplaceLineEndings("\n");
}
