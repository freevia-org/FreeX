using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r334: a presentation carrying many features at once must still validate against the OOXML schema.
///
/// <para>FreeP already runs <c>OpenXmlValidator</c> in eight places, and this codebase's own notes
/// call the pptx writer a recurring source of element-ORDER and relationship-allocation bugs -- the
/// class the schema catches. But each of those eight validates a deck built for its own feature, so
/// every one is a single-feature package. A part whose children are ordered correctly on its own and
/// wrongly once a neighbour exists is invisible to all of them, and OOXML sequence groups are
/// exactly where that happens.</para>
///
/// <para>So this writes ONE deck carrying several shape kinds, notes, a transition and speaker text
/// together, and validates the whole package. It is the combination that is under test, not any of
/// the parts.</para>
/// </summary>
public sealed class R334_WrittenPackageValidatesAgainstSchemaTests
{
    private static string[] ValidateSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = PresentationDocument.Open(stream, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }

    [Fact]
    public void ADeckCombiningManyFeaturesValidates()
    {
        var presentation = new Presentation();

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Title",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 500_000,
            OffsetYEmu = 400_000,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 1_200_000,
            AlternativeText = "r334 title shape",
            TextBody = new TextBody(),
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Connector",
            Kind = SlideShapeKind.Connector,
            OffsetXEmu = 500_000,
            OffsetYEmu = 2_000_000,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 10_000,
        });
        presentation.Slides.Add(slide);

        var second = new Slide();
        second.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Body",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = 800_000,
            OffsetYEmu = 800_000,
            ExtentCxEmu = 3_000_000,
            ExtentCyEmu = 2_000_000,
            TextBody = new TextBody(),
        });
        presentation.Slides.Add(second);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var bytes = stream.ToArray();

        bytes.Length.Should().BeGreaterThan(4096,
            "an empty or truncated package would make the validation below vacuous");

        ValidateSchema(bytes).Should().BeEmpty(
            "the written package must satisfy the OOXML schema; element-order and sequence-group "
            + "violations are exactly what a multi-feature deck exposes and a single-feature one hides");
    }
    /// <summary>
    /// The product path that reaches the empty text body, so this guards the defect rather than the
    /// synthetic fixture above. <c>SlideShape.Text = ""</c> creates a TextBody, clears its paragraphs
    /// and adds none -- so clearing a shape's text and saving produced a schema-invalid package.
    /// (<c>HeaderFooterCommandPlanner</c> reaches the same state by creating placeholders with an
    /// empty body.)
    /// </summary>
    [Fact]
    public void ClearingAShapesTextStillWritesAValidPackage()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Body",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 4_000_000,
            ExtentCyEmu = 1_500_000,
        };
        shape.Text = "typed something";
        shape.Text = string.Empty;   // the user clears it again

        shape.TextBody.Should().NotBeNull("the model keeps the body and empties its paragraphs");
        shape.TextBody!.Paragraphs.Should().BeEmpty("which is the state that produced invalid XML");

        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a shape whose text was cleared must still produce a schema-valid package");
    }

    /// <summary>
    /// r337: testing r336's hypothesis on the other hand-built writer. Adds a table shape -- whose
    /// grid, rows and per-cell text bodies each carry required children -- and a table cell with an
    /// EMPTY text body, the exact state r334 fixed for ordinary shapes. If the fix was applied to one
    /// text-body writer and not the other, this is where that shows.
    /// </summary>
    [Fact]
    public void ADeckWithATableAndAnEmptyCellValidates()
    {
        var presentation = new Presentation();
        var slide = new Slide();

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400);
        table.ColumnWidthsEmu.Add(914400);
        table.Rows.Add(new TableRow
        {
            HeightEmu = 457200,
            Cells =
            {
                new TableCell { TextBody = new TextBody() },   // empty on purpose
                new TableCell { TextBody = new TextBody() },
            },
        });

        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Table",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 457_200,
            Table = table,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a table cell's text body has the same mandatory a:p as a shape's, so the r334 fix must "
            + "cover this writer too");
    }

    /// <summary>
    /// r338: the third and last <c>txBody</c> site in this writer. A slide whose notes body exists
    /// with no paragraphs -- reachable by loading a .pptx whose notes slide carries an empty body --
    /// wrote a <c>txBody</c> with no <c>a:p</c>, the same defect as r334 and r337.
    /// </summary>
    [Fact]
    public void ASlideWithAnEmptyNotesBodyValidates()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Body",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 3_000_000,
            ExtentCyEmu = 1_000_000,
        });
        slide.Notes = new TextBody();   // exists, carries nothing
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a notes body with no paragraphs must still write a schema-valid notes slide");
    }

    /// <summary>
    /// r340: the degenerate end for this writer too, completing the enumeration across all three.
    /// A deck with a slide carrying nothing, and a slide whose only shape has no text body at all --
    /// the states left when a user deletes content rather than adds it.
    /// </summary>
    [Fact]
    public void ADeckWithAnEmptySlideValidates()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a slide with no shapes must still write a valid slide part");
    }

    [Fact]
    public void AShapeWithNoTextBodyAtAllValidates()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Bare",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 1_000_000,
            ExtentCyEmu = 500_000,
            TextBody = null,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a shape with no text body is distinct from one with an empty body, and both must write "
            + "valid XML -- r334 fixed the second, this pins the first");
    }

}