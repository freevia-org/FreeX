using Avalonia.Input;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaKeyTipTokenFormatterTests
{
    [Theory]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Z, "Z")]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D9, "9")]
    [InlineData(Key.NumPad0, null)]
    [InlineData(Key.F10, null)]
    [InlineData(Key.LeftAlt, null)]
    [InlineData(Key.OemPlus, null)]
    [InlineData(Key.None, null)]
    public void Format_preserves_the_three_host_key_mapping(Key key, string? expected) =>
        AvaloniaKeyTipTokenFormatter.Format(key).Should().Be(expected);

    [Fact]
    public void Avalonia_hosts_delegate_key_tip_formatting_and_normalization()
    {
        var root = FindRepositoryRoot();
        var hostSources = new[]
        {
            Read(root, "src", "FreeX.App.Avalonia", "MainWindow.DesktopChrome.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in hostSources)
        {
            source.Should().Contain("AvaloniaRibbonKeyTipInputPlanner.Resolve(")
                .And.NotContain("AvaloniaKeyTipTokenFormatter.Format(")
                .And.NotContain("ToRibbonKeyTipToken");
        }

        var inputPlanner = Read(
            root,
            "shared",
            "Free.Shared.Ribbon.Avalonia",
            "AvaloniaRibbonKeyTipInputPlanner.cs");
        inputPlanner.Should().Contain("AvaloniaKeyTipTokenFormatter.Format(");

        var legacySequences = Read(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.LegacyShortcutSequences.cs");
        legacySequences.Should().Contain("AvaloniaKeyTipTokenFormatter.Format(")
            .And.NotContain("ToRibbonKeyTipToken");

        var renderer = Read(
            root,
            "shared",
            "Free.Shared.Ribbon.Avalonia",
            "AvaloniaRibbonRenderer.cs");
        renderer.Should().Contain("RibbonKeyTipText.NormalizeOrEmpty(keyTip)")
            .And.NotContain("keyTip.Trim().ToUpperInvariant()");

        var routes = Read(root, "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        routes.Should().Contain("Routes.Value.Match(input)")
            .And.NotContain("private static string Normalize(string value)");

        var routeCatalog = Read(
            root,
            "src",
            "FreeX.App.Presentation",
            "Ribbon",
            "FreeXRibbonKeyTipRoutePlanner.cs");
        routeCatalog.Should().Contain("RibbonKeyTipText.Normalize(input)");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
