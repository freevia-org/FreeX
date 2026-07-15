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
    public void FloatingOverlay_RendersShapeFromSharedPlanWithActualGeometryFillOutlineAndEffect()
    {
        var shape = new Shape(ShapeKind.Ellipse, 72, 36, "#FF0000")
        {
            OutlineColorHex = "#00AA11",
            OutlineWidthPt = 2,
            Effects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowColorHex = "112233",
                ShadowAlpha = 50000
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
        ellipse.Effect.Should().BeOfType<DropShadowEffect>()
            .Which.Color.Should().Be(Color.FromRgb(0x11, 0x22, 0x33));
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
    public void FloatingOverlay_RendersWarpedWordArtWithContrastingTextAndFill()
    {
        var wordArt = new WordArt("Warped FX", WordArtStyle.GlowBlue, 28)
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
        glyphs.Any(glyph => glyph.RenderTransform is RotateTransform rotation && Math.Abs(rotation.Angle) > 0.1)
            .Should().BeTrue();
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

        var plot = LogicalDescendants<Canvas>(root).First(c => c.Width > 24 && c.Height > 24);
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
        nodeColors.Distinct().Should().HaveCountGreaterThan(1);

        LogicalDescendants<System.Windows.Shapes.Line>(root)
            .Should()
            .HaveCountGreaterThanOrEqualTo(2);
    }
}
