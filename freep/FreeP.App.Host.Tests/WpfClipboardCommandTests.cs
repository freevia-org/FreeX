using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class WpfClipboardCommandTests
{
    [StaFact]
    public void RibbonCut_ExportsSelectionBeforeSingleUndoableDelete()
    {
        var fixture = CreateFixture();

        ExecuteRibbonCut(fixture.Editor, fixture.Service);

        fixture.Clipboard.WriteCount.Should().Be(1);
        fixture.Clipboard.LastDataObject!
            .GetData(DataFormats.UnicodeText)
            .Should().Be("Clipboard parity");
        fixture.Renderer.RenderedShapeNames.Should().Equal("Parity shape");
        fixture.Slide.Shapes.Should().BeEmpty();
        fixture.Editor.CanPaste.Should().BeTrue();
        fixture.Editor.CanUndo.Should().BeTrue();

        fixture.Editor.Undo();

        fixture.Slide.Shapes.Should().ContainSingle();
        fixture.Editor.CanUndo.Should().BeFalse("cut should add exactly one delete to undo history");
    }

    [StaFact]
    public void RibbonCut_InternalPasteRetainsEditableShapeFidelity()
    {
        var fixture = CreateFixture();

        ExecuteRibbonCut(fixture.Editor, fixture.Service);
        fixture.Service.Paste(fixture.Editor, preferOsClipboard: true);

        var pasted = fixture.Slide.Shapes.Should().ContainSingle().Subject;
        pasted.Kind.Should().Be(SlideShapeKind.AutoShape);
        pasted.Name.Should().Be("Parity shape");
        pasted.AutoShapeKind.Should().Be(Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle);
        pasted.ExtentCxEmu.Should().Be(2_743_200);
        pasted.ExtentCyEmu.Should().Be(1_828_800);
        GetText(pasted).Should().Be("Clipboard parity");
    }

    [StaFact]
    public void RibbonCut_MatchesKeyboardCutBehavior()
    {
        var ribbon = CreateFixture();
        var keyboard = CreateFixture();

        ExecuteRibbonCut(ribbon.Editor, ribbon.Service);
        WpfClipboardCommands.Cut(keyboard.Editor, keyboard.Service);

        ribbon.Clipboard.WriteCount.Should().Be(keyboard.Clipboard.WriteCount);
        ribbon.Clipboard.LastDataObject!.GetData(DataFormats.UnicodeText)
            .Should().Be(keyboard.Clipboard.LastDataObject!.GetData(DataFormats.UnicodeText));
        ribbon.Renderer.RenderedShapeNames.Should().Equal(keyboard.Renderer.RenderedShapeNames);
        ribbon.Slide.Shapes.Count.Should().Be(keyboard.Slide.Shapes.Count);
        ribbon.Editor.CanPaste.Should().Be(keyboard.Editor.CanPaste);
        ribbon.Editor.CanUndo.Should().Be(keyboard.Editor.CanUndo);
    }

    private static void ExecuteRibbonCut(EditingSession editor, OsClipboardService service)
    {
        var registry = FreePRibbonCommands.Build(
            new Free.Shared.Ribbon.RibbonStateStore(),
            editor,
            osClipboard: service);

        registry.TryGet("freep.cut", out var command).Should().BeTrue();
        command!.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
    }

    private static ClipboardFixture CreateFixture()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1u,
            Name = "Parity shape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 2_743_200,
            ExtentCyEmu = 1_828_800,
            TextBody = new TextBody()
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Clipboard parity" });
        shape.TextBody.Paragraphs.Add(paragraph);
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        var clipboard = new RecordingClipboard();
        var renderer = new RecordingRenderer();
        var service = new OsClipboardService(clipboard, renderer);
        return new ClipboardFixture(editor, slide, service, clipboard, renderer);
    }

    private static string GetText(SlideShape shape) =>
        string.Concat(shape.TextBody!.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));

    private sealed record ClipboardFixture(
        EditingSession Editor,
        Slide Slide,
        OsClipboardService Service,
        RecordingClipboard Clipboard,
        RecordingRenderer Renderer);

    private sealed class RecordingClipboard : IOsClipboard
    {
        public int WriteCount { get; private set; }
        public DataObject? LastDataObject { get; private set; }
        public long SequenceNumber { get; private set; } = 1;

        public bool ContainsImage() => false;
        public bool ContainsText() => false;
        public byte[]? GetImagePngBytes() => null;
        public string? GetText() => null;

        public void SetDataObject(DataObject data)
        {
            WriteCount++;
            LastDataObject = data;
            SequenceNumber++;
        }
    }

    private sealed class RecordingRenderer : IShapeRenderer
    {
        public List<string> RenderedShapeNames { get; } = new();

        public byte[] RenderShapesToPng(
            Presentation presentation,
            Slide slide,
            IReadOnlyList<SlideShape> shapes,
            int widthPx,
            int heightPx)
        {
            RenderedShapeNames.AddRange(shapes.Select(shape => shape.Name));
            return Array.Empty<byte>();
        }
    }
}
