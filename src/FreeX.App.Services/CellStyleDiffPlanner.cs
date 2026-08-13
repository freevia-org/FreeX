using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Services;

public enum CellStylePreset
{
    Normal,
    Good,
    Bad,
    Neutral,
    Input,
    Output,
    Calculation,
    CheckCell,
    LinkedCell,
    ExplanatoryText,
    Heading1,
    Heading2,
    Note,
    WarningText,
    Total,
    Accent1_20,
    Accent2_20,
    Accent3_20,
    Accent4_20,
    Accent5_20,
    Accent6_20,
    Accent1_40,
    Accent2_40,
    Accent3_40,
    Accent4_40,
    Accent5_40,
    Accent6_40,
    Accent1_60,
    Accent2_60,
    Accent3_60,
    Accent4_60,
    Accent5_60,
    Accent6_60
}

public static class CellStyleDiffPlanner
{
    public static StyleDiff ClearFormatsDiff() =>
        new(
            Bold: false,
            Italic: false,
            Underline: false,
            DoubleUnderline: false,
            Strikethrough: false,
            Superscript: false,
            Subscript: false,
            FontName: "Calibri",
            FontSize: 11,
            FontColor: CellColor.Black,
            ClearFill: true,
            NumberFormat: "General",
            HAlign: CellHAlign.General,
            VAlign: CellVAlign.Bottom,
            WrapText: false,
            ShrinkToFit: false,
            IndentLevel: 0,
            TextRotation: 0,
            BorderTop: new CellBorder(BorderStyle.None),
            BorderBottom: new CellBorder(BorderStyle.None),
            BorderLeft: new CellBorder(BorderStyle.None),
            BorderRight: new CellBorder(BorderStyle.None),
            BorderDiagonalDown: new CellBorder(BorderStyle.None),
            BorderDiagonalUp: new CellBorder(BorderStyle.None),
            Locked: true,
            Hidden: false);

    public static StyleDiff UnderlineDiff(bool enabled) =>
        new(Underline: enabled, Strikethrough: enabled ? false : null);

    public static StyleDiff StrikethroughDiff(bool enabled) =>
        new(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null);

    public static StyleDiff DoubleUnderlineDiff(bool enabled) =>
        new(DoubleUnderline: enabled, Underline: enabled ? false : null, Strikethrough: enabled ? false : null);

    public static StyleDiff GetCellStylePresetDiff(CellStylePreset preset) =>
        GetCellStylePresetDiff(preset, WorkbookTheme.Office);

    public static string GetCellStylePresetDisplayName(CellStylePreset preset) =>
        preset switch
        {
            CellStylePreset.CheckCell => "Check Cell",
            CellStylePreset.LinkedCell => "Linked Cell",
            CellStylePreset.ExplanatoryText => "Explanatory Text",
            CellStylePreset.Heading1 => "Heading 1",
            CellStylePreset.Heading2 => "Heading 2",
            CellStylePreset.WarningText => "Warning Text",
            CellStylePreset.Accent1_20 => "20% - Accent 1",
            CellStylePreset.Accent2_20 => "20% - Accent 2",
            CellStylePreset.Accent3_20 => "20% - Accent 3",
            CellStylePreset.Accent4_20 => "20% - Accent 4",
            CellStylePreset.Accent5_20 => "20% - Accent 5",
            CellStylePreset.Accent6_20 => "20% - Accent 6",
            CellStylePreset.Accent1_40 => "40% - Accent 1",
            CellStylePreset.Accent2_40 => "40% - Accent 2",
            CellStylePreset.Accent3_40 => "40% - Accent 3",
            CellStylePreset.Accent4_40 => "40% - Accent 4",
            CellStylePreset.Accent5_40 => "40% - Accent 5",
            CellStylePreset.Accent6_40 => "40% - Accent 6",
            CellStylePreset.Accent1_60 => "60% - Accent 1",
            CellStylePreset.Accent2_60 => "60% - Accent 2",
            CellStylePreset.Accent3_60 => "60% - Accent 3",
            CellStylePreset.Accent4_60 => "60% - Accent 4",
            CellStylePreset.Accent5_60 => "60% - Accent 5",
            CellStylePreset.Accent6_60 => "60% - Accent 6",
            _ => preset.ToString()
        };

