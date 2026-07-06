using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using ModelRun = FreeW.Core.Model.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Rendered Design Effects parity: the selected document effect set must be visible on FreeW-authored
/// drawing objects, not only preserved in the document theme.
/// </summary>
public sealed class DocumentEffectRenderingTests
{
    [StaFact]
    public void OfficeEffectSet_LeavesInlineDrawingObjectsVisuallyNeutral()
    {
        var view = RenderWithEffectSet("Office",
            ModelRun.FromShape(Shape.Preset(ShapeKind.Rectangle, 72, 36, "D9EAF7")),
            ModelRun.FromChart(Chart.Create(ChartKind.Column, ["Q1"], [1.0], title: "Sales")),
            ModelRun.FromWordArt(WordArt.Create("WordArt")),
            ModelRun.FromSmartArt(SmartArt.Create(SmartArtKind.Process, ["One", "Two"])));

        SingleTagged<Border, Shape>(view).BorderThickness.Left.Should().Be(1);
        SingleTagged<Border, Shape>(view).Effect.Should().BeNull();
        SingleTagged<Border, Chart>(view).BorderThickness.Left.Should().Be(1);
        SingleTagged<Border, Chart>(view).Effect.Should().BeNull();
        SingleTagged<Border, SmartArt>(view).BorderThickness.Left.Should().Be(1);
        SingleTagged<Border, SmartArt>(view).Effect.Should().BeNull();
        SingleTagged<TextBlock, WordArt>(view).Effect.Should().BeNull();
    }

    [StaFact]
    public void IntenseEffectSet_RendersDrawingObjectsWithWordStyleWeightAndShadow()
    {
        var view = RenderWithEffectSet("Intense",
            ModelRun.FromShape(Shape.Preset(ShapeKind.Rectangle, 72, 36, "D9EAF7")),
            ModelRun.FromChart(Chart.Create(ChartKind.Column, ["Q1"], [1.0], title: "Sales")),
            ModelRun.FromSmartArt(SmartArt.Create(SmartArtKind.Process, ["One", "Two"])));

        var shape = SingleTagged<Border, Shape>(view);
        shape.BorderThickness.Left.Should().BeGreaterThan(1);
        shape.Effect.Should().BeOfType<DropShadowEffect>();

        var chart = SingleTagged<Border, Chart>(view);
        chart.BorderThickness.Left.Should().BeGreaterThan(1);
        chart.Effect.Should().BeOfType<DropShadowEffect>();

        var smartArt = SingleTagged<Border, SmartArt>(view);
        smartArt.BorderThickness.Left.Should().BeGreaterThan(1);
        smartArt.Effect.Should().BeOfType<DropShadowEffect>();
    }

    [StaFact]
    public void IntenseEffectSet_RendersWordArtWithThemeShadow()
    {
        var view = RenderWithEffectSet("Intense", ModelRun.FromWordArt(WordArt.Create("WordArt")));

        var wordArt = SingleTagged<TextBlock, WordArt>(view);
        wordArt.Effect.Should().BeOfType<DropShadowEffect>();
    }

    [StaFact]
    public void OfficeEffectSet_RendersInlineWordArtPresetGlow()
    {
        var view = RenderWithEffectSet(
            "Office",
            ModelRun.FromWordArt(new WordArt("Glow", WordArtStyle.GlowGold, fontSizePt: 24)));

        var wordArt = SingleTagged<TextBlock, WordArt>(view);
        wordArt.Effect.Should().BeOfType<DropShadowEffect>();
    }

    private static DocumentView RenderWithEffectSet(string effectSetName, params ModelRun[] runs)
    {
        var document = TextDocument.CreateEmpty();
        document.Theme = document.Theme with { EffectSetName = effectSetName };
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var run in runs)
            paragraph.Runs.Add(run);
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static TElement SingleTagged<TElement, TTag>(DocumentView view)
        where TElement : FrameworkElement =>
        LogicalDescendants<TElement>(view.Document).Single(element => element.Tag is TTag);

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is T t)
                    result.Add(t);
                result.AddRange(LogicalDescendants<T>(d));
            }
        return result;
    }
}
