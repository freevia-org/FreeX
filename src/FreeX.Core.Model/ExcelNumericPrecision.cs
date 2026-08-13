namespace FreeX.Core.Model;

/// <summary>Applies Excel's 15-significant-digit storage precision to numeric values.</summary>
public static class ExcelNumericPrecision
{
    public const int SignificantDigits = 15;

    public static double CapSignificantDigits(double value)
    {
        if (value == 0 || !double.IsFinite(value))
            return value;

        var scale = SignificantDigits - (int)Math.Floor(Math.Log10(Math.Abs(value))) - 1;
        if (scale < 0)
        {
            var divisor = Math.Pow(10, -scale);
            return Math.Truncate(value / divisor) * divisor;
        }

        // Math.Round only accepts 0-15 decimal places. Tiny values need no decimal-place
        // rounding here; forcing them through 15 places would underflow valid input to zero.
        return scale > SignificantDigits
            ? value
            : Math.Round(value, scale, MidpointRounding.AwayFromZero);
    }
}
