using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public enum PasteSpecialDialogMode
{
    All,
    Values,
    Formulas,
    Formats,
    Comments,
    Validation,
    AllExceptBorders,
    ColumnWidths,
    FormulasAndNumberFormats,
    ValuesAndNumberFormats,
    Picture,
    LinkedPicture
}

public sealed class PasteSpecialDialog : Window
{
    private readonly RadioButton _rbAll;
    private readonly RadioButton _rbValues;
    private readonly RadioButton _rbFormats;
    private readonly RadioButton _rbFormulas;
    private readonly RadioButton _rbComments;
    private readonly RadioButton _rbValidation;
    private readonly RadioButton _rbAllExceptBorders;
    private readonly RadioButton _rbColumnWidths;
    private readonly RadioButton _rbFormulasAndNumberFormats;
    private readonly RadioButton _rbValuesAndNumberFormats;
    private readonly RadioButton _rbPicture;
    private readonly RadioButton _rbLinkedPicture;
    private readonly CheckBox _pasteLink;
    private readonly CheckBox _skipBlanks;
    private readonly CheckBox _transpose;
    private readonly CheckBox _keepColumnWidths;
    private readonly ComboBox _operation;

    public PasteSpecialDialogMode Mode => true switch
    {
        _ when _rbValues.IsChecked == true => PasteSpecialDialogMode.Values,
        _ when _rbFormulas.IsChecked == true => PasteSpecialDialogMode.Formulas,
        _ when _rbFormats.IsChecked == true => PasteSpecialDialogMode.Formats,
        _ when _rbComments.IsChecked == true => PasteSpecialDialogMode.Comments,
        _ when _rbValidation.IsChecked == true => PasteSpecialDialogMode.Validation,
        _ when _rbAllExceptBorders.IsChecked == true => PasteSpecialDialogMode.AllExceptBorders,
        _ when _rbColumnWidths.IsChecked == true => PasteSpecialDialogMode.ColumnWidths,
        _ when _rbFormulasAndNumberFormats.IsChecked == true => PasteSpecialDialogMode.FormulasAndNumberFormats,
        _ when _rbValuesAndNumberFormats.IsChecked == true => PasteSpecialDialogMode.ValuesAndNumberFormats,
        _ when _rbPicture.IsChecked == true => PasteSpecialDialogMode.Picture,
        _ when _rbLinkedPicture.IsChecked == true => PasteSpecialDialogMode.LinkedPicture,
        _ => PasteSpecialDialogMode.All
    };

    public bool PasteValues    => Mode == PasteSpecialDialogMode.Values;
    public bool PasteFormats   => Mode == PasteSpecialDialogMode.Formats;
    public bool PasteFormulas  => Mode == PasteSpecialDialogMode.Formulas;
    public bool PastePicture   => Mode is PasteSpecialDialogMode.Picture or PasteSpecialDialogMode.LinkedPicture;
    public bool PasteLink      => _pasteLink.IsChecked == true || Mode == PasteSpecialDialogMode.LinkedPicture;
    public bool SkipBlanks     => _skipBlanks.IsChecked == true;
    public bool Transpose      => _transpose.IsChecked == true;
    public bool KeepColumnWidths => _keepColumnWidths.IsChecked == true;
    public string Operation    => (_operation.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";

    public PasteSpecialDialog()
    {
        Title = "Paste Special";
        Width = 360; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        _rbAll      = new RadioButton { Content = "All",            IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        _rbValues   = new RadioButton { Content = "Values only",    Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulas = new RadioButton { Content = "Formulas only",  Margin = new Thickness(0, 0, 0, 6) };
        _rbFormats  = new RadioButton { Content = "Formats only",   Margin = new Thickness(0, 0, 0, 6) };
        _rbComments = new RadioButton { Content = "Comments and notes", Margin = new Thickness(0, 0, 0, 6) };
        _rbValidation = new RadioButton { Content = "Validation", Margin = new Thickness(0, 0, 0, 6) };
        _rbAllExceptBorders = new RadioButton { Content = "All except borders", Margin = new Thickness(0, 0, 0, 6) };
        _rbColumnWidths = new RadioButton { Content = "Column widths", Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulasAndNumberFormats = new RadioButton { Content = "Formulas and number formats", Margin = new Thickness(0, 0, 0, 6) };
        _rbValuesAndNumberFormats = new RadioButton { Content = "Values and number formats", Margin = new Thickness(0, 0, 0, 6) };
        _rbPicture  = new RadioButton { Content = "Picture",        Margin = new Thickness(0, 0, 0, 12) };
        _rbLinkedPicture = new RadioButton { Content = "Linked picture", Margin = new Thickness(0, 0, 0, 12) };
        _pasteLink  = new CheckBox { Content = "Paste Link", Margin = new Thickness(0, 0, 0, 8) };
        _skipBlanks = new CheckBox { Content = "Skip blanks", Margin = new Thickness(0, 0, 0, 8) };
        _transpose  = new CheckBox { Content = "Transpose", Margin = new Thickness(0, 4, 0, 8) };
        _keepColumnWidths = new CheckBox { Content = "Keep source column widths", Margin = new Thickness(0, 0, 0, 8) };
        _operation  = new ComboBox { Margin = new Thickness(0, 0, 0, 12), Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var op in new[] { "None", "Add", "Subtract", "Multiply", "Divide" })
            _operation.Items.Add(new ComboBoxItem { Content = op });
        _operation.SelectedIndex = 0;

        stack.Children.Add(_rbAll);
        stack.Children.Add(_rbValues);
        stack.Children.Add(_rbFormulas);
        stack.Children.Add(_rbFormats);
        stack.Children.Add(_rbComments);
        stack.Children.Add(_rbValidation);
        stack.Children.Add(_rbAllExceptBorders);
        stack.Children.Add(_rbColumnWidths);
        stack.Children.Add(_rbFormulasAndNumberFormats);
        stack.Children.Add(_rbValuesAndNumberFormats);
        stack.Children.Add(_rbPicture);
        stack.Children.Add(_rbLinkedPicture);
        stack.Children.Add(_pasteLink);
        stack.Children.Add(_skipBlanks);
        stack.Children.Add(_transpose);
        stack.Children.Add(_keepColumnWidths);
        stack.Children.Add(new TextBlock { Text = "Operation", Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(_operation);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
    }
}
