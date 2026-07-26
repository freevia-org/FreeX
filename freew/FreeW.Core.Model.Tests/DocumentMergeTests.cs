namespace FreeW.Core.Model.Tests;

public class DocumentMergeTests
{
    [Fact]
    public void CloneBlocks_CopiesTextAndFormatting_AndLeavesSourceUntouched()
    {
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold bit", new RunFormatting { Bold = true, ColorHex = "#FF0000" }));
        paragraph.Runs.Add(new Run(" plain"));
        paragraph.StyleId = "Heading1";
        source.Blocks.Add(paragraph);

        var clones = DocumentMerge.CloneBlocks(source);

        var clonedParagraph = clones.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Subject;
        clonedParagraph.PlainText.Should().Be("Bold bit plain");
        clonedParagraph.StyleId.Should().Be("Heading1");
        clonedParagraph.Runs[0].Formatting.Bold.Should().BeTrue();
        clonedParagraph.Runs[0].Formatting.ColorHex.Should().Be("#FF0000");

        // The clone is independent: mutating it must not touch the source.
        clonedParagraph.Runs[0].Text = "Changed";
        source.Blocks.OfType<Paragraph>().Single().Runs[0].Text.Should().Be("Bold bit");
        ReferenceEquals(clonedParagraph.Runs[0], paragraph.Runs[0]).Should().BeFalse();
        ReferenceEquals(clonedParagraph, paragraph).Should().BeFalse();
    }

    [Fact]
    public void CloneBlocks_PreservesSharedBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("References") { BlockContentControl = control });
        source.Blocks.Add(new Paragraph("Entry") { BlockContentControl = control });

        var clones = DocumentMerge.CloneBlocks(source);

        clones.Should().HaveCount(2);
        clones[0].BlockContentControl.Should().Be(control);
        ReferenceEquals(clones[1].BlockContentControl, clones[0].BlockContentControl).Should().BeTrue();
    }

    [Fact]
    public void CloneBlocks_DeepCopiesTables()
    {
        var source = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("A1"));
        table.Rows[0].Cells[0].ShadingColorHex = "#00FF00";
        source.Blocks.Add(table);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Table>().Subject;

        clone.Rows[0].Cells[0].PlainText.Should().Be("A1");
        clone.Rows[0].Cells[0].ShadingColorHex.Should().Be("#00FF00");

        // Independence: editing the cloned cell does not change the source table.
        clone.Rows[0].Cells[0].Paragraphs[0].Runs[0].Text = "Z";
        table.Rows[0].Cells[0].PlainText.Should().Be("A1");
        ReferenceEquals(clone.Rows[0].Cells[0], table.Rows[0].Cells[0]).Should().BeFalse();
    }

    [Fact]
    public void CloneBlocks_PreservesRichImageState_WithoutSharingTheImageModel()
    {
        var image = new InlineImage([1, 2, 3], 144, 72)
        {
            AltText = "Cropped floating image",
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 18,
            VerticalOffsetPt = 12,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Page,
            ZOrderIndex = 7,
            RotationAngle = 15,
            FlipH = true,
            CropLeft = 0.1,
            CropBottom = 0.2,
            BorderColorHex = "4472C4",
            BorderWidthPt = 2,
            BrightnessPct = 20,
            ContrastPct = -15,
            SaturationPct = 80,
            TransparencyPct = 10,
            RecolorMode = ImageRecolorMode.Sepia,
            ColorTemperature = 25,
            ShadowPreset = 2,
            GlowSizePt = 4,
            GlowColorHex = "70AD47",
            ReflectionPreset = 1,
            SoftEdgePt = 3,
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
            PictureStylePreset = 8
        };
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(string.Empty) { Image = image });
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().Image!;

        clone.Should().NotBeSameAs(image);
        clone.Bytes.Should().BeSameAs(image.Bytes);
        clone.AltText.Should().Be("Cropped floating image");
        clone.Wrapping.Should().Be(ImageWrapping.Square);
        clone.HorizontalOffsetPt.Should().Be(18);
        clone.VerticalOffsetPt.Should().Be(12);
        clone.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.ZOrderIndex.Should().Be(7);
        clone.RotationAngle.Should().Be(15);
        clone.FlipH.Should().BeTrue();
        clone.CropLeft.Should().Be(0.1);
        clone.CropBottom.Should().Be(0.2);
        clone.BorderColorHex.Should().Be("4472C4");
        clone.BrightnessPct.Should().Be(20);
        clone.ContrastPct.Should().Be(-15);
        clone.SaturationPct.Should().Be(80);
        clone.TransparencyPct.Should().Be(10);
        clone.RecolorMode.Should().Be(ImageRecolorMode.Sepia);
        clone.ColorTemperature.Should().Be(25);
        clone.ShadowPreset.Should().Be(2);
        clone.GlowSizePt.Should().Be(4);
        clone.ReflectionPreset.Should().Be(1);
        clone.SoftEdgePt.Should().Be(3);
        clone.ArtisticEffect.Should().Be(ImageArtisticEffect.GlowDiffused);
        clone.PictureStylePreset.Should().Be(8);

        clone.WidthPt = 100;
        image.WidthPt.Should().Be(144);
    }

    [Fact]
    public void Merge_AppendsSourceBlocks_WithTextIntact_AndSourceUnchanged()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("Target one"));
        target.Blocks.Add(new Paragraph("Target two"));

        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("Source one"));
        source.Blocks.Add(new Paragraph("Source two"));

        DocumentMerge.Merge(target, target.Blocks.Count, source);

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Target one", "Target two", "Source one", "Source two");

        // Source is untouched (still two blocks, same text, and not aliased into the target).
        source.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Source one", "Source two");
        target.Blocks.Should().NotContain(source.Blocks[0]);
    }

    [Fact]
    public void InsertBlocksAt_PlacesBlocksAtTheGivenIndex()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("First"));
        target.Blocks.Add(new Paragraph("Last"));

        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("Inserted A"));
        source.Blocks.Add(new Paragraph("Inserted B"));

        DocumentMerge.Merge(target, 1, source);

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("First", "Inserted A", "Inserted B", "Last");
    }

    [Fact]
    public void InsertBlocksAt_ClampsOutOfRangeIndexToTheBodyEnd()
    {
        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("Only"));

        DocumentMerge.InsertBlocksAt(target, 999, new[] { new Paragraph("Appended") });

        target.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Only", "Appended");
    }
}
