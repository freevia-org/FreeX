using System.Text.RegularExpressions;

namespace FreeX.Core.Calc;

public static partial class NumberFormatter
{
    private static readonly Regex NumericElapsedTokenRegex = new(@"\[([hH])\]|\[([mM])\]|\[([sS])\]");
    private static readonly Regex NumericBracketDirectiveRegex = new(@"\[[^\]]*\]");

    private readonly record struct FormatDirectivePreprocessResult(
        string Format,
        Match ElapsedTimeMatch);

    private static FormatDirectivePreprocessResult PreprocessBracketFormatDirectives(string format)
    {
        if (format.IndexOf('[') < 0)
            return new FormatDirectivePreprocessResult(format, Match.Empty);

        var elapsedTimeMatch = NumericElapsedTokenRegex.Match(format);
        if (elapsedTimeMatch.Success)
        {
            return new FormatDirectivePreprocessResult(
                RemoveSpacingAndFillDirectives(format),
                elapsedTimeMatch);
        }

        return new FormatDirectivePreprocessResult(
            NumericBracketDirectiveRegex.Replace(format, ""),
            elapsedTimeMatch);
    }
}
