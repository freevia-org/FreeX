namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionsDialogFormSessionOwnershipTests
{
    [Fact]
    public void NativeChartOptionFormsDelegateStateOwnershipToPortableSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var source in new[]
                 {
                     Read(root, "freep", "FreeP.App.Host", "ChartOptionsDialogChrome.cs"),
                     Read(root, "freep", "FreeP.App.Avalonia", "ChartOptionsDialogChrome.cs"),
                 })
        {
            source.Should().Contain("ChartOptionsDialogFormAdapter<Control,")
                .And.Contain("FormSession.Register(")
                .And.Contain("FormSession.CompleteInitialRender()")
                .And.NotContain("public ChartOptionsDialogValues CaptureValues()")
                .And.NotContain("Dictionary<ChartOptionsDialogFieldId")
                .And.NotContain("foreach (var (fieldId, value) in values.Fields)")
                .And.NotContain("foreach (var field in plan.Fields.Values)")
                .And.NotContain("_applyingPlan");
        }
    }

    [Fact]
    public void AvaloniaChartOptionDialogsUseTheSharedDialogWindowAndWindowsChrome()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var baseSource = Read(root, "freep", "FreeP.App.Avalonia", "FreePDialogWindow.cs");
        var hostSource = Read(root, "freep", "FreeP.App.Avalonia", "ChartOptionsDialogHost.cs");
        var chromeSource = Read(root, "freep", "FreeP.App.Avalonia", "ChartOptionsDialogChrome.cs");

        baseSource.Should().Contain("class FreePDialogWindow : AvaloniaDialogWindow")
            .And.NotContain("Background =")
            .And.NotContain("FontFamily.Default");
        chromeSource.Should().Contain("AvaloniaCompactDialogChrome.WindowsStyle")
            .And.NotContain("new(FontFamily.Default)");
        hostSource.Should().Contain("class ChartOptionsDialogHost<TSession> : FreePDialogWindow")
            .And.NotContain("Background =")
            .And.NotContain("using Avalonia.Media;");

        foreach (var fileName in ChartOptionDialogFiles)
        {
            var source = Read(root, "freep", "FreeP.App.Avalonia", fileName);
            source.Should().Contain($"class {Path.GetFileNameWithoutExtension(fileName)} : ChartOptionsDialogHost<", fileName)
                .And.NotContain(" : Window", fileName)
                .And.NotContain("Background =", fileName)
                .And.NotContain("Color.FromRgb(0xF3", fileName)
                .And.NotContain("using Avalonia.Media;", fileName);
        }
    }

    [Fact]
    public void PortableFormSessionOwnsRegistryProjectionAndPlanApplicationWithoutRendererDependencies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "ChartOptionsDialogFormSession.cs");

        source.Should().Contain("public sealed class ChartOptionsDialogFormSession<TControl, TRow>")
            .And.Contain("public ChartOptionsDialogValues CaptureValues()")
            .And.Contain("public void ApplyValues(ChartOptionsDialogValues values)")
            .And.Contain("public void ApplyPlan(ChartOptionsDialogPlan plan)")
            .And.Contain("public bool IsApplyingPlan { get; private set; } = true;")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    [Fact]
    public void NativeSelectionCallbacksOnlyCaptureIndicesAndRenderPortableTransitions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var dialogFiles = new[]
        {
            "ChartAxisOptionsDialog.cs",
            "ChartAreaOptionsDialog.cs",
            "ChartSeriesOptionsDialog.cs",
            "ChartLayoutOptionsDialog.cs",
            "ChartPointOptionsDialog.cs",
            "ChartExSeriesLayoutDialog.cs",
        };

        foreach (var app in new[] { "FreeP.App.Host", "FreeP.App.Avalonia" })
        {
            var host = Read(root, "freep", app, "ChartOptionsDialogHost.cs");
            host.Should().Contain("_form.SelectedIndex(fieldId)")
                .And.Contain("_form.ApplyPlan(updated)");

            foreach (var dialogFile in dialogFiles)
            {
                var source = Read(root, "freep", app, dialogFile);
                source.Should().Contain("session.TryApplySelectionChange(fieldId, selectedIndex, out var plan)")
                    .And.NotContain("_session.SelectAxis(")
                    .And.NotContain("_session.SelectTarget(")
                    .And.NotContain("_session.SelectSeries(")
                    .And.NotContain("_session.SelectPoint(");
            }
        }
    }

    [Fact]
    public void PortableFormSession_NormalizesNativeTextAndOwnsTypedAccessors()
    {
        var text = new FakeControl
        {
            Value = new PresentationDialogFieldValue(Text: null!),
        };
        var choice = new FakeControl
        {
            Value = new PresentationDialogFieldValue(SelectedIndex: 2),
        };
        var toggle = new FakeControl
        {
            Value = new PresentationDialogFieldValue(IsChecked: null),
        };
        var form = new ChartOptionsDialogFormSession<FakeControl, FakeRow>(
            static control => control.Value,
            static (control, value) => control.Value = value,
            static (control, field) => control.Value = new(
                field.Text,
                field.SelectedIndex,
                field.IsChecked),
            static (row, visible) => row.IsVisible = visible);
        form.Register(ChartOptionsDialogFieldId.FontFamily, text, new FakeRow());
        form.Register(ChartOptionsDialogFieldId.ScatterStyle, choice, new FakeRow());
        form.Register(ChartOptionsDialogFieldId.Bold, toggle, new FakeRow());

        form.Text(ChartOptionsDialogFieldId.FontFamily).Should().BeEmpty();
        form.SelectedIndex(ChartOptionsDialogFieldId.ScatterStyle).Should().Be(2);
        form.NullableChecked(ChartOptionsDialogFieldId.Bold).Should().BeNull();
        form.CaptureValues().Text(ChartOptionsDialogFieldId.FontFamily).Should().BeEmpty();
    }

    [Fact]
    public void ChartPlan_OwnsStableActionSemantics()
    {
        var plan = new ChartOptionsDialogPlan(
            commandId: "Chart.Format.Axis",
            title: "Format Axis",
            width: 420,
            height: 300,
            minimumWidth: 320,
            minimumHeight: 220,
            isResizable: true,
            isScrollable: false,
            hint: null,
            acceptLabel: "OK",
            cancelLabel: "Cancel",
            groups: Array.Empty<ChartOptionsDialogGroupPlan>());

        plan.AcceptAction.IsDefault.Should().BeTrue();
        plan.AcceptAction.AccessibleName.Should().Be("Apply Format Axis");
        plan.AcceptAction.AutomationId.Should().Be("FreeP.ChartOptions.ChartFormatAxis.Accept");
        plan.CancelAction.IsCancel.Should().BeTrue();
        plan.CancelAction.AutomationId.Should().Be("FreeP.ChartOptions.ChartFormatAxis.Cancel");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));

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

    private sealed class FakeControl
    {
        public PresentationDialogFieldValue Value { get; set; } = new();
    }

    private sealed class FakeRow
    {
        public bool IsVisible { get; set; }
    }
}
