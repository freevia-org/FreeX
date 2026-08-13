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

        string[] tokenNames =
        [
            "BorderHex",
            "FieldBorderHex",
            "DisabledForegroundHex",
            "DisabledBorderHex",
        ];

        foreach (var tokenName in tokenNames)
        {
            portable.Should().Contain($"public const string {tokenName}");
            avalonia.Should().Contain($"CompactDialogVisualTokens.{tokenName}");
            wpf.Should().Contain($"CompactDialogVisualTokens.{tokenName}");
        }

        wpf.Should().Contain("CompactDialogVisualTokens.PrimaryPressedHex");
        wpf.Should().Contain("CompactDialogVisualTokens.PrimaryDisabledHex");
    }
}
