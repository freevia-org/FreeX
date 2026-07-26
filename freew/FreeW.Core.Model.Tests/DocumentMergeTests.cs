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
    public void CloneBlocks_PreservesFloatingWordArt_WithoutSharingItsPlacement()
    {
        var wordArt = new WordArt("Merged WordArt", WordArtStyle.GlowGold, 32)
        {
            FontFamily = "Aptos Display",
            Bold = true,
            WidthPt = 220,
            HeightPt = 48,
            RotationAngle = 12,
            FlipH = true,
            AltText = "Decorative merged heading",
            Warp = WordArtWarp.Wave2,
            TextFitMode = WordArtTextFitMode.NormalAutoFit,
            NormalAutoFitFontScale = 85000,
            NormalAutoFitLineSpacingReduction = 12000,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 24,
                VerticalOffsetPt = 18,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 5
            }
        };
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromWordArt(wordArt));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().WordArt!;

        clone.Should().NotBeSameAs(wordArt);
        clone.Text.Should().Be("Merged WordArt");
        clone.Style.Should().Be(WordArtStyle.GlowGold);
        clone.FontFamily.Should().Be("Aptos Display");
        clone.Bold.Should().BeTrue();
        clone.WidthPt.Should().Be(220);
        clone.HeightPt.Should().Be(48);
        clone.RotationAngle.Should().Be(12);
        clone.FlipH.Should().BeTrue();
        clone.AltText.Should().Be("Decorative merged heading");
        clone.Warp.Should().Be(WordArtWarp.Wave2);
        clone.TextFitMode.Should().Be(WordArtTextFitMode.NormalAutoFit);
        clone.NormalAutoFitFontScale.Should().Be(85000);
        clone.NormalAutoFitLineSpacingReduction.Should().Be(12000);
        clone.Placement.Should().NotBeSameAs(wordArt.Placement);
        clone.Placement!.Wrapping.Should().Be(ImageWrapping.InFront);
        clone.Placement.HorizontalOffsetPt.Should().Be(24);
        clone.Placement.VerticalOffsetPt.Should().Be(18);
        clone.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.Placement.ZOrderIndex.Should().Be(5);

        clone.Placement.HorizontalOffsetPt = 0;
        wordArt.Placement!.HorizontalOffsetPt.Should().Be(24);
    }

    [Fact]
    public void CloneBlocks_PreservesSmartArtHierarchy_WithoutSharingNodesOrPlacement()
    {
        var smartArt = new SmartArt
        {
            Kind = SmartArtKind.Hierarchy,
            WidthPt = 360,
            HeightPt = 180,
            LayoutId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
            ColorSchemeId = "accent1_2",
            StyleId = "simple1",
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 20,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 4
            }
        };
        var root = new SmartArtNode("Chief");
        root.AddChild("Operations").AddChild("Field");
        smartArt.Nodes.Add(root);

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().SmartArt!;

        clone.Should().NotBeSameAs(smartArt);
        clone.Kind.Should().Be(SmartArtKind.Hierarchy);
        clone.WidthPt.Should().Be(360);
        clone.HeightPt.Should().Be(180);
        clone.LayoutId.Should().Be("urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3");
        clone.ColorSchemeId.Should().Be("accent1_2");
        clone.StyleId.Should().Be("simple1");
        clone.Placement.Should().NotBeSameAs(smartArt.Placement);
        clone.Placement!.ZOrderIndex.Should().Be(4);
        clone.Nodes.Single().Should().NotBeSameAs(root);
        clone.Nodes.Single().Text.Should().Be("Chief");
        clone.Nodes.Single().Children.Single().Text.Should().Be("Operations");
        clone.Nodes.Single().Children.Single().Children.Single().Text.Should().Be("Field");

        clone.Nodes.Single().Children.Single().Text = "Changed";
        root.Children.Single().Text.Should().Be("Operations");
    }

    [Fact]
    public void CloneBlocks_PreservesFloatingChart_WithoutSharingDataOrPlacement()
    {
        var chart = new Chart
        {
            Kind = ChartKind.Line,
            Title = "Merged chart",
            ShowLegend = true,
            CategoryAxisTitle = "Month",
            ValueAxisTitle = "Revenue",
            WidthPt = 360,
            HeightPt = 216,
            StyleId = 6,
            ColorSchemeId = "mono-blue",
            QuickLayoutId = 4,
            NativeVisualSettings = new ChartNativeVisualSettings(
                ShowGridlines: true,
                HasPlotAreaFill: true,
                ShowDataLabels: false,
                ScatterConnectsPoints: false),
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 20,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Page,
                ZOrderIndex = 4
            }
        };
        chart.Categories.AddRange(["Jan", "Feb"]);
        chart.Series.Add(new ChartSeries("Actual", [10, 20]));
        chart.Series.Add(new ChartSeries("Plan", [12, 24]));

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs.Single().Chart!;

        clone.Should().NotBeSameAs(chart);
        clone.Kind.Should().Be(ChartKind.Line);
        clone.Title.Should().Be("Merged chart");
        clone.ShowLegend.Should().BeTrue();
        clone.CategoryAxisTitle.Should().Be("Month");
        clone.ValueAxisTitle.Should().Be("Revenue");
        clone.WidthPt.Should().Be(360);
        clone.HeightPt.Should().Be(216);
        clone.StyleId.Should().Be(6);
        clone.ColorSchemeId.Should().Be("mono-blue");
        clone.QuickLayoutId.Should().Be(4);
        clone.NativeVisualSettings.Should().Be(chart.NativeVisualSettings);
        clone.Placement.Should().NotBeSameAs(chart.Placement);
        clone.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        clone.Placement.HorizontalOffsetPt.Should().Be(36);
        clone.Placement.VerticalOffsetPt.Should().Be(20);
        clone.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        clone.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        clone.Placement.ZOrderIndex.Should().Be(4);
        clone.Categories.Should().Equal("Jan", "Feb");
        clone.Series.Should().HaveCount(2);
        clone.Series[0].Should().NotBeSameAs(chart.Series[0]);
        clone.Series[0].Name.Should().Be("Actual");
        clone.Series[0].Values.Should().Equal(10, 20);
        clone.Series[1].Name.Should().Be("Plan");
        clone.Series[1].Values.Should().Equal(12, 24);

        clone.Categories[0] = "Changed";
        clone.Series[0].Values[0] = 99;
        clone.Placement.HorizontalOffsetPt = 0;
        chart.Categories[0].Should().Be("Jan");
        chart.Series[0].Values[0].Should().Be(10);
        chart.Placement!.HorizontalOffsetPt.Should().Be(36);
    }

    [Fact]
    public void CloneBlocks_PreservesSemanticInlinePayloads_WithoutSharingMutableState()
    {
        var numerator = new Equation([MathRun.Superscript("x", "2")]);
        var denominator = new Equation([MathRun.Subscript("y", "1")]);
        var equation = new Equation([MathRun.Fraction(numerator, denominator)]);
        var embedded = new EmbeddedObject([1, 2, 3], "Excel.Sheet.12")
        {
            Icon = new InlineImage([4, 5, 6], 48, 36) { AltText = "Workbook" },
            WidthPt = 72,
            HeightPt = 54
        };
        var ruby = new RubyAnnotation
        {
            Alignment = RubyAlignment.DistributeSpace,
            PhoneticSizeHalfPoints = 12,
            RaiseHalfPoints = 9
        };
        ruby.BaseFragments.Add(new RubyTextFragment("漢字", new RunFormatting { Bold = true }));
        ruby.PhoneticFragments.Add(new RubyTextFragment("かんじ", new RunFormatting { FontSizePt = 6 }));
        var references = new List<PreservedDrawingReference>
        {
            new("rId7", "/word/charts/chart42.xml", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart")
        };
        var drawing = new PreservedDrawing("<w:drawing />", references);

        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(Run.FromEmbeddedObject(embedded));
        paragraph.Runs.Add(Run.FromRuby(ruby));
        paragraph.Runs.Add(Run.FromPreservedDrawing(drawing));
        source.Blocks.Add(paragraph);

        var clone = DocumentMerge.CloneBlocks(source).Single().Should().BeOfType<Paragraph>().Subject.Runs;
        var clonedEquation = clone[0].Equation!;
        var clonedEmbedded = clone[1].EmbeddedObject!;
        var clonedRuby = clone[2].Ruby!;
        var clonedDrawing = clone[3].PreservedDrawing!;

        clonedEquation.Should().NotBeSameAs(equation);
        clonedEquation.LinearText.Should().Be("x^2/y_1");
        clonedEquation.Runs.Single().NumeratorEquation.Should().NotBeSameAs(numerator);
        clonedEquation.Runs.Single().DenominatorEquation.Should().NotBeSameAs(denominator);
        clonedEmbedded.Should().NotBeSameAs(embedded);
        clonedEmbedded.Payload.Should().Equal([1, 2, 3]);
        clonedEmbedded.ProgId.Should().Be("Excel.Sheet.12");
        clonedEmbedded.Icon.Should().NotBeSameAs(embedded.Icon);
        clonedEmbedded.Icon!.AltText.Should().Be("Workbook");
        clonedEmbedded.WidthPt.Should().Be(72);
        clonedEmbedded.HeightPt.Should().Be(54);
        clonedRuby.Should().NotBeSameAs(ruby);
        clonedRuby.Alignment.Should().Be(RubyAlignment.DistributeSpace);
        clonedRuby.PhoneticSizeHalfPoints.Should().Be(12);
        clonedRuby.RaiseHalfPoints.Should().Be(9);
        clonedRuby.BaseFragments.Should().Equal(ruby.BaseFragments);
        clonedRuby.PhoneticFragments.Should().Equal(ruby.PhoneticFragments);
        clonedDrawing.Should().NotBeSameAs(drawing);
        clonedDrawing.Xml.Should().Be("<w:drawing />");
        clonedDrawing.References.Should().Equal(drawing.References);
        clonedDrawing.References.Should().NotBeSameAs(drawing.References);

        clonedEquation.Runs.Single().NumeratorEquation!.Runs.Add(MathRun.PlainText("+1"));
        clonedEmbedded.Payload[0] = 9;
        clonedRuby.BaseFragments[0] = new RubyTextFragment("変更", new RunFormatting());
        references.Add(new PreservedDrawingReference("rId8", "/word/charts/chart43.xml"));
        numerator.LinearText.Should().Be("x^2");
        embedded.Payload[0].Should().Be(1);
        ruby.BaseText.Should().Be("漢字");
        clonedDrawing.References.Should().ContainSingle();
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
