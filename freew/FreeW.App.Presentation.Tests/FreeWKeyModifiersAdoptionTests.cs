using Free.Shared.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWKeyModifiersAdoptionTests
{
    private static readonly string[] SourceProjects =
    [
        "FreeW.App.Presentation",
        "FreeW.App.Host",
        "FreeW.App.Avalonia",
    ];

    [Fact]
    public void NeutralEnumKeepsTheDocumentedBitValues()
    {
        ((int)FreeKeyModifiers.Control).Should().Be(1);
        ((int)FreeKeyModifiers.Shift).Should().Be(2);
        ((int)FreeKeyModifiers.Alt).Should().Be(4);
        ((int)FreeKeyModifiers.Meta).Should().Be(8);
    }

    [Fact]
    public void FreeWDoesNotRedeclareAProductModifierEnum()
    {
        foreach (var (file, text) in ProductionSources())
        {
            System.Text.RegularExpressions.Regex.IsMatch(text, @"\benum\s+\w*Modifiers?\b")
                .Should().BeFalse(file);
            text.Should().NotContain("DocumentEditorInputModifiers", file);
            text.Should().NotContain("FreeWKeyboardModifiers", file);
        }
    }

    [Fact]
    public void FreeWDoesNotHandWriteNativeToNeutralModifierMappers()
    {
        foreach (var (file, text) in ProductionSources())
        {
            text.Should().NotContain("result |= FreeKeyModifiers.", file);
            text.Should().NotContain("ModifierKeys result", file);
        }
    }

    [Fact]
    public void FreeWHostsMapModifiersThroughTheSharedMappersWithAnExplicitMetaPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        wpf.Should().Contain("WpfKeyModifiersMapper.FromWpf(modifiers, KeyModifierMetaPolicy.Drop)");
        avalonia.Should().Contain("AvaloniaKeyModifiersMapper.FromAvalonia(modifiers, KeyModifierMetaPolicy.AsControl)");
        avaloniaWindow.Should().Contain("AvaloniaKeyModifiersMapper.FromAvalonia(modifiers, KeyModifierMetaPolicy.AsControl)");
    }

    private static IEnumerable<(string File, string Text)> ProductionSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sep = Path.DirectorySeparatorChar;
        foreach (var project in SourceProjects)
        {
            var dir = Path.Combine(root, "freew", project);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}"))
                    continue;
                yield return (file, File.ReadAllText(file));
            }
        }
    }
}
