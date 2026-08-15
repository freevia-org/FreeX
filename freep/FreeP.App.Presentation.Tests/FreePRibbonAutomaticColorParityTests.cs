using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonAutomaticColorParityTests
{
    [Fact]
    public void AutomaticColorChoice_IsTheSharedClearExplicitColorContract()
    {
        FreePRibbonChoiceCatalog.TryResolve(
                "color.automatic",
                FreePRibbonChoiceCatalog.ColorChoices,
                out FreePRibbonColorChoiceDescriptor descriptor)
            .Should().BeTrue();

        descriptor.Color.Should().BeNull();
    }

    [Fact]
    public void NativeRichTextEditors_ConsumeTheSharedAutomaticColorContract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfShape = Read(root, "freep", "FreeP.App.Rendering.Wpf", "InCanvasTextEditor.cs");
        var wpfTable = Read(root, "freep", "FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs");
        var wpfProfile = Read(root, "freep", "FreeP.App.Host", "MainWindow.RibbonProfile.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditor.cs");

        wpfShape.Should().Contain("session.ApplyValueFormat(")
            .And.NotContain("if (color is null)");
        wpfTable.Should().Contain("InCanvasTextEditPlanner.ApplyTextValueFormat(")
            .And.Contain("TableCellTextValueFormatKind.Color,")
            .And.NotContain("_cellTextBox is null || color is null");
        wpfProfile.Should().Contain("canvas.TextEditor?.ApplyColor(color) == true")
            .And.Contain("canvas.TableCellEditor?.ApplyColor(color) == true")
            .And.NotContain("WithShapeEditor(editor => editor.ApplyColor(color))")
            .And.NotContain("WithTableEditor(editor => editor.ApplyColor(color))");
        avalonia.Should().Contain("_session.ApplyValueFormat(")
            .And.Contain("TableCellTextValueFormatKind.Color,");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
