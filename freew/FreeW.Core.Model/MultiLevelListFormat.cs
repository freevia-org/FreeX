namespace FreeW.Core.Model;

/// <summary>
/// Number style for a modelled FreeW multilevel-list level.
/// </summary>
public enum ListNumberFormat
{
    Decimal,
    LowerLetter,
    UpperLetter,
    LowerRoman,
    UpperRoman
}

/// <summary>
/// The single modelled FreeW multilevel-list definition. FreeW currently stores one outline definition
/// per document, matching the fixed multilevel numbering definition emitted to DOCX.
/// </summary>
public sealed class MultiLevelListFormat
{
    public const int LevelCount = 9;

    private readonly List<ListNumberFormat> _numberFormats =
        Enumerable.Repeat(ListNumberFormat.Decimal, LevelCount).ToList();

    // Per-level DOCX w:lvlText pattern (e.g. "%1)" or "%1.%2:"), null when the level has no captured
    // pattern of its own and falls back to the default dotted "%1." / "%1.%2." / ... outline text FreeW
    // has always rendered (its own "Define new Multilevel list" styles, which only ever specify
    // NumberFormats, rely on this default).
    private readonly List<string?> _levelTexts =
        Enumerable.Repeat((string?)null, LevelCount).ToList();

    public IReadOnlyList<ListNumberFormat> NumberFormats => _numberFormats;

    /// <summary>
    /// Per-level lvlText pattern captured from a DOCX abstractNum level, or null for a level that uses
    /// the default dotted outline pattern. See <see cref="MultiLevelListMarkerState"/> for how a pattern's
    /// %1..%9 placeholders and literal text are rendered into the on-screen marker.
    /// </summary>
    public IReadOnlyList<string?> LevelTexts => _levelTexts;

    public ListNumberFormat GetNumberFormat(int level) =>
        _numberFormats[Math.Clamp(level, 0, LevelCount - 1)];

    public void SetNumberFormat(int level, ListNumberFormat format) =>
        _numberFormats[Math.Clamp(level, 0, LevelCount - 1)] = format;

    public string? GetLevelText(int level) => _levelTexts[Math.Clamp(level, 0, LevelCount - 1)];

    public void SetLevelText(int level, string? lvlText) =>
        _levelTexts[Math.Clamp(level, 0, LevelCount - 1)] = lvlText;

    public void SetNumberFormats(IEnumerable<ListNumberFormat> numberFormats)
    {
        ArgumentNullException.ThrowIfNull(numberFormats);

        var index = 0;
        foreach (var format in numberFormats)
        {
            if (index >= LevelCount)
                break;
            _numberFormats[index++] = format;
        }

        for (; index < LevelCount; index++)
            _numberFormats[index] = ListNumberFormat.Decimal;
    }

    /// <summary>
    /// Replaces the per-level lvlText patterns. A null entry (including every entry, when
    /// <paramref name="levelTexts"/> is empty/all-null) leaves that level on the default dotted outline
    /// pattern.
    /// </summary>
    public void SetLevelTexts(IEnumerable<string?> levelTexts)
    {
        ArgumentNullException.ThrowIfNull(levelTexts);

        var index = 0;
        foreach (var text in levelTexts)
        {
            if (index >= LevelCount)
                break;
            _levelTexts[index++] = text;
        }

        for (; index < LevelCount; index++)
            _levelTexts[index] = null;
    }

    public static IReadOnlyList<ListNumberFormat> DecimalNumberFormats { get; } =
        Enumerable.Repeat(ListNumberFormat.Decimal, LevelCount).ToArray();

    public static IReadOnlyList<ListNumberFormat> DecimalLowerLetterLowerRomanNumberFormats { get; } =
    [
        ListNumberFormat.Decimal,
        ListNumberFormat.LowerLetter,
        ListNumberFormat.LowerRoman,
        ListNumberFormat.Decimal,
        ListNumberFormat.Decimal,
        ListNumberFormat.Decimal,
        ListNumberFormat.Decimal,
        ListNumberFormat.Decimal,
        ListNumberFormat.Decimal
    ];
}

