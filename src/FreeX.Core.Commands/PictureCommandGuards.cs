using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PictureCommandGuards
{
    private const string InvalidPictureSizeMessage = "Picture size must be positive.";
    private const string PictureNotFoundMessage = "Picture was not found.";
    private const string PictureAnchorOnTargetSheetMessage = "Picture anchor must be on the target sheet.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: same sheet-level "Edit objects" protection check as
    /// <see cref="RejectIfEditObjectsBlocked(Sheet)"/>, but layers in the per-picture
    /// <see cref="PictureModel.Locked"/> flag -- mirrors
    /// <see cref="DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(Sheet, DrawingShapeModel)"/>: an
    /// author-unlocked picture (<c>Locked == false</c>) stays movable/resizable even while the sheet is
    /// protected with "Edit objects" blocked, matching Excel's per-object Locked checkbox. A locked
    /// picture (the default) is rejected exactly like the sheet-only overload.
    /// </summary>
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet, PictureModel picture) =>
        picture.Locked ? RejectIfEditObjectsBlocked(sheet) : null;

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidPictureSizeMessage);

    public static CommandOutcome PictureAnchorOnTargetSheet() =>
        new(false, PictureAnchorOnTargetSheetMessage);

    public static bool TryFindPicture(
        Sheet sheet,
        Guid pictureId,
        [NotNullWhen(true)] out PictureModel? picture)
    {
        foreach (var item in sheet.Pictures)
        {
            if (item.Id != pictureId)
                continue;

            picture = item;
            return true;
        }

        picture = null;
        return false;
    }

    public static CommandOutcome PictureNotFound() =>
        new(false, PictureNotFoundMessage);
}
