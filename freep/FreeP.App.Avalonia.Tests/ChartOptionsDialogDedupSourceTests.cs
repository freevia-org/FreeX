using System.IO;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void AvaloniaChartOptionDialogsDelegateDecisionsAndChrome()
    {
        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", fileName));
            (source.Contains("ChartDialogOptionProjection.", StringComparison.Ordinal)
                || source.Contains("DialogSession", StringComparison.Ordinal))
                .Should().BeTrue(fileName);
            source.Should().Contain("private readonly ChartOptionsDialogForm _form", fileName);
            source.Should().Contain("_session.BuildDialogPlan(", fileName);
            source.Should().Contain("ChartOptionsDialogChrome.CreateForm(", fileName);
            source.Should().Contain("Content = _form.Content", fileName);
            if (!string.Equals(fileName, "ChartExSeriesLayoutDialog.cs", StringComparison.Ordinal))
            {
                source.Should().Contain("_session.BuildInput(_form.CaptureValues())", fileName);
                source.Should().Contain("_session.BuildCommitPlanForTests(_form.CaptureValues()", fileName);
                source.Should().NotContain("_form.SetText(", fileName);
                source.Should().NotContain("_form.SetSelectedIndex(", fileName);
                source.Should().NotContain("_form.SetChecked(", fileName);
                source.Should().NotContain("_form.SetChoices(", fileName);
                if (source.Contains("SetOptionsForTests", StringComparison.Ordinal)
                    || source.Contains("SetOfPieOptionsForTests", StringComparison.Ordinal))
                {
                    source.Should().Contain("_form.ApplyValues(_session.BuildTestValues(", fileName);
                }
            }
            source.Should().NotContain("NumberStyles.", fileName);
            source.Should().NotContain("double.TryParse", fileName);
            source.Should().NotContain("int.TryParse", fileName);
            source.Should().NotContain("new TextBox", fileName);
            source.Should().NotContain("new ComboBox", fileName);
            source.Should().NotContain("new CheckBox", fileName);
            source.Should().NotContain("new Grid", fileName);
            source.Should().NotContain("ItemsSource =", fileName);
            source.Should().NotContain("CreateRow(", fileName);
            source.Should().NotContain("AvaloniaCompactDialogChromeStyle DialogChromeStyle", fileName);
            source.Should().NotContain("private static Button MakeButton", fileName);
            source.Should().NotContain("new TextBlock { Text = label", fileName);
        }
    }

    [Fact]
    public void AvaloniaChartOptionChromeRetainsEstablishedMetrics()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "ChartOptionsDialogChrome.cs"));

        source.Should().Contain("Spacing = 8");
        source.Should().Contain("Margin = new Thickness(0, 12, 0, 0)");
        source.Should().Contain("Margin = new Thickness(0, 0, 8, 0)");
        source.Should().Contain("MinWidth = 80");
        source.Should().Contain("isDefault: true");
        source.Should().Contain("public void ApplyValues(ChartOptionsDialogValues values)");
        source.Should().Contain("foreach (var (fieldId, value) in values.Fields)");
        source.Should().Contain("case TextBox textBox:");
        source.Should().Contain("case ComboBox comboBox:");
        source.Should().Contain("case CheckBox checkBox:");
    }

    private static readonly string[] ChartOptionDialogFiles =
    [
        "Chart3DViewOptionsDialog.cs",
        "ChartAreaOptionsDialog.cs",
        "ChartAxisOptionsDialog.cs",
        "ChartBubbleOptionsDialog.cs",
        "ChartDataTableOptionsDialog.cs",
        "ChartDisplayOptionsDialog.cs",
        "ChartExSeriesLayoutDialog.cs",
        "ChartLayoutOptionsDialog.cs",
        "ChartPieOptionsDialog.cs",
        "ChartPlotStyleOptionsDialog.cs",
        "ChartPointOptionsDialog.cs",
        "ChartProtectionOptionsDialog.cs",
        "ChartSeriesOptionsDialog.cs",
        "ChartTextOptionsDialog.cs",
    ];

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
