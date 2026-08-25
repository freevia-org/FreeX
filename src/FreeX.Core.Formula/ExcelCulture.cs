using System.Globalization;

namespace FreeX.Core.Formula;

internal static class ExcelCulture
{
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Uses the operating-system culture when one is configured. Headless Unix environments often
    /// expose the invariant culture instead; Excel-compatible text functions use the US baseline in
    /// that case instead of emitting the invariant culture's placeholder currency symbol (¤).
    /// </summary>
    public static CultureInfo Current =>
        CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture)
            ? DefaultCulture
            : CultureInfo.CurrentCulture;
}
