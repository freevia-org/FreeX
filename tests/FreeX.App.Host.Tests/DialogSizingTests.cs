using System.Windows;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DialogSizingTests
{
    [Fact]
    public void ApplyContentHeight_PreservesWidthAndMinimumHeightWithoutFixedHeight()
    {
        StaTestRunner.Run(() =>
        {
            var window = new Window
            {
                Width = 420,
                Height = 160,
                MaxHeight = 600
            };

            DialogSizing.ApplyContentHeight(window);

            window.Width.Should().Be(420);
            window.MinWidth.Should().Be(420);
            window.MinHeight.Should().Be(160);
            window.MaxHeight.Should().Be(600);
            double.IsNaN(window.Height).Should().BeTrue();
            window.SizeToContent.Should().Be(SizeToContent.Height);
        });
    }

    [Fact]
    public void ApplyContentHeight_KeepsMinimumHeightInsideMaxHeight()
    {
        StaTestRunner.Run(() =>
        {
            var window = new Window
            {
                Height = 900
            };

            DialogSizing.ApplyContentHeight(window, maxHeight: 500);

            window.MinHeight.Should().Be(500);
            window.MaxHeight.Should().Be(500);
            window.SizeToContent.Should().Be(SizeToContent.Height);
        });
    }

    [Fact]
    public void AutomaticSizing_TargetsCustomNoResizeDialogsOnly()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AddWatchDialog("Sheet1!A1");
            var ordinaryWindow = new Window
            {
                ResizeMode = ResizeMode.NoResize
            };

            DialogSizing.ShouldApplyAutomaticSizing(dialog).Should().BeTrue();
            DialogSizing.ShouldApplyAutomaticSizing(ordinaryWindow).Should().BeFalse();
        });
    }

    [Fact]
    public void AppStartup_RegistersAutomaticDialogSizing()
    {
        // DialogSizing was extracted into the shared shell helpers project; App.xaml.cs
        // (still in the host) registers it at startup.
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs")
            + Environment.NewLine
            + DialogSourceTestSupport.ReadShellSources("DialogSizing.cs");

        source.Should().Contain("DialogSizing.RegisterAppDialogSizing();");
        source.Should().Contain("FrameworkElement.LoadedEvent");
        source.Should().Contain("ShouldApplyAutomaticSizing(window)");
        source.Should().Contain("type.Name.EndsWith(\"Dialog\", StringComparison.Ordinal)");
    }

    [Fact]
    public void PivotWorkflowDialogs_UseContentHeightSizingInsteadOfFixedWindowHeights()
    {
        var sources = DialogSourceTestSupport.ReadHostSources(
            "PivotTableDialog.cs",
            "PivotTableActionDialogs.cs",
            "PivotTableDataSourceDialog.cs",
            "PivotTableOptionsDialog.cs",
            "RecommendedPivotTablesDialog.cs");

        sources.Should().Contain("DialogSizing.ApplyContentHeight(this, width: 500, minHeight: 320);");
        sources.Should().Contain("DialogSizing.ApplyContentHeight(this, width: 360, minHeight: 150);");
        sources.Should().Contain("DialogSizing.ApplyContentHeight(this, width: 420, minHeight: 160);");
        sources.Should().Contain("DialogSizing.ApplyContentHeight(this, width: PivotOptionsPlanner.DialogWidth, minHeight: PivotOptionsPlanner.DialogMinHeight);");
        sources.Should().Contain("width: RecommendedPivotTablesDialogPlanner.Width");
        sources.Should().Contain("minHeight: RecommendedPivotTablesDialogPlanner.MinHeight");
        sources.Should().NotContain("Height = 320;");
        sources.Should().NotContain("Height = 160;");
        sources.Should().NotContain("Height = 150;");
        sources.Should().NotContain("Height = 500;");
        sources.Should().NotContain("Height = 340;");
    }
}
