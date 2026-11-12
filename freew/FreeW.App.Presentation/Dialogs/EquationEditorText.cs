using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>The localized strings of the equation editor, resolved through the app's resource catalog.</summary>
public sealed record EquationEditorSurface(
    string Title,
    string LinearLabel,
    string PreviewLabel,
    string InlineLabel,
    string DisplayLabel,
    string JustificationLabel,
    IReadOnlyList<(MathJustification Justification, string Label)> Justifications,
    string Hint,
    string OpaquePartsNotice,
    string EmptyPreview)
{
    public const string AutomationId = "EquationEditorWindow";
    public const string LinearTextAutomationId = "EquationEditorLinearText";
    public const string PreviewAutomationId = "EquationEditorPreview";
    public const string InlineAutomationId = "EquationEditorInline";
    public const string DisplayAutomationId = "EquationEditorDisplay";
    public const string JustificationAutomationId = "EquationEditorJustification";
}

/// <summary>Resource keys and English fallbacks for the equation editor.</summary>
public static class EquationEditorText
{
    private static readonly ResourceTextDescriptor Title = new("EquationEditor_Dialog_Title", "Equation");
    private static readonly ResourceTextDescriptor Linear = new("EquationEditor_Linear_Label", "Linear format:");
    private static readonly ResourceTextDescriptor Preview = new("EquationEditor_Preview_Label", "Preview:");
    private static readonly ResourceTextDescriptor Inline = new("EquationEditor_Inline_Label", "Inline");
    private static readonly ResourceTextDescriptor Display = new("EquationEditor_Display_Label", "Display");
    private static readonly ResourceTextDescriptor Justification = new("EquationEditor_Justification_Label", "Justification:");
    private static readonly ResourceTextDescriptor JustifyLeft = new("EquationEditor_JustifyLeft_Label", "Left");
    private static readonly ResourceTextDescriptor JustifyCenter = new("EquationEditor_JustifyCenter_Label", "Centered");
    private static readonly ResourceTextDescriptor JustifyRight = new("EquationEditor_JustifyRight_Label", "Right");
    private static readonly ResourceTextDescriptor JustifyCenterGroup = new("EquationEditor_JustifyCenterGroup_Label", "Centered as a Group");
    private static readonly ResourceTextDescriptor Hint = new(
        "EquationEditor_Hint",
        @"Type the equation in linear format, for example x=(-b\pm\sqrt(b^2-4ac))/(2a). Press Enter to accept or Esc to cancel.");
    private static readonly ResourceTextDescriptor OpaqueParts = new(
        "EquationEditor_OpaqueParts_Notice",
        "Parts shown as a numbered token cannot be edited as text. They are kept exactly as they are.");
    private static readonly ResourceTextDescriptor EmptyPreview = new("EquationEditor_EmptyPreview", "Type equation here.");

    private static readonly ResourceTextDescriptor[] All =
    [
        Title, Linear, Preview, Inline, Display, Justification, JustifyLeft, JustifyCenter, JustifyRight,
        JustifyCenterGroup, Hint, OpaqueParts, EmptyPreview,
    ];

    public static IReadOnlyList<string> RequiredResourceKeys => All.Select(text => text.ResourceKey).ToArray();

    public static EquationEditorSurface BuildSurface(Func<string, string?>? getText = null) => new(
        Title.Resolve(getText),
        Linear.Resolve(getText),
        Preview.Resolve(getText),
        Inline.Resolve(getText),
        Display.Resolve(getText),
        Justification.Resolve(getText),
        [
            (MathJustification.CenterGroup, JustifyCenterGroup.Resolve(getText)),
            (MathJustification.Left, JustifyLeft.Resolve(getText)),
            (MathJustification.Center, JustifyCenter.Resolve(getText)),
            (MathJustification.Right, JustifyRight.Resolve(getText)),
        ],
        Hint.Resolve(getText),
        OpaqueParts.Resolve(getText),
        EmptyPreview.Resolve(getText));
}
