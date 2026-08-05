using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using Free.Shared.Opc;

namespace FreeW.App.Host;

/// <summary>
/// A small modal editor for the document's core metadata (docProps/core.xml). Shows and edits
/// the editable core fields and reports Word's read-only save timestamps/identity. It returns an immutable payload on OK. The editor
/// applies that payload through its undo stack. Code-only to match the rest of the FreeW window style.
/// </summary>
internal sealed class PropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Properties;
    private readonly DocumentPropertiesDialogSession _session;
    private readonly Dictionary<DocumentPropertiesDialogField, TextBox> _editors = [];

    public DocumentPropertiesDialogValues? Result { get; private set; }

    public PropertiesDialog(Window owner, DocumentProperties properties)
    {
        _session = new DocumentPropertiesDialogSession(properties, CultureInfo.CurrentCulture);
        Owner = owner;
        Title = _session.Surface.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < _session.Surface.Fields.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var row = 0; row < _session.Surface.Fields.Count; row++)
        {
            var spec = _session.Surface.Fields[row];
            FrameworkElement field = spec.IsEditable ? CreateEditor(spec) : ReadOnlyValue(spec);
            AddRow(grid, row, spec.Label, field);
        }

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Commit, buttonWidth: 84, rowMargin: new Thickness(14, 0, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(buttons);
        Content = outer;
        Loaded += (_, _) => FocusTitle();
    }

    private void FocusTitle()
    {
        var title = _editors[DocumentPropertiesDialogField.Title];
        if (FocusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(title);
        else
            DialogFocus.Focus(title);
    }

    private void Commit()
    {
        var plan = _session.PlanCommit(accepted: true, CaptureInput());
        if (!plan.ShouldExecuteCommand)
            return;

        Result = plan.Values;
        DialogResult = true;
    }

    private TextBox CreateEditor(DocumentPropertiesDialogFieldSpec spec)
    {
        var editor = new TextBox
        {
            MinWidth = 280,
            Text = spec.Value,
            MinHeight = spec.IsMultiline ? 60 : 0,
            AcceptsReturn = spec.IsMultiline,
            TextWrapping = spec.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = spec.IsMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
        };
        AutomationProperties.SetAutomationId(editor, spec.AutomationId);
        _editors.Add(spec.Field, editor);
        return editor;
    }

    private static TextBlock ReadOnlyValue(DocumentPropertiesDialogFieldSpec spec)
    {
        var text = new TextBlock
        {
            Text = spec.Value,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(text, spec.AutomationId);
        return text;
    }

    private DocumentPropertiesDialogInput CaptureInput() =>
        new(
            Text(DocumentPropertiesDialogField.Title),
            Text(DocumentPropertiesDialogField.Author),
            Text(DocumentPropertiesDialogField.Subject),
            Text(DocumentPropertiesDialogField.Keywords),
            Text(DocumentPropertiesDialogField.Comments),
            Text(DocumentPropertiesDialogField.Category),
            Text(DocumentPropertiesDialogField.ContentStatus),
            Text(DocumentPropertiesDialogField.Language),
            Text(DocumentPropertiesDialogField.Version));

    private string? Text(DocumentPropertiesDialogField field) => _editors[field].Text;

    private static void AddRow(Grid grid, int row, string label, FrameworkElement field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        field.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }
}
