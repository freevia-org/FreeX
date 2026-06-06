using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class PortPreviewWorkbookFactory
{
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

        return new StartupWorkbookLoadResult(workbook, workbook.Name, status, isFallback);
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
