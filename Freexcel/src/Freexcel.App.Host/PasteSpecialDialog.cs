using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

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
    Picture,
    LinkedPicture
}

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
    private bool _pasteLinkRequested;

    public PasteSpecialDialogMode Mode => true switch
    {
        _ when _rbValues.IsChecked == true => PasteSpecialDialogMode.Values,
        _ when _rbFormulas.IsChecked == true => PasteSpecialDialogMode.Formulas,
        _ when _rbFormats.IsChecked == true => PasteSpecialDialogMode.Formats,
        _ when _rbComments.IsChecked == true => PasteSpecialDialogMode.Comments,
        _ when _rbValidation.IsChecked == true => PasteSpecialDialogMode.Validation,
        _ when _rbAllUsingSourceTheme.IsChecked == true => PasteSpecialDialogMode.AllUsingSourceTheme,
        _ when _rbAllExceptBorders.IsChecked == true => PasteSpecialDialogMode.AllExceptBorders,
        _ when _rbAllMergingConditionalFormats.IsChecked == true => PasteSpecialDialogMode.AllMergingConditionalFormats,
        _ when _rbColumnWidths.IsChecked == true => PasteSpecialDialogMode.ColumnWidths,
        _ when _rbFormulasAndNumberFormats.IsChecked == true => PasteSpecialDialogMode.FormulasAndNumberFormats,
        _ when _rbValuesAndNumberFormats.IsChecked == true => PasteSpecialDialogMode.ValuesAndNumberFormats,
        _ when _rbValuesAndSourceFormatting.IsChecked == true => PasteSpecialDialogMode.ValuesAndSourceFormatting,
        _ when _rbPicture.IsChecked == true => PasteSpecialDialogMode.Picture,
        _ when _rbLinkedPicture.IsChecked == true => PasteSpecialDialogMode.LinkedPicture,
        _ => PasteSpecialDialogMode.All
    };

    public bool PasteValues    => Mode == PasteSpecialDialogMode.Values;
    public bool PasteFormats   => Mode == PasteSpecialDialogMode.Formats;
    public bool PasteFormulas  => Mode == PasteSpecialDialogMode.Formulas;
    public bool PastePicture   => Mode is PasteSpecialDialogMode.Picture or PasteSpecialDialogMode.LinkedPicture;
    public bool PasteLink      => _pasteLinkRequested || Mode == PasteSpecialDialogMode.LinkedPicture;
    public bool SkipBlanks     => _skipBlanks.IsChecked == true;
    public bool Transpose      => _transpose.IsChecked == true;
    public bool KeepColumnWidths => _keepColumnWidths.IsChecked == true;
    public string Operation => true switch
    {
        _ when _opAdd.IsChecked == true => "Add",
        _ when _opSubtract.IsChecked == true => "Subtract",
        _ when _opMultiply.IsChecked == true => "Multiply",
        _ when _opDivide.IsChecked == true => "Divide",
        _ => "None"
    };

    public PasteSpecialDialog()
    {
        Title = "Paste Special";
        Width = 470; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        _rbAll      = new RadioButton { Content = "_All",            IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        _rbValues   = new RadioButton { Content = "_Values",    Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulas = new RadioButton { Content = "_Formulas",  Margin = new Thickness(0, 0, 0, 6) };
        _rbFormats  = new RadioButton { Content = "Forma_ts",   Margin = new Thickness(0, 0, 0, 6) };
        _rbComments = new RadioButton { Content = "_Comments and notes", Margin = new Thickness(0, 0, 0, 6) };
        _rbValidation = new RadioButton { Content = "Validatio_n", Margin = new Thickness(0, 0, 0, 6) };
        _rbAllUsingSourceTheme = new RadioButton { Content = "All using source t_heme", Margin = new Thickness(0, 0, 0, 6) };
        _rbAllExceptBorders = new RadioButton { Content = "All e_xcept borders", Margin = new Thickness(0, 0, 0, 6) };
        _rbAllMergingConditionalFormats = new RadioButton { Content = "All merging conditional _formats", Margin = new Thickness(0, 0, 0, 6) };
        _rbColumnWidths = new RadioButton { Content = "Column _widths", Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulasAndNumberFormats = new RadioButton { Content = "Formulas and number fo_rmats", Margin = new Thickness(0, 0, 0, 6) };
        _rbValuesAndNumberFormats = new RadioButton { Content = "Values and number for_mats", Margin = new Thickness(0, 0, 0, 6) };
        _rbValuesAndSourceFormatting = new RadioButton { Content = "Values and source f_ormatting", Margin = new Thickness(0, 0, 0, 6) };
        _rbPicture  = new RadioButton { Content = "_Picture",        Margin = new Thickness(0, 0, 0, 6) };
        _rbLinkedPicture = new RadioButton { Content = "_Linked picture", Margin = new Thickness(0, 0, 0, 6) };
        _pasteLinkButton = new Button { Content = "Paste _Link", Width = 96, Margin = new Thickness(0, 0, 8, 0) };
        _skipBlanks = new CheckBox { Content = "S_kip blanks", Margin = new Thickness(0, 0, 0, 8) };
        _transpose  = new CheckBox { Content = "Transpos_e", Margin = new Thickness(0, 4, 0, 8) };
        _keepColumnWidths = new CheckBox { Content = "Keep source column _widths", Margin = new Thickness(0, 0, 0, 8) };
        _opNone = CreateOperationButton("_None", isChecked: true);
        _opAdd = CreateOperationButton("_Add");
        _opSubtract = CreateOperationButton("_Subtract");
        _opMultiply = CreateOperationButton("_Multiply");
        _opDivide = CreateOperationButton("_Divide");

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

}
