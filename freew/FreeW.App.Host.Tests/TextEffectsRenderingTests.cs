using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word text effects on body text in the WPF editor: the w14 fill paints the glyphs (and so reaches
/// print), outline/shadow/glow/reflection get the overlay only when a run carries them, and committing
/// the view never turns the fill into an explicit font colour or loses the stored effects.
/// </summary>
public sealed class TextEffectsRenderingTests
{
    private static (DocumentView View, AdornerDecorator Host) Hosted(TextDocument document)
    {
        var view = new DocumentView();
        var host = new AdornerDecorator { Child = view };
        host.Measure(new Size(816, 1056));
        host.Arrange(new Rect(0, 0, 816, 1056));
        view.LoadModel(document);
        host.UpdateLayout();
        return (view, host);
    }

    private static TextDocument DocumentWith(RunFormatting formatting)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new FreeW.Core.Model.Paragraph();
        paragraph.Runs.Add(new FreeW.Core.Model.Run("Effect text", formatting));
        paragraph.Runs.Add(new FreeW.Core.Model.Run(" plain"));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static object? Adorner(DocumentView view) =>
        typeof(DocumentView).GetField("_textEffectsAdorner", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);

    private static IEnumerable<WpfRun> Runs(DocumentView view) =>
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines.OfType<WpfRun>());

    [StaFact]
    public void SolidFill_PaintsTheGlyphs_WithoutAnOverlay_AndCommitKeepsTheModel()
    {
        var formatting = new RunFormatting
        {
            ColorHex = "112233",
            TextEffects = ((RunTextEffects?)null).WithFill(TextFillEffect.Solid(TextEffectColor.FromRgb("C00000"))),
        };
        var (view, _) = Hosted(DocumentWith(formatting));

        var run = Runs(view).First(r => r.Text == "Effect text");
        run.Foreground.Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Color.FromRgb(0xC0, 0x00, 0x00));
        Adorner(view).Should().BeNull("a fill alone needs no overlay");

        view.CommitToModel();

        var committed = view.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().Single().Runs[0].Formatting;
        committed.ColorHex.Should().Be("112233");
        committed.TextEffects.Should().Be(formatting.TextEffects);
    }

    [StaFact]
    public void GradientFill_BecomesAGradientForegroundThatSurvivesThePrintClone()
    {
        var formatting = new RunFormatting
        {
            TextEffects = ((RunTextEffects?)null).WithFill(TextFillEffect.LinearGradient(
                90,
                new TextGradientStop(0, TextEffectColor.FromRgb("FF0000")),
                new TextGradientStop(100000, TextEffectColor.FromRgb("0000FF")))),
        };
        var (view, _) = Hosted(DocumentWith(formatting));

        Runs(view).First(r => r.Text == "Effect text").Foreground.Should().BeOfType<LinearGradientBrush>();

        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();
        paginator.PageCount.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void OutlineShadowGlowReflection_GetTheOverlay_OnlyWhileARunCarriesThem()
    {
        var effects = ((RunTextEffects?)null)
            .WithOutline(new TextOutlineEffect
            {
                WidthEmu = TextEffectUnits.PointsToEmu(1),
                Fill = TextFillEffect.Solid(TextEffectColor.FromScheme("accent2")),
            })
            .WithShadow(new TextShadowEffect
            {
                BlurRadiusEmu = TextEffectUnits.PointsToEmu(3),
                DistanceEmu = TextEffectUnits.PointsToEmu(2),
                DirectionAngle = TextEffectUnits.DegreesToAngle(45),
                Color = TextEffectColor.FromRgb("000000", 50),
            })
            .WithGlow(new TextGlowEffect(TextEffectUnits.PointsToEmu(5), TextEffectColor.FromScheme("accent1", 60)))
            .WithReflection(new TextReflectionEffect { StartOpacity = 50000, EndOpacity = 300, EndPosition = 55000 });
        var (view, _) = Hosted(DocumentWith(new RunFormatting { FontSizePt = 24, TextEffects = effects }));

        var adorner = Adorner(view);
        adorner.Should().NotBeNull();
        // The overlay normally rebuilds at Loaded priority after layout; run that pass directly.
        adorner!.GetType().GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(adorner, null);
        var layerCount = (int)adorner!.GetType().GetProperty("LayerCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(adorner)!;
        // shadow + glow + one reflection line + the top layer (glyphs and outline).
        layerCount.Should().Be(4);

        view.CommitToModel();
        view.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().Single().Runs[0].Formatting.TextEffects.Should().Be(effects);

        view.LoadModel(DocumentWith(new RunFormatting()));
        Adorner(view).Should().BeNull("a document without effects drops the overlay");
    }

    private static RunTextEffects ShadowGlowOutline() => ((RunTextEffects?)null)
        .WithOutline(new TextOutlineEffect
        {
            WidthEmu = TextEffectUnits.PointsToEmu(1),
            Fill = TextFillEffect.Solid(TextEffectColor.FromRgb("C00000")),
        })
        .WithShadow(new TextShadowEffect
        {
            BlurRadiusEmu = TextEffectUnits.PointsToEmu(3),
            DistanceEmu = TextEffectUnits.PointsToEmu(2),
            DirectionAngle = TextEffectUnits.DegreesToAngle(45),
            Color = TextEffectColor.FromRgb("000000", 50),
        })
        .WithGlow(new TextGlowEffect(TextEffectUnits.PointsToEmu(5), TextEffectColor.FromRgb("FFC000", 60)))!;

    private static IEnumerable<Visual> VisualTree(Visual root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is Visual child)
                foreach (var nested in VisualTree(child))
                    yield return nested;
        }
    }

    [StaFact]
    public void PrintedPages_DrawOutlineShadowAndGlow_WithTheSharedPainter()
    {
        var (view, _) = Hosted(DocumentWith(new RunFormatting { FontSizePt = 24, TextEffects = ShadowGlowOutline() }));

        var paginator = PrintLayout.BuildPaginator(view);
        paginator.Should().BeOfType<TextEffectsPrintPaginator>();
        paginator.ComputePageCount();
        var page = paginator.GetPage(0);

        var blurred = VisualTree(page.Visual).OfType<DrawingVisual>()
            .Where(visual => visual.Effect is System.Windows.Media.Effects.BlurEffect)
            .ToList();
        blurred.Should().HaveCount(2, "the shadow and the glow are each their own blurred layer on the printed page");
        var effectBounds = blurred.Select(visual => visual.ContentBounds).Aggregate(Rect.Union);
        effectBounds.IsEmpty.Should().BeFalse();
        effectBounds.Top.Should().BeLessThan(page.Size.Height / 2, "the effects sit on the first line, not at the page origin");
        effectBounds.Left.Should().BeGreaterThan(10, "the effects follow the glyphs inside the page margin");

        // A repeat request (PDF export asks twice per page) must not throw on the reused page visual.
        paginator.GetPage(0).Visual.Should().NotBeNull();
        PdfExport.RenderToBytes(paginator, "Effects").Should().NotBeEmpty();
    }

    [StaFact]
    public void MultiSectionPrintedPages_DrawOutlineShadowAndGlow_ThroughTheSectionAwarePaginator()
    {
        var document = DocumentWith(new RunFormatting { FontSizePt = 24, TextEffects = ShadowGlowOutline() });
        var first = document.Blocks.OfType<FreeW.Core.Model.Paragraph>().Single();
        first.SectionBreak = new FreeW.Core.Model.Section(
            new PageSettings { WidthPt = 792, HeightPt = 612, Landscape = true },
            SectionBreakKind.NextPage);
        var second = new FreeW.Core.Model.Paragraph();
        second.Runs.Add(new FreeW.Core.Model.Run("Second section effect", new RunFormatting { FontSizePt = 24, TextEffects = ShadowGlowOutline() }));
        document.Blocks.Add(second);
        var (view, _) = Hosted(document);

        var paginator = PrintLayout.BuildPaginator(view);
        paginator.Should().BeOfType<SectionAwareDocumentPaginator>();
        paginator.ComputePageCount();
        paginator.PageCount.Should().Be(2);

        for (var index = 0; index < 2; index++)
        {
            var blurred = VisualTree(paginator.GetPage(index).Visual).OfType<DrawingVisual>()
                .Count(visual => visual.Effect is System.Windows.Media.Effects.BlurEffect);
            blurred.Should().Be(2, $"page {index + 1}'s effect text gets its shadow and glow layers in print");
        }
    }

    [StaFact]
    public void PrintWithoutEffectText_IsNotWrapped()
    {
        var (view, _) = Hosted(DocumentWith(new RunFormatting { FontSizePt = 12 }));

        PrintLayout.BuildPaginator(view).Should().NotBeOfType<TextEffectsPrintPaginator>();
    }

    [StaFact]
    public void Overlay_RepaintsAfterAPageGeometryChange_AndThenGoesIdle()
    {
        var (view, host) = Hosted(DocumentWith(new RunFormatting { FontSizePt = 24, TextEffects = ShadowGlowOutline() }));
        var adorner = (DocumentView.TextEffectsAdorner)Adorner(view)!;
        Pump();
        var before = adorner.RefreshCount;
        var beforeBounds = TopLayerBounds(adorner);

        // Page setup / view changes re-lay the text out without any scroll, size or text change.
        view.Document.PagePadding = new Thickness(view.Document.PagePadding.Left + 120, 48, 48, 48);
        host.UpdateLayout();
        Pump();

        adorner.RefreshCount.Should().BeGreaterThan(before, "a re-layout that moves the effect text repaints the overlay");
        TopLayerBounds(adorner).Left.Should().BeGreaterThan(beforeBounds.Left + 60, "the effects follow the moved text");

        var settled = adorner.RefreshCount;
        for (var pass = 0; pass < 5; pass++)
        {
            host.InvalidateMeasure();
            host.UpdateLayout();
            Pump();
        }

        adorner.RefreshCount.Should().Be(settled, "layout passes that move nothing must not repaint (no refresh loop)");

        view.ZoomLevel = 1.5;
        host.UpdateLayout();
        Pump();
        adorner.RefreshCount.Should().BeGreaterThan(settled, "zoom repaints the overlay");
    }

    private static Rect TopLayerBounds(DocumentView.TextEffectsAdorner adorner) =>
        ((DrawingVisual)VisualTreeHelper.GetChild(adorner, VisualTreeHelper.GetChildrenCount(adorner) - 1)).ContentBounds;

    // Runs the dispatcher for a fixed slice: a refresh loop would show up as a runaway RefreshCount
    // instead of hanging the test.
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            System.Windows.Threading.DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    [StaFact]
    public void ThreeDOnlyEffects_AreNotDrawn_ButRoundTripThroughTheView()
    {
        var effects = new RunTextEffects
        {
            Scene3D = "<w14:scene3d xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"/>",
        };
        var (view, _) = Hosted(DocumentWith(new RunFormatting { TextEffects = effects }));

        Adorner(view).Should().BeNull();
        view.CommitToModel();
        view.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().Single().Runs[0].Formatting.TextEffects.Should().Be(effects);
    }
}
