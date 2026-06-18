using System;
using System.Collections.Generic;

namespace FreeX.App.Avalonia;

/// <summary>
/// A small, hand-curated synonym map backing the Review ▸ Thesaurus command. This is a modest
/// built-in word list (like <see cref="SpellingWordList"/>), NOT a full thesaurus database; it
/// covers a few dozen common business/spreadsheet words so the feature is functional offline.
/// </summary>
internal static class ThesaurusData
{
    private static readonly IReadOnlyDictionary<string, string[]> Synonyms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["total"] = new[] { "sum", "aggregate", "grand total", "whole" },
            ["sum"] = new[] { "total", "aggregate", "amount" },
            ["amount"] = new[] { "quantity", "sum", "total", "value" },
            ["value"] = new[] { "worth", "amount", "figure" },
            ["increase"] = new[] { "rise", "growth", "gain", "boost" },
            ["decrease"] = new[] { "decline", "reduction", "drop", "fall" },
            ["profit"] = new[] { "gain", "earnings", "return", "income" },
            ["loss"] = new[] { "deficit", "shortfall", "decline" },
            ["revenue"] = new[] { "income", "sales", "turnover", "earnings" },
            ["cost"] = new[] { "expense", "price", "charge", "outlay" },
            ["expense"] = new[] { "cost", "outlay", "expenditure" },
            ["price"] = new[] { "cost", "rate", "charge", "value" },
            ["customer"] = new[] { "client", "buyer", "patron" },
            ["client"] = new[] { "customer", "patron", "account" },
            ["product"] = new[] { "item", "good", "merchandise" },
            ["region"] = new[] { "area", "zone", "territory", "district" },
            ["category"] = new[] { "class", "group", "type", "kind" },
            ["summary"] = new[] { "overview", "synopsis", "digest", "recap" },
            ["report"] = new[] { "statement", "summary", "account" },
            ["average"] = new[] { "mean", "typical", "norm" },
            ["estimate"] = new[] { "approximation", "projection", "forecast" },
            ["forecast"] = new[] { "projection", "prediction", "outlook" },
            ["budget"] = new[] { "plan", "allocation", "allowance" },
            ["target"] = new[] { "goal", "objective", "aim" },
            ["goal"] = new[] { "target", "objective", "aim" },
            ["growth"] = new[] { "increase", "expansion", "rise" },
            ["change"] = new[] { "variation", "shift", "difference" },
            ["balance"] = new[] { "remainder", "residue", "net" },
            ["quantity"] = new[] { "amount", "number", "count" },
            ["rate"] = new[] { "ratio", "percentage", "speed" },
            ["large"] = new[] { "big", "great", "substantial" },
            ["small"] = new[] { "little", "minor", "slight" },
            ["high"] = new[] { "elevated", "tall", "great" },
            ["low"] = new[] { "small", "reduced", "minimal" },
            ["new"] = new[] { "recent", "fresh", "novel" },
            ["old"] = new[] { "former", "previous", "prior" },
        };

    /// <summary>Returns synonyms for <paramref name="word"/>, or an empty list if none are known.</summary>
    public static IReadOnlyList<string> Lookup(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return Array.Empty<string>();

        return Synonyms.TryGetValue(word.Trim(), out var matches)
            ? matches
            : Array.Empty<string>();
    }
}
