using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// WPF-host evidence for the shared grouped-shape text command route. The host and Avalonia editor
/// must resolve the same child path and preserve the native group graph while editing the leaf.
/// </summary>
public sealed class NestedGroupedShapeTextParityTests
{
    [Fact]
    public void Wpf_shared_shape_text_commands_edit_the_nested_leaf_and_restore_it_on_undo()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var leaf = Shape.TextBoxWith("WPF leaf", 120, 48);
        var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 24, 20));
        inner.ChildOffsets.Add((5, 5));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((32, 12));
        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 130 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((18, 10));
        outer.Children.Add(new Shape(ShapeKind.Ellipse, 30, 20));
        outer.ChildOffsets.Add((180, 70));
        var paragraph = new Paragraph();
        var groupRun = Run.FromDrawingGroup(outer);
        paragraph.Runs.Add(groupRun);
        document.Blocks.Add(paragraph);

        var context = new Context(document);
        var path = new[] { 0, 1 };
        var replacement = new Paragraph();
        replacement.Runs.Add(new Run("WPF edited", RunFormatting.Default with { Bold = true }));
        var command = new ReplaceShapeTextParagraphsCommand(0, 0, [replacement], path);

        command.Apply(context);

        leaf.PlainText.Should().Be("WPF edited");
        leaf.TextParagraphs[0].Runs.Single().Formatting.Bold.Should().BeTrue();
        groupRun.Text.Should().BeEmpty();
        outer.Children.Should().ContainSingle(child => ReferenceEquals(child, inner));

        command.Revert(context);

        leaf.PlainText.Should().Be("WPF leaf");
        outer.Children.Should().HaveCount(2);
        inner.Children.Should().HaveCount(2);
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
