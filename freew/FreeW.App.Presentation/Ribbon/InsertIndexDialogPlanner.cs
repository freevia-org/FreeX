namespace FreeW.App.Presentation.Ribbon;

public sealed record InsertIndexDialogState(string Identifier);

public sealed record InsertIndexDialogResult(string? Identifier);

public static class InsertIndexDialogPlanner
{
    public const string Title = "Insert Index";
    public const string UpdateTitle = "Update Index";
    public const string IdentifierLabel = "Index identifier (optional):";
    public const string IdentifierHint = "Leave blank to build the default index.";
    public const string InsertButtonLabel = "Insert";
    public const string UpdateButtonLabel = "Update";
    public const double DialogWidth = 420;

    public static InsertIndexDialogState BuildInitialState(string? identifier = null) =>
        new(identifier?.Trim() ?? string.Empty);

    public static InsertIndexDialogResult BuildResult(InsertIndexDialogState state)
    {
        var identifier = state.Identifier.Trim();
        return new(identifier.Length == 0 ? null : identifier);
    }
}
