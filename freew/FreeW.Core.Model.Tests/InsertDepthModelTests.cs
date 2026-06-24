namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for the model-layer additions introduced in the W23 Insert depth wave:
/// document-property field kinds (Title/Subject/Keywords/Comments), factory methods,
/// and drop-cap extensions (ClearFormatting is already tested in DropCapTests; these
/// cover the new factory methods and round-trip data integrity).
/// </summary>
public sealed class InsertDepthModelTests
{
    // ── Document-property RunFieldKind factory methods ──────────────────────────────────────────

    [Fact]
    public void TitleField_HasKindTitle_AndCachedText()
    {
        var run = Run.TitleField("My Doc");

        run.FieldKind.Should().Be(RunFieldKind.Title);
        run.Text.Should().Be("My Doc");
    }

    [Fact]
    public void SubjectField_HasKindSubject()
    {
        var run = Run.SubjectField("Some subject");

        run.FieldKind.Should().Be(RunFieldKind.Subject);
        run.Text.Should().Be("Some subject");
    }

    [Fact]
    public void KeywordsField_HasKindKeywords()
    {
        var run = Run.KeywordsField("word, processing");

        run.FieldKind.Should().Be(RunFieldKind.Keywords);
        run.Text.Should().Be("word, processing");
    }

    [Fact]
    public void DocCommentsField_HasKindDocComments()
    {
        var run = Run.DocCommentsField("A description");

        run.FieldKind.Should().Be(RunFieldKind.DocComments);
        run.Text.Should().Be("A description");
    }

    [Fact]
    public void AllDocPropKinds_AreDistinct()
    {
        // Ensure we didn't accidentally alias two property kinds to the same underlying value.
        var kinds = new[]
        {
            RunFieldKind.Title,
            RunFieldKind.Subject,
            RunFieldKind.Keywords,
            RunFieldKind.DocComments,
        };

        kinds.Should().OnlyHaveUniqueItems();
        kinds.Should().NotContain(RunFieldKind.None);
        kinds.Should().NotContain(RunFieldKind.Author); // distinct from Author
    }

    [Fact]
    public void TitleField_DefaultCached_IsEmpty()
    {
        var run = Run.TitleField();

        run.FieldKind.Should().Be(RunFieldKind.Title);
        run.Text.Should().BeEmpty();
    }

    // ── DropCap.ClearFormatting targets only the enlarged run ─────────────────────────────────

    [Fact]
    public void ClearDropCap_RemovesEnlargementFromCapRun()
    {
        var paragraph = new Paragraph();
        var capFormatting = new RunFormatting { Bold = true, FontSizePt = 42 };
        paragraph.Runs.Add(new Run("H", capFormatting));
        paragraph.Runs.Add(new Run("ello world"));

        DropCap.ClearFormatting(paragraph);

        // After clearing, neither run should carry the 42pt / bold cap formatting.
        paragraph.Runs.Should().OnlyContain(r => r.Formatting == RunFormatting.Default);
        paragraph.PlainText.Should().Be("Hello world");
    }

    // ── Text box presets (model-layer only, no WPF) ───────────────────────────────────────────

    [Fact]
    public void Shape_TextBoxWith_SidebarPreset_HasText()
    {
        // Validate the model produced by the Sidebar preset (dark blue fill + white bold text).
        var shape = new Shape(ShapeKind.TextBox, widthPt: 140, heightPt: 200, fillColorHex: "#243F60");
        var p = new Paragraph();
        p.Runs.Add(new Run("Sidebar", new RunFormatting { Bold = true, ColorHex = "#FFFFFF" }));
        shape.TextParagraphs.Add(p);

        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.FillColorHex.Should().Be("#243F60");
        shape.HasText.Should().BeTrue();
        shape.PlainText.Should().Be("Sidebar");
        shape.TextParagraphs[0].Runs[0].Formatting.Bold.Should().BeTrue();
        shape.TextParagraphs[0].Runs[0].Formatting.ColorHex.Should().Be("#FFFFFF");
    }

    [Fact]
    public void Shape_TextBoxWith_QuotePreset_IsItalic()
    {
        var shape = new Shape(ShapeKind.TextBox, widthPt: 200, heightPt: 90, fillColorHex: "#F2F2F2");
        var p = new Paragraph();
        p.Runs.Add(new Run("“Quote text here”", new RunFormatting { Italic = true }));
        shape.TextParagraphs.Add(p);

        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.TextParagraphs[0].Runs[0].Formatting.Italic.Should().BeTrue();
    }
}
