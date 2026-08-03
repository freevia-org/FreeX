using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's Table &gt; Data &gt; Formula dialog: a formula box (seeded with a sensible default such as
/// <c>=SUM(ABOVE)</c>), a number-format box, and a "Paste function" helper that appends a function name to
/// the formula. Returns the chosen <see cref="TableFormulaField"/> (expression + optional number format),
/// or null when cancelled.
///
/// The formula evaluation itself lives in the pure, testable <see cref="TableFormulaEvaluator"/> in the
/// model project; this dialog only gathers the formula text and format.
/// </summary>
internal sealed class TableFormulaDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _formula;
    private readonly ComboBox _format;
    private TableFormulaField? _result;
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.TableFormula;

    private TableFormulaDialog(Window? owner, TableFormulaDialogInitialState initialState)
    {
        Owner = owner;
        Title = TableFormulaDialogPlanner.Title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock { Text = TableFormulaDialogPlanner.FormulaLabel, Margin = new Thickness(0, 0, 0, 4) });
        _formula = new TextBox { Text = initialState.FormulaText };
        AutomationProperties.SetAutomationId(_formula, FocusPlan.InitialFocusTargetAutomationId);
        panel.Children.Add(_formula);

        panel.Children.Add(new TextBlock { Text = TableFormulaDialogPlanner.NumberFormatLabel, Margin = new Thickness(0, 10, 0, 4) });
        _format = new ComboBox { IsEditable = true };
        foreach (var format in TableFormulaDialogPlanner.NumberFormats)
            _format.Items.Add(format);
        _format.SelectedIndex = Math.Clamp(initialState.NumberFormatIndex, 0, _format.Items.Count - 1);
        panel.Children.Add(_format);

        panel.Children.Add(new TextBlock { Text = TableFormulaDialogPlanner.PasteFunctionLabel, Margin = new Thickness(0, 10, 0, 4) });
        var function = new ComboBox();
        foreach (var name in TableFormulaDialogPlanner.Functions)
            function.Items.Add(name);
        function.SelectionChanged += (_, _) =>
        {
            if (function.SelectedItem is string name)
            {
                var pasted = TableFormulaDialogPlanner.PasteFunction(_formula.Text, name);
                _formula.Text = pasted.Text;
                _formula.Focus();
                _formula.CaretIndex = pasted.CaretIndex;
                function.SelectedIndex = -1;
            }
        };
        panel.Children.Add(function);

        // Reuse the shared OK/Cancel button row (accelerators, shell strings; Cancel is IsCancel for Esc).
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept, buttonWidth: 72, rowMargin: new Thickness(0, 14, 0, 0)));

        Content = panel;
        Loaded += (_, _) => FocusFormula();
    }

    private void FocusFormula()
    {
        if (FocusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(_formula);
        else
            DialogFocus.Focus(_formula);
    }

    private void Accept()
    {
        if (!TableFormulaDialogPlanner.TryBuildResult(
                new TableFormulaDialogInput(_formula.Text, _format.Text),
                out var result,
                out var errorMessage))
        {
            DialogMessageHelper.ShowWarning(this, errorMessage ?? TableFormulaDialogPlanner.ValidationMessage, TableFormulaDialogPlanner.Title);
            FocusFormula();
            return;
        }

        _result = result;
        DialogResult = true;
    }

    /// <summary>
    /// Show the dialog seeded with <paramref name="initialState"/> (e.g. <c>=SUM(ABOVE)</c>); returns the
    /// chosen <see cref="TableFormulaField"/>, or null if cancelled.
    /// </summary>
    public static TableFormulaField? Prompt(Window? owner, TableFormulaDialogInitialState initialState)
    {
        var dialog = new TableFormulaDialog(owner, initialState);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
