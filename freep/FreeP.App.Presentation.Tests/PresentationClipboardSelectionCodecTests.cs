using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationClipboardSelectionCodecTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

    [Fact]
    public void MixedEditableSelection_RoundTripsRichShapeTableAndPictureContent()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();

        var richText = new TextBody();
        var paragraph = new Paragraph
        {
            Align = TextAlign.Center,
            BulletKind = BulletKind.Char,
            BulletChar = "*",
        };
        paragraph.Runs.Add(new Run
        {
            Text = "Editable selection",
            FontFamily = "Aptos",
            FontSizePt = 19.5,
            Bold = true,
            BoldSet = true,
            Italic = true,
            ItalicSet = true,
            Underline = true,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
        });
        richText.Paragraphs.Add(paragraph);

        var richShape = new SlideShape
        {
            Id = 11,
            Name = "Rich shape",
            AlternativeTextTitle = "Chart summary",
            AlternativeText = "Editable clipboard shape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 2_743_200,
            ExtentCyEmu = 1_371_600,
            RotationDeg = 12.5,
            Fill = new ShapeFill.Solid(
                new ThemeAwareColor(SrgbColor.FromRgb(0xD9EAF7))),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x2F5597), 1.75),
            TextBody = richText,
        };

        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([1_371_600, 1_371_600]);
        var row = new TableRow { HeightEmu = 685_800 };
        row.Cells.Add(new TableCell { TextBody = Body("Region") });
        row.Cells.Add(new TableCell { TextBody = Body("Q1") });
        table.Rows.Add(row);
        var tableShape = new SlideShape
        {
            Id = 12,
            Name = "Editable table",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 914_400,
            OffsetYEmu = 2_286_000,
            ExtentCxEmu = 2_743_200,
            ExtentCyEmu = 685_800,
            Table = table,
        };

        var pictureShape = new SlideShape
        {
            Id = 13,
            Name = "Embedded picture",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 4_114_800,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 914_400,
            ExtentCyEmu = 914_400,
            Picture = new ImagePart { Bytes = Png, ContentType = "image/png" },
        };
        var unselected = new SlideShape
        {
            Id = 99,
            Name = "Do not copy",
            Kind = SlideShapeKind.AutoShape,
        };
        slide.Shapes.AddRange([richShape, tableShape, pictureShape, unselected]);

        var bytes = PresentationClipboardSelectionCodec.Serialize(
            presentation,
            slide,
            [richShape, tableShape, pictureShape]);
        var roundTrip = PresentationClipboardSelectionCodec.Deserialize(bytes);

        roundTrip.Select(shape => shape.Id).Should().Equal(11u, 12u, 13u);
        roundTrip.Should().NotContain(shape => shape.Id == 99u);

        var richCopy = roundTrip[0];
        richCopy.Kind.Should().Be(SlideShapeKind.AutoShape);
        richCopy.AutoShapeKind.Should().Be(DrawingShapeKind.RoundedRectangle);
        richCopy.OffsetXEmu.Should().Be(914_400);
        richCopy.RotationDeg.Should().BeApproximately(12.5, 0.001);
        richCopy.AlternativeText.Should().Be("Editable clipboard shape");
        richCopy.Fill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xD9EAF7));
        richCopy.Outline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.WidthPt.Should().BeApproximately(1.75, 0.001);
        var richRun = richCopy.TextBody!.Paragraphs.Single().Runs.Single();
        richRun.Text.Should().Be("Editable selection");
        richRun.FontFamily.Should().Be("Aptos");
        richRun.FontSizePt.Should().BeApproximately(19.5, 0.001);
        richRun.Bold.Should().BeTrue();
        richRun.Italic.Should().BeTrue();
        richRun.Underline.Should().BeTrue();
        richRun.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));

        roundTrip[1].Table!.Rows.Single().Cells
            .Select(cell => cell.TextBody!.Paragraphs.Single().Runs.Single().Text)
            .Should().Equal("Region", "Q1");
        roundTrip[2].Picture!.ContentType.Should().Be("image/png");
        roundTrip[2].Picture!.Bytes.Should().Equal(Png);
    }

    private static TextBody Body(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }
}
