using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingShapeRichTextModelTests
{
    [Fact]
    public void DocumentProjectsParagraphsFieldsAndBreaks_AndCopiesInputCollections()
    {
        var nodes = new List<DrawingShapeTextInline>
        {
            new(DrawingShapeTextInlineKind.Run, "Hello", style: new DrawingShapeTextRunStyle(Bold: true)),
            new(DrawingShapeTextInlineKind.Break),
            new(DrawingShapeTextInlineKind.Field, "42", "field-id", "slidenum")
        };
        var paragraphs = new List<DrawingShapeTextParagraph> { new(nodes), new([new(DrawingShapeTextInlineKind.Run, "Next")]) };
        var document = new DrawingShapeTextDocument(paragraphs);
        nodes.Clear();
        paragraphs.Clear();
        document.PlainText.Should().Be("Hello\n42\nNext");
        document.Paragraphs.Should().HaveCount(2);
        document.Paragraphs[0].Inlines.Should().HaveCount(3);
        document.Paragraphs.Should().NotBeOfType<DrawingShapeTextParagraph[]>();
        document.Paragraphs[0].Inlines.Should().NotBeOfType<DrawingShapeTextInline[]>();
    }

    [Fact]
    public void PlainTextNoOpPreservesDocument_ChangedTextClearsIt_AndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var document = new DrawingShapeTextDocument([new([new(DrawingShapeTextInlineKind.Run, "Before")])]);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), ShapeTextDocument = document };
        sheet.DrawingShapes.Add(shape);
        shape.ShapeText.Should().Be("Before");
        shape.ShapeText = "Before";
        shape.ShapeTextDocument.Should().BeSameAs(document);
        var context = new TestCommandContext(workbook);
        new SetDrawingShapeTextCommand(sheet.Id, shape.Id, "Before").Apply(context).IsNoOp.Should().BeTrue();
        var command = new SetDrawingShapeTextCommand(sheet.Id, shape.Id, "After");
        command.Apply(context).Success.Should().BeTrue();
        shape.ShapeTextDocument.Should().BeNull();
        command.Revert(context);
        shape.ShapeText.Should().Be("Before");
        shape.ShapeTextDocument.Should().BeSameAs(document);
        command.Apply(context).Success.Should().BeTrue();
        shape.ShapeTextDocument.Should().BeNull();
    }

    [Fact]
    public void StructuredTextCannotBeAttachedToConnectorOrConvertedToConnector()
    {
        var document = new DrawingShapeTextDocument([new([new(DrawingShapeTextInlineKind.Run, "Text")])]);
        var connector = new DrawingShapeModel { Kind = DrawingShapeKind.Line };
        Action attach = () => connector.ShapeTextDocument = document;
        attach.Should().Throw<InvalidOperationException>();
        var shape = new DrawingShapeModel { ShapeTextDocument = document };
        Action convert = () => shape.Kind = DrawingShapeKind.Line;
        convert.Should().Throw<InvalidOperationException>();
        shape.ShapeTextDocument.Should().BeSameAs(document);
    }
}
