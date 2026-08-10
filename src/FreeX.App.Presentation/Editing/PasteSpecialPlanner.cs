using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Editing;

public static class PasteSpecialPlanner
{
    public static PasteSpecialSurfaceSpec Surface { get; } = CreateSurface();

    public static PasteSpecialDialogSelection CreateSelection(
        PasteSpecialDialogMode mode,
        PasteSpecialOperation operation,
        bool skipBlanks = false,
        bool transpose = false,
        bool keepColumnWidths = false,
        bool pasteLink = false)
    {
        _ = Surface.GetChoice(mode);
        _ = Surface.GetOperation(operation);

        return new PasteSpecialDialogSelection(
            mode,
            operation,
            skipBlanks,
            transpose,
            keepColumnWidths,
            pasteLink);
    }

    public static PasteSpecialDialogSelection CreatePasteLinkSelection() =>
        new(
            PasteSpecialDialogMode.All,
            PasteSpecialOperation.None,
            SkipBlanks: false,
            Transpose: false,
            KeepColumnWidths: false,
            PasteLink: true);

    public static PasteSpecialPlan CreatePlan(PasteSpecialDialogSelection selection)
    {
        var choice = Surface.GetChoice(selection.Mode);
        var options = new PasteSpecialOptions(
            Transpose: selection.Transpose,
            Operation: selection.Operation,
            SkipBlanks: selection.SkipBlanks,
            ContentKind: choice.ContentKind);

        var pasteLinkRequested = selection.PasteLink && choice.Action != PasteSpecialAction.LinkedPicture;
        var action = pasteLinkRequested ? PasteSpecialAction.Link : choice.Action;
        var label = pasteLinkRequested
            ? Surface.GetAction(PasteSpecialDialogActionKind.PasteLink).AvaloniaLabel
            : choice.AvaloniaLabel;

        return new PasteSpecialPlan(
            action,
            choice.PasteMode,
            options,
            selection.KeepColumnWidths,
            label);
    }

    internal static PasteSpecialOperation ParseOperation(string operation) =>
        operation.ToLowerInvariant() switch
        {
            "add" => PasteSpecialOperation.Add,
            "subtract" => PasteSpecialOperation.Subtract,
            "multiply" => PasteSpecialOperation.Multiply,
            "divide" => PasteSpecialOperation.Divide,
            _ => PasteSpecialOperation.None
        };

