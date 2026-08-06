namespace FreeX.App.Services;

public sealed record ThesaurusLookupPlan(
    string OriginalText,
    string Word,
    int StartIndex,
    int Length,
    IReadOnlyList<string> Synonyms);

public static class ThesaurusWorkflowPlanner
{
    private static readonly IReadOnlyDictionary<string, string[]> Synonyms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["total"] = ["sum", "aggregate", "grand total", "whole"],
            ["sum"] = ["total", "aggregate", "amount"],
            ["amount"] = ["quantity", "sum", "total", "value"],
            ["value"] = ["worth", "amount", "figure"],
            ["increase"] = ["rise", "growth", "gain", "boost"],
            ["decrease"] = ["decline", "reduction", "drop", "fall"],
            ["profit"] = ["gain", "earnings", "return", "income"],
            ["loss"] = ["deficit", "shortfall", "decline"],
            ["revenue"] = ["income", "sales", "turnover", "earnings"],
            ["cost"] = ["expense", "price", "charge", "outlay"],
            ["expense"] = ["cost", "outlay", "expenditure"],
            ["price"] = ["cost", "rate", "charge", "value"],
            ["customer"] = ["client", "buyer", "patron"],
            ["client"] = ["customer", "patron", "account"],
            ["product"] = ["item", "good", "merchandise"],
            ["region"] = ["area", "zone", "territory", "district"],
            ["category"] = ["class", "group", "type", "kind"],
            ["summary"] = ["overview", "synopsis", "digest", "recap"],
            ["report"] = ["statement", "summary", "account"],
            ["average"] = ["mean", "typical", "norm"],
            ["estimate"] = ["approximation", "projection", "forecast"],
            ["forecast"] = ["projection", "prediction", "outlook"],
            ["budget"] = ["plan", "allocation", "allowance"],
            ["target"] = ["goal", "objective", "aim"],
            ["goal"] = ["target", "objective", "aim"],
            ["growth"] = ["increase", "expansion", "rise"],
            ["change"] = ["variation", "shift", "difference"],
            ["balance"] = ["remainder", "residue", "net"],
            ["quantity"] = ["amount", "number", "count"],
            ["rate"] = ["ratio", "percentage", "speed"],
            ["large"] = ["big", "great", "substantial"],
            ["small"] = ["little", "minor", "slight"],
            ["high"] = ["elevated", "tall", "great"],
            ["low"] = ["small", "reduced", "minimal"],
            ["new"] = ["recent", "fresh", "novel"],
            ["old"] = ["former", "previous", "prior"],
        };

    public static bool TryCreateLookup(string? text, out ThesaurusLookupPlan plan)
    {
        plan = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var start = 0;
        while (start < text.Length && !char.IsLetter(text[start]))
            start++;
        if (start == text.Length)
            return false;

        var end = start + 1;
        while (end < text.Length && char.IsLetter(text[end]))
            end++;

        var word = text[start..end];
        plan = new(
            text,
            word,
            start,
            end - start,
            Lookup(word));
        return true;
    }

    public static string ApplyReplacement(ThesaurusLookupPlan plan, string? replacement)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var normalizedReplacement = replacement?.Trim();
        if (string.IsNullOrEmpty(normalizedReplacement) ||
            plan.StartIndex < 0 ||
            plan.Length <= 0 ||
            plan.StartIndex > plan.OriginalText.Length - plan.Length)
        {
            return plan.OriginalText;
        }

        return plan.OriginalText[..plan.StartIndex] +
               normalizedReplacement +
               plan.OriginalText[(plan.StartIndex + plan.Length)..];
    }

    public static IReadOnlyList<string> Lookup(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return [];

        return Synonyms.TryGetValue(word.Trim(), out var matches)
            ? matches
            : [];
    }
}
