namespace FreeW.Core.Model;

/// <summary>Stable identifiers of the deterministic grammar rules emitted as proofing diagnostics.</summary>
public static class GrammarRuleIds
{
    public const string RepeatedWord = "repeated-word";
    public const string SentenceCapitalization = "sentence-capitalization";
    public const string MultipleSpaces = "multiple-spaces";
    public const string SpaceBeforePunctuation = "space-before-punctuation";
    public const string MissingSpaceAfterPunctuation = "missing-space-after-punctuation";
    public const string IndefiniteArticle = "indefinite-article";
    public const string RepeatedPunctuation = "repeated-punctuation";
    public const string UnpairedBracket = "unpaired-bracket";
    public const string UnpairedQuote = "unpaired-quote";
    public const string CommonConfusion = "common-confusion";
}
