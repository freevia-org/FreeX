using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed partial class PasteSpecialDialog : Window
{
    private readonly RadioButton _rbAll;
    private readonly RadioButton _rbValues;
    private readonly RadioButton _rbFormats;
    private readonly RadioButton _rbFormulas;
    private readonly RadioButton _rbComments;
    private readonly RadioButton _rbValidation;
    private readonly RadioButton _rbAllUsingSourceTheme;
    private readonly RadioButton _rbAllExceptBorders;
    private readonly RadioButton _rbAllMergingConditionalFormats;
    private readonly RadioButton _rbColumnWidths;
    private readonly RadioButton _rbFormulasAndNumberFormats;
    private readonly RadioButton _rbValuesAndNumberFormats;
    private readonly RadioButton _rbValuesAndSourceFormatting;
    private readonly RadioButton _rbText;
    private readonly RadioButton _rbUnicodeText;
    private readonly RadioButton _rbPicture;
    private readonly RadioButton _rbLinkedPicture;
    private readonly Button _pasteLinkButton;
    private readonly CheckBox _skipBlanks;
    private readonly CheckBox _transpose;
    private readonly CheckBox _keepColumnWidths;
    private readonly RadioButton _opNone;
    private readonly RadioButton _opAdd;
    private readonly RadioButton _opSubtract;
    private readonly RadioButton _opMultiply;
    private readonly RadioButton _opDivide;
    private readonly IReadOnlyDictionary<PasteSpecialDialogMode, RadioButton> _pasteChoiceButtons;
    private readonly IReadOnlyDictionary<PasteSpecialOperation, RadioButton> _operationButtons;
    private bool _pasteLinkRequested;

    public PasteSpecialDialogMode Mode
    {
        get
        {
            foreach (var choice in PasteSpecialPlanner.Surface.WpfChoices)
            {
                if (_pasteChoiceButtons[choice.Mode].IsChecked == true)
                    return choice.Mode;
            }

            return PasteSpecialDialogMode.All;
        }
    }

    public bool PasteValues    => Mode == PasteSpecialDialogMode.Values;
    public bool PasteFormats   => Mode == PasteSpecialDialogMode.Formats;
    public bool PasteFormulas  => Mode == PasteSpecialDialogMode.Formulas;
    public bool PastePicture   => Mode is PasteSpecialDialogMode.Picture or PasteSpecialDialogMode.LinkedPicture;
    public bool PasteLink      => _pasteLinkRequested || Mode == PasteSpecialDialogMode.LinkedPicture;
    public bool SkipBlanks     => _skipBlanks.IsChecked == true;
    public bool Transpose      => _transpose.IsChecked == true;
    public bool KeepColumnWidths => _keepColumnWidths.IsChecked == true;
    public PasteSpecialOperation Operation
    {
        get
        {
            foreach (var operation in PasteSpecialPlanner.Surface.Operations.OrderBy(descriptor => descriptor.Order))
            {
                if (_operationButtons[operation.Operation].IsChecked == true)
                    return operation.Operation;
            }

            return PasteSpecialOperation.None;
        }
    }

    public PasteSpecialDialogSelection Selection =>
        PasteSpecialPlanner.CreateSelection(Mode, Operation, SkipBlanks, Transpose, KeepColumnWidths, PasteLink);

    public PasteSpecialDialog()
    {
        var surface = PasteSpecialPlanner.Surface;

        Title = surface.Title.ResolveWpf(UiText.Get);
        Width = 470; Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        _rbAll = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.All));
        _rbValues = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Values));
        _rbFormulas = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Formulas));
        _rbFormats = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Formats));
        _rbComments = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Comments));
        _rbValidation = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Validation));
        _rbAllUsingSourceTheme = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.AllUsingSourceTheme));
        _rbAllExceptBorders = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.AllExceptBorders));
        _rbAllMergingConditionalFormats = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.AllMergingConditionalFormats));
        _rbColumnWidths = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.ColumnWidths));
        _rbFormulasAndNumberFormats = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.FormulasAndNumberFormats));
        _rbValuesAndNumberFormats = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.ValuesAndNumberFormats));
        _rbValuesAndSourceFormatting = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.ValuesAndSourceFormatting));
        _rbText = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Text));
        _rbUnicodeText = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.UnicodeText));
        _rbPicture = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.Picture));
        _rbLinkedPicture = CreatePasteChoiceButton(surface.GetChoice(PasteSpecialDialogMode.LinkedPicture));
        _pasteChoiceButtons = new Dictionary<PasteSpecialDialogMode, RadioButton>
        {
            [PasteSpecialDialogMode.All] = _rbAll,
            [PasteSpecialDialogMode.Values] = _rbValues,
            [PasteSpecialDialogMode.Formulas] = _rbFormulas,
            [PasteSpecialDialogMode.Formats] = _rbFormats,
            [PasteSpecialDialogMode.Comments] = _rbComments,
            [PasteSpecialDialogMode.Validation] = _rbValidation,
            [PasteSpecialDialogMode.AllUsingSourceTheme] = _rbAllUsingSourceTheme,
            [PasteSpecialDialogMode.AllExceptBorders] = _rbAllExceptBorders,
            [PasteSpecialDialogMode.AllMergingConditionalFormats] = _rbAllMergingConditionalFormats,
            [PasteSpecialDialogMode.ColumnWidths] = _rbColumnWidths,
            [PasteSpecialDialogMode.FormulasAndNumberFormats] = _rbFormulasAndNumberFormats,
            [PasteSpecialDialogMode.ValuesAndNumberFormats] = _rbValuesAndNumberFormats,
            [PasteSpecialDialogMode.ValuesAndSourceFormatting] = _rbValuesAndSourceFormatting,
            [PasteSpecialDialogMode.Text] = _rbText,
            [PasteSpecialDialogMode.UnicodeText] = _rbUnicodeText,
            [PasteSpecialDialogMode.Picture] = _rbPicture,
            [PasteSpecialDialogMode.LinkedPicture] = _rbLinkedPicture,
        };

        var pasteLinkAction = surface.GetAction(PasteSpecialDialogActionKind.PasteLink);
        _pasteLinkButton = new Button
        {
            Content = UiText.Get(pasteLinkAction.WpfLabelTextKey),
            Width = 96,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = pasteLinkAction.IsEnabled,
        };

        _skipBlanks = CreateToggle(surface.GetToggle(PasteSpecialToggleKind.SkipBlanks), new Thickness(0, 0, 0, 8));
        _transpose = CreateToggle(surface.GetToggle(PasteSpecialToggleKind.Transpose), new Thickness(0, 4, 0, 8));
        _keepColumnWidths = CreateToggle(surface.GetToggle(PasteSpecialToggleKind.KeepColumnWidths), new Thickness(0, 0, 0, 8));

        _opNone = CreateOperationButton(surface.GetOperation(PasteSpecialOperation.None));
        _opAdd = CreateOperationButton(surface.GetOperation(PasteSpecialOperation.Add));
        _opSubtract = CreateOperationButton(surface.GetOperation(PasteSpecialOperation.Subtract));
        _opMultiply = CreateOperationButton(surface.GetOperation(PasteSpecialOperation.Multiply));
        _opDivide = CreateOperationButton(surface.GetOperation(PasteSpecialOperation.Divide));
        _operationButtons = new Dictionary<PasteSpecialOperation, RadioButton>
        {
            [PasteSpecialOperation.None] = _opNone,
            [PasteSpecialOperation.Add] = _opAdd,
            [PasteSpecialOperation.Subtract] = _opSubtract,
            [PasteSpecialOperation.Multiply] = _opMultiply,
            [PasteSpecialOperation.Divide] = _opDivide,
        };
        ApplyAutomationMetadata();

        stack.Children.Add(CreatePasteGroup());
        stack.Children.Add(CreatePasteOptionsPanel());
        stack.Children.Add(CreateOperationGroup());
        stack.Children.Add(CreateFooterRow());

        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _rbAll.Focus();
        Keyboard.Focus(_rbAll);
    }

    private static RadioButton CreatePasteChoiceButton(PasteSpecialChoiceDescriptor choice) =>
        new()
        {
            Content = UiText.Get(choice.WpfLabelTextKey),
            IsChecked = choice.IsDefault,
            IsEnabled = choice.IsEnabled,
            Margin = new Thickness(0, 0, 0, 6),
        };

    private static CheckBox CreateToggle(PasteSpecialToggleDescriptor toggle, Thickness margin) =>
        new()
        {
            Content = UiText.Get(toggle.WpfLabelTextKey),
            IsChecked = toggle.IsCheckedByDefault,
            IsEnabled = toggle.IsEnabled,
            Margin = margin,
        };

}
