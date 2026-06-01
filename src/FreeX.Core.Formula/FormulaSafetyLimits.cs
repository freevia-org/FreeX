namespace FreeX.Core.Formula;

internal static class FormulaSafetyLimits
{
    public const int MaxParseTokens = 16_384;
    public const int MaxParseDepth = 512;
    public const int MaxParseNesting = 256;

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
}
