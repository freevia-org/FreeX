using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FreeW.App.Host.Tests;

public sealed class FloatingObjectRenderTests
{
    private static TextDocument DocWithFloatingShape()
    {
        var shape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 2
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithInlineShape()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(new Shape(ShapeKind.Ellipse, 60, 30)));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithImportedFloatingChart()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [1.2, 1.7, 1.4, 2.1],
            seriesName: "Revenue",
            title: "Quarterly revenue");
        chart.WidthPt = 210;
        chart.HeightPt = 126;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        chart.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.TopAndBottom,
            HorizontalAnchor = HorizontalAnchor.Margin,
            HorizontalOffsetPt = 210,
            VerticalAnchor = VerticalAnchor.Paragraph,
            VerticalOffsetPt = 120,
            ZOrderIndex = 4
        };

        var doc = new TextDocument();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static TextDocument DocWithImportedBehindTextbox()
    {
        var shape = Shape.TextBoxWith("Behind text box\nwith shadow", widthPt: 150, heightPt: 60, fillColorHex: "#D9EAD3");
        shape.OutlineColorHex = "#38761D";
        shape.OutlineWidthPt = 1.5;
        shape.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Behind,
            HorizontalAnchor = HorizontalAnchor.Margin,
            HorizontalOffsetPt = 18,
            VerticalAnchor = VerticalAnchor.Paragraph,
            VerticalOffsetPt = 12,
            ZOrderIndex = 1
        };
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowAlpha = 35000 };

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static TextDocument DocWithImportedWatermarkBackingTextbox()
    {
        var shape = Shape.TextBoxWith("watermark backing layer", widthPt: 170, heightPt: 58, fillColorHex: "#E2F0D9");
        shape.OutlineColorHex = "#70AD47";
        shape.OutlineWidthPt = 1.25;
        shape.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
        };

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static TextDocument DocWithMixedFloatingBands(out Shape behindShape, out Shape frontShape)
    {
        behindShape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Behind,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 99
            }
        };
        frontShape = new Shape(ShapeKind.Ellipse, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 72,
                VerticalOffsetPt = 36,
                ZOrderIndex = 1
            }
        };

        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(frontShape));
        para.Runs.Add(Run.FromShape(behindShape));
        doc.Blocks.Add(para);
        return doc;
    }

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            if (dependencyObject is T typed)
                result.Add(typed);
            result.AddRange(LogicalDescendants<T>(dependencyObject));
        }

        return result;
    }

    private static byte[] MinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    [StaFact]
    public void FloatingShape_SurvivesCommitToModel()
    {
        var original = DocWithFloatingShape();
        var view = new DocumentView();
        view.LoadModel(original);
        view.CommitToModel();
        var recovered = view.Model;

        var para = (Paragraph)recovered.Blocks[0];
        var shape = para.Runs[0].Shape;
        shape.Should().NotBeNull();
        shape!.IsFloating.Should().BeTrue();
        shape.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        shape.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.01);
        shape.Placement.ZOrderIndex.Should().Be(2);
    }

    [StaFact]
    public void InlineShape_Unaffected_ByFloatingPath()
    {
        var original = DocWithInlineShape();
        var view = new DocumentView();
        view.LoadModel(original);
        view.CommitToModel();
        var recovered = view.Model;

        var para = (Paragraph)recovered.Blocks[0];
        var shape = para.Runs[0].Shape;
        shape.Should().NotBeNull();
        shape!.IsFloating.Should().BeFalse();
    }

    [StaFact]
    public void FloatingOverlay_UsesPlannerBandDrawOrder()
    {
        var original = DocWithMixedFloatingBands(out var behindShape, out var frontShape);
        var view = new DocumentView();
        var canvas = new System.Windows.Controls.Canvas();

        view.LoadModel(original);
        view.SetFloatingCanvas(canvas);

        canvas.Children
            .OfType<System.Windows.FrameworkElement>()
            .Select(child => child.Tag)
            .Should()
            .Equal(behindShape, frontShape);
    }

    [StaFact]
    public void FloatingOverlay_ParagraphAnchorUsesLeadingContentPosition()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Leading paragraph places the next drawing anchor below page origin."));

        var shape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                VerticalAnchor = VerticalAnchor.Paragraph,
                VerticalOffsetPt = 0,
            }
        };
        var anchor = new Paragraph();
        anchor.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(anchor);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var rendered = canvas.Children.OfType<FrameworkElement>().Single();
        Canvas.GetTop(rendered).Should().BeGreaterThan(0);
    }

    [StaFact]
    public void ImportedFloatingChart_UsesItsMeasuredWordOverlayRegistration()
    {
        var view = new DocumentView();
        var canvas = new Canvas();
        view.SetFloatingCanvas(canvas);
        view.LoadModel(DocWithImportedFloatingChart());

        Canvas.GetTop(canvas.Children.OfType<FrameworkElement>().Single())
            .Should().BeApproximately(241, 0.01);
    }

    [StaFact]
    public void ImportedBehindTextbox_UsesItsMeasuredWordOverlayRegistration()
    {
        var view = new DocumentView();
        var canvas = new Canvas();
        view.SetFloatingCanvas(canvas);
        view.LoadModel(DocWithImportedBehindTextbox());

        var rendered = canvas.Children.OfType<FrameworkElement>().Single();
        Canvas.GetTop(rendered).Should().BeApproximately(96, 0.01);
        rendered.Width.Should().BeApproximately(203, 0.01);
        rendered.Height.Should().BeApproximately(84, 0.01);
    }

    [StaFact]
    public void ImportedWatermarkBackingTextbox_UsesMeasuredOutlineRasterFit()
    {
        var view = new DocumentView();
        var canvas = new Canvas();
        view.SetFloatingCanvas(canvas);
        view.LoadModel(DocWithImportedWatermarkBackingTextbox());

        var border = canvas.Children.OfType<Border>().Single();
        border.BorderThickness.Left.Should().Be(2.5);
        border.BorderThickness.Top.Should().Be(2.5);
        border.BorderThickness.Right.Should().Be(2.5);
        border.BorderThickness.Bottom.Should().Be(2.5);
    }

    [StaFact]
    public void FloatingRichShapeText_UsesSharedRunLayoutAndWpfDecorations()
    {
        var shape = new Shape(ShapeKind.TextBox, 150, 80, "#FFFFFF")
        {
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront }
        };
        var first = new Paragraph();
        first.Runs.Add(new Run("Rich", RunFormatting.Default with
        {
            FontFamily = "Arial",
            FontSizePt = 14,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            ColorHex = "#C00000",
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
            StylisticSet = 4,
        }));
        var second = new Paragraph();
        second.Runs.Add(new Run("next", RunFormatting.Default with { FontFamily = "Courier New" }));
        shape.TextParagraphs.Add(first);
        shape.TextParagraphs.Add(second);

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        var textCanvas = LogicalDescendants<Canvas>(root).Single();
        var glyphs = textCanvas.Children.OfType<TextBlock>().ToArray();
        glyphs.Select(glyph => glyph.Text).Should().ContainInOrder("R", "i", "c", "h", "n", "e", "x", "t");
        var richGlyph = glyphs[0];
        richGlyph.FontFamily.Source.Should().Be("Arial");
        richGlyph.FontSize.Should().BeApproximately(14 * 96.0 / 72.0, 0.01);
        richGlyph.FontWeight.Should().Be(FontWeights.Bold);
        richGlyph.FontStyle.Should().Be(FontStyles.Italic);
        richGlyph.Foreground.Should().BeOfType<SolidColorBrush>().Which.Color.Should()
            .Be(Color.FromRgb(0xC0, 0x00, 0x00));
        richGlyph.TextDecorations.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Underline);
        richGlyph.TextDecorations.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
        System.Windows.Documents.Typography.GetNumeralStyle(richGlyph).Should().Be(FontNumeralStyle.OldStyle);
        System.Windows.Documents.Typography.GetNumeralAlignment(richGlyph).Should().Be(FontNumeralAlignment.Tabular);
        System.Windows.Documents.Typography.GetStylisticSet4(richGlyph).Should().BeTrue();
        Canvas.GetTop(glyphs[4]).Should().BeGreaterThan(Canvas.GetTop(glyphs[0]),
            "the second paragraph must use a shared hard-break line");
    }

    [StaTheory]
    [InlineData(ShapeTextDirection.Rotate90)]
    [InlineData(ShapeTextDirection.Rotate270)]
    public void FloatingRotatedShapeText_ArrangesSwappedCanvasCenteredAndClipped(ShapeTextDirection direction)
    {
        var shape = Shape.TextBoxWith("Rotate", widthPt: 150, heightPt: 80);
        shape.TextDirection = direction;
        shape.Effects = new ShapeEffectLst
        {
            HasShadow = true,
            ShadowColorHex = "000000",
            ShadowAlpha = 35000,
            ShadowBlurRad = 50800,
            ShadowDist = 38100,
            ShadowDir = 2700000
        };
        shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront };

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        var textViewport = LogicalDescendants<Border>(root).Single(border => border.Child is Canvas);
        var textCanvas = LogicalDescendants<Canvas>(root).Single();
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();

        var effect = root.Effect.Should().BeOfType<DropShadowEffect>().Subject;
        root.Clip.Should().BeNull("the effect-bearing shape border must leave room for its outer shadow");
        canvas.ClipToBounds.Should().BeFalse();
        effect.BlurRadius.Should().BeGreaterThan(0);
        effect.ShadowDepth.Should().BeGreaterThan(0);
        textViewport.ActualWidth.Should().BeApproximately(root.ActualWidth, 0.01);
        textViewport.ActualHeight.Should().BeApproximately(root.ActualHeight, 0.01);
        textCanvas.ActualWidth.Should().BeApproximately(root.ActualHeight, 0.01);
        textCanvas.ActualHeight.Should().BeApproximately(root.ActualWidth, 0.01);
        var transformedBounds = textCanvas.TransformToAncestor(textViewport)
            .TransformBounds(new Rect(0, 0, textCanvas.ActualWidth, textCanvas.ActualHeight));
        transformedBounds.Left.Should().BeGreaterThanOrEqualTo(-0.01);
        transformedBounds.Top.Should().BeGreaterThanOrEqualTo(-0.01);
        transformedBounds.Right.Should().BeLessThanOrEqualTo(textViewport.ActualWidth + 0.01);
        transformedBounds.Bottom.Should().BeLessThanOrEqualTo(textViewport.ActualHeight + 0.01);
        textViewport.Clip.Should().BeOfType<RectangleGeometry>()
            .Which.Bounds.Size.Should().Be(new Size(root.ActualWidth, root.ActualHeight));
        textCanvas.Clip.Should().BeOfType<RectangleGeometry>()
            .Which.Bounds.Size.Should().Be(new Size(textCanvas.ActualWidth, textCanvas.ActualHeight));
    }

    [StaFact]
    public void FloatingOverlay_RendersShapeFromSharedPlanWithActualGeometryFillOutlineAndEffect()
    {
        var shape = new Shape(ShapeKind.Ellipse, 72, 36, "#FF0000")
        {
            OutlineColorHex = "#00AA11",
            OutlineWidthPt = 2,
            Effects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowColorHex = "000000",
                ShadowAlpha = 35000,
                ShadowBlurRad = 50800,
                ShadowDist = 38100,
                ShadowDir = 2700000
            },
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 3
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new System.Windows.Controls.Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var ellipse = canvas.Children.OfType<System.Windows.Shapes.Ellipse>().Single();
        ellipse.Tag.Should().BeSameAs(shape);
        ellipse.Fill.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0xFF, 0x00, 0x00));
        ellipse.Stroke.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0x00, 0xAA, 0x11));
        ellipse.StrokeThickness.Should().BeApproximately(2 * 96.0 / 72.0, 0.01);
        var effect = ellipse.Effect.Should().BeOfType<DropShadowEffect>().Subject;
        effect.Color.Should().Be(Colors.Black);
        effect.Direction.Should().Be(315);
    }

    [StaFact]
    public void FloatingOverlay_RendersGroupedChildShapeGlowFromSharedPlan()
    {
        var group = new FreeW.Core.Model.DrawingGroup
        {
            WidthPt = 140,
            HeightPt = 70,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 6
            }
        };
        group.Children.Add(new Shape(ShapeKind.Ellipse, 70, 42, "#CFE2F3")
        {
            OutlineColorHex = "#1155CC",
            Effects = new ShapeEffectLst
            {
                HasGlow = true,
                GlowColorHex = "4472C4",
                GlowRad = 63500
            }
        });
        group.ChildOffsets.Add((0, 10));

        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var groupRoot = canvas.Children.OfType<Border>().Single(border => ReferenceEquals(border.Tag, group));
        var child = LogicalDescendants<System.Windows.Shapes.Ellipse>(groupRoot).Single();
        var effect = child.Effect.Should().BeOfType<DropShadowEffect>().Subject;
        effect.Color.Should().Be(Color.FromRgb(0x44, 0x72, 0xC4));
        effect.ShadowDepth.Should().Be(0);
        effect.BlurRadius.Should().BeApproximately(63500 / 12700.0 * 96.0 / 72.0, 0.01);
    }

    [StaFact]
    public void FloatingOverlay_RendersWordArtEffectFromSharedPlan()
    {
        var wordArt = new WordArt("Floating FX", WordArtStyle.ShadowOrange, 28)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 4
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);
        var sharedPlan = DrawingObjectVisualPlanner.BuildVisualPlan(
            wordArt,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.WordArt,
                BlockIndex: 0,
                RunIndex: 0,
                new DocumentFloatRect(48, 24, 180, 64),
                BehindText: false,
                ZOrderIndex: 4,
                ImageWrapping.InFront));

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        root.Tag.Should().BeSameAs(wordArt);
        var textBlock = LogicalDescendants<TextBlock>(root).Single();
        textBlock.Foreground.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Colors.Black);
        root.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0xED, 0x7D, 0x31));
        var effect = textBlock.Effect.Should().BeOfType<DropShadowEffect>().Subject;
        effect.Color.Should().Be(Color.FromRgb(0xED, 0x7D, 0x31));
        effect.BlurRadius.Should().BeApproximately(sharedPlan.Effects.ShadowBlurDip, 0.01);
        effect.ShadowDepth.Should().BeApproximately(sharedPlan.Effects.ShadowDistanceDip, 0.01);
        sharedPlan.Effects.Summary.Should().Be("shadow");
    }

    [StaFact]
    public void FloatingOverlay_AppliesWordArtCentreRotationAndFlips()
    {
        var wordArt = new WordArt("Transform", WordArtStyle.GlowBlue, 28)
        {
            RotationAngle = 30,
            FlipH = true,
            FlipV = true,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
            },
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);
        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        var transforms = root.RenderTransform.Should().BeOfType<TransformGroup>().Subject;

        transforms.Children.OfType<ScaleTransform>().Single().ScaleX.Should().Be(-1);
        transforms.Children.OfType<ScaleTransform>().Single().ScaleY.Should().Be(-1);
        transforms.Children.OfType<RotateTransform>().Single().Angle.Should().Be(30);
    }

    [StaFact]
    public void FloatingOverlay_RendersWarpedWordArtWithContrastingTextAndFill()
    {
        var wordArt = new WordArt("FreeW CONFIDENTIAL", WordArtStyle.GlowBlue, 28)
        {
            Warp = WordArtWarp.Wave1,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 4
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Canvas>().Single();
        root.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0x24, 0x24, 0x24));

        root.Measure(new Size(180, 64));
        root.Arrange(new Rect(0, 0, 180, 64));
        root.UpdateLayout();

        var glyphs = root.Children.OfType<TextBlock>().ToList();
        glyphs.Should().HaveCount(wordArt.Text.Length);
        glyphs.All(glyph => glyph.Foreground is SolidColorBrush brush && brush.Color == Colors.White)
            .Should().BeTrue();
        glyphs.All(glyph => glyph.RenderTransform is TransformGroup).Should().BeTrue();
        var transforms = glyphs.Select(glyph => (TransformGroup)glyph.RenderTransform).ToList();
        transforms.All(transform => transform.Children.OfType<ScaleTransform>().Single().ScaleX > 1).Should().BeTrue();
        transforms.Any(transform => transform.Children.OfType<RotateTransform>()
                .Any(rotation => Math.Abs(rotation.Angle) > 0.1))
            .Should().BeTrue();
    }

    [StaFact]
    public void FloatingOverlay_AppliesAuthoredNormalAutoFitFontScale()
    {
        var wordArt = new WordArt("Auto fit", WordArtStyle.GlowBlue, 30)
        {
            Warp = WordArtWarp.Wave1,
            TextFitMode = WordArtTextFitMode.NormalAutoFit,
            NormalAutoFitFontScale = 85000,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 4
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Canvas>().Single();
        root.Measure(new Size(180, 64));
        root.Arrange(new Rect(0, 0, 180, 64));
        root.UpdateLayout();

        var glyphs = root.Children.OfType<TextBlock>().ToList();
        glyphs.Should().HaveCount(wordArt.Text.Length);
        glyphs.Should().OnlyContain(glyph => Math.Abs(glyph.FontSize - 34) < 0.01);
    }

    [StaFact]
    public void FloatingOverlay_UsesOuterOnlyGlowLayerForImportedWave1Signature()
    {
        var wordArt = new WordArt("FreeW CONFIDENTIAL", WordArtStyle.GlowBlue, 32)
        {
            Warp = WordArtWarp.Wave1,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 4
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Canvas>().Single();
        root.Measure(new Size(476, 68));
        root.Arrange(new Rect(0, 0, 476, 68));
        root.UpdateLayout();

        root.Effect.Should().BeNull();
        root.Children.OfType<Border>().Should().HaveCount(3);
        var glowRing = root.Children.OfType<Border>()
            .Single(border => border.Effect is null && border.Opacity == 0.6);
        var glowBrush = glowRing.Background.Should().BeOfType<LinearGradientBrush>().Subject;
        glowBrush.StartPoint.Should().Be(new Point(0.5, 0));
        glowBrush.EndPoint.Should().Be(new Point(0.5, 1));
        glowBrush.GradientStops.Select(stop => (stop.Color, stop.Offset)).Should().Equal(
            (Color.FromArgb(158, 0x2E, 0x75, 0xB6), 0),
            (Color.FromRgb(0x2E, 0x75, 0xB6), 0.05),
            (Color.FromRgb(0x2E, 0x75, 0xB6), 1));
        glowRing.Width.Should().BeApproximately(488.16, 0.01);
        glowRing.Height.Should().BeApproximately(80.2667, 0.01);
        var fillLayer = root.Children.OfType<Border>()
            .Single(border => border.Effect is null
                && border.Opacity == 1
                && border.Background is SolidColorBrush { Color: var color }
                && color == Color.FromRgb(0x24, 0x24, 0x24));
        fillLayer.Width.Should().BeApproximately(489.16, 0.01);
        fillLayer.Height.Should().BeApproximately(78.2667, 0.01);
        Canvas.GetLeft(fillLayer).Should().Be(-7);
        Canvas.GetTop(fillLayer).Should().Be(-2);
        root.Children.OfType<Border>().Single(border => border.Effect is not null)
            .Effect.Should().BeOfType<DropShadowEffect>();
        var glyphs = root.Children.OfType<TextBlock>().ToList();
        glyphs.Should().HaveCount(wordArt.Text.Length);
        glyphs.Select(glyph => ((TransformGroup)glyph.RenderTransform).Children
                .OfType<ScaleTransform>().Single().ScaleX)
            .Should().OnlyContain(scale => Math.Abs(scale - 1.2349) < 0.001);
        glyphs.Select(glyph => ((TransformGroup)glyph.RenderTransform).Children
                .OfType<ScaleTransform>().Single().ScaleY)
            .Should().OnlyContain(scale => Math.Abs(scale - 1.78) < 0.01);
        var rotations = glyphs.Select(glyph => ((TransformGroup)glyph.RenderTransform).Children
                .OfType<RotateTransform>().Single().Angle)
            .ToList();
        rotations.Should().Contain(angle => angle < -1,
            "Wave1 slopes glyphs downward at both ends");
        rotations.Should().Contain(angle => angle > 1,
            "Wave1 rises through the center");
        rotations.Select(angle => Math.Abs(angle)).Max().Should().BeGreaterThan(1.5,
            "the imported Wave1 signature carries visible per-glyph rotation");
    }

    [StaFact]
    public void FloatingOverlay_ExtendsMaterialLayerForImportedReviewCopySignature()
    {
        var wordArt = new WordArt("Review Copy", WordArtStyle.FillGold, 26)
        {
            Warp = WordArtWarp.ArchUp,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 260,
                VerticalOffsetPt = 142,
                ZOrderIndex = 9
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Canvas>().Single();
        root.Measure(new Size(236, 56));
        root.Arrange(new Rect(0, 0, 236, 56));
        root.UpdateLayout();

        var materialLayer = root.Children.OfType<Border>().Single();
        materialLayer.Width.Should().BeApproximately(root.ActualWidth + 1, 0.01);
        materialLayer.Height.Should().BeApproximately(root.ActualHeight + 13, 0.01);
        Canvas.GetLeft(materialLayer).Should().Be(-1);
        Canvas.GetTop(materialLayer).Should().Be(-6);
        root.Children.OfType<TextBlock>().Should().HaveCount(wordArt.Text.Length);
    }

    [StaFact]
    public void FloatingOverlay_UsesOuterOnlyGlowLayerForImportedFreeW30PointWave1Signature()
    {
        var wordArt = new WordArt("FreeW", WordArtStyle.GlowBlue, 30)
        {
            Warp = WordArtWarp.Wave1,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 300,
                VerticalOffsetPt = 30,
                ZOrderIndex = 8
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Canvas>().Single();
        root.Measure(new Size(124, 64));
        root.Arrange(new Rect(0, 0, 124, 64));
        root.UpdateLayout();

        Canvas.GetTop(root).Should().BeApproximately(121, 0.01);
        root.Effect.Should().BeNull();
        root.Children.OfType<Border>().Should().HaveCount(3);
        root.Children.OfType<Border>().Single(border => border.Effect is null && border.Opacity == 0.6)
            .Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0x2E, 0x75, 0xB6));
        root.Children.OfType<TextBlock>().Should().HaveCount(wordArt.Text.Length);
    }

    [StaFact]
    public void InlineOverlay_RendersArchUpWordArtThroughWarpedVisualAdapter()
    {
        var wordArt = new WordArt("Inline warp", WordArtStyle.GlowBlue, 24)
        {
            Warp = WordArtWarp.ArchUp
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var container = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .Single()
            .Inlines.OfType<System.Windows.Documents.InlineUIContainer>()
            .Single();
        var canvas = container.Child.Should().BeOfType<Canvas>().Subject;
        canvas.Tag.Should().BeSameAs(wordArt);
        container.BaselineAlignment.Should().Be(BaselineAlignment.Center);
        canvas.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0x24, 0x24, 0x24));
        canvas.Effect.Should().BeOfType<DropShadowEffect>()
            .Which.Color.Should().Be(Color.FromRgb(0x2E, 0x75, 0xB6));
        canvas.Width.Should().BeGreaterThan(0);
        canvas.Height.Should().BeGreaterThan(0);

        canvas.Measure(new Size(400, 100));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();

        var glyphs = canvas.Children.OfType<TextBlock>().ToList();
        glyphs.Should().HaveCount(wordArt.Text.Length);
        glyphs.Any(glyph => glyph.RenderTransform is RotateTransform rotation && Math.Abs(rotation.Angle) > 0.1)
            .Should().BeTrue();
    }

    [StaFact]
    public void FloatingOverlay_RendersGroupedMixedChildrenWithoutPlaceholderLabels()
    {
        var image = new InlineImage(MinimalPng(), widthPt: 32, heightPt: 24)
        {
            AltText = "Grouped image"
        };
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2"],
            [4.0, 7.0],
            seriesName: "Revenue",
            title: "Grouped sales");
        chart.WidthPt = 104;
        chart.HeightPt = 72;
        chart.StyleId = 2;
        chart.ColorSchemeId = "colorful2";
        chart.QuickLayoutId = 5;
        chart.ShowLegend = true;
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build"]);
        smartArt.WidthPt = 128;
        smartArt.HeightPt = 46;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "moderate1";

        var group = new FreeW.Core.Model.DrawingGroup
        {
            WidthPt = 190,
            HeightPt = 116,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 6
            }
        };
        group.Children.Add(image);
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(chart);
        group.ChildOffsets.Add((44, 0));
        group.Children.Add(smartArt);
        group.ChildOffsets.Add((0, 66));

        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var groupRoot = canvas.Children.OfType<Border>().Single(border => ReferenceEquals(border.Tag, group));
        groupRoot.BorderBrush.Should().BeNull("unselected groups have no authored outline");
        groupRoot.BorderThickness.Should().Be(new Thickness(0), "the group frame is selection-only chrome");
        LogicalDescendants<System.Windows.Controls.Image>(groupRoot)
            .Should()
            .ContainSingle(imageElement => ReferenceEquals(imageElement.Tag, image));

        var chartRoot = LogicalDescendants<Border>(groupRoot).Single(border => ReferenceEquals(border.Tag, chart));
        chartRoot.Child.Should().NotBeOfType<TextBlock>(
            "grouped charts should render through the typed chart visual instead of the old child placeholder");

        var smartArtRoot = LogicalDescendants<Border>(groupRoot).Single(border => ReferenceEquals(border.Tag, smartArt));
        smartArtRoot.Child.Should().NotBeOfType<TextBlock>(
            "grouped SmartArt should render through the typed diagram visual instead of the old child placeholder");

        var texts = LogicalDescendants<TextBlock>(groupRoot)
            .Select(textBlock => textBlock.Text)
            .ToList();
        texts.Should().Contain(["Grouped sales", "Q1", "Plan", "Build"]);
        texts.Should().NotContain(["Image", "Column Chart", "SmartArt"]);

        view.SelectFloatingObject(group);
        var selectedGroupRoot = canvas.Children.OfType<Border>().Single(border => ReferenceEquals(border.Tag, group));
        selectedGroupRoot.BorderBrush.Should().BeSameAs(System.Windows.Media.Brushes.DodgerBlue);
        selectedGroupRoot.BorderThickness.Should().Be(new Thickness(2));
    }

    [StaFact]
    public void FloatingOverlay_RendersPayloadFreeGroupedChildrenAsSerializedPlaceholders()
    {
        var image = new InlineImage([], widthPt: 32, heightPt: 24);
        var chart = new Chart { WidthPt = 104, HeightPt = 72 };
        var smartArt = new SmartArt { WidthPt = 128, HeightPt = 46 };
        var group = new FreeW.Core.Model.DrawingGroup { WidthPt = 190, HeightPt = 116 };
        group.Children.Add(image);
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(chart);
        group.ChildOffsets.Add((44, 0));
        group.Children.Add(smartArt);
        group.ChildOffsets.Add((0, 66));

        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var groupRoot = canvas.Children.OfType<Border>().Single(border => ReferenceEquals(border.Tag, group));
        foreach (var child in new object[] { image, chart, smartArt })
        {
            var placeholder = LogicalDescendants<Border>(groupRoot)
                .Single(border => ReferenceEquals(border.Tag, child));
            placeholder.Background.Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Color.FromRgb(0xC0, 0xC0, 0xC0));
            placeholder.BorderBrush.Should().BeNull();
            placeholder.Child.Should().BeNull();
        }
    }

    [StaFact]
    public void FloatingOverlay_RendersNestedGroupChildrenThroughSharedPlan()
    {
        var inner = new FreeW.Core.Model.DrawingGroup { WidthPt = 72, HeightPt = 42, RotationAngle = 15 };
        var ellipse = new Shape(ShapeKind.Ellipse, 30, 24, "#70AD47");
        inner.Children.Add(ellipse);
        inner.ChildOffsets.Add((6, 6));
        inner.Children.Add(new WordArt("Inner", WordArtStyle.GlowGold, 16));
        inner.ChildOffsets.Add((36, 9));

        var outer = new FreeW.Core.Model.DrawingGroup { WidthPt = 180, HeightPt = 96 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 18));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 36, "#4472C4"));
        outer.ChildOffsets.Add((108, 24));

        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var outerRoot = canvas.Children.OfType<Border>().Single(border => ReferenceEquals(border.Tag, outer));
        LogicalDescendants<Border>(outerRoot)
            .Should().Contain(border => ReferenceEquals(border.Tag, inner));
        LogicalDescendants<System.Windows.Shapes.Ellipse>(outerRoot)
            .Should().ContainSingle("the nested group's ellipse should render instead of a placeholder");
    }

    [StaFact]
    public void FloatingOverlay_RendersChartFromSharedPlanWithActualGeometryTextAndStyle()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3"],
            [4.0, 8.0, 6.0],
            seriesName: "Revenue",
            title: "Sales");
        chart.WidthPt = 216;
        chart.HeightPt = 144;
        chart.StyleId = 2;
        chart.ColorSchemeId = "colorful2";
        chart.QuickLayoutId = 5;
        chart.ShowLegend = true;
        chart.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.InFront,
            HorizontalOffsetPt = 24,
            VerticalOffsetPt = 18,
            ZOrderIndex = 4
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        root.Tag.Should().BeSameAs(chart);
        root.Child.Should().NotBeOfType<TextBlock>("floating charts should use the planned chart visual, not a label placeholder");

        var texts = LogicalDescendants<TextBlock>(root)
            .Select(textBlock => textBlock.Text)
            .ToList();
        texts.Should().Contain("Sales");
        texts.Should().Contain("Q1");

        var plot = LogicalDescendants<Border>(root)
            .First(border => border != root && border.Width > 24 && border.Height > 24);
        plot.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromRgb(0xD9, 0xE2, 0xF3));

        var barFills = LogicalDescendants<System.Windows.Shapes.Rectangle>(root)
            .Where(rectangle => rectangle.Width > 12 || rectangle.Height > 12)
            .Select(rectangle => (rectangle.Fill as SolidColorBrush)?.Color)
            .Where(color => color is not null)
            .ToList();
        barFills.Should().Contain(Color.FromRgb(0xED, 0x7D, 0x31));
    }

    [StaFact]
    public void FloatingOverlay_RendersSmartArtFromSharedPlanWithNodeTextColorsAndArrows()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.WidthPt = 300;
        smartArt.HeightPt = 96;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "moderate1";
        smartArt.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.InFront,
            HorizontalOffsetPt = 30,
            VerticalOffsetPt = 20,
            ZOrderIndex = 5
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromSmartArt(smartArt));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        var canvas = new Canvas();
        view.LoadModel(doc);
        view.SetFloatingCanvas(canvas);

        var root = canvas.Children.OfType<Border>().Single();
        root.Tag.Should().BeSameAs(smartArt);
        root.Child.Should().NotBeOfType<TextBlock>("floating SmartArt should use the planned diagram visual, not a label placeholder");

        var texts = LogicalDescendants<TextBlock>(root)
            .Select(textBlock => textBlock.Text)
            .ToList();
        texts.Should().Contain(["Plan", "Build", "Verify"]);

        var nodeColors = LogicalDescendants<Border>(root)
            .Where(border => border.Background is SolidColorBrush)
            .Select(border => ((SolidColorBrush)border.Background!).Color)
            .Where(color => color.A > 0 && (color.R != 0xFF || color.G != 0xFF || color.B != 0xFF))
            .ToList();
        nodeColors.Should().Contain(Color.FromRgb(0x1F, 0x38, 0x64));
        nodeColors.Distinct().Should().ContainSingle();

        LogicalDescendants<System.Windows.Shapes.Line>(root)
            .Should()
            .HaveCountGreaterThanOrEqualTo(2);
    }
}
