using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void WpfChartOptionDialogsDelegateDecisionsAndChrome()
    {
        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = ReadHostSource(fileName);
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
            source.Should().NotContain("new Label { Content = label", fileName);
            source.Should().NotContain("new Button { Content = surface.OkLabel", fileName);
        }
    }

    [Fact]
    public void WpfChartOptionChromeRetainsEstablishedMetrics()
    {
        var source = ReadHostSource("ChartOptionsDialogChrome.cs");

        source.Should().Contain("Margin = new Thickness(0, 0, 0, 8)");
        source.Should().Contain("MinWidth = 80");
        source.Should().Contain("Margin = new Thickness(4)");
        source.Should().Contain("IsDefault = plan.IsDefault");
        source.Should().Contain("IsCancel = plan.IsCancel");
        source.Should().Contain("AutomationProperties.SetName(button, plan.AccessibleName)");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, plan.AutomationId)");
        source.Should().Contain("public void ApplyValues(ChartOptionsDialogValues values)");
        source.Should().Contain("_formSession.Text(fieldId)");
        source.Should().Contain("_formSession.SelectedIndex(fieldId)");
        source.Should().Contain("_formSession.NullableChecked(fieldId)");
        source.Should().NotContain("private TControl Control<TControl>");
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

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
    }
}
