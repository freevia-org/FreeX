using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum NamedStyleApplicationKind
{
    Paragraph,
    Character,
}

public sealed record NamedStyleApplicationPlan(
    string RequestedStyleId,
    DocumentStyle EffectiveStyle,
    NamedStyleApplicationKind Kind);

/// <summary>
/// Resolves Word's linked paragraph/character style behavior before either host mutates its editor model.
/// A paragraph style applies its linked character style only when actual text is selected; a caret-only
/// application remains paragraph-level.
/// </summary>
public static class NamedStyleApplicationPlanner
{
    public static NamedStyleApplicationPlan? Resolve(
        TextDocument document,
        string requestedStyleId,
        bool hasTextSelection)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(requestedStyleId)
            || !document.Styles.TryGetValue(requestedStyleId, out var requested))
        {
            return null;
        }

        if (requested.Type == StyleType.Paragraph
            && hasTextSelection
            && requested.LinkedStyleId is { Length: > 0 } linkedStyleId
            && document.Styles.TryGetValue(linkedStyleId, out var linked)
            && linked.Type == StyleType.Character)
        {
            return new NamedStyleApplicationPlan(
                requestedStyleId,
                linked,
                NamedStyleApplicationKind.Character);
        }

        return new NamedStyleApplicationPlan(
            requestedStyleId,
            requested,
            requested.Type == StyleType.Character
                ? NamedStyleApplicationKind.Character
                : NamedStyleApplicationKind.Paragraph);
    }

    /// <summary>
    /// Applies the fields represented by FreeW's character-style model without clearing direct formatting
    /// that the style leaves unspecified.
    /// </summary>
    public static RunFormatting OverlayCharacterStyle(RunFormatting baseRun, RunFormatting styleRun) => baseRun with
    {
        Bold = baseRun.Bold || styleRun.Bold,
        Italic = baseRun.Italic || styleRun.Italic,
        Underline = baseRun.Underline || styleRun.Underline,
        Strikethrough = baseRun.Strikethrough || styleRun.Strikethrough,
        DoubleStrikethrough = baseRun.DoubleStrikethrough || styleRun.DoubleStrikethrough,
        Hidden = baseRun.Hidden || styleRun.Hidden,
        WebHidden = baseRun.WebHidden || styleRun.WebHidden,
        NoProof = baseRun.NoProof || styleRun.NoProof,
        SmallCaps = baseRun.SmallCaps || styleRun.SmallCaps,
        AllCaps = baseRun.AllCaps || styleRun.AllCaps,
        FontFamily = styleRun.FontFamily ?? baseRun.FontFamily,
        FontSizePt = styleRun.FontSizePt ?? baseRun.FontSizePt,
        ColorHex = styleRun.ColorHex ?? baseRun.ColorHex,
    };
}
