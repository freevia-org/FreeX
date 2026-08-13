using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum FreeWParagraphValueKind
{
    IndentLeft,
    IndentRight,
    SpaceBefore,
    SpaceAfter,
}

public sealed record FreeWRibbonFormattingPorts(
    Func<ParagraphFormatting> GetCurrentParagraph,
    Action<double> ApplyIndentLeft,
    Action<double> ApplyIndentRight,
    Action<double> ApplySpaceBefore,
    Action<double> ApplySpaceAfter,
    Func<TextDocument> GetDocument,
    Func<string?> GetCurrentParagraphStyleId,
    Action<string> ApplyParagraphStyle,
    Action<DocumentTheme> ApplyTheme,
    Action<DocumentStyleSet> ApplyStyleSet);

/// <summary>
/// Owns renderer-neutral parsing, catalog resolution, and state projection for FreeW's
/// value-backed paragraph and document-formatting ribbon commands.
/// </summary>
public sealed class FreeWRibbonFormattingSession
{
    private readonly FreeWRibbonFormattingPorts _ports;

    public FreeWRibbonFormattingSession(FreeWRibbonFormattingPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        _ports = ports;
    }

    public bool ApplyParagraphValue(FreeWParagraphValueKind kind, string? rawValue)
    {
        if (!TryParseNonNegativePoints(rawValue, out var points))
            return false;

        ApplyParagraphValue(kind, points);
        return true;
    }

    public string CurrentParagraphValue(FreeWParagraphValueKind kind)
    {
        var paragraph = _ports.GetCurrentParagraph();
        var value = kind switch
        {
            FreeWParagraphValueKind.IndentLeft => paragraph.IndentLeftPt,
            FreeWParagraphValueKind.IndentRight => paragraph.IndentRightPt,
            FreeWParagraphValueKind.SpaceBefore => paragraph.SpaceBeforePt,
            FreeWParagraphValueKind.SpaceAfter => paragraph.SpaceAfterPt,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return FormatPoints(value);
    }

    public bool ApplyParagraphStyle(string? choice)
    {
        var styleId = ResolveParagraphStyleId(_ports.GetDocument(), choice);
        if (styleId is null)
            return false;

        _ports.ApplyParagraphStyle(styleId);
        return true;
    }

    public string CurrentParagraphStyleName() =>
        ResolveParagraphStyleName(_ports.GetDocument(), _ports.GetCurrentParagraphStyleId());

    public bool ApplyTheme(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice) || DocumentTheme.FindByName(choice) is not { } theme)
            return false;

        _ports.ApplyTheme(theme);
        return true;
    }

    public string CurrentThemeName() => _ports.GetDocument().Theme.Name;

    public bool ApplyStyleSet(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice) || DocumentStyleSet.FindByName(choice) is not { } styleSet)
            return false;

        _ports.ApplyStyleSet(styleSet);
        return true;
    }

    public string? CurrentStyleSetName() => DocumentStyleSet.FindMatching(_ports.GetDocument())?.Name;

    public static bool TryParseNonNegativePoints(string? rawValue, out double points) =>
        double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out points)
        && points >= 0;

    public static string FormatPoints(double points) =>
        FreeWRibbonNumericValueParser.FormatInvariant(points);

    public static string? ResolveParagraphStyleId(TextDocument document, string? choice)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(choice))
            return null;
        if (document.Styles.ContainsKey(choice))
            return choice;

        foreach (var style in document.Styles.Values)
        {
            if (string.Equals(style.Name, choice, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Compact(style.Id), Compact(choice), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Compact(style.Name), Compact(choice), StringComparison.OrdinalIgnoreCase))
            {
                return style.Id;
            }
        }

        return null;
    }

    public static string ResolveParagraphStyleName(TextDocument document, string? styleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(styleId))
            return "Normal";
        if (BuiltInStyles.Find(styleId) is { } builtIn)
            return builtIn.Name;
        return document.Styles.TryGetValue(styleId, out var style) ? style.Name : styleId;
    }

    private void ApplyParagraphValue(FreeWParagraphValueKind kind, double points)
    {
        switch (kind)
        {
            case FreeWParagraphValueKind.IndentLeft:
                _ports.ApplyIndentLeft(points);
                break;
            case FreeWParagraphValueKind.IndentRight:
                _ports.ApplyIndentRight(points);
                break;
            case FreeWParagraphValueKind.SpaceBefore:
                _ports.ApplySpaceBefore(points);
                break;
            case FreeWParagraphValueKind.SpaceAfter:
                _ports.ApplySpaceAfter(points);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static string Compact(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);
}
