using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableDataSourceDialogResult(string SourceRangeText);
public sealed record PivotTableDataSourceRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed class PivotTableDataSourceDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _sourceBox = new();
    private readonly Func<string, SheetId?> _resolveSheetId;
    private readonly Action<PivotTableDataSourceRangeSelectionRequest>? _requestRangeSelection;

    public PivotTableDataSourceDialogResult Result { get; private set; }
    public PivotTableDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PivotTableDataSourceDialog(
        string sourceRangeText,
        Action<PivotTableDataSourceRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default,
        Func<string, SheetId?>? resolveSheetId = null)
    {
        _sheetId = sheetId;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(sourceRangeText);
        Title = "Change PivotTable Data Source";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sourceBox.Text = Result.SourceRangeText;
        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());

    public static PivotTableDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(string rangeText)
    {
        _sourceBox.Text = rangeText;
        FocusRangeSelectionInput(_sourceBox);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        PivotDialogLayout.AddLabeledControl(
            stack,
            "Table/_Range:",
            CreateReferenceEditor(_sourceBox, "Select PivotTable source range"),
            _sourceBox,
            new Thickness(0, 0, 0, 16));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(() =>
        {
            if (!ValidateInputs())
                return;

            Result = CreateResult(_sourceBox.Text);
            DialogResult = true;
        }));
        return stack;
    }

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
                FocusRangeSelectionInput(request.Target);
            });

    private bool ValidateInputs()
    {
        if (!WorkbookRangeTextCodec.TryParse(_sheetId, _sourceBox.Text, ResolveSheetIdByName, out _))
        {
            ShowInvalidInputWarning("Enter a valid PivotTable source range.", _sourceBox);
            return false;
        }

        return true;
    }

    private SheetId? ResolveSheetIdByName(string sheetName) => _resolveSheetId(sheetName);

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        FocusRangeSelectionInput(target);
        return false;
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void FocusInitialKeyboardTarget()
    {
        _sourceBox.Focus();
        _sourceBox.SelectAll();
        Keyboard.Focus(_sourceBox);
    }
}

internal static class PivotDialogLayout
{
    public static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 76, rowMargin: new Thickness(0, 12, 0, 0));

    public static GroupBox CreateGroupBox(string header, UIElement content, Thickness? margin = null) => new()
    {
        Header = header,
        Content = content,
        Margin = margin ?? new Thickness(0, 0, 0, 12)
    };

    public static StackPanel CreateGroupPanel() => new() { Margin = new Thickness(10, 8, 10, 10) };

    public static void AddLabeledControl(Panel stack, string label, UIElement control) =>
        AddLabeledControl(stack, label, control, control, new Thickness(0, 0, 0, 8));

    public static void AddLabeledControl(
        Panel stack,
        string label,
        UIElement content,
        UIElement target,
        Thickness margin)
    {
        stack.Children.Add(new Label
        {
            Content = label,
            Target = target,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 0, 4)
        });

        if (content is FrameworkElement frameworkElement)
            frameworkElement.Margin = margin;

        stack.Children.Add(content);
    }
}
