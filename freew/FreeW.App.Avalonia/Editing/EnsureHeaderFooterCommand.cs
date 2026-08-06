using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Creates the document header or footer region when it is missing or empty, seeding it with one
/// paragraph. The Avalonia editor retains the native caret and projection workflow around this command.
/// </summary>
internal sealed class EnsureHeaderFooterCommand(bool isFooter) : IDocumentCommand
{
    private HeaderFooter? _previous;
    private bool _applied;

    public string Label => isFooter ? "Insert Footer" : "Insert Header";

    public void Apply(IDocumentCommandContext context)
    {
        var document = context.Document;
        _previous = isFooter ? document.Footer : document.Header;
        if (_previous is { IsEmpty: false })
        {
            _applied = false;
            return;
        }

        var region = new HeaderFooter();
        region.Paragraphs.Add(new Paragraph());
        if (isFooter)
            document.Footer = region;
        else
            document.Header = region;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        if (isFooter)
            context.Document.Footer = _previous;
        else
            context.Document.Header = _previous;
        _applied = false;
    }
}
