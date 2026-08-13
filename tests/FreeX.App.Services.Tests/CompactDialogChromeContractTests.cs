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
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.ButtonCornerRadius}");
        wpf.Should().Contain("{x:Static local:WpfCompactDialogMetrics.UniformBorderThickness}");
        wpf.Should().NotContain("<Setter Property=\"MinHeight\" Value=\"26\"/>");
        wpf.Should().NotContain("<Setter Property=\"FontSize\" Value=\"12\"/>");
        wpf.Should().NotContain("CornerRadius=\"3\"");

        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonPaddingHorizontal");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonPaddingVertical");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.ButtonCornerRadius");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.BorderThickness");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
