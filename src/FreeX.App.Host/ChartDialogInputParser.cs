using System.Windows.Controls;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class ChartDialogInputParser
{
    public static bool TryReadOptionalColor(TextBox textBox, out CellColor? color) =>
        ColorInputParser.TryParseOptionalHexColor(textBox.Text, out color);

    public static bool TryReadNullableDouble(TextBox textBox, out double? value) =>
        ChartDialogValueParser.TryParseNullableDouble(textBox.Text, out value);

    public static bool TryReadNullablePositiveDouble(TextBox textBox, out double? value) =>
        ChartDialogValueParser.TryParseNullablePositiveDouble(textBox.Text, out value);

    public static bool TryReadPositiveDouble(TextBox textBox, out double value) =>
        ChartDialogValueParser.TryParsePositiveDouble(textBox.Text, out value);

    public static bool TryReadClampedDouble(TextBox textBox, double min, double max, out double value) =>
        ChartDialogValueParser.TryParseClampedDouble(textBox.Text, min, max, out value);
}
