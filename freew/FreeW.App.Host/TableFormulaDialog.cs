using System.Windows;
using System.Windows.Controls;
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
    // The functions offered in the "Paste function" picker, matching Word's common table formulas.
    private static readonly string[] Functions =
        ["SUM", "AVERAGE", "COUNT", "PRODUCT", "MIN", "MAX"];

    // A few common number-format pictures (Word's "Number format" dropdown), plus a blank "general" option.
    private static readonly string[] NumberFormats =
        ["", "0", "0.00", "#,##0", "#,##0.00", "0%", "$#,##0.00;($#,##0.00)"];

    private readonly TextBox _formula;
    private readonly ComboBox _format;
    private TableFormulaField? _result;

    private TableFormulaDialog(Window? owner, string defaultFormula)
    {
        Owner = owner;
        Title = "Formula";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock { Text = "Formula:", Margin = new Thickness(0, 0, 0, 4) });
        _formula = new TextBox { Text = defaultFormula };
        panel.Children.Add(_formula);

        panel.Children.Add(new TextBlock { Text = "Number format:", Margin = new Thickness(0, 10, 0, 4) });
        _format = new ComboBox { IsEditable = true };
        foreach (var format in NumberFormats)
            _format.Items.Add(format);
        _format.SelectedIndex = 0;
        panel.Children.Add(_format);

        panel.Children.Add(new TextBlock { Text = "Paste function:", Margin = new Thickness(0, 10, 0, 4) });
        var function = new ComboBox();
        foreach (var name in Functions)
            function.Items.Add(name);
        // Selecting a function appends "NAME()" to the formula and parks the caret between the parentheses.
        function.SelectionChanged += (_, _) =>
        {
            if (function.SelectedItem is string name)
            {
                if (!_formula.Text.TrimStart().StartsWith('='))
                    _formula.Text = "=" + _formula.Text.Trim();
                _formula.Text += name + "()";
                _formula.Focus();
                _formula.CaretIndex = _formula.Text.Length - 1;
                function.SelectedIndex = -1;
            }
        };
        panel.Children.Add(function);

        // Reuse the shared OK/Cancel button row (accelerators, shell strings; Cancel is IsCancel for Esc).
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept, buttonWidth: 72, rowMargin: new Thickness(0, 14, 0, 0)));

        Content = panel;
        Loaded += (_, _) => DialogFocus.FocusAndSelect(_formula);
    }

    private void Accept()
    {
        var expression = _formula.Text.Trim();
        if (expression.Length == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Please enter a formula.", "Formula");
            DialogFocus.FocusAndSelect(_formula);
            return;
        }
        var format = (_format.Text ?? string.Empty).Trim();
        _result = new TableFormulaField(expression, string.IsNullOrEmpty(format) ? null : format);
        DialogResult = true;
    }

    /// <summary>
    /// Show the dialog seeded with <paramref name="defaultFormula"/> (e.g. <c>=SUM(ABOVE)</c>); returns the
    /// chosen <see cref="TableFormulaField"/>, or null if cancelled.
    /// </summary>
    public static TableFormulaField? Prompt(Window? owner, string defaultFormula)
    {
        var dialog = new TableFormulaDialog(owner, defaultFormula);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
