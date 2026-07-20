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
}
