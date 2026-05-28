using System.Globalization;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class ChartDialogInputParser
{
    public static bool TryReadOptionalColor(TextBox textBox, out CellColor? color) =>
        ColorInputParser.TryParseOptionalHexColor(textBox.Text, out color);

    public static bool TryReadNullableDouble(TextBox textBox, out double? value)
    {
        value = null;
        var text = textBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TryReadFiniteDouble(text, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    public static bool TryReadNullablePositiveDouble(TextBox textBox, out double? value) =>
        TryReadNullableDouble(textBox, out value) && value is null or > 0;

    public static bool TryReadPositiveDouble(TextBox textBox, out double value) =>
        TryReadFiniteDouble(textBox.Text, out value) && value > 0;

    public static bool TryReadClampedDouble(TextBox textBox, double min, double max, out double value) =>
        TryReadFiniteDouble(textBox.Text, out value) && value >= min && value <= max;

    private static bool TryReadFiniteDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);
}
