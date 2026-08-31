using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>Maps presentation auto-number types to and from DrawingML <c>a:buAutoNum/@type</c> tokens.</summary>
internal static class PptxAutoNumberTypeCodec
{
    public static AutoNumType Parse(string? token) => token switch
    {
        "arabicPeriod"     => AutoNumType.ArabicPeriod,
        "arabicParenR"     => AutoNumType.ArabicParenR,
        "arabicParenBoth"  => AutoNumType.ArabicParenBoth,
        "romanUcPeriod"    => AutoNumType.RomanUcPeriod,
        "romanLcPeriod"    => AutoNumType.RomanLcPeriod,
        "romanUcParenR"    => AutoNumType.RomanUcParenR,
        "romanLcParenR"    => AutoNumType.RomanLcParenR,
        "alphaUcPeriod"    => AutoNumType.AlphaUcPeriod,
        "alphaLcPeriod"    => AutoNumType.AlphaLcPeriod,
        "alphaUcParenR"    => AutoNumType.AlphaUcParenR,
        "alphaLcParenR"    => AutoNumType.AlphaLcParenR,
        "alphaUcParenBoth" => AutoNumType.AlphaUcParenBoth,
        "alphaLcParenBoth" => AutoNumType.AlphaLcParenBoth,
        _                   => AutoNumType.ArabicPeriod,
    };

    public static string Format(AutoNumType type) => type switch
    {
        AutoNumType.ArabicParenR     => "arabicParenR",
        AutoNumType.ArabicParenBoth  => "arabicParenBoth",
        AutoNumType.RomanUcPeriod    => "romanUcPeriod",
        AutoNumType.RomanLcPeriod    => "romanLcPeriod",
        AutoNumType.RomanUcParenR    => "romanUcParenR",
        AutoNumType.RomanLcParenR    => "romanLcParenR",
        AutoNumType.AlphaUcPeriod    => "alphaUcPeriod",
        AutoNumType.AlphaLcPeriod    => "alphaLcPeriod",
        AutoNumType.AlphaUcParenR    => "alphaUcParenR",
        AutoNumType.AlphaLcParenR    => "alphaLcParenR",
        AutoNumType.AlphaUcParenBoth => "alphaUcParenBoth",
        AutoNumType.AlphaLcParenBoth => "alphaLcParenBoth",
        _                            => "arabicPeriod",
    };
}