    private static PasteSpecialSurfaceSpec CreateSurface()
    {
        PasteSpecialChoiceDescriptor Choice(
            PasteSpecialDialogMode mode,
            PasteSpecialAction action,
            PasteMode pasteMode,
            PasteSpecialContentKind contentKind,
            string wpfLabelTextKey,
            string avaloniaLabel,
            string wpfAutomationId,
            string avaloniaAutomationId,
            string wpfAutomationNameTextKey,
            string wpfAutomationHelpTextKey,
            int wpfOrder,
            int wpfRow,
            int wpfColumn,
            int? avaloniaOrder,
            bool isDefault = false) =>
            new(
                mode,
                action,
                pasteMode,
                contentKind,
                wpfLabelTextKey,
                avaloniaLabel,
                wpfAutomationId,
                avaloniaAutomationId,
                wpfAutomationNameTextKey,
                wpfAutomationHelpTextKey,
                wpfOrder,
                new PasteSpecialGridPosition(wpfRow, wpfColumn),
                avaloniaOrder,
                isDefault);

        var choices = new[]
        {
            Choice(PasteSpecialDialogMode.All, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_All", "All", "PasteSpecialAllOption", "PasteSpecialAllRadio",
                "PasteSpecial_AllAutomationName", "PasteSpecial_PasteAllCellContentsAndFormatting", 0, 0, 0, 0, isDefault: true),
            Choice(PasteSpecialDialogMode.Formulas, PasteSpecialAction.Paste, PasteMode.Formulas, PasteSpecialContentKind.Default,
                "PasteSpecial_Formulas", "Formulas", "PasteSpecialFormulasOption", "PasteSpecialFormulasRadio",
                "PasteSpecial_FormulasAutomationName", "PasteSpecial_PasteFormulasWithoutChangingExistingFormatting", 1, 1, 0, 2),
            Choice(PasteSpecialDialogMode.Values, PasteSpecialAction.Paste, PasteMode.Values, PasteSpecialContentKind.Default,
                "PasteSpecial_Values", "Values", "PasteSpecialValuesOption", "PasteSpecialValuesRadio",
                "PasteSpecial_ValuesAutomationName", "PasteSpecial_PasteOnlyCellValues", 2, 2, 0, 1),
            Choice(PasteSpecialDialogMode.Formats, PasteSpecialAction.Paste, PasteMode.Formats, PasteSpecialContentKind.Default,
                "PasteSpecial_Formats", "Formats", "PasteSpecialFormatsOption", "PasteSpecialFormatsRadio",
                "PasteSpecial_FormatsAutomationName", "PasteSpecial_PasteOnlyCellFormatting", 3, 3, 0, 3),
            Choice(PasteSpecialDialogMode.Comments, PasteSpecialAction.Comments, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_CommentsAndNotes", "Comments and Notes", "PasteSpecialCommentsAndNotesOption", "PasteSpecialCommentsRadio",
                "PasteSpecial_CommentsAndNotesAutomationName", "PasteSpecial_PasteOnlyCommentsAndNotes", 4, 4, 0, 4),
            Choice(PasteSpecialDialogMode.Validation, PasteSpecialAction.Validation, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_Validation", "Validation", "PasteSpecialValidationOption", "PasteSpecialValidationRadio",
                "PasteSpecial_ValidationAutomationName", "PasteSpecial_PasteOnlyDataValidationRules", 5, 5, 0, 5),
            Choice(PasteSpecialDialogMode.AllUsingSourceTheme, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllUsingSourceTheme,
                "PasteSpecial_AllUsingSourceTheme", "All Using Source Theme", "PasteSpecialAllUsingSourceThemeOption", "PasteSpecialAllUsingSourceThemeRadio",
                "PasteSpecial_AllUsingSourceThemeAutomationName", "PasteSpecial_PasteAllContentUsingTheCopiedSourceTheme", 6, 6, 0, null),
            Choice(PasteSpecialDialogMode.AllExceptBorders, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllExceptBorders,
                "PasteSpecial_AllExceptBorders", "All Except Borders", "PasteSpecialAllExceptBordersOption", "PasteSpecialAllExceptBordersRadio",
                "PasteSpecial_AllExceptBordersAutomationName", "PasteSpecial_PasteAllContentAndFormattingExceptCellBorders", 7, 7, 0, 6),
            Choice(PasteSpecialDialogMode.ColumnWidths, PasteSpecialAction.ColumnWidths, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_ColumnWidths", "Column Widths", "PasteSpecialColumnWidthsOption", "PasteSpecialColumnWidthsRadio",
                "PasteSpecial_ColumnWidthsAutomationName", "PasteSpecial_PasteOnlyCopiedColumnWidths", 8, 8, 0, 8),
            Choice(PasteSpecialDialogMode.FormulasAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.FormulasAndNumberFormats,
                "PasteSpecial_FormulasAndNumberFormats", "Formulas and Number Formats", "PasteSpecialFormulasAndNumberFormatsOption", "PasteSpecialFormulasAndNumberFormatsRadio",
                "PasteSpecial_FormulasAndNumberFormatsAutomationName", "PasteSpecial_PasteFormulasAndNumberFormats", 9, 0, 1, 9),
            Choice(PasteSpecialDialogMode.ValuesAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.ValuesAndNumberFormats,
                "PasteSpecial_ValuesAndNumberFormats", "Values and Number Formats", "PasteSpecialValuesAndNumberFormatsOption", "PasteSpecialValuesAndNumberFormatsRadio",
                "PasteSpecial_ValuesAndNumberFormatsAutomationName", "PasteSpecial_PasteValuesAndNumberFormats", 10, 1, 1, 10),
            Choice(PasteSpecialDialogMode.AllMergingConditionalFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllMergingConditionalFormats,
                "PasteSpecial_AllMergingConditionalFormats", "All Merging Conditional Formats", "PasteSpecialAllMergingConditionalFormatsOption", "PasteSpecialAllMergingConditionalFormatsRadio",
                "PasteSpecial_AllMergingConditionalFormatsAutomationName", "PasteSpecial_PasteAllContentWhileMergingConditionalFormattingRules", 11, 2, 1, 7),
            Choice(PasteSpecialDialogMode.ValuesAndSourceFormatting, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.ValuesAndSourceFormatting,
                "PasteSpecial_ValuesAndSourceFormatting", "Values and Source Formatting", "PasteSpecialValuesAndSourceFormattingOption", "PasteSpecialValuesAndSourceFormattingRadio",
                "PasteSpecial_ValuesAndSourceFormattingAutomationName", "PasteSpecial_PasteValuesWithCopiedSourceFormatting", 12, 3, 1, 11),
            Choice(PasteSpecialDialogMode.Text, PasteSpecialAction.ExternalText, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_Text", "Text", "PasteSpecialTextOption", "PasteSpecialTextRadio",
                "PasteSpecial_TextAutomationName", "PasteSpecial_PasteClipboardText", 13, 4, 1, 12),
            Choice(PasteSpecialDialogMode.UnicodeText, PasteSpecialAction.ExternalText, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_UnicodeText", "Unicode Text", "PasteSpecialUnicodeTextOption", "PasteSpecialUnicodeTextRadio",
                "PasteSpecial_UnicodeTextAutomationName", "PasteSpecial_PasteClipboardUnicodeText", 14, 5, 1, 13),
            Choice(PasteSpecialDialogMode.Picture, PasteSpecialAction.Picture, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_Picture", "Picture", "PasteSpecialPictureOption", "PasteSpecialPictureRadio",
                "PasteSpecial_PictureAutomationName", "PasteSpecial_PasteCopiedCellsAsAPicture", 15, 6, 1, 14),
            Choice(PasteSpecialDialogMode.LinkedPicture, PasteSpecialAction.LinkedPicture, PasteMode.All, PasteSpecialContentKind.Default,
                "PasteSpecial_LinkedPicture", "Linked Picture", "PasteSpecialLinkedPictureOption", "PasteSpecialLinkedPictureRadio",
                "PasteSpecial_LinkedPictureAutomationName", "PasteSpecial_PasteCopiedCellsAsALinkedPicture", 16, 7, 1, 15),
        };

        var toggles = new[]
        {
            new PasteSpecialToggleDescriptor(PasteSpecialToggleKind.SkipBlanks, "PasteSpecial_SkipBlanks", "Skip Blanks",
                "PasteSpecialSkipBlanksBox", "PasteSpecialSkipBlanksBox", "PasteSpecial_SkipBlanksAutomationName",
                "PasteSpecial_SkipBlankCellsFromTheCopiedRange", 0),
            new PasteSpecialToggleDescriptor(PasteSpecialToggleKind.Transpose, "PasteSpecial_Transpose", "Transpose",
                "PasteSpecialTransposeBox", "PasteSpecialTransposeBox", "PasteSpecial_TransposeAutomationName",
                "PasteSpecial_SwitchCopiedRowsAndColumnsWhilePasting", 1),
            new PasteSpecialToggleDescriptor(PasteSpecialToggleKind.KeepColumnWidths, "PasteSpecial_KeepSourceColumnWidths", "Keep Source Column Widths",
                "PasteSpecialKeepColumnWidthsBox", "PasteSpecialKeepColumnWidthsBox", "PasteSpecial_KeepSourceColumnWidthsAutomationName",
                "PasteSpecial_ApplyTheCopiedSourceColumnWidths", 2),
        };

        var operations = new[]
        {
            new PasteSpecialOperationDescriptor(PasteSpecialOperation.None, "PasteSpecial_OperationNone", "None",
                "PasteSpecialOperationNoneOption", "PasteSpecialOperationNoneRadio", "PasteSpecial_OperationNoneAutomationName",
                "PasteSpecial_PasteWithoutAMathematicalOperation", new PasteSpecialGridPosition(0, 0), 0, IsDefault: true),
            new PasteSpecialOperationDescriptor(PasteSpecialOperation.Add, "PasteSpecial_OperationAdd", "Add",
                "PasteSpecialOperationAddOption", "PasteSpecialOperationAddRadio", "PasteSpecial_OperationAddAutomationName",
                "PasteSpecial_AddCopiedValuesToDestinationValues", new PasteSpecialGridPosition(0, 1), 1),
            new PasteSpecialOperationDescriptor(PasteSpecialOperation.Subtract, "PasteSpecial_OperationSubtract", "Subtract",
                "PasteSpecialOperationSubtractOption", "PasteSpecialOperationSubtractRadio", "PasteSpecial_OperationSubtractAutomationName",
                "PasteSpecial_SubtractCopiedValuesFromDestinationValues", new PasteSpecialGridPosition(1, 0), 2),
            new PasteSpecialOperationDescriptor(PasteSpecialOperation.Multiply, "PasteSpecial_OperationMultiply", "Multiply",
                "PasteSpecialOperationMultiplyOption", "PasteSpecialOperationMultiplyRadio", "PasteSpecial_OperationMultiplyAutomationName",
                "PasteSpecial_MultiplyDestinationValuesByCopiedValues", new PasteSpecialGridPosition(1, 1), 3),
            new PasteSpecialOperationDescriptor(PasteSpecialOperation.Divide, "PasteSpecial_OperationDivide", "Divide",
                "PasteSpecialOperationDivideOption", "PasteSpecialOperationDivideRadio", "PasteSpecial_OperationDivideAutomationName",
                "PasteSpecial_DivideDestinationValuesByCopiedValues", new PasteSpecialGridPosition(2, 0), 4),
        };

        var actions = new[]
        {
            new PasteSpecialDialogActionDescriptor(PasteSpecialDialogActionKind.PasteLink, "PasteSpecial_PasteLink", null, "Paste Link",
                "PasteSpecialPasteLinkButton", "PasteSpecialPasteLinkButton", "PasteSpecial_PasteLinkAutomationName",
                "PasteSpecial_PasteFormulasThatLinkToTheCopiedCells"),
            new PasteSpecialDialogActionDescriptor(PasteSpecialDialogActionKind.Accept, "Common_Ok", "TableLoc_OK", "OK",
                "PasteSpecialOkButton", "PasteSpecialOkButton", "PasteSpecial_OkAutomationName",
                "PasteSpecial_ApplyTheSelectedPasteSpecialOptions", IsDefault: true),
            new PasteSpecialDialogActionDescriptor(PasteSpecialDialogActionKind.Cancel, "Common_Cancel", "TableLoc_Cancel", "Cancel",
                "PasteSpecialCancelButton", "PasteSpecialCancelButton", "PasteSpecial_CancelAutomationName",
                "PasteSpecial_CloseThePasteSpecialDialogWithoutApplyingChanges", IsCancel: true),
        };

        return new PasteSpecialSurfaceSpec(
            new PasteSpecialSurfaceTextDescriptor("PasteSpecial_PasteSpecial", "TableLoc_PasteSpecialTitle", "Paste Special"),
            new PasteSpecialSurfaceTextDescriptor("PasteSpecial_PasteGroup", "TableLoc_PasteSpecialPasteLabel", "Paste"),
            new PasteSpecialSurfaceTextDescriptor("PasteSpecial_OperationGroup", null, "Operation"),
            choices,
            toggles,
            operations,
            actions,
            "PasteSpecialDialog");
    }
}

