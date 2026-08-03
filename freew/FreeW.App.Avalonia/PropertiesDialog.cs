using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Opc;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Edits the DOCX core properties persisted by the shared OPC model.</summary>
internal sealed class PropertiesDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly DocumentProperties _properties;
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Properties;
    private readonly TextBox _title = new() { MinWidth = 280 };
    private readonly TextBox _author = new() { MinWidth = 280 };
    private readonly TextBox _subject = new() { MinWidth = 280 };
    private readonly TextBox _keywords = new() { MinWidth = 280 };
    private readonly TextBox _comments = new()
    {
        MinWidth = 280,
        MinHeight = 72,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };

    public bool Accepted { get; private set; }

    public PropertiesDialog(DocumentProperties properties)
    {
        _properties = properties ?? throw new ArgumentNullException(nameof(properties));

        Title = "Document Properties";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "DocumentPropertiesDialog");

        _title.Text = properties.Title ?? string.Empty;
        _author.Text = properties.Author ?? string.Empty;
        _subject.Text = properties.Subject ?? string.Empty;
        _keywords.Text = properties.Keywords ?? string.Empty;
        _comments.Text = properties.Comments ?? string.Empty;

        var grid = new Grid
        {
            Margin = new Thickness(16, 12, 16, 8),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        AddRow(grid, 0, "Title:", _title, FocusPlan.InitialFocusTargetAutomationId);
        AddRow(grid, 1, "Author:", _author, "DocumentPropertiesAuthor");
        AddRow(grid, 2, "Subject:", _subject, "DocumentPropertiesSubject");
        AddRow(grid, 3, "Keywords:", _keywords, "DocumentPropertiesKeywords");
        AddRow(grid, 4, "Comments:", _comments, "DocumentPropertiesComments");

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
        if (FocusPlan.SelectAllOnFocus)
            AvaloniaCompactDialogChrome.FocusAndSelect(_title);
        else
            _title.Focus();
    }

    private void Commit()
    {
        _properties.Title = Normalize(_title.Text);
        _properties.Author = Normalize(_author.Text);
        _properties.Subject = Normalize(_subject.Text);
        _properties.Keywords = Normalize(_keywords.Text);
        _properties.Comments = Normalize(_comments.Text);
        Accepted = true;
        Close();
    }

    private static void AddRow(Grid grid, int row, string label, TextBox field, string automationId)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AvaloniaCompactDialogChrome.ApplyTextBox(field, DialogChromeStyle);
        field.Margin = new Thickness(0, 4, 0, 4);
        AutomationProperties.SetAutomationId(field, automationId);

        var caption = new TextBlock
        {
            Text = label,
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
