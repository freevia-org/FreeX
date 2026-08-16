using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns the renderer-neutral decision for Word's omitted application-default
/// paragraph line spacing. Native renderers retain their own font-metric scale.
/// </summary>
public static class ImportedWordLineSpacingPlanner
{
    public static bool UsesApplicationDefaultLineSpacing(
        TextDocument document,
        ParagraphFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.UseWordApplicationDefaultLineSpacing &&
               !formatting.LineSpacingIsSet &&
               formatting.LineRule == LineSpacingRule.Multiple &&
               Math.Abs(formatting.LineSpacing - ParagraphFormatting.Default.LineSpacing) <= 0.0001;
    }

    public static bool UsesApplicationDefaultRunLineHeightCalibration(
        TextDocument document,
        ParagraphFormatting formatting)
    {
        return document.UseWordApplicationDefaultRunFormatting &&
               UsesApplicationDefaultLineSpacing(document, formatting);
    }
}
