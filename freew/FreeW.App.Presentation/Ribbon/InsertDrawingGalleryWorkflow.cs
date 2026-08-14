using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum InsertDrawingPreset
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    SimpleTextBox,
    SidebarTextBox,
    QuoteTextBox,
}

public sealed record InsertDrawingGalleryChoice(
    RibbonCommandId CommandId,
    InsertDrawingPreset Preset);

public sealed record InsertDrawingGalleryPorts(Action<Shape> InsertShape);

/// <summary>
/// Owns Insert &gt; Shapes and Text Box gallery policy for both renderers. Hosts provide only the
/// undoable shape insertion adapter; Presentation owns preset construction and command identity.
/// </summary>
public static class InsertDrawingGalleryWorkflow
{
    private static readonly InsertDrawingGalleryChoice[] ShapeChoiceItems =
    [
        new("freew.shape-rectangle", InsertDrawingPreset.Rectangle),
        new("freew.shape-rounded", InsertDrawingPreset.RoundedRectangle),
        new("freew.shape-ellipse", InsertDrawingPreset.Ellipse),
        new("freew.shape-textbox", InsertDrawingPreset.SimpleTextBox),
    ];

    private static readonly InsertDrawingGalleryChoice[] TextBoxChoiceItems =
    [
        new("freew.textbox-simple", InsertDrawingPreset.SimpleTextBox),
        new("freew.textbox-sidebar", InsertDrawingPreset.SidebarTextBox),
        new("freew.textbox-quote", InsertDrawingPreset.QuoteTextBox),
    ];

    public static IReadOnlyList<InsertDrawingGalleryChoice> ShapeChoices => ShapeChoiceItems;
    public static IReadOnlyList<InsertDrawingGalleryChoice> TextBoxChoices => TextBoxChoiceItems;

    public static void Register(
        IRibbonCommandRegistry bindings,
        InsertDrawingGalleryPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertShape);

        bindings.Register("freew.shapes", EmptyRibbonCommand.Instance);

        var commands = new Dictionary<InsertDrawingPreset, IRibbonCommand>();
        foreach (var choice in ShapeChoiceItems.Concat(TextBoxChoiceItems))
        {
            if (!commands.TryGetValue(choice.Preset, out var command))
            {
                var captured = choice.Preset;
                command = new ActionRibbonCommand(() => ports.InsertShape(CreateShape(captured)));
                commands.Add(captured, command);
            }

            bindings.Register(choice.CommandId, command);
        }

        bindings.Register("freew.shape", commands[InsertDrawingPreset.Rectangle]);
        bindings.Register("freew.text-box", commands[InsertDrawingPreset.SimpleTextBox]);
    }

    public static Shape CreateShape(InsertDrawingPreset preset) => preset switch
    {
        InsertDrawingPreset.Rectangle =>
            Shape.Preset(ShapeKind.Rectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"),
        InsertDrawingPreset.RoundedRectangle =>
            Shape.Preset(ShapeKind.RoundedRectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"),
        InsertDrawingPreset.Ellipse =>
            Shape.Preset(ShapeKind.Ellipse, widthPt: 100, heightPt: 100, fillColorHex: "#DCE6F1"),
        InsertDrawingPreset.SimpleTextBox =>
            Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"),
        InsertDrawingPreset.SidebarTextBox => CreateSidebarTextBox(),
        InsertDrawingPreset.QuoteTextBox => CreateQuoteTextBox(),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    private static Shape CreateSidebarTextBox()
    {
        var shape = new Shape(ShapeKind.TextBox, widthPt: 140, heightPt: 200, fillColorHex: "#243F60");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(
            "Sidebar",
            new RunFormatting { Bold = true, ColorHex = "#FFFFFF" }));
        shape.TextParagraphs.Add(paragraph);
        return shape;
    }

    private static Shape CreateQuoteTextBox()
    {
        var shape = new Shape(ShapeKind.TextBox, widthPt: 200, heightPt: 90, fillColorHex: "#F2F2F2");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(
            "\u201cQuote text here\u201d",
            new RunFormatting { Italic = true }));
        shape.TextParagraphs.Add(paragraph);
        return shape;
    }
}
