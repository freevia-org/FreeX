using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentListMarkerPlan(
    ListKind Kind,
    int Level,
    string? MarkerText,
    int? NumberValue);

/// <summary>Stateful native-list marker sequencing shared by the WPF and Avalonia document renderers.</summary>
public sealed class DocumentListMarkerSequencePlanner
{
    public const int MaximumDepth = 9;

    private readonly int[] _numberCounters = new int[MaximumDepth];
    private readonly MultiLevelListMarkerState _multiLevelMarkers;

    public DocumentListMarkerSequencePlanner(
        IReadOnlyList<ListNumberFormat>? numberFormats = null,
        IReadOnlyList<string?>? levelTexts = null)
    {
        _multiLevelMarkers = new MultiLevelListMarkerState(numberFormats, levelTexts);
    }

    public DocumentListMarkerPlan Advance(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return Advance(paragraph.Formatting);
    }

    public DocumentListMarkerPlan Advance(ParagraphFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        var kind = formatting.ListKind;
        var level = Math.Clamp(formatting.ListLevel, 0, MaximumDepth - 1);
        switch (kind)
        {
            case ListKind.Number:
                _numberCounters[level] = ListRestartCounter.NextCount(_numberCounters[level], formatting.ListStartOverride);
                Array.Clear(_numberCounters, level + 1, MaximumDepth - level - 1);
                return new DocumentListMarkerPlan(
                    kind,
                    level,
                    $"{_numberCounters[level]}.",
                    _numberCounters[level]);

            case ListKind.MultiLevel:
                return new DocumentListMarkerPlan(
                    kind,
                    level,
                    _multiLevelMarkers.Advance(level, formatting.ListStartOverride),
                    NumberValue: null);

            case ListKind.Bullet:
                return new DocumentListMarkerPlan(kind, level, "\u2022", NumberValue: null);

            default:
                return new DocumentListMarkerPlan(ListKind.None, level, MarkerText: null, NumberValue: null);
        }
    }

    public void Reset()
    {
        Array.Clear(_numberCounters, 0, _numberCounters.Length);
        _multiLevelMarkers.Reset();
    }
}
