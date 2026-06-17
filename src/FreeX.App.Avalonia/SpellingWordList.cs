using System;
using System.Collections.Generic;
using System.Text;

namespace FreeX.App.Avalonia;

// Lightweight, SELF-CONTAINED spell-checker support for the Review ▸ Spelling command.
//
// IMPORTANT: this is NOT a full dictionary or a real spell engine (no Hunspell/ISpell, no
// morphology, no locale rules). The repository has no spell/dictionary service, so this file
// embeds a modest built-in set of common English words (a few hundred entries) plus a naive
// edit-distance-1 suggestion helper. It is intended to catch obvious typos in worksheet text,
// not to be linguistically authoritative. Real words outside this small set will be flagged as
// "unknown"; that is an accepted limitation of a built-in checker of this size.
internal static class SpellingWordList
{
    // A modest set of common English words. Stored lowercase; lookups are case-insensitive.
    // Kept deliberately small (hundreds, not tens of thousands) so the file stays maintainable.
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        // Articles, pronouns, conjunctions, prepositions, common function words.
        "a", "an", "the", "and", "or", "but", "nor", "so", "yet", "for", "if", "then", "than",
        "as", "at", "by", "in", "into", "of", "off", "on", "onto", "out", "over", "to", "up",
        "down", "with", "without", "within", "from", "about", "above", "below", "under", "between",
        "through", "during", "before", "after", "again", "once", "here", "there", "where", "when",
        "why", "how", "all", "any", "both", "each", "few", "more", "most", "other", "some", "such",
        "no", "not", "only", "own", "same", "too", "very", "can", "will", "just", "should", "now",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them", "my", "your",
        "his", "its", "our", "their", "this", "that", "these", "those", "who", "whom", "whose",
        "which", "what", "am", "is", "are", "was", "were", "be", "been", "being", "have", "has",
        "had", "do", "does", "did", "doing", "would", "could", "shall", "may", "might", "must",
        "yes", "ok", "okay", "per", "via", "etc", "vs",

        // Business / spreadsheet vocabulary (this is a spreadsheet app).
        "total", "totals", "subtotal", "sum", "average", "count", "min", "max", "value", "values",
        "amount", "amounts", "price", "prices", "cost", "costs", "quantity", "qty", "rate", "rates",
        "tax", "taxes", "discount", "discounts", "net", "gross", "profit", "loss", "revenue",
        "income", "expense", "expenses", "budget", "balance", "credit", "debit", "invoice",
        "invoices", "payment", "payments", "account", "accounts", "number", "numbers", "date",
        "dates", "time", "times", "name", "names", "address", "addresses", "phone", "email",
        "emails", "customer", "customers", "client", "clients", "vendor", "vendors", "product",
        "products", "item", "items", "order", "orders", "sales", "sale", "purchase", "purchases",
        "report", "reports", "summary", "detail", "details", "data", "row", "rows", "column",
        "columns", "cell", "cells", "sheet", "sheets", "workbook", "table", "tables", "chart",
        "charts", "category", "categories", "region", "regions", "department", "departments",
        "employee", "employees", "manager", "managers", "team", "teams", "project", "projects",
        "status", "active", "inactive", "pending", "complete", "completed", "open", "closed",
        "year", "years", "month", "months", "week", "weeks", "day", "days", "quarter", "quarters",
        "january", "february", "march", "april", "may", "june", "july", "august", "september",
        "october", "november", "december", "monday", "tuesday", "wednesday", "thursday", "friday",
        "saturday", "sunday", "total", "percent", "percentage", "ratio", "growth", "target",
        "targets", "actual", "actuals", "forecast", "estimate", "estimates", "currency", "dollar",
        "dollars", "euro", "euros", "unit", "units", "code", "codes", "type", "types", "group",
        "groups", "level", "levels", "page", "pages", "title", "titles", "header", "headers",
        "footer", "footers", "note", "notes", "comment", "comments", "label", "labels",

