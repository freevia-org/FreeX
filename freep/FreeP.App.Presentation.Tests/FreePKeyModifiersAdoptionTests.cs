using System.Text.RegularExpressions;
using Free.Shared.Shell;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePKeyModifiersAdoptionTests
{
    private static readonly string[] SourceProjects =
    [
        "FreeP.App.Presentation",
        "FreeP.App.Host",
        "FreeP.App.Avalonia",
        "FreeP.App.Rendering.Wpf",
        "FreeP.App.Rendering.Avalonia",
    ];

    private static readonly string[] RemovedEnumNames =
    [
        "CanvasGestureModifiers",
        "FreePKeyboardModifiers",
        "SmartArtTextPaneShortcutModifiers",
        "TableCellEditKeyboardModifiers",
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
    public void FreePDoesNotRedeclareAProductModifierEnum()
    {
        foreach (var (file, text) in ProductionSources())
        {
            Regex.IsMatch(text, @"\benum\s+\w*Modifiers?\b").Should().BeFalse(file);
            foreach (var name in RemovedEnumNames)
                Regex.IsMatch(text, $@"\b{name}\b").Should().BeFalse($"{file} still references {name}");
        }
    }

    [Fact]
    public void FreePDoesNotHandWriteNativeToNeutralModifierMappers()
    {
        foreach (var (file, text) in ProductionSources())
        {
            text.Should().NotContain("result |= FreeKeyModifiers.", file);
            text.Should().NotContain("modifiers |= FreeKeyModifiers.", file);
            text.Should().NotContain("ModifierKeys result", file);
            text.Should().NotContain("MapModifiers(", file);
        }
    }

    [Fact]
    public void FreePHostsMapModifiersThroughTheSharedMappersWithAnExplicitMetaPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, "freep", .. parts]));

        var wpfWindow = Read("FreeP.App.Host", "MainWindow.cs");
        var avaloniaWindow = Read("FreeP.App.Avalonia", "MainWindow.cs");

        wpfWindow.Should().Contain("WpfKeyModifiersMapper.FromWpf(keyboardModifiers, KeyModifierMetaPolicy.Drop)");
        wpfWindow.Should().Contain("WpfKeyModifiersMapper.ToWpf(shortcut.Modifiers)");
        avaloniaWindow.Should().Contain("AvaloniaKeyModifiersMapper.FromAvalonia(keyboardModifiers, KeyModifierMetaPolicy.AsControl)");
        avaloniaWindow.Should().Contain("AvaloniaKeyModifiersMapper.FromAvalonia(modifiers, KeyModifierMetaPolicy.AsControl)");

        Read("FreeP.App.Rendering.Wpf", "CanvasGestureHandler.cs")
            .Should().Contain("WpfKeyModifiersMapper.FromWpf(modifiers, KeyModifierMetaPolicy.Keep)");
        Read("FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs")
            .Should().Contain("WpfKeyModifiersMapper.FromWpf(modifiers, KeyModifierMetaPolicy.Keep)");
        foreach (var file in new[] { "AvaloniaCanvasGestureHandler.cs", "AvaloniaInCanvasTextEditor.cs", "AvaloniaRichTextEditor.cs" })
        {
            Read("FreeP.App.Rendering.Avalonia", file)
                .Should().Contain("AvaloniaKeyModifiersMapper.FromAvalonia(modifiers, KeyModifierMetaPolicy.Keep)", file);
        }
    }

    private static IEnumerable<(string File, string Text)> ProductionSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sep = Path.DirectorySeparatorChar;
        foreach (var project in SourceProjects)
        {
            var dir = Path.Combine(root, "freep", project);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}"))
                    continue;
                yield return (file, File.ReadAllText(file));
            }
        }
    }
}
