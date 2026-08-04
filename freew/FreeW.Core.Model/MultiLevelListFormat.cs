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

    public IReadOnlyList<ListNumberFormat> NumberFormats => _numberFormats;

    public ListNumberFormat GetNumberFormat(int level) =>
        _numberFormats[Math.Clamp(level, 0, LevelCount - 1)];

    public void SetNumberFormat(int level, ListNumberFormat format) =>
        _numberFormats[Math.Clamp(level, 0, LevelCount - 1)] = format;

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
        IReadOnlyList<ListNumberFormat>? numberFormats = null)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var state = new MultiLevelListMarkerState(numberFormats);
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
    private readonly int[] _counters = new int[MultiLevelListFormat.LevelCount];

    public MultiLevelListMarkerState(IReadOnlyList<ListNumberFormat>? numberFormats = null)
    {
        _numberFormats = numberFormats;
    }

    public string Advance(int rawLevel, int? startAt = null)
    {
        var level = Math.Clamp(rawLevel, 0, MultiLevelListFormat.LevelCount - 1);
        _counters[level] = startAt.HasValue ? Math.Max(1, startAt.Value) : _counters[level] + 1;

        for (var deeper = level + 1; deeper < MultiLevelListFormat.LevelCount; deeper++)
            _counters[deeper] = 0;

        var builder = new System.Text.StringBuilder();
        for (var ancestor = 0; ancestor <= level; ancestor++)
        {
            var value = _counters[ancestor] == 0 ? 1 : _counters[ancestor];
            var format = ancestor < (_numberFormats?.Count ?? 0)
                ? _numberFormats![ancestor]
                : ListNumberFormat.Decimal;
            builder.Append(MultiLevelListMarkerFormatter.FormatNumber(value, format)).Append('.');
        }
        return builder.ToString();
    }

    public void Reset() => Array.Clear(_counters, 0, _counters.Length);
}
