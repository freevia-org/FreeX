using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell.Wpf;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The in-place equation editor: a small modeless window showing the equation's linear source with a
/// live built-up preview drawn by the document's own equation renderer. Enter (OK) commits, Esc cancels.
/// All rules live in <see cref="EquationEditorSession"/>; this class only binds controls to it.
/// </summary>
internal sealed class EquationEditorWindow : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EquationEditorSession _session;
    private readonly EquationEditorSurface _surface;
    private readonly TextBox _linearText;
    private readonly Border _preview;
    private readonly RadioButton _inline;
    private readonly RadioButton _display;
    private readonly ComboBox _justification;
    private readonly TextBlock _opaqueNotice;
    private bool _updating;

    public EquationEditorWindow(EquationEditorSession session, Func<string, string?>? getText = null)
    {
        _session = session;
        _surface = EquationEditorText.BuildSurface(getText);

        Title = _surface.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, EquationEditorSurface.AutomationId);
        AutomationProperties.SetName(this, _surface.Title);

        _preview = new Border
        {
            MinHeight = 64,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 10),
            Background = Brushes.White,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(_preview, EquationEditorSurface.PreviewAutomationId);
        AutomationProperties.SetName(_preview, _surface.PreviewLabel);

        _linearText = new TextBox
        {
            Text = session.Text,
            FontFamily = new FontFamily(EquationVisualPlanner.DefaultMathFontFamily),
            FontSize = 16,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 4, 0, 6),
            AcceptsReturn = false,
        };
        AutomationProperties.SetAutomationId(_linearText, EquationEditorSurface.LinearTextAutomationId);
        AutomationProperties.SetName(_linearText, _surface.LinearLabel);
        _linearText.TextChanged += (_, _) => OnTextChanged();

        _inline = new RadioButton
        {
            Content = _surface.InlineLabel,
            GroupName = "EquationLayout",
            IsChecked = !session.IsDisplay,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_inline, EquationEditorSurface.InlineAutomationId);
        _display = new RadioButton
        {
            Content = _surface.DisplayLabel,
            GroupName = "EquationLayout",
            IsChecked = session.IsDisplay,
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_display, EquationEditorSurface.DisplayAutomationId);
        _inline.Checked += (_, _) => OnLayoutChanged();
        _display.Checked += (_, _) => OnLayoutChanged();

        _justification = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_justification, EquationEditorSurface.JustificationAutomationId);
        AutomationProperties.SetName(_justification, _surface.JustificationLabel);
        foreach (var (justification, label) in _surface.Justifications)
            _justification.Items.Add(new ComboBoxItem { Content = label, Tag = justification });
        SelectJustification(session.Justification);
        _justification.SelectionChanged += (_, _) => OnLayoutChanged();

        _opaqueNotice = new TextBlock
        {
            Text = _surface.OpaquePartsNotice,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = session.HasOpaqueParts ? Visibility.Visible : Visibility.Collapsed,
        };

        var layoutRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        layoutRow.Children.Add(_inline);
        layoutRow.Children.Add(_display);
        layoutRow.Children.Add(new TextBlock
        {
            Text = _surface.JustificationLabel,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        layoutRow.Children.Add(_justification);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 6, 0, 0));
        if (buttons.Children.Count > 1 && buttons.Children[1] is Button cancel)
            cancel.Click += (_, _) => Close();

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = _surface.PreviewLabel });
        panel.Children.Add(_preview);
        panel.Children.Add(new TextBlock { Text = _surface.LinearLabel });
        panel.Children.Add(_linearText);
        panel.Children.Add(_opaqueNotice);
        panel.Children.Add(layoutRow);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.Hint,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });
        panel.Children.Add(buttons);
        Content = panel;

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) =>
        {
            _linearText.Focus();
            _linearText.CaretIndex = _linearText.Text.Length;
            Keyboard.Focus(_linearText);
        };

        UpdateLayoutControls();
        UpdatePreview();
    }

    /// <summary>Raised once when OK commits a change; the window closes right after.</summary>
    public event Action<EquationEditorCommit>? Committed;

    /// <summary>The session this window edits.</summary>
    internal EquationEditorSession Session => _session;

    /// <summary>The linear text box (for tests and focus routing).</summary>
    internal TextBox LinearTextBox => _linearText;

    /// <summary>The element currently shown in the preview (a placeholder prompt while the text is empty).</summary>
    internal UIElement? PreviewContent => _preview.Child;

    /// <summary>Inserts a symbol or structure template over the text box selection.</summary>
    public void InsertText(string snippet)
    {
        var edit = EquationEditorSession.Insert(
            _linearText.Text,
            _linearText.SelectionStart,
            _linearText.SelectionLength,
            snippet);
        ApplyEdit(edit);
        Activate();
        _linearText.Focus();
    }

    /// <summary>Commits the edit (if there is one) and closes — what OK and Enter do.</summary>
    internal void Accept()
    {
        var commit = _session.Commit();
        Close();
        if (commit is not null)
            Committed?.Invoke(commit);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key is Key.Enter or Key.Return && Keyboard.Modifiers == ModifierKeys.None && !_justification.IsDropDownOpen)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void OnTextChanged()
    {
        if (_updating)
            return;

        // Word's math autocorrect: a space typed after \name turns the name into its character.
        if (EquationEditorSession.AutoCorrect(_linearText.Text, _linearText.CaretIndex) is { } corrected)
        {
            ApplyEdit(corrected);
            return;
        }

        _session.SetText(_linearText.Text);
        UpdatePreview();
    }

    private void ApplyEdit(EquationEditorTextEdit edit)
    {
        _updating = true;
        try
        {
            _linearText.Text = edit.Text;
            _linearText.Select(edit.SelectionStart, edit.SelectionLength);
        }
        finally
        {
            _updating = false;
        }

        _session.SetText(edit.Text);
        UpdatePreview();
    }

    private void OnLayoutChanged()
    {
        if (_updating)
            return;

        _session.IsDisplay = _display.IsChecked == true;
        if (_justification.SelectedItem is ComboBoxItem { Tag: MathJustification justification })
            _session.Justification = justification;
        UpdateLayoutControls();
        UpdatePreview();
    }

    private void UpdateLayoutControls() => _justification.IsEnabled = _session.IsDisplay;

    private void SelectJustification(MathJustification justification)
    {
        foreach (var item in _justification.Items)
        {
            if (item is ComboBoxItem { Tag: MathJustification candidate } && candidate == justification)
            {
                _justification.SelectedItem = item;
                return;
            }
        }

        _justification.SelectedIndex = 0;
    }

    private void UpdatePreview()
    {
        var equation = _session.BuildEquation();
        if (equation.Runs.Count == 0)
        {
            _preview.Child = new TextBlock
            {
                Text = _surface.EmptyPreview,
                Opacity = 0.55,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            return;
        }

        var visual = DocumentView.BuildEquationPreview(equation);
        visual.VerticalAlignment = VerticalAlignment.Center;
        visual.HorizontalAlignment = !_session.IsDisplay
            ? HorizontalAlignment.Left
            : _session.Justification switch
            {
                MathJustification.Left => HorizontalAlignment.Left,
                MathJustification.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center,
            };
        TextElementFontSize(visual);
        _preview.Child = visual;
    }

    /// <summary>The preview is read at a comfortable size regardless of the document's font size.</summary>
    private static void TextElementFontSize(FrameworkElement visual) =>
        System.Windows.Documents.TextElement.SetFontSize(visual, 20);
}