        // High-frequency general English words.
        "and", "about", "after", "all", "also", "any", "back", "because", "been", "before",
        "being", "between", "both", "business", "call", "came", "come", "company", "could", "day",
        "different", "does", "down", "during", "each", "early", "even", "every", "example", "find",
        "first", "from", "give", "good", "great", "group", "hand", "have", "high", "home", "into",
        "just", "keep", "know", "large", "last", "late", "left", "less", "life", "like", "line",
        "list", "little", "long", "look", "made", "make", "many", "mean", "much", "must", "name",
        "need", "never", "next", "number", "often", "only", "other", "over", "part", "people",
        "place", "point", "right", "said", "same", "small", "some", "still", "such", "take",
        "tell", "than", "that", "their", "them", "then", "there", "these", "they", "thing",
        "think", "this", "those", "time", "under", "used", "using", "very", "want", "water",
        "well", "went", "were", "what", "when", "where", "which", "while", "will", "with", "work",
        "working", "would", "write", "year", "your", "world", "another", "around", "available",
        "based", "begin", "best", "better", "case", "change", "check", "children", "city", "class",
        "close", "color", "common", "control", "country", "create", "current", "design", "develop",
        "development", "easy", "education", "end", "enough", "enter", "error", "event", "fact",
        "family", "field", "figure", "follow", "form", "free", "full", "general", "give", "goal",
        "government", "hard", "head", "help", "history", "hold", "house", "human", "idea",
        "important", "include", "increase", "information", "interest", "issue", "job", "key",
        "kind", "land", "language", "law", "lead", "learn", "leave", "letter", "local", "low",
        "main", "market", "matter", "member", "money", "month", "morning", "move", "music",
        "national", "natural", "near", "new", "night", "office", "old", "open", "order", "part",
        "party", "pay", "person", "phone", "plan", "play", "police", "policy", "political", "poor",
        "power", "present", "price", "problem", "process", "program", "provide", "public", "put",
        "question", "quick", "quite", "rather", "reach", "read", "ready", "real", "really",
        "reason", "receive", "record", "red", "remember", "remove", "report", "require", "rest",
        "result", "return", "rise", "road", "role", "room", "rule", "run", "school", "science",
        "season", "second", "section", "seem", "send", "sense", "series", "service", "set",
        "several", "share", "short", "show", "side", "sign", "simple", "since", "single", "site",
        "size", "social", "society", "soon", "sort", "sound", "source", "space", "speak",
        "special", "spend", "stage", "stand", "standard", "start", "state", "statement", "step",
        "stop", "store", "story", "street", "strong", "structure", "student", "study", "style",
        "subject", "success", "suggest", "support", "sure", "system", "table", "talk", "task",
        "teacher", "term", "test", "text", "thank", "thanks", "third", "those", "though",
        "thought", "thousand", "through", "today", "together", "tonight", "top", "total", "toward",
        "town", "trade", "train", "travel", "treat", "tree", "trip", "true", "truth", "try", "turn",
        "type", "understand", "until", "upon", "use", "user", "usually", "value", "various",
        "view", "voice", "wait", "walk", "wall", "watch", "way", "wear", "week", "weight", "west",
        "whether", "white", "whole", "whose", "wide", "wife", "win", "window", "wish", "within",
        "without", "woman", "women", "wonder", "word", "words", "worker", "worth", "yet", "young",
        "appro", "apple", "banana", "orange", "grape", "fruit", "vegetable", "color", "colour",
        "favorite", "favourite", "center", "centre", "organize", "organise", "analyze", "analyse",
        "hello", "world", "welcome", "please", "thank", "you", "goodbye", "morning", "afternoon",
        "evening", "today", "tomorrow", "yesterday",
    };

    public static bool IsKnown(string word)
    {
        if (string.IsNullOrEmpty(word))
            return true;

        // Numbers and tokens containing digits are not spell-checked.
        var hasLetter = false;
        foreach (var ch in word)
        {
            if (char.IsDigit(ch))
                return true;
            if (char.IsLetter(ch))
                hasLetter = true;
        }

        if (!hasLetter)
            return true;

        if (Words.Contains(word))
            return true;

        // Tolerate a trailing possessive ("Customer's" -> "customer").
        if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) && word.Length > 2)
            return Words.Contains(word[..^2]);

        return false;
    }

    // Best-effort suggestions: every dictionary word reachable from the input by a single
    // edit (insert / delete / substitute / transpose). Naive and intentionally simple.
    public static IReadOnlyList<string> Suggest(string word, int maxSuggestions = 5)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(word))
            return results;

        var lower = word.ToLowerInvariant();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in Words)
        {
            if (Math.Abs(candidate.Length - lower.Length) > 1)
                continue;
            if (IsEditDistanceOne(lower, candidate) && seen.Add(candidate))
            {
                results.Add(MatchCasing(word, candidate));
                if (results.Count >= maxSuggestions)
                    break;
            }
        }

        return results;
    }

    // True when a and b differ by at most one single-character edit (insert/delete/substitute)
    // or an adjacent transposition.
    private static bool IsEditDistanceOne(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var diff = lenA - lenB;
        if (diff is < -1 or > 1)
            return false;

        if (lenA == lenB)
        {
            // Count substitutions; allow exactly one, or one adjacent transposition.
            var firstMismatch = -1;
            var mismatches = 0;
            for (var i = 0; i < lenA; i++)
            {
                if (a[i] != b[i])
                {
                    mismatches++;
                    if (firstMismatch < 0)
                        firstMismatch = i;
                }
            }

            if (mismatches == 1)
                return true;
            if (mismatches == 2 && firstMismatch >= 0 && firstMismatch + 1 < lenA &&
                a[firstMismatch] == b[firstMismatch + 1] && a[firstMismatch + 1] == b[firstMismatch])
            {
                return true;
            }

            return false;
        }

        // Lengths differ by one: confirm the shorter is the longer with a single character removed.
        var longer = lenA > lenB ? a : b;
        var shorter = lenA > lenB ? b : a;
        var iL = 0;
        var iS = 0;
        var edits = 0;
        while (iL < longer.Length && iS < shorter.Length)
        {
            if (longer[iL] == shorter[iS])
            {
                iL++;
                iS++;
            }
            else
            {
                edits++;
                if (edits > 1)
                    return false;
                iL++;
            }
        }

        return true;
    }

    // Apply the original token's leading-capital / all-caps casing to a lowercase suggestion.
    private static string MatchCasing(string original, string lowerCandidate)
    {
        if (original.Length == 0)
            return lowerCandidate;

        var allUpper = true;
        foreach (var ch in original)
        {
            if (char.IsLetter(ch) && !char.IsUpper(ch))
            {
                allUpper = false;
                break;
            }
        }

        if (allUpper && original.Length > 1)
            return lowerCandidate.ToUpperInvariant();

        if (char.IsUpper(original[0]))
        {
            var sb = new StringBuilder(lowerCandidate);
            sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }

        return lowerCandidate;
    }
}
