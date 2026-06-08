namespace FreeX.Core.Commands;

internal static class PictureCommandGuards
{
    private const string InvalidPictureSizeMessage = "Picture size must be positive.";

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidPictureSizeMessage);
}