public sealed record PasteSpecialDialogSelection(
    PasteSpecialDialogMode Mode,
    PasteSpecialOperation Operation,
    bool SkipBlanks = false,
    bool Transpose = false,
    bool KeepColumnWidths = false,
    bool PasteLink = false)
{
    public PasteSpecialDialogSelection(
        PasteSpecialDialogMode mode,
        string operation,
        bool SkipBlanks = false,
        bool Transpose = false,
        bool KeepColumnWidths = false,
        bool PasteLink = false)
        : this(mode, PasteSpecialPlanner.ParseOperation(operation), SkipBlanks, Transpose, KeepColumnWidths, PasteLink)
    {
    }
}

public sealed record PasteSpecialPlan(
    PasteSpecialAction Action,
    PasteMode PasteMode,
    PasteSpecialOptions Options,
    bool KeepColumnWidths,
    string Label);

public sealed record PasteSpecialSurfaceSpec(
    PasteSpecialSurfaceTextDescriptor Title,
    PasteSpecialSurfaceTextDescriptor PasteGroup,
    PasteSpecialSurfaceTextDescriptor OperationGroup,
    IReadOnlyList<PasteSpecialChoiceDescriptor> Choices,
    IReadOnlyList<PasteSpecialToggleDescriptor> Toggles,
    IReadOnlyList<PasteSpecialOperationDescriptor> Operations,
    IReadOnlyList<PasteSpecialDialogActionDescriptor> Actions,
    string AutomationId)
{
    public IReadOnlyList<PasteSpecialChoiceDescriptor> WpfChoices =>
        Choices.OrderBy(choice => choice.WpfOrder).ToArray();

    public IReadOnlyList<PasteSpecialChoiceDescriptor> AvaloniaChoices =>
        Choices
            .Where(choice => choice.AvaloniaOrder.HasValue)
            .OrderBy(choice => choice.AvaloniaOrder)
            .ToArray();

    public PasteSpecialChoiceDescriptor GetChoice(PasteSpecialDialogMode mode) =>
        Choices.Single(choice => choice.Mode == mode);

    public PasteSpecialToggleDescriptor GetToggle(PasteSpecialToggleKind kind) =>
        Toggles.Single(toggle => toggle.Kind == kind);

    public PasteSpecialOperationDescriptor GetOperation(PasteSpecialOperation operation) =>
        Operations.Single(descriptor => descriptor.Operation == operation);

    public PasteSpecialDialogActionDescriptor GetAction(PasteSpecialDialogActionKind kind) =>
        Actions.Single(action => action.Kind == kind);
}

