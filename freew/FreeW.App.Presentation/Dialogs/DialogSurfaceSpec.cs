namespace FreeW.App.Presentation.Dialogs;

public readonly record struct DialogFieldSurfaceSpec<TField>(
    TField Field,
    string Label,
    string AutomationId,
    string AutomationName)
    where TField : struct, Enum;

public sealed record DialogSurfaceSpec<TField>(
    string Title,
    string AutomationId,
    string AutomationName,
    IReadOnlyList<DialogFieldSurfaceSpec<TField>> Fields,
    string SupportingText = "",
    string? ValidationAutomationId = null)
    where TField : struct, Enum
{
    public DialogFieldSurfaceSpec<TField> Field(TField field) =>
        Fields.First(spec => EqualityComparer<TField>.Default.Equals(spec.Field, field));
}
