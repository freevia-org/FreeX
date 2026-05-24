using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static string FormatGeneral(ScalarValue value) => value switch
    {
        NumberValue n => FormatNumberGeneral(n.Value),
        DateTimeValue d => FormatGeneralDateTime(d.Value),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        ErrorValue e => e.Code,
        BlankValue => "",
        _ => ""
    };

    private static string FormatGeneralDateTime(double value)
    {
        try { return DateTime.FromOADate(value).ToString("d", CultureInfo.InvariantCulture); }
        catch { return FormatNumberGeneral(value); }
    }

    private static string FormatNumberGeneral(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return value.ToString(CultureInfo.InvariantCulture);
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("G10", CultureInfo.InvariantCulture);
    }
}
