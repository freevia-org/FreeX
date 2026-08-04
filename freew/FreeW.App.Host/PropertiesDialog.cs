using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using Free.Shared.Opc;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small modal editor for the document's core metadata (docProps/core.xml). Shows and edits
/// the editable core fields and reports Word's read-only save timestamps/identity. It returns an immutable payload on OK. The editor
/// applies that payload through its undo stack. Code-only to match the rest of the FreeW window style.
/// </summary>
internal sealed class PropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _title = new() { MinWidth = 280 };
    private readonly TextBox _author = new() { MinWidth = 280 };
    private readonly TextBox _subject = new() { MinWidth = 280 };
    private readonly TextBox _keywords = new() { MinWidth = 280 };
    private readonly TextBox _category = new() { MinWidth = 280 };
    private readonly TextBox _contentStatus = new() { MinWidth = 280 };
    private readonly TextBox _language = new() { MinWidth = 280 };
    private readonly TextBox _version = new() { MinWidth = 280 };
    private readonly TextBox _comments = new()
    {
        MinWidth = 280,
        MinHeight = 60,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Properties;

    public DocumentPropertiesDialogValues? Result { get; private set; }

    public PropertiesDialog(Window owner, DocumentProperties properties)
    {
        Owner = owner;
        Title = "Document Properties";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _title.Text = properties.Title ?? string.Empty;
        _author.Text = properties.Author ?? string.Empty;
        _subject.Text = properties.Subject ?? string.Empty;
        _keywords.Text = properties.Keywords ?? string.Empty;
        _comments.Text = properties.Comments ?? string.Empty;
        _category.Text = properties.Category ?? string.Empty;
        _contentStatus.Text = properties.ContentStatus ?? string.Empty;
        _language.Text = properties.Language ?? string.Empty;
        _version.Text = properties.Version ?? string.Empty;
        AutomationProperties.SetAutomationId(_title, FocusPlan.InitialFocusTargetAutomationId);
        AutomationProperties.SetAutomationId(_author, "DocumentPropertiesAuthor");
        AutomationProperties.SetAutomationId(_subject, "DocumentPropertiesSubject");
        AutomationProperties.SetAutomationId(_category, "DocumentPropertiesCategory");
        AutomationProperties.SetAutomationId(_keywords, "DocumentPropertiesKeywords");
        AutomationProperties.SetAutomationId(_comments, "DocumentPropertiesComments");
        AutomationProperties.SetAutomationId(_contentStatus, "DocumentPropertiesContentStatus");
        AutomationProperties.SetAutomationId(_language, "DocumentPropertiesLanguage");
        AutomationProperties.SetAutomationId(_version, "DocumentPropertiesVersion");

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 12; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Title:", _title);
        AddRow(grid, 1, "Author:", _author);
        AddRow(grid, 2, "Subject:", _subject);
        AddRow(grid, 3, "Category:", _category);
        AddRow(grid, 4, "Keywords:", _keywords);
        AddRow(grid, 5, "Comments:", _comments);
        AddRow(grid, 6, "Status:", _contentStatus);
        AddRow(grid, 7, "Language:", _language);
        AddRow(grid, 8, "Version:", _version);
        AddRow(grid, 9, "Last saved by:", ReadOnlyValue(properties.LastModifiedBy, "DocumentPropertiesLastModifiedBy"));
        AddRow(grid, 10, "Created:", ReadOnlyValue(FormatDate(properties.Created), "DocumentPropertiesCreated"));
        AddRow(grid, 11, "Modified:", ReadOnlyValue(FormatDate(properties.Modified), "DocumentPropertiesModified"));

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
        if (FocusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(_title);
        else
            DialogFocus.Focus(_title);
    }

    private void Commit()
    {
        Result = DocumentPropertiesDialogValues.FromInput(
            _title.Text,
            _author.Text,
            _subject.Text,
            _keywords.Text,
            _comments.Text,
            _category.Text,
            _contentStatus.Text,
            _language.Text,
            _version.Text);
        DialogResult = true;
    }

    private static TextBlock ReadOnlyValue(string? value, string automationId)
    {
        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(text, automationId);
        return text;
    }

    private static string? FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

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
