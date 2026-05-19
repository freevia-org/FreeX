using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

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
}
