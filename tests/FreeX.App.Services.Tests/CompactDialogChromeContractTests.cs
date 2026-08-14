using FluentAssertions;
using Free.Shared.Shell;

namespace FreeX.App.Services.Tests;

public sealed class CompactDialogChromeContractTests
{
    [Fact]
    public void SharedMetrics_KeepTheCompactDesktopDialogContract()
    {
        CompactDialogVisualTokens.ControlHeight.Should().Be(24);
        CompactDialogVisualTokens.ButtonHeight.Should().Be(26);
        CompactDialogVisualTokens.FontSize.Should().Be(12);
        CompactDialogVisualTokens.ButtonPaddingHorizontal.Should().Be(12);
        CompactDialogVisualTokens.ButtonPaddingVertical.Should().Be(3);
        CompactDialogVisualTokens.TextBoxPaddingHorizontal.Should().Be(5);
        CompactDialogVisualTokens.TextBoxPaddingVertical.Should().Be(3);
        CompactDialogVisualTokens.ComboBoxPaddingHorizontal.Should().Be(5);
        CompactDialogVisualTokens.ComboBoxPaddingVertical.Should().Be(2);
        CompactDialogVisualTokens.TogglePaddingLeft.Should().Be(4);
        CompactDialogVisualTokens.LabelPadding.Should().Be(0);
        CompactDialogVisualTokens.GroupBoxMarginVertical.Should().Be(4);
        CompactDialogVisualTokens.GroupBoxPaddingHorizontal.Should().Be(8);
        CompactDialogVisualTokens.GroupBoxPaddingVertical.Should().Be(6);
        CompactDialogVisualTokens.ButtonCornerRadius.Should().Be(3);
        CompactDialogVisualTokens.BorderThickness.Should().Be(1);
    }

    [Fact]
    public void WpfAndAvaloniaChrome_ConsumeSharedMetricsInsteadOfRestatingThem()
    {
        var avalonia = Read(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs");
        var wpf = Read(
            "shared", "Free.Shared.Shell.Wpf", "DialogResources.xaml");
        var wpfAdapter = Read(
            "shared", "Free.Shared.Shell.Wpf", "WpfCompactDialogMetrics.cs");

        avalonia.Should().Contain("= CompactDialogVisualTokens.ControlHeight;");
        avalonia.Should().Contain("= CompactDialogVisualTokens.ButtonHeight;");
        avalonia.Should().Contain("= CompactDialogVisualTokens.FontSize;");
        avalonia.Should().Contain("CompactDialogVisualTokens.ButtonPaddingHorizontal");
        avalonia.Should().Contain("CompactDialogVisualTokens.ButtonPaddingVertical");
        avalonia.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingHorizontal");
        avalonia.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingVertical");
        avalonia.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingHorizontal");
        avalonia.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingVertical");
        avalonia.Should().Contain("CompactDialogVisualTokens.TogglePaddingLeft");
        avalonia.Should().Contain("CompactDialogVisualTokens.LabelPadding");
        avalonia.Should().Contain("CompactDialogVisualTokens.GroupBoxMarginVertical");
        avalonia.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingHorizontal");
        avalonia.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingVertical");
        avalonia.Should().Contain("case GroupBox groupBox:");
        avalonia.Should().Contain("case Label label:");
        avalonia.Should().Contain("groupBox.Margin = style.GroupBoxMargin;");
        avalonia.Should().Contain("groupBox.Padding = style.GroupBoxPadding;");
        avalonia.Should().Contain("label.Padding = style.LabelPadding;");
        avalonia.Should().Contain("checkBox.Padding = style.TogglePadding;");
        avalonia.Should().Contain("radioButton.Padding = style.TogglePadding;");
        avalonia.Should().Contain("checkBox.VerticalContentAlignment = VerticalAlignment.Center;");
        avalonia.Should().Contain("radioButton.VerticalContentAlignment = VerticalAlignment.Center;");
        avalonia.Should().Contain("CompactDialogVisualTokens.ButtonCornerRadius");
        avalonia.Should().Contain("new Thickness(CompactDialogVisualTokens.BorderThickness)");
        avalonia.Should().NotContain("public double ControlHeight { get; init; } = 24;");
        avalonia.Should().NotContain("public double ButtonHeight { get; init; } = 26;");
        avalonia.Should().NotContain("public double FontSize { get; init; } = 12;");
        avalonia.Should().NotContain("public Thickness ButtonPadding { get; init; } = new(12, 3);");

        wpf.Should().Contain("{x:Static shell:CompactDialogVisualTokens.ControlHeight}");
        wpf.Should().Contain("{x:Static shell:CompactDialogVisualTokens.ButtonHeight}");
        wpf.Should().Contain("{x:Static shell:CompactDialogVisualTokens.FontSize}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.ButtonPadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.TextBoxPadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.ComboBoxPadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.TogglePadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.LabelPadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.GroupBoxMargin}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.GroupBoxPadding}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.ButtonCornerRadius}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.UniformBorderThickness}");
        wpf.Should().NotContain("<Setter Property=\"MinHeight\" Value=\"26\"/>");
        wpf.Should().NotContain("<Setter Property=\"FontSize\" Value=\"12\"/>");
        wpf.Should().NotContain("CornerRadius=\"3\"");

        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonPaddingHorizontal");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonPaddingVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingHorizontal");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.TextBoxPaddingVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingHorizontal");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ComboBoxPaddingVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.TogglePaddingLeft");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.LabelPadding");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.GroupBoxMarginVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingHorizontal");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.GroupBoxPaddingVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonCornerRadius");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.BorderThickness");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
