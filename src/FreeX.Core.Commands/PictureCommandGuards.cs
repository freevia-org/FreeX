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