public sealed record PasteSpecialSurfaceTextDescriptor(
    string WpfTextKey,
    string? AvaloniaTextKey,
    string AvaloniaFallbackText)
{
    public string ResolveWpf(Func<string, string> getText) => getText(WpfTextKey);

    public string ResolveAvalonia(Func<string, string> getText) =>
        AvaloniaTextKey is { Length: > 0 } key ? getText(key) : AvaloniaFallbackText;
}

public sealed record PasteSpecialChoiceDescriptor(
    PasteSpecialDialogMode Mode,
    PasteSpecialAction Action,
    PasteMode PasteMode,
    PasteSpecialContentKind ContentKind,
    string WpfLabelTextKey,
    string AvaloniaLabel,
    string WpfAutomationId,
    string AvaloniaAutomationId,
    string WpfAutomationNameTextKey,
    string WpfAutomationHelpTextKey,
    int WpfOrder,
    PasteSpecialGridPosition WpfPlacement,
    int? AvaloniaOrder,
    bool IsDefault = false,
    bool IsEnabled = true);

public sealed record PasteSpecialToggleDescriptor(
    PasteSpecialToggleKind Kind,
    string WpfLabelTextKey,
    string AvaloniaLabel,
    string WpfAutomationId,
    string AvaloniaAutomationId,
    string WpfAutomationNameTextKey,
    string WpfAutomationHelpTextKey,
    int Order,
    bool IsCheckedByDefault = false,
    bool IsEnabled = true);

