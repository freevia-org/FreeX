namespace FreeX.App.Presentation.PivotUI;

public enum PivotCalculatedInputTarget
{
    None,
    SourceField,
    Name,
    Formula
}

public enum PivotCalculatedWorkflowOperation
{
    Save,
    Delete
}

public sealed record PivotCalculatedWorkflowIssue(
    PivotCalculatedInputTarget Target,
    string Message);

public sealed record PivotCalculatedDraft(string Name, string Formula)
{
    public static PivotCalculatedDraft Normalize(string? name, string? formula) =>
        new(name?.Trim() ?? string.Empty, formula?.Trim() ?? string.Empty);
}