/// <summary>Replaces the document's multilevel number formats as one reversible formatting edit.</summary>
public sealed class SetMultiLevelNumberFormatsCommand(IEnumerable<ListNumberFormat> formats) : IDocumentCommand
{
    private readonly ListNumberFormat[] _formats = [.. formats];
    private ListNumberFormat[]? _previous;

    public string Label => "Define Multilevel List";
    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyFormatting;

    public void Apply(IDocumentCommandContext context)
    {
        _previous = [.. context.Document.MultiLevelList.NumberFormats];
        context.Document.MultiLevelList.SetNumberFormats(_formats);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;

        context.Document.MultiLevelList.SetNumberFormats(_previous);
        _previous = null;
    }
}

/// <summary>
/// Shared formatter for the accumulated markers rendered by WPF/Avalonia and described by DOCX lvlText.
/// </summary>
public static class MultiLevelListMarkerFormatter
{
    public static IReadOnlyList<string> MarkerSequence(
        IEnumerable<int> levels,
        IReadOnlyList<ListNumberFormat>? numberFormats = null,
        IReadOnlyList<string?>? levelTexts = null)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var state = new MultiLevelListMarkerState(numberFormats, levelTexts);
        var markers = new List<string>();
        foreach (var level in levels)
            markers.Add(state.Advance(level));
        return markers;
    }

    public static string FormatNumber(int value, ListNumberFormat format)
    {
        if (value < 1)
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return format switch
        {
            ListNumberFormat.LowerLetter => FormatLetters(value, upper: false),
            ListNumberFormat.UpperLetter => FormatLetters(value, upper: true),
            ListNumberFormat.LowerRoman => FormatRoman(value, upper: false),
            ListNumberFormat.UpperRoman => FormatRoman(value, upper: true),
            _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static string ToOoxmlToken(ListNumberFormat format) => format switch
    {
        ListNumberFormat.LowerLetter => "lowerLetter",
        ListNumberFormat.UpperLetter => "upperLetter",
        ListNumberFormat.LowerRoman => "lowerRoman",
        ListNumberFormat.UpperRoman => "upperRoman",
        _ => "decimal"
    };

    /// <summary>
    /// Formats a single flat (non-multilevel) <see cref="ListKind.Number"/> marker from its captured
    /// <c>w:lvlText</c> pattern: each "%N" token is substituted with <paramref name="value"/> formatted per
    /// <paramref name="format"/> (a flat list has only one counter, so every placeholder — however numbered
    /// — resolves to the same running value); every other character, including any literal separator/prefix/
    /// suffix the source list actually used (")", ":", "(", …), is copied through unchanged. A null
    /// <paramref name="pattern"/> (no captured lvlText — true for every FreeW-authored numbered list) falls
    /// back to the classic "N." shape this class has always produced.
    /// </summary>
    public static string FormatSingleLevelMarker(string? pattern, int value, ListNumberFormat format)
    {
        var text = pattern ?? "%1.";
        var formattedValue = FormatNumber(value, format);
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '%' && i + 1 < text.Length && text[i + 1] is >= '1' and <= '9')
            {
                builder.Append(formattedValue);
                i++;
            }
            else
            {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }

    public static ListNumberFormat FromOoxmlToken(string? token) => token switch
    {
        "lowerLetter" => ListNumberFormat.LowerLetter,
        "upperLetter" => ListNumberFormat.UpperLetter,
        "lowerRoman" => ListNumberFormat.LowerRoman,
        "upperRoman" => ListNumberFormat.UpperRoman,
        _ => ListNumberFormat.Decimal
    };

    private static string FormatLetters(int value, bool upper)
    {
        var chars = new Stack<char>();
        var n = value;
        while (n > 0)
        {
            n--;
            chars.Push((char)((upper ? 'A' : 'a') + (n % 26)));
            n /= 26;
        }
        return new string(chars.ToArray());
    }

    private static string FormatRoman(int value, bool upper)
    {
        (int Value, string Numeral)[] numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];

        var builder = new System.Text.StringBuilder();
        var remaining = value;
        foreach (var (amount, numeral) in numerals)
        {
            while (remaining >= amount)
            {
                builder.Append(numeral);
                remaining -= amount;
            }
        }

        var result = builder.ToString();
        return upper ? result : result.ToLowerInvariant();
    }
}

