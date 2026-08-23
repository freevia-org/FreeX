using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartOptionsDialogDedupSourceTests
{
    [Fact]
    public void WpfChartOptionDialogsDelegateDecisionsAndChrome()
    {
        var host = ReadHostSource("ChartOptionsDialogHost.cs");
        host.Should().Contain("ChartOptionsDialogChrome.CreateForm(");
        host.Should().Contain("Content = _form.Content");
        host.Should().Contain("WindowStartupLocation = WindowStartupLocation.CenterOwner");
        host.Should().Contain("ResizeMode = ResizeMode.NoResize");

        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = ReadHostSource(fileName);
            var testSupport = ReadChartOptionsTestSupport();
            var dialogName = Path.GetFileNameWithoutExtension(fileName);
            source.Should().Contain($"ChartOptionsDialogHost<{dialogName}Session>", fileName);
            source.Should().Contain($"new {dialogName}Session(", fileName);
            source.Should().Contain("session.BuildDialogPlan(", fileName);
            source.Should().Contain("Submit", fileName);
            source.Should().NotContain("ChartOptionsDialogChrome.CreateForm(", fileName);
            source.Should().NotContain("Content =", fileName);
            source.Should().NotContain("WindowStartupLocation =", fileName);
            if (!string.Equals(fileName, "ChartExSeriesLayoutDialog.cs", StringComparison.Ordinal))
            {
                source.Should().Contain("session.BuildInput(values)", fileName);
                source.Should().NotContain("ForTests", fileName);
                testSupport.Should().Contain(
                    $"partial class {dialogName}",
                    fileName);
                testSupport.Should().Contain("BuildCommitPlanForTests()", fileName);
                source.Should().NotContain("_form.SetText(", fileName);
                source.Should().NotContain("_form.SetSelectedIndex(", fileName);
                source.Should().NotContain("_form.SetChecked(", fileName);
                source.Should().NotContain("_form.SetChoices(", fileName);
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
        source.Should().Contain("ChartOptionsDialogFormAdapter<Control, FrameworkElement>");
        source.Should().Contain("ChartOptionsDialogNativeFieldBinding<Control, TextBox, ComboBox, CheckBox>");
        source.Should().Contain("ChartOptionsDialogNativeRenderer<Control, FrameworkElement>");
        source.Should().Contain("FormSession.CompleteInitialRender()");
        source.Should().Contain("FormSession.Register(field.Id, control, row)");
        source.Should().NotContain("public void ApplyValues(ChartOptionsDialogValues values)");
        source.Should().NotContain("private TControl Control<TControl>");
        source.Should().NotContain("case TextBox textBox:");
        source.Should().NotContain("case ComboBox comboBox:");
        source.Should().NotContain("case CheckBox checkBox:");
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

    private static string ReadChartOptionsTestSupport()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "HostAccess.Wpf",
            "ChartOptionsDialogs.TestAccess.cs"));
    }
}
