using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SetHeaderFooterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetHeaderFooter _header;
    private readonly WorksheetHeaderFooter _footer;
    private readonly WorksheetHeaderFooter _firstPageHeader;
    private readonly WorksheetHeaderFooter _firstPageFooter;
    private readonly WorksheetHeaderFooter _evenPageHeader;
    private readonly WorksheetHeaderFooter _evenPageFooter;
    private readonly WorksheetHeaderFooterPictureSet? _headerPictures;
    private readonly WorksheetHeaderFooterPictureSet? _footerPictures;
    private readonly WorksheetHeaderFooterPictureSet? _firstPageHeaderPictures;
    private readonly WorksheetHeaderFooterPictureSet? _firstPageFooterPictures;
    private readonly WorksheetHeaderFooterPictureSet? _evenPageHeaderPictures;
    private readonly WorksheetHeaderFooterPictureSet? _evenPageFooterPictures;
    private readonly bool _differentFirstPage;
    private readonly bool _differentOddEvenPages;
    private readonly bool _scaleWithDocument;
    private readonly bool _alignWithMargins;
    private WorksheetHeaderFooter _previousHeader;
    private WorksheetHeaderFooter _previousFooter;
    private WorksheetHeaderFooter _previousFirstPageHeader;
    private WorksheetHeaderFooter _previousFirstPageFooter;
    private WorksheetHeaderFooter _previousEvenPageHeader;
    private WorksheetHeaderFooter _previousEvenPageFooter;
    private WorksheetHeaderFooterPictureSet _previousHeaderPictures;
    private WorksheetHeaderFooterPictureSet _previousFooterPictures;
    private WorksheetHeaderFooterPictureSet _previousFirstPageHeaderPictures;
    private WorksheetHeaderFooterPictureSet _previousFirstPageFooterPictures;
    private WorksheetHeaderFooterPictureSet _previousEvenPageHeaderPictures;
    private WorksheetHeaderFooterPictureSet _previousEvenPageFooterPictures;
    private bool _previousDifferentFirstPage;
    private bool _previousDifferentOddEvenPages;
    private bool _previousScaleWithDocument;
    private bool _previousAlignWithMargins;
    private bool _applied;

    public string Label => "Header & Footer";

    public SetHeaderFooterCommand(
        SheetId sheetId,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        WorksheetHeaderFooter? firstPageHeader = null,
        WorksheetHeaderFooter? firstPageFooter = null,
        WorksheetHeaderFooter? evenPageHeader = null,
        WorksheetHeaderFooter? evenPageFooter = null,
        bool differentFirstPage = false,
        bool differentOddEvenPages = false,
        bool scaleWithDocument = true,
        bool alignWithMargins = true,
        WorksheetHeaderFooterPictureSet? headerPictures = null,
        WorksheetHeaderFooterPictureSet? footerPictures = null,
        WorksheetHeaderFooterPictureSet? firstPageHeaderPictures = null,
        WorksheetHeaderFooterPictureSet? firstPageFooterPictures = null,
        WorksheetHeaderFooterPictureSet? evenPageHeaderPictures = null,
        WorksheetHeaderFooterPictureSet? evenPageFooterPictures = null)
    {
        _sheetId = sheetId;
        _header = header;
        _footer = footer;
        _firstPageHeader = firstPageHeader ?? new WorksheetHeaderFooter("", "", "");
        _firstPageFooter = firstPageFooter ?? new WorksheetHeaderFooter("", "", "");
        _evenPageHeader = evenPageHeader ?? new WorksheetHeaderFooter("", "", "");
        _evenPageFooter = evenPageFooter ?? new WorksheetHeaderFooter("", "", "");
        _headerPictures = headerPictures?.DeepClone();
        _footerPictures = footerPictures?.DeepClone();
        _firstPageHeaderPictures = firstPageHeaderPictures?.DeepClone();
        _firstPageFooterPictures = firstPageFooterPictures?.DeepClone();
        _evenPageHeaderPictures = evenPageHeaderPictures?.DeepClone();
        _evenPageFooterPictures = evenPageFooterPictures?.DeepClone();
        _differentFirstPage = differentFirstPage;
        _differentOddEvenPages = differentOddEvenPages;
        _scaleWithDocument = scaleWithDocument;
        _alignWithMargins = alignWithMargins;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousHeader = sheet.PageHeader;
        _previousFooter = sheet.PageFooter;
        _previousFirstPageHeader = sheet.FirstPageHeader;
        _previousFirstPageFooter = sheet.FirstPageFooter;
        _previousEvenPageHeader = sheet.EvenPageHeader;
        _previousEvenPageFooter = sheet.EvenPageFooter;
        _previousHeaderPictures = sheet.PageHeaderPictures.DeepClone();
        _previousFooterPictures = sheet.PageFooterPictures.DeepClone();
        _previousFirstPageHeaderPictures = sheet.FirstPageHeaderPictures.DeepClone();
        _previousFirstPageFooterPictures = sheet.FirstPageFooterPictures.DeepClone();
        _previousEvenPageHeaderPictures = sheet.EvenPageHeaderPictures.DeepClone();
        _previousEvenPageFooterPictures = sheet.EvenPageFooterPictures.DeepClone();
        _previousDifferentFirstPage = sheet.DifferentFirstPageHeaderFooter;
        _previousDifferentOddEvenPages = sheet.DifferentOddEvenHeaderFooter;
        _previousScaleWithDocument = sheet.HeaderFooterScaleWithDocument;
        _previousAlignWithMargins = sheet.HeaderFooterAlignWithMargins;
        sheet.PageHeader = _header;
        sheet.PageFooter = _footer;
        sheet.FirstPageHeader = _firstPageHeader;
        sheet.FirstPageFooter = _firstPageFooter;
        sheet.EvenPageHeader = _evenPageHeader;
        sheet.EvenPageFooter = _evenPageFooter;
        if (_headerPictures is { } headerPictures)
            sheet.PageHeaderPictures = headerPictures.DeepClone();
        if (_footerPictures is { } footerPictures)
            sheet.PageFooterPictures = footerPictures.DeepClone();
        if (_firstPageHeaderPictures is { } firstPageHeaderPictures)
            sheet.FirstPageHeaderPictures = firstPageHeaderPictures.DeepClone();
        if (_firstPageFooterPictures is { } firstPageFooterPictures)
            sheet.FirstPageFooterPictures = firstPageFooterPictures.DeepClone();
        if (_evenPageHeaderPictures is { } evenPageHeaderPictures)
            sheet.EvenPageHeaderPictures = evenPageHeaderPictures.DeepClone();
        if (_evenPageFooterPictures is { } evenPageFooterPictures)
            sheet.EvenPageFooterPictures = evenPageFooterPictures.DeepClone();
        sheet.DifferentFirstPageHeaderFooter = _differentFirstPage;
        sheet.DifferentOddEvenHeaderFooter = _differentOddEvenPages;
        sheet.HeaderFooterScaleWithDocument = _scaleWithDocument;
        sheet.HeaderFooterAlignWithMargins = _alignWithMargins;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PageHeader = _previousHeader;
        sheet.PageFooter = _previousFooter;
        sheet.FirstPageHeader = _previousFirstPageHeader;
        sheet.FirstPageFooter = _previousFirstPageFooter;
        sheet.EvenPageHeader = _previousEvenPageHeader;
        sheet.EvenPageFooter = _previousEvenPageFooter;
        sheet.PageHeaderPictures = _previousHeaderPictures.DeepClone();
        sheet.PageFooterPictures = _previousFooterPictures.DeepClone();
        sheet.FirstPageHeaderPictures = _previousFirstPageHeaderPictures.DeepClone();
        sheet.FirstPageFooterPictures = _previousFirstPageFooterPictures.DeepClone();
        sheet.EvenPageHeaderPictures = _previousEvenPageHeaderPictures.DeepClone();
        sheet.EvenPageFooterPictures = _previousEvenPageFooterPictures.DeepClone();
        sheet.DifferentFirstPageHeaderFooter = _previousDifferentFirstPage;
        sheet.DifferentOddEvenHeaderFooter = _previousDifferentOddEvenPages;
        sheet.HeaderFooterScaleWithDocument = _previousScaleWithDocument;
        sheet.HeaderFooterAlignWithMargins = _previousAlignWithMargins;
        _applied = false;
    }
}
