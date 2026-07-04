using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static string FormatGeneral(ScalarValue value, bool uses1904DateSystem = false) => value switch
    {
        NumberValue n => FormatNumberGeneral(n.Value),
        DateTimeValue d => FormatGeneralDateTime(d.Value, uses1904DateSystem),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        ErrorValue e => e.Code,
        BlankValue => "",
        _ => ""
    };

    private static string FormatGeneralDateTime(double value, bool uses1904DateSystem = false)
    {
        try
        {
            var dt = uses1904DateSystem
                ? ExcelDateSystem.SerialToDate(value, uses1904DateSystem)
                : DateTime.FromOADate(value);
            return dt.ToString("d", CultureInfo.InvariantCulture);
        }
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
