using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Insert &gt; Shapes gallery (WPF): every catalog entry draws a real thumbnail, and a shape inserted
/// from the gallery survives the editor's commit round-trip as the same DrawingML preset.
/// </summary>
public sealed class ShapeGalleryDialogTests
{
    [StaFact]
    public void EveryCatalogEntry_DrawsANonEmptyThumbnail()
    {
        foreach (var item in PresetShapeCatalog.Items)
        {
            var thumbnail = ShapeGalleryDialog.BuildThumbnail(item)
                .Should().BeOfType<System.Windows.Shapes.Path>().Subject;

            thumbnail.Data.Should().NotBeNull($"'{item.Id}' needs a drawn thumbnail");
            var bounds = thumbnail.Data.GetRenderBounds(new Pen(Brushes.Black, 1));
            bounds.IsEmpty.Should().BeFalse($"'{item.Id}' thumbnail must not be empty");
            (bounds.Width + bounds.Height).Should().BeGreaterThan(4, $"'{item.Id}' thumbnail must be visible");
        }
    }

    [StaTheory]
    [InlineData("Star5", "star5")]
    [InlineData("RightArrow", "rightArrow")]
    [InlineData("FlowDecision", "flowChartDecision")]
    [InlineData("SpeechOval", "wedgeEllipseCallout")]
    [InlineData("ElbowArrow", "bentConnector3")]
    [InlineData("MathDivide", "mathDivide")]
    public void GalleryShape_InsertedIntoTheEditor_KeepsItsPresetThroughCommit(string itemId, string preset)
    {
        PresetShapeCatalog.TryGetItem(itemId, out var item).Should().BeTrue();
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());

        editor.InsertShape(PresetShapeCatalog.CreateShape(item!));

        var shape = editor.Model.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Shape is not null).Shape!;
        shape.PresetName.Should().Be(preset);
        shape.HasIntactPresetGeometry.Should().BeTrue();
    }

    [StaFact]
    public void PresetShapeWithText_RendersItsTextOverTheOutline()
    {
        var shape = PresetShapeFactory.Create("hexagon", 140, 90, "#DCE6F1");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Inside"));
        shape.TextParagraphs.Add(paragraph);

        var visual = PresetShapeInlineVisual.Build(
            shape, shape.CustomGeometry!, 180, 120, Brushes.White, Brushes.Black, 1);

        var grid = visual.Should().BeOfType<System.Windows.Controls.Grid>().Subject;
        grid.Children.OfType<System.Windows.Shapes.Path>().Should().ContainSingle();
        grid.Children.OfType<System.Windows.Controls.TextBlock>().Single().Text.Should().Be("Inside");
    }
}
