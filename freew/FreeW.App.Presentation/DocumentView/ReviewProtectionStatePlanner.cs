using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record ReviewProtectionCommandState(
    string CommandId,
    bool IsChecked,
    string StatusText);

public sealed record ReviewProtectionStatePlan(
    ReviewProtectionCommandState MarkAsFinal,
    ReviewProtectionCommandState RestrictEditing)
{
    public IReadOnlyList<ReviewProtectionCommandState> Commands { get; } =
    [
        MarkAsFinal,
        RestrictEditing
    ];
}

public static class ReviewProtectionStatePlanner
{
    public const string MarkAsFinalCommandId = "freew.mark-as-final";
    public const string RestrictEditingCommandId = "freew.restrict-editing";

    public static ReviewProtectionStatePlan Build(ProtectionSettings? protection, bool isMarkedAsFinal)
    {
        var currentProtection = protection ?? ProtectionSettings.Unprotected;
        return new ReviewProtectionStatePlan(
            new ReviewProtectionCommandState(
                MarkAsFinalCommandId,
                isMarkedAsFinal,
                isMarkedAsFinal
                    ? "Document is marked as final."
                    : "Document is not marked as final."),
            new ReviewProtectionCommandState(
                RestrictEditingCommandId,
                currentProtection.IsProtected,
                currentProtection.IsProtected
                    ? $"Editing restrictions are enforced: {currentProtection.Mode}."
                    : "Editing restrictions are not enforced."));
    }
}
