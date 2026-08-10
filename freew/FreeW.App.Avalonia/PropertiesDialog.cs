using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Globalization;
using Free.Shared.Opc;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Edits the DOCX core properties persisted by the shared OPC model.</summary>
internal sealed class PropertiesDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Properties;
    private readonly DocumentPropertiesDialogSession _session;
    private readonly Dictionary<DocumentPropertiesDialogField, TextBox> _editors = [];

    public bool Accepted { get; private set; }
    public DocumentPropertiesDialogValues? Result { get; private set; }

    public PropertiesDialog(DocumentProperties properties)
    {
        _session = new DocumentPropertiesDialogSession(properties, CultureInfo.CurrentCulture);

        Title = _session.Surface.Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "DocumentPropertiesDialog");

        var grid = new Grid
        {
            Margin = new Thickness(16, 12, 16, 8),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        for (var row = 0; row < _session.Surface.Fields.Count; row++)
        {
            var spec = _session.Surface.Fields[row];
            if (spec.IsEditable)
                AddRow(grid, row, spec, CreateEditor(spec));
            else
                AddReadOnlyRow(grid, row, spec);
        }

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "DocumentPropertiesOkButton");
        ok.Click += (_, _) => Commit();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 84);
        AutomationProperties.SetAutomationId(cancel, "DocumentPropertiesCancelButton");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Children =
            {
                grid,
                AvaloniaCompactDialogChrome.CreateActionRow(
                    [ok, cancel],
                    new Thickness(16, 4, 16, 14)),
            },
        };

        Opened += (_, _) => FocusTitle();
    }

    private void FocusTitle()
    {
        var title = _editors[DocumentPropertiesDialogField.Title];
        if (FocusPlan.SelectAllOnFocus)
            AvaloniaCompactDialogChrome.FocusAndSelect(title);
        else
            title.Focus();
    }

    private void Commit()
    {
        var plan = _session.PlanCommit(accepted: true, CaptureInput());
        if (!plan.ShouldExecuteCommand)
            return;

        Result = plan.Values;
        Accepted = true;
        Close();
    }

    private TextBox CreateEditor(DocumentPropertiesDialogFieldSpec spec)
    {
        var editor = new TextBox
        {
            MinWidth = 280,
            MinHeight = spec.IsMultiline ? 72 : 0,
            AcceptsReturn = spec.IsMultiline,
            TextWrapping = spec.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Text = spec.Value,
        };
        AutomationProperties.SetAutomationId(editor, spec.AutomationId);
        _editors.Add(spec.Field, editor);
        return editor;
    }

    private static void AddRow(
        Grid grid,
        int row,
        DocumentPropertiesDialogFieldSpec spec,
        TextBox field)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AvaloniaCompactDialogChrome.ApplyTextBox(field, DialogChromeStyle);
        field.Margin = new Thickness(0, 4, 0, 4);

        var caption = new TextBlock
        {
            Text = spec.Label,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
        };
        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        grid.Children.Add(caption);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static void AddReadOnlyRow(Grid grid, int row, DocumentPropertiesDialogFieldSpec spec)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var field = new TextBlock
        {
            Text = spec.Value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
        };
        AutomationProperties.SetAutomationId(field, spec.AutomationId);

        var caption = new TextBlock
        {
            Text = spec.Label,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
        };
        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        grid.Children.Add(caption);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private DocumentPropertiesDialogInput CaptureInput() =>
        DocumentPropertiesDialogInput.Capture(Text);

    private string? Text(DocumentPropertiesDialogField field) => _editors[field].Text;

}
