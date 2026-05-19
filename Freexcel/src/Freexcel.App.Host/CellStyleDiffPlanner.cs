using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

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
    Accent6_20
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
            FontName: "Calibri",
            FontSize: 11,
            FontColor: CellColor.Black,
            ClearFill: true,
            NumberFormat: "General",
            HAlign: CellHAlign.General,
            VAlign: CellVAlign.Bottom,
            WrapText: false,
            IndentLevel: 0,
            BorderTop: new CellBorder(BorderStyle.None),
            BorderBottom: new CellBorder(BorderStyle.None),
            BorderLeft: new CellBorder(BorderStyle.None),
            BorderRight: new CellBorder(BorderStyle.None));

    public static StyleDiff UnderlineDiff(bool enabled) =>
        new(Underline: enabled, Strikethrough: enabled ? false : null);

    public static StyleDiff StrikethroughDiff(bool enabled) =>
        new(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null);

    public static StyleDiff DoubleUnderlineDiff(bool enabled) =>
        new(DoubleUnderline: enabled, Underline: enabled ? false : null, Strikethrough: enabled ? false : null);

    public static StyleDiff GetCellStylePresetDiff(CellStylePreset preset) =>
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
                FontSize: 16,
                FillColor: new CellColor(31, 115, 70),
                FontColor: CellColor.White),
            CellStylePreset.Heading2 => new StyleDiff(
                Bold: true,
                FontSize: 14),
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
            CellStylePreset.Accent1_20 => Accent20(new CellColor(221, 235, 247), new CellColor(91, 155, 213)),
            CellStylePreset.Accent2_20 => Accent20(new CellColor(226, 239, 218), new CellColor(112, 173, 71)),
            CellStylePreset.Accent3_20 => Accent20(new CellColor(255, 242, 204), new CellColor(255, 192, 0)),
            CellStylePreset.Accent4_20 => Accent20(new CellColor(252, 228, 214), new CellColor(237, 125, 49)),
            CellStylePreset.Accent5_20 => Accent20(new CellColor(234, 232, 246), new CellColor(112, 48, 160)),
            CellStylePreset.Accent6_20 => Accent20(new CellColor(218, 238, 243), new CellColor(75, 172, 198)),
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

    private static StyleDiff Accent20(CellColor fillColor, CellColor borderColor) =>
        new(
            FillColor: fillColor,
            FontColor: CellColor.Black,
            BorderBottom: new CellBorder(BorderStyle.Thin, borderColor));

    private static CellBorder ThinGrayBorder() =>
        new(BorderStyle.Thin, new CellColor(128, 128, 128));
}
