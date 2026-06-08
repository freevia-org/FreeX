using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class PortPreviewWorkbookFactory
{
    public const string PreviewShapeName = "Port readiness shape";
    public const string PreviewTextBoxName = "Port preview note";
    public const string PreviewPictureName = "Port preview logo";
    public const string PreviewCellRangeSnapshotName = "Port preview cell snapshot";

    private static readonly Guid PreviewShapeId = Guid.Parse("9f5c4fe4-7d85-4ea1-a74b-9463e1f4be41");
    private static readonly Guid PreviewTextBoxId = Guid.Parse("5c82f7de-cf4e-4a30-bb96-8fd6f258b6f8");
    private static readonly Guid PreviewPictureId = Guid.Parse("ce0512d4-9455-4e11-a569-76da291e2c3a");
    private static readonly Guid PreviewCellRangeSnapshotId = Guid.Parse("afca9121-b098-45f6-9300-fb6920647169");
    private static readonly byte[] PreviewPictureBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    public static StartupWorkbookLoadResult Create(string status, bool isFallback)
    {
        var workbook = new Workbook("macOS Preview Workbook");
        var sheet = workbook.AddSheet("Port Plan");
        workbook.ActiveSheetIndex = 0;
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.ColumnWidths[1] = 22;
        sheet.ColumnWidths[2] = 18;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 34;
        sheet.ColumnWidths[5] = 18;

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = CellColor.FromArgb(232, 238, 247),
            FontColor = CellColor.FromArgb(25, 31, 40),
        });
        var greenStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = CellColor.FromArgb(226, 242, 232),
            FontColor = CellColor.FromArgb(25, 92, 52),
        });
        var amberStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = CellColor.FromArgb(255, 242, 214),
            FontColor = CellColor.FromArgb(119, 73, 10),
        });

        Set(sheet, 1, 1, "Area", headerStyle);
        Set(sheet, 1, 2, "Windows", headerStyle);
        Set(sheet, 1, 3, "macOS", headerStyle);
        Set(sheet, 1, 4, "Next port task", headerStyle);
        Set(sheet, 1, 5, "Priority", headerStyle);

        Set(sheet, 2, 1, "Core model", null);
        Set(sheet, 2, 2, "Shipping", greenStyle);
        Set(sheet, 2, 3, "Portable", greenStyle);
        Set(sheet, 2, 4, "Keep WPF/Win32 references out of Core.*", null);
        Set(sheet, 2, 5, 1, null);

        Set(sheet, 3, 1, "Formula/calc", null);
        Set(sheet, 3, 2, "Shipping", greenStyle);
        Set(sheet, 3, 3, "Portable", greenStyle);
        Set(sheet, 3, 4, "Run the default test lane on macOS runners", null);
        Set(sheet, 3, 5, 1, null);

        Set(sheet, 4, 1, "Workbook IO", null);
        Set(sheet, 4, 2, "Shipping", greenStyle);
        Set(sheet, 4, 3, "Preview", amberStyle);
        Set(sheet, 4, 4, "Load XLSX/CSV/FXL through shared adapters", null);
        Set(sheet, 4, 5, 2, null);

        Set(sheet, 5, 1, "App host", null);
        Set(sheet, 5, 2, "WPF", amberStyle);
        Set(sheet, 5, 3, "Avalonia shell", amberStyle);
        Set(sheet, 5, 4, "Extract reusable app services from WPF host", null);
        Set(sheet, 5, 5, 2, null);

        Set(sheet, 6, 1, "Grid", null);
        Set(sheet, 6, 2, "WPF renderer", greenStyle);
        Set(sheet, 6, 3, "Read-only viewport", amberStyle);
        Set(sheet, 6, 4, "Add selection, editing, frozen panes, and virtualization", null);
        Set(sheet, 6, 5, 1, null);

        Set(sheet, 7, 1, "Packaging", null);
        Set(sheet, 7, 2, "MSIX/EXE", greenStyle);
        Set(sheet, 7, 3, ".app artifact", amberStyle);
        Set(sheet, 7, 4, "Add Developer ID signing and notarization later", null);
        Set(sheet, 7, 5, 3, null);
        AddPreviewDrawingObjects(sheet);

        return new StartupWorkbookLoadResult(workbook, workbook.Name, status, isFallback);
    }

    private static void AddPreviewDrawingObjects(Sheet sheet)
    {
        var shape = new DrawingShapeModel
        {
            Id = PreviewShapeId,
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Name = PreviewShapeName,
            Kind = DrawingShapeKind.Rectangle,
            Width = 152,
            Height = 48,
            FillColor = CellColor.FromArgb(225, 244, 242),
            OutlineColor = CellColor.FromArgb(11, 112, 116),
            Title = "macOS readiness shape",
            AltText = "Decorative preview object used by the macOS packaging smoke."
        };
        var textBox = new TextBoxModel
        {
            Id = PreviewTextBoxId,
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Name = PreviewTextBoxName,
            Text = "Avalonia preview renders object bounds.",
            Width = 188,
            Height = 56,
            FillColor = CellColor.FromArgb(255, 255, 204),
            OutlineColor = CellColor.FromArgb(170, 136, 0),
            Title = "macOS preview note",
            AltText = "Text box preview object used by the macOS packaging smoke."
        };
        var picture = new PictureModel
        {
            Id = PreviewPictureId,
            Anchor = new CellAddress(sheet.Id, 8, 1),
            Name = PreviewPictureName,
            Kind = PictureKind.Image,
            ImageBytes = PreviewPictureBytes,
            ContentType = "image/png",
            Width = 96,
            Height = 56,
            CropLeft = 0.08,
            CropTop = 0.12,
            CropRight = 0.18,
            CropBottom = 0.1,
            Title = "macOS preview logo",
            AltText = "Small cropped image preview object used by the macOS packaging smoke."
        };
        var snapshot = new PictureModel
        {
            Id = PreviewCellRangeSnapshotId,
            Anchor = new CellAddress(sheet.Id, 8, 3),
            Name = PreviewCellRangeSnapshotName,
            Kind = PictureKind.CellRangeSnapshot,
            SourceRowCount = 2,
            SourceColumnCount = 3,
            Width = 132,
            Height = 64,
            Title = "macOS preview cell snapshot",
            AltText = "Cell-range snapshot preview object used by the macOS packaging smoke."
        };
        snapshot.Cells.AddRange(
        [
            new PictureCellSnapshot(0, 0, "Area"),
            new PictureCellSnapshot(0, 1, "macOS"),
            new PictureCellSnapshot(0, 2, "Priority"),
            new PictureCellSnapshot(1, 0, "Grid"),
            new PictureCellSnapshot(1, 1, "Read-only viewport"),
            new PictureCellSnapshot(1, 2, "1"),
        ]);

        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(textBox);
        sheet.Pictures.Add(picture);
        sheet.Pictures.Add(snapshot);
        sheet.DrawingObjectZOrder.AddRange(
        [
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, snapshot.Id),
        ]);
    }

    private static void Set(Sheet sheet, uint row, uint col, string value, StyleId? styleId) =>
        SetCell(sheet, row, col, new TextValue(value), styleId);

    private static void Set(Sheet sheet, uint row, uint col, double value, StyleId? styleId) =>
        SetCell(sheet, row, col, new NumberValue(value), styleId);

    private static void SetCell(Sheet sheet, uint row, uint col, ScalarValue value, StyleId? styleId)
    {
        var cell = Cell.FromValue(value);
        if (styleId is { } id)
            cell.StyleId = id;

        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }
}
