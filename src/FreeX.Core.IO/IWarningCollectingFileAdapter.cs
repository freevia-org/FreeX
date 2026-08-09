namespace FreeX.Core.IO;

/// <summary>
/// Implemented by <see cref="IFileAdapter"/>s whose save pipeline can drop individual, non-fatal
/// items (a comment, a hyperlink, a merged region, a named range, a data-validation rule, ...)
/// while still writing the rest of the file -- and that can report which items were dropped.
/// <see cref="FreeX.App.Services.WorkbookSaveService"/> checks for this interface (rather than any
/// concrete adapter type) so every format built on top of the same warning-collecting save pipeline
/// -- currently <see cref="XlsxFileAdapter"/> plus every adapter that composes it internally
/// (<see cref="XlsmFileAdapter"/>, <see cref="XltmFileAdapter"/>, <see cref="XltxFileAdapter"/>) --
/// automatically surfaces the same "file saved with warnings" outcome to the user, instead of only
/// the one concrete type happening to be checked at the call site.
/// </summary>
public interface IWarningCollectingFileAdapter
{
    /// <summary>
    /// Saves a workbook to the given stream and returns any non-fatal warnings collected during
    /// the save. The file is always written; warnings indicate partial data loss.
    /// </summary>
    XlsxSaveResult SaveWithWarnings(FreeX.Core.Model.Workbook workbook, System.IO.Stream stream);
}
