using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelTable = FreeW.Core.Model.Table;
using ModelTableCell = FreeW.Core.Model.TableCell;
using ModelTableRow = FreeW.Core.Model.TableRow;

namespace FreeW.App.Host.Tests;

public sealed class ImageAndTableConversionParityTests
{
    [StaFact]
    public void ImageCropHostRoute_MutatesSelectedImageAndUndoRestoresIt()
    {
        var image = new InlineImage([0x89, 0x50, 0x4E, 0x47], 120, 80)
        {
            Wrapping = ImageWrapping.Square,
        };
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(Run.FromImage(image));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingImage(image);

        view.SetSelectedImageCrop(0.1, 0.2, 0.15, 0.05);

        image.CropLeft.Should().Be(0.1);
        image.CropRight.Should().Be(0.2);
        image.CropTop.Should().Be(0.15);
        image.CropBottom.Should().Be(0.05);

        view.Commands.Undo();

        image.CropLeft.Should().Be(0);
        image.CropRight.Should().Be(0);
        image.CropTop.Should().Be(0);
        image.CropBottom.Should().Be(0);
    }

    [StaFact]
    public void PictureCoreHostRoutes_MutateSelectedImageAndUndoRestoreIt()
    {
        var (view, image) = SelectedImage();

        view.SetSelectedImageAltText("  Updated description  ");
        image.AltText.Should().Be("Updated description");
        view.Commands.Undo();
        image.AltText.Should().Be("Original description");

        view.SetSelectedImageBorder("AABBCC", 2.25, "dot");
        (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
            .Should().Be(("AABBCC", 2.25, "dot"));
        view.Commands.Undo();
        (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
            .Should().Be(("112233", 0.75, "dash"));

        view.SetSelectedImageSize(210, 105);
        (image.WidthPt, image.HeightPt).Should().Be((210, 105));
        view.Commands.Undo();
        (image.WidthPt, image.HeightPt).Should().Be((240, 120));

        view.ResetSelectedImage();
        (image.WidthPt, image.HeightPt).Should().Be((150, 75));
        image.RotationAngle.Should().Be(0);
        image.FlipH.Should().BeFalse();
        image.HasCrop.Should().BeFalse();
        image.BrightnessPct.Should().Be(0);
        view.Commands.Undo();
        (image.WidthPt, image.HeightPt).Should().Be((240, 120));
        image.RotationAngle.Should().Be(45);
        image.FlipH.Should().BeTrue();
        image.CropLeft.Should().Be(0.1);
        image.BrightnessPct.Should().Be(20);
    }

    [StaFact]
    public void PictureStyleRegistryRoutes_ApplySharedCatalogPresetAndUndo()
    {
        var (view, image) = SelectedImage();
        image.ShadowPreset = 5;
        image.ReflectionPreset = 2;
        image.SoftEdgePt = 1.25;
        image.PictureStylePreset = 99;
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            registry.TryGet($"freew.image-style-{preset.Id}", out var command).Should().BeTrue();
            var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
            stateful.GetState().IsEnabled.Should().BeTrue();

            command.Execute(RibbonCommandContext.Empty);

            PictureStyle(image).Should().Be(PictureStyle(preset));
            view.Commands.Undo().Should().BeTrue();
            PictureStyle(image).Should().Be((99, "112233", 0.75, "dash", 5, 2, 1.25));
        }

        var emptyRegistry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            emptyRegistry.TryGet($"freew.image-style-{preset.Id}", out var command).Should().BeTrue();
            command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject
                .GetState().IsEnabled.Should().BeFalse();
        }
    }

    [StaFact]
    public void TableToTextHostRoute_UsesSharedConverterAndUndoRestoresTable()
    {
        var table = new ModelTable();
        table.Rows.Add(Row("North", "120"));
        table.Rows.Add(Row("South", "98"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        var renderedTable = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single();
        view.CaretPosition = renderedTable.RowGroups[0].Rows[0].Cells[0].ContentStart;

        view.ConvertTableToText(';');

        view.Model.Blocks.OfType<ModelParagraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("North;120", "South;98");

        view.Commands.Undo();

        view.Model.Blocks.Should().ContainSingle().Which.Should().BeOfType<ModelTable>();
    }

    private static ModelTableRow Row(params string[] values)
    {
        var row = new ModelTableRow();
        foreach (var value in values)
            row.Cells.Add(new ModelTableCell(value));
        return row;
    }

    private static (DocumentView View, InlineImage Image) SelectedImage()
    {
        var image = new InlineImage([0x89, 0x50, 0x4E, 0x47], 240, 120)
        {
            Wrapping = ImageWrapping.Square,
            AltText = "Original description",
            BorderColorHex = "112233",
            BorderWidthPt = 0.75,
            BorderDash = "dash",
            RotationAngle = 45,
            FlipH = true,
            CropLeft = 0.1,
            BrightnessPct = 20,
            OriginalPixelWidth = 200,
            OriginalPixelHeight = 100,
        };
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(Run.FromImage(image));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingImage(image);
        return (view, image);
    }

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(InlineImage image) =>
        (image.PictureStylePreset, image.BorderColorHex, image.BorderWidthPt, image.BorderDash,
            image.ShadowPreset, image.ReflectionPreset, image.SoftEdgePt);

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(PictureStylePreset preset) =>
        (preset.Id, preset.BorderColorHex, preset.BorderWidthPt, preset.BorderDash,
            preset.ShadowPreset, preset.ReflectionPreset, preset.SoftEdgePt);
}
