namespace FreeX.Core.IO;

/// <summary>
/// r292: implemented by <see cref="IFileAdapter"/>s whose on-disk format can represent only ONE
/// worksheet, so saving a multi-sheet workbook to them keeps the first sheet and discards the rest.
///
/// <para>The loss is inherent to the format rather than a defect -- CSV has nowhere to put a second
/// sheet -- but Excel warns before doing it and, until r292, FreeX did not: the other sheets simply
/// were not in the file the next time it was opened.</para>
///
/// <para>Declared as a capability rather than a list of extensions for the same reason
/// <see cref="IWarningCollectingFileAdapter"/> is: <see cref="FreeX.App.Services.WorkbookSaveService"/>
/// asks the adapter what it can do, so a new single-sheet format surfaces the warning by
/// implementing this, without every call site learning its name. A contract test asserts the marker
/// agrees with what the adapters actually do, so the declaration cannot drift from the behaviour.</para>
/// </summary>
public interface ISingleSheetFileAdapter
{
}
