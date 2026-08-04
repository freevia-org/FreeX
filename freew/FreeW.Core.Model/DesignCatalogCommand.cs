namespace FreeW.Core.Model;

/// <summary>
/// Reversible document-wide Design catalog mutation. The supplied model operation may update the
/// document defaults, theme, and built-in styles; this command snapshots and restores those shared
/// owners so WPF and Avalonia expose the same single-step Undo behavior.
/// </summary>
public sealed class DesignCatalogCommand(string label, Action<TextDocument> apply) : IDocumentCommand
{
    private static readonly string[] AffectedStyleIds =
        ["Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote"];

    private RunFormatting? _defaultRun;
    private ParagraphFormatting? _defaultParagraph;
    private DocumentTheme? _theme;
    private (RunFormatting Run, ParagraphFormatting Paragraph)?[]? _styleSnapshots;

    public string Label => label;

    public void Apply(IDocumentCommandContext context)
    {
        var document = context.Document;
        if (_defaultRun is null)
        {
            _defaultRun = document.DefaultRun;
            _defaultParagraph = document.DefaultParagraph;
            _theme = document.Theme;
            _styleSnapshots = new (RunFormatting, ParagraphFormatting)?[AffectedStyleIds.Length];
            for (var index = 0; index < AffectedStyleIds.Length; index++)
            {
                if (document.Styles.TryGetValue(AffectedStyleIds[index], out var style))
                    _styleSnapshots[index] = (style.Run, style.Paragraph);
            }
        }

        apply(document);
    }

    public void Revert(IDocumentCommandContext context)
    {
        var document = context.Document;
        if (_defaultRun is null || _styleSnapshots is null)
            return;

        document.DefaultRun = _defaultRun;
        document.DefaultParagraph = _defaultParagraph!;
        document.Theme = _theme!;
        for (var index = 0; index < AffectedStyleIds.Length; index++)
        {
            if (_styleSnapshots[index] is { } snapshot
                && document.Styles.TryGetValue(AffectedStyleIds[index], out var style))
            {
                style.Run = snapshot.Run;
                style.Paragraph = snapshot.Paragraph;
            }
        }
    }
}
