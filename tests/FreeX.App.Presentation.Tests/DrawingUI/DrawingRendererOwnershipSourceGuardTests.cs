using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingRendererOwnershipSourceGuardTests
{
    private static readonly string[] PortableDrawingCommandConstruction =
    [
        "new RotatePictureCommand(",
        "new SetPictureCropCommand(",
        "new SetDrawingShapeGradientCommand(",
        "new SetDrawingShapeEffectCommand(",
        "new SetTextBoxTextCommand(",
        "new DuplicateDrawingObjectCommand(",
    ];

    [Fact]
    public void OwnedRendererFiles_DoNotConstructPortableDrawingCommands()
    {
        var rendererSources = OwnedRendererFiles().Select(File.ReadAllText).ToArray();

        foreach (var commandConstruction in PortableDrawingCommandConstruction)
            rendererSources.Should().OnlyContain(source => !source.Contains(commandConstruction, StringComparison.Ordinal));
    }

    [Fact]
    public void Renderers_DelegateInteractionClipboardAndInlineEditPolicy()
    {
        var sourceRoot = RepositoryFileLocator.FindDirectory("src");
        var wpfInput = Read(sourceRoot, "FreeX.App.UI", "GridView.FormControls.Input.cs");
        var avaloniaForm = Read(sourceRoot, "FreeX.App.Avalonia", "MainWindow.FormControls.cs");
        var wpfClipboard = Read(sourceRoot, "FreeX.App.Host", "MainWindow.ClipboardCommands.cs");
        var avaloniaClipboard = Read(sourceRoot, "FreeX.App.Avalonia", "MainWindow.DrawingObjectClipboard.cs");
        var avaloniaFormat = Read(sourceRoot, "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs");

        wpfInput.Should().Contain("FormControlRenderPlanner.PlanInteraction(");
        avaloniaForm.Should().Contain("FormControlRenderPlanner.PlanInteraction(");
        wpfClipboard.Should().Contain("CreatePasteSelectionPlan(");
        avaloniaClipboard.Should().Contain("CreatePasteSelectionPlan(");
        avaloniaFormat.Should().Contain("ColorInputParser.TryParseRgbColorText(");
        avaloniaFormat.Should().NotContain(".Split(',', StringSplitOptions.TrimEntries)");
    }

    [Fact]
    public void PortableDrawingPolicy_RemainsRendererNeutral()
    {
        var sourceRoot = RepositoryFileLocator.FindDirectory("src");
        var sources = new[]
        {
            Read(sourceRoot, "FreeX.App.Presentation", "Drawing", "FormControlRenderPlanner.cs"),
            Read(sourceRoot, "FreeX.App.Presentation", "DrawingInteraction", "ObjectDragPlanner.cs"),
            Read(sourceRoot, "FreeX.App.Presentation", "DrawingUI", "DrawingObjectClipboardSession.cs"),
            Read(sourceRoot, "FreeX.App.Presentation", "DrawingUI", "TextBoxInlineEditSession.cs"),
        };

        sources.Should().OnlyContain(source => !source.Contains("System.Windows", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("Avalonia.", StringComparison.Ordinal));
    }

    private static IEnumerable<string> OwnedRendererFiles()
    {
        var sourceRoot = RepositoryFileLocator.FindDirectory("src");
        var files = new[]
        {
            ("FreeX.App.Host", "MainWindow.Drawing.cs"),
            ("FreeX.App.Host", "MainWindow.DrawingContextualTabs.cs"),
            ("FreeX.App.Host", "MainWindow.FormControls.cs"),
            ("FreeX.App.Host", "MainWindow.TextBoxInlineEditing.cs"),
            ("FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs"),
            ("FreeX.App.Avalonia", "MainWindow.DrawingObjectClipboard.cs"),
            ("FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"),
            ("FreeX.App.Avalonia", "MainWindow.FormControls.cs"),
            ("FreeX.App.Avalonia", "MainWindow.TextBoxInlineEditing.cs"),
        };
        return files.Select(file => Path.Combine(sourceRoot, file.Item1, file.Item2));
    }

    private static string Read(string sourceRoot, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { sourceRoot }.Concat(parts).ToArray()));
}