public sealed record PasteSpecialOperationDescriptor(
    PasteSpecialOperation Operation,
    string WpfLabelTextKey,
    string AvaloniaLabel,
    string WpfAutomationId,
    string AvaloniaAutomationId,
    string WpfAutomationNameTextKey,
    string WpfAutomationHelpTextKey,
    PasteSpecialGridPosition Placement,
    int Order,
    bool IsDefault = false,
    bool IsEnabled = true);

public sealed record PasteSpecialDialogActionDescriptor(
    PasteSpecialDialogActionKind Kind,
    string WpfLabelTextKey,
    string? AvaloniaLabelTextKey,
    string AvaloniaLabel,
    string WpfAutomationId,
    string AvaloniaAutomationId,
    string WpfAutomationNameTextKey,
    string WpfAutomationHelpTextKey,
    bool IsDefault = false,
    bool IsCancel = false,
    bool IsEnabled = true)
{
    public string ResolveAvaloniaLabel(Func<string, string> getText) =>
        AvaloniaLabelTextKey is { Length: > 0 } key ? getText(key) : AvaloniaLabel;
}

public readonly record struct PasteSpecialGridPosition(int Row, int Column);

public enum PasteSpecialToggleKind
{
    SkipBlanks,
    Transpose,
    KeepColumnWidths
}

public enum PasteSpecialDialogActionKind
{
    PasteLink,
    Accept,
    Cancel
}

public enum PasteSpecialAction
{
    Paste,
    ColumnWidths,
    Comments,
    Validation,
    Picture,
    LinkedPicture,
    ExternalText,
    Link
}

public enum PasteMode
{
    All,
    Values,
    Formulas,
    Formats
}

public enum PasteSpecialDialogMode
{
    All,
    Values,
    Formulas,
    Formats,
    Comments,
    Validation,
    AllUsingSourceTheme,
    AllExceptBorders,
    AllMergingConditionalFormats,
    ColumnWidths,
    FormulasAndNumberFormats,
    ValuesAndNumberFormats,
    ValuesAndSourceFormatting,
    Text,
    UnicodeText,
    Picture,
    LinkedPicture
}
