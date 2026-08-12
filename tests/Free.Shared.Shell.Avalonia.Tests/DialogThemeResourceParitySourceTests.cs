namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class DialogThemeResourceParitySourceTests
{
    [Fact]
    public void Avalonia_compact_chrome_consumes_the_WPF_dialog_theme_aliases()
    {
        var avalonia = File.ReadAllText(RepoFile(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs"));
        var wpf = File.ReadAllText(RepoFile(
            "shared", "Free.Shared.Shell.Wpf", "DialogResources.xaml"));

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
        var source = File.ReadAllText(RepoFile(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("selector.OfType<Button>().Class(\":pointerover\")");
        source.Should().Contain("selector.OfType<Button>().Class(\":pressed\")");
        source.Should().Contain("selector.OfType<Button>().Class(\":disabled\")");
        source.Should().Contain("selector.OfType<TextBox>().Class(\":focus\")");
        source.Should().Contain("selector.OfType<TextBox>().Class(\":pointerover\")");
        source.Should().Contain("groupBox.Foreground = accent;");
        source.Should().Contain("Foreground = accent,");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");
        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