    public static string GetCellStylePresetLabelResourceKey(CellStylePreset preset) =>
        preset switch
        {
            CellStylePreset.Normal => "MainWindow_Header_Normal",
            CellStylePreset.Good => "MainWindow_Header_Good",
            CellStylePreset.Bad => "MainWindow_Header_Bad",
            CellStylePreset.Neutral => "MainWindow_Header_Neutral",
            CellStylePreset.Input => "MainWindow_Header_Input",
            CellStylePreset.Output => "MainWindow_Header_Output",
            CellStylePreset.Calculation => "MainWindow_Header_Calculation",
            CellStylePreset.CheckCell => "MainWindow_Header_CheckCell",
            CellStylePreset.LinkedCell => "MainWindow_Header_LinkedCell",
            CellStylePreset.ExplanatoryText => "MainWindow_Header_ExplanatoryText",
            CellStylePreset.Heading1 => "MainWindow_Header_Heading1",
            CellStylePreset.Heading2 => "MainWindow_Header_Heading2",
            CellStylePreset.Note => "MainWindow_Header_Note",
            CellStylePreset.WarningText => "MainWindow_Header_WarningText",
            CellStylePreset.Total => "MainWindow_Header_Total",
            CellStylePreset.Accent1_20 => "MainWindow_Header_20Accent1",
            CellStylePreset.Accent2_20 => "MainWindow_Header_20Accent2",
            CellStylePreset.Accent3_20 => "MainWindow_Header_20Accent3",
            CellStylePreset.Accent4_20 => "MainWindow_Header_20Accent4",
            CellStylePreset.Accent5_20 => "MainWindow_Header_20Accent5",
            CellStylePreset.Accent6_20 => "MainWindow_Header_20Accent6",
            CellStylePreset.Accent1_40 => "MainWindow_Header_40Accent1",
            CellStylePreset.Accent2_40 => "MainWindow_Header_40Accent2",
            CellStylePreset.Accent3_40 => "MainWindow_Header_40Accent3",
            CellStylePreset.Accent4_40 => "MainWindow_Header_40Accent4",
            CellStylePreset.Accent5_40 => "MainWindow_Header_40Accent5",
            CellStylePreset.Accent6_40 => "MainWindow_Header_40Accent6",
            CellStylePreset.Accent1_60 => "MainWindow_Header_60Accent1",
            CellStylePreset.Accent2_60 => "MainWindow_Header_60Accent2",
            CellStylePreset.Accent3_60 => "MainWindow_Header_60Accent3",
            CellStylePreset.Accent4_60 => "MainWindow_Header_60Accent4",
            CellStylePreset.Accent5_60 => "MainWindow_Header_60Accent5",
            CellStylePreset.Accent6_60 => "MainWindow_Header_60Accent6",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

    public static StyleDiff GetCellStylePresetDiff(CellStylePreset preset, WorkbookTheme theme) =>
        preset switch
        {
            CellStylePreset.Normal => ClearFormatsDiff(),
            CellStylePreset.Good => new StyleDiff(
                FillColor: new CellColor(198, 239, 206),
                FontColor: new CellColor(0, 97, 0)),
            CellStylePreset.Bad => new StyleDiff(
                FillColor: new CellColor(255, 199, 206),
                FontColor: new CellColor(156, 0, 6)),
            CellStylePreset.Neutral => new StyleDiff(
                FillColor: new CellColor(255, 235, 156),
                FontColor: new CellColor(156, 101, 0)),
            CellStylePreset.Input => BoxedStyle(
                fillColor: new CellColor(255, 255, 204),
                fontColor: CellColor.Black,
                bold: false,
                numberFormat: "#,##0.00"),
            CellStylePreset.Output => BoxedStyle(
                fillColor: new CellColor(242, 242, 242),
                fontColor: CellColor.Black,
                bold: true,
                numberFormat: "#,##0.00"),
            CellStylePreset.Calculation => BoxedStyle(
                fillColor: new CellColor(242, 220, 219),
                fontColor: CellColor.Black,
                bold: true,
                numberFormat: "#,##0.00"),
            CellStylePreset.CheckCell => new StyleDiff(
                FillColor: new CellColor(252, 228, 214),
                FontColor: new CellColor(156, 87, 0),
                Bold: true,
                NumberFormat: "General",
                BorderBottom: ThinGrayBorder()),
            CellStylePreset.LinkedCell => new StyleDiff(
                FillColor: new CellColor(221, 235, 247),
                FontColor: new CellColor(5, 99, 193),
                Underline: true,
                DoubleUnderline: false,
                Strikethrough: false,
                Bold: false,
                NumberFormat: "General",
                BorderBottom: ThinGrayBorder()),
            CellStylePreset.ExplanatoryText => new StyleDiff(
                FillColor: new CellColor(242, 242, 242),
                FontColor: new CellColor(89, 89, 89),
                Italic: true,
                Bold: false,
                NumberFormat: "General"),
            CellStylePreset.Heading1 => new StyleDiff(
                Bold: true,
                FontSize: 15,
                FontColor: CellColor.Black,
                ClearFill: true,
                BorderBottom: new CellBorder(BorderStyle.Medium, theme.GetColor(WorkbookThemeColorSlot.Accent1))),
            CellStylePreset.Heading2 => new StyleDiff(
                Bold: true,
                FontSize: 14,
                FontColor: CellColor.Black,
                ClearFill: true,
                BorderBottom: new CellBorder(BorderStyle.Thin, theme.GetColor(WorkbookThemeColorSlot.Accent1))),
            CellStylePreset.Note => new StyleDiff(
                FillColor: new CellColor(255, 255, 204),
                BorderBottom: new CellBorder(BorderStyle.Thin, CellColor.Black)),
            CellStylePreset.WarningText => new StyleDiff(
                FillColor: new CellColor(255, 192, 0),
                FontColor: CellColor.Black,
                Bold: true),
            CellStylePreset.Total => new StyleDiff(
                Bold: true,
                BorderTop: new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderBottom: new CellBorder(BorderStyle.Double, CellColor.Black)),
            CellStylePreset.Accent1_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent1, 0.8),
            CellStylePreset.Accent2_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent2, 0.8),
            CellStylePreset.Accent3_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent3, 0.8),
            CellStylePreset.Accent4_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent4, 0.8),
            CellStylePreset.Accent5_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent5, 0.8),
            CellStylePreset.Accent6_20 => AccentDepth(theme, WorkbookThemeColorSlot.Accent6, 0.8),
            CellStylePreset.Accent1_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent1, 0.6),
            CellStylePreset.Accent2_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent2, 0.6),
            CellStylePreset.Accent3_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent3, 0.6),
            CellStylePreset.Accent4_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent4, 0.6),
            CellStylePreset.Accent5_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent5, 0.6),
            CellStylePreset.Accent6_40 => AccentDepth(theme, WorkbookThemeColorSlot.Accent6, 0.6),
            CellStylePreset.Accent1_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent1, 0.4),
            CellStylePreset.Accent2_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent2, 0.4),
            CellStylePreset.Accent3_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent3, 0.4),
            CellStylePreset.Accent4_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent4, 0.4),
            CellStylePreset.Accent5_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent5, 0.4),
            CellStylePreset.Accent6_60 => AccentDepth(theme, WorkbookThemeColorSlot.Accent6, 0.4),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

    private static StyleDiff BoxedStyle(CellColor fillColor, CellColor fontColor, bool bold, string numberFormat)
    {
        var border = ThinGrayBorder();
        return new StyleDiff(
            FillColor: fillColor,
            FontColor: fontColor,
            Bold: bold,
            NumberFormat: numberFormat,
            BorderTop: border,
            BorderRight: border,
            BorderBottom: border,
            BorderLeft: border);
    }

    private static StyleDiff AccentDepth(WorkbookTheme theme, WorkbookThemeColorSlot slot, double tint) =>
        new(
            FillThemeColor: new WorkbookThemeColorReference(slot, tint),
            FontColor: CellColor.Black,
            BorderBottom: new CellBorder(BorderStyle.Thin, theme.GetColor(slot)));

    private static CellBorder ThinGrayBorder() =>
        new(BorderStyle.Thin, new CellColor(128, 128, 128));
}
