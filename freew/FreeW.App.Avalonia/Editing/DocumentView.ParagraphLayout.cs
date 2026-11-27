using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public partial class DocumentView
{
    private ParagraphFormatting ResolveBodyParagraphLayoutFormatting(int blockIndex, Paragraph paragraph)
    {
        var formatting = ResolveParagraphFmt(paragraph);
        var spacing = DocumentBodyParagraphLayoutPlanner.Spacing(_doc, blockIndex, formatting);
        return formatting with
        {
            SpaceBeforePt = spacing.BeforePt,
            SpaceAfterPt = spacing.AfterPt,
            FirstLineIndentPt = DocumentBodyParagraphLayoutPlanner.TextFirstLineIndentPt(formatting),
        };
    }
}