public sealed class MultiLevelListMarkerState
{
    private readonly IReadOnlyList<ListNumberFormat>? _numberFormats;
    private readonly IReadOnlyList<string?>? _levelTexts;
    private readonly int[] _counters = new int[MultiLevelListFormat.LevelCount];

    public MultiLevelListMarkerState(
        IReadOnlyList<ListNumberFormat>? numberFormats = null,
        IReadOnlyList<string?>? levelTexts = null)
    {
        _numberFormats = numberFormats;
        _levelTexts = levelTexts;
    }

    public string Advance(int rawLevel, int? startAt = null)
    {
        var level = Math.Clamp(rawLevel, 0, MultiLevelListFormat.LevelCount - 1);
        _counters[level] = startAt.HasValue ? Math.Max(1, startAt.Value) : _counters[level] + 1;

        for (var deeper = level + 1; deeper < MultiLevelListFormat.LevelCount; deeper++)
            _counters[deeper] = 0;

        var pattern = level < (_levelTexts?.Count ?? 0) ? _levelTexts![level] : null;
        return pattern is not null ? FormatFromLevelText(pattern, level) : FormatDefaultDottedOutline(level);
    }

    /// <summary>
    /// FreeW's own "Define new Multilevel list" styles (and any level whose source lvlText was not
    /// captured) never had a real DOCX lvlText to honour -- they only ever specify a
    /// <see cref="ListNumberFormat"/> per level -- so they keep rendering the classic accumulated
    /// "1.", "1.1.", "1.1.1." dotted outline this class has always produced.
    /// </summary>
    private string FormatDefaultDottedOutline(int level)
    {
        var builder = new System.Text.StringBuilder();
        for (var ancestor = 0; ancestor <= level; ancestor++)
            builder.Append(FormatAncestorValue(ancestor)).Append('.');
        return builder.ToString();
    }

    /// <summary>
    /// Renders a captured DOCX <c>w:lvlText</c> pattern for <paramref name="level"/>: each "%N" token
    /// (N = 1..9) is substituted with the formatted running value of ancestor level N-1 (clamped to
    /// <paramref name="level"/> itself for a malformed pattern that references a deeper level); every
    /// other character -- including any separator/prefix/suffix the source list actually used, e.g.
    /// ")", ":", "Section " -- is copied through literally instead of the hardcoded "." this class used
    /// to force onto every level.
    /// </summary>
    private string FormatFromLevelText(string pattern, int level)
    {
        var builder = new System.Text.StringBuilder(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '%' && i + 1 < pattern.Length && pattern[i + 1] is >= '1' and <= '9')
            {
                var placeholderLevel = Math.Clamp(pattern[i + 1] - '1', 0, level);
                builder.Append(FormatAncestorValue(placeholderLevel));
                i++;
            }
            else
            {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }

    private string FormatAncestorValue(int ancestor)
    {
        var value = _counters[ancestor] == 0 ? 1 : _counters[ancestor];
        var format = ancestor < (_numberFormats?.Count ?? 0)
            ? _numberFormats![ancestor]
            : ListNumberFormat.Decimal;
        return MultiLevelListMarkerFormatter.FormatNumber(value, format);
    }

    public void Reset() => Array.Clear(_counters, 0, _counters.Length);
}
