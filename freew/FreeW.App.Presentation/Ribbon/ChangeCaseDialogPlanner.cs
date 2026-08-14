using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ChangeCaseDialogChoice(string Label, CaseKind Kind);

/// <summary>
/// Canonical Change Case surface shared by WPF and Avalonia. Renderers own only the native picker;
/// the available operations and their ordering live here.
/// </summary>
public static class ChangeCaseDialogPlanner
{
    public static IReadOnlyList<ChangeCaseDialogChoice> Choices { get; } =
    [
        new("UPPERCASE", CaseKind.Upper),
        new("lowercase", CaseKind.Lower),
        new("Sentence case", CaseKind.Sentence),
        new("Capitalize Each Word", CaseKind.Capitalize),
        new("tOGGLE cASE", CaseKind.Toggle),
    ];

    public static string Apply(string selectedText, CaseKind kind)
    {
        ArgumentNullException.ThrowIfNull(selectedText);
        return ChangeCase.Apply(selectedText, kind);
    }
}
