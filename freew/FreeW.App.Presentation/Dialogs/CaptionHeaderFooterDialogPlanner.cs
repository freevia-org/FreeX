using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CaptionDialogChoice(CaptionLabel Value, string Label);

public sealed record CaptionDialogPlan(
    string Title,
    string LabelPrompt,
    string CaptionPrompt,
    IReadOnlyList<CaptionDialogChoice> Choices,
    int SelectedIndex);

public sealed record CaptionDialogResult(CaptionLabel Label, string Text);

public static class CaptionDialogPlanner
{
    private static readonly CaptionLabel[] Labels =
    [
        CaptionLabel.Figure,
        CaptionLabel.Table,
        CaptionLabel.Equation
    ];

    public static CaptionDialogPlan Build(CaptionLabel defaultLabel) =>
        new(
            "Insert Caption",
            "Label:",
            "Caption:",
            Labels.Select(label => new CaptionDialogChoice(label, Captions.LabelText(label))).ToArray(),
            Math.Max(0, Array.IndexOf(Labels, defaultLabel)));

    public static CaptionDialogResult BuildResult(int selectedIndex, string? text)
    {
        var index = Math.Clamp(selectedIndex, 0, Labels.Length - 1);
        return new CaptionDialogResult(Labels[index], text?.Trim() ?? string.Empty);
    }
}

public sealed record HeaderFooterTextDialogPlan(
    string Title,
    string PromptLabel,
    string InitialText);

public static class HeaderFooterTextDialogPlanner
{
    public static HeaderFooterTextDialogPlan Build(bool footer, string? initialText)
    {
        var label = footer ? "Footer" : "Header";
        return new HeaderFooterTextDialogPlan(
            $"Edit {label}",
            $"{label} text:",
            initialText ?? string.Empty);
    }

    public static string BuildResult(string? text) => text ?? string.Empty;
}
