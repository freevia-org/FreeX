namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class DialogThemeResourceParitySourceTests
{
    [Fact]
    public void Avalonia_compact_chrome_consumes_the_WPF_dialog_theme_aliases()
    {
        var avalonia = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Wpf", "DialogResources.xaml");

        string[] sharedResourceKeys =
        [
            "ThemeNeutralTextBrush",
            "ThemeNeutralWhiteBrush",
            "ThemeNeutralSheetSurfaceBrush",
            "ThemeAccentBrush",
            "ThemeAccentSoftBrush",
            "ThemeAccentPressedBrush",
        ];

        foreach (var key in sharedResourceKeys)
        {
            avalonia.Should().Contain($"\"{key}\"");
            wpf.Should().Contain(key);
        }

        avalonia.Should().Contain("\"ThemeNeutralDangerBrush\"");
    }

    [Fact]
    public void Avalonia_compact_controls_match_WPF_accent_interaction_states()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs");

        source.Should().Contain("selector.OfType<Button>().Class(\":pointerover\")");
        source.Should().Contain("selector.OfType<Button>().Class(\":pressed\")");
        source.Should().Contain("selector.OfType<Button>().Class(\":disabled\")");
        source.Should().Contain("selector.OfType<TextBox>().Class(\":focus\")");
        source.Should().Contain("selector.OfType<TextBox>().Class(\":pointerover\")");
        source.Should().Contain("groupBox.Foreground = accent;");
        source.Should().Contain("Foreground = accent,");
    }

    [Fact]
    public void Compact_dialog_structural_colors_are_owned_by_the_portable_shell()
    {
        var portable = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell", "CompactDialogVisualTokens.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Wpf", "DialogResources.xaml");
        var wpfAdapter = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell.Wpf", "WpfCompactDialogMetrics.cs");

        (string Token, string WpfProperty)[] structuralTokens =
        [
            ("BorderHex", "BorderColor"),
            ("FieldBorderHex", "FieldBorderColor"),
            ("DisabledForegroundHex", "DisabledForegroundBrush"),
            ("DisabledBorderHex", "DisabledBorderBrush"),
        ];

        foreach (var (token, wpfProperty) in structuralTokens)
        {
            portable.Should().Contain($"public const string {token}");
            avalonia.Should().Contain($"CompactDialogVisualTokens.{token}");
            wpfAdapter.Should().Contain($"CompactDialogVisualTokens.{token}");
            wpf.Should().Contain($"WpfCompactDialogMetrics.{wpfProperty}");
        }

        wpfAdapter.Should().Contain("CompactDialogVisualTokens.PrimaryPressedHex");
        wpfAdapter.Should().Contain("CompactDialogVisualTokens.PrimaryDisabledHex");
        wpf.Should().Contain("WpfCompactDialogMetrics.PrimaryPressedBrush");
        wpf.Should().Contain("WpfCompactDialogMetrics.PrimaryDisabledBrush");

        wpfAdapter.Should().NotContain("#C8C8C8");
        wpfAdapter.Should().NotContain("#B7BCC2");
        wpf.Should().NotContain("Color=\"#C8C8C8\"");
        wpf.Should().NotContain("Color=\"#B7BCC2\"");
    }
}
