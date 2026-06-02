using FreeX.Core.Model;

namespace FreeX.Core.Formula;

internal static class FormulaSafetyLimits
{
    public const int MaxParseTokens = 16_384;
    public const int MaxParseDepth = 512;
    public const int MaxParseNesting = 256;
    public const long MaxMaterializedRangeCells = 1_000_000L;
    public const long MaxStreamingRangeCells = 1_048_576L;
    public const int MaxRegexCacheEntries = 1_024;
    public const int MaxParsedFormulaCacheEntries = 1_024;
    public const int MaxTokenizedFormulaCacheEntries = 1_024;
    public const int MaxParsedTokenFormulaCacheEntries = 1_024;

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static long GetRangeCellCount(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var rows = Math.Abs((long)endRow - startRow) + 1;
        var cols = Math.Abs((long)endCol - startCol) + 1;
        return rows * cols;
    }
}

internal sealed record RangeMaterializationErrorValue(ErrorValue Error) : ScalarValue;
