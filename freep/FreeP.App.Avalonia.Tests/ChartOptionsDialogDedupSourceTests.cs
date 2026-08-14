using System.IO;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void AvaloniaChartOptionDialogsDelegateDecisionsAndChrome()
    {
        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", fileName));
            var testSupport = File.ReadAllText(RepoFile(
                "freep",
                "TestSupport",
                "HostAccess.Avalonia",
                "ChartOptionsDialogs.TestAccess.cs"));
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
                source.Should().NotContain("ForTests", fileName);
                testSupport.Should().Contain(
                    $"partial class {Path.GetFileNameWithoutExtension(fileName)}",
                    fileName);
                testSupport.Should().Contain("BuildCommitPlanForTests()", fileName);
                source.Should().NotContain("_form.SetText(", fileName);
                source.Should().NotContain("_form.SetSelectedIndex(", fileName);
                source.Should().NotContain("_form.SetChecked(", fileName);
                source.Should().NotContain("_form.SetChoices(", fileName);
                if (source.Contains("SetOptionsForTests", StringComparison.Ordinal)
                    || source.Contains("SetOfPieOptionsForTests", StringComparison.Ordinal))
                {
                    source.Should().Contain("_form.ApplyValues(buildValues(_session))", fileName);
                }
                source.Should().NotContain("OptionsDialogTestSettings", fileName);
                source.Should().NotContain("_session.BuildTestValues(", fileName);
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
        var adapter = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Presentation",
            "ChartOptionsDialogFormAdapter.cs"));

        source.Should().Contain("Spacing = 8");
        source.Should().Contain("Margin = new Thickness(0, 12, 0, 0)");
        source.Should().Contain("Margin = new Thickness(0, 0, 8, 0)");
        source.Should().Contain("MinWidth = 80");
        source.Should().Contain("IsDefault = plan.IsDefault");
        source.Should().Contain("IsCancel = plan.IsCancel");
        source.Should().Contain("AutomationProperties.SetName(button, plan.AccessibleName)");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, plan.AutomationId)");
        source.Should().Contain("ChartOptionsDialogFormAdapter<Control, Control>");
        source.Should().Contain("ChartOptionsDialogNativeRenderer<Control, Control>");
        source.Should().Contain("ChartOptionsDialogNativeFieldBinding<Control, TextBox, ComboBox, CheckBox>");
        source.Should().Contain("FieldBinding.ApplyPlan");
        source.Should().Contain("FormSession.Register(field.Id, control, row)");
        source.Should().Contain("FormSession.CompleteInitialRender()");
        source.Should().NotContain("public void ApplyValues(ChartOptionsDialogValues values)");
        source.Should().NotContain("private TControl Control<TControl>");
        source.Should().NotContain("case TextBox textBox:");
        source.Should().NotContain("case ComboBox comboBox:");
        source.Should().NotContain("case CheckBox checkBox:");

        adapter.Should().Contain("public void ApplyValues(ChartOptionsDialogValues values)");
        adapter.Should().Contain("public string Text(ChartOptionsDialogFieldId fieldId)");
        adapter.Should().Contain("public int SelectedIndex(ChartOptionsDialogFieldId fieldId)");
        adapter.Should().Contain("public bool? NullableChecked(ChartOptionsDialogFieldId fieldId)");
        adapter.Should().Contain("FormSession.ApplyValues(values)");
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
        TestWorkspaceFileLocator.Find(parts);
}
