using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Opc;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small modal editor for the document's core metadata (docProps/core.xml). Shows and edits
/// Title / Author / Subject / Keywords / Comments and, on OK, writes the values straight back to
/// <see cref="DocumentProperties"/> so the next save round-trips them. Code-only to match the rest
/// of the FreeW window style.
/// </summary>
internal sealed class PropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly DocumentProperties _properties;
    private readonly TextBox _title = new() { MinWidth = 280 };
    private readonly TextBox _author = new() { MinWidth = 280 };
    private readonly TextBox _subject = new() { MinWidth = 280 };
    private readonly TextBox _keywords = new() { MinWidth = 280 };
    private readonly TextBox _comments = new()
    {
        MinWidth = 280,
        MinHeight = 60,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Properties;

    public PropertiesDialog(Window owner, DocumentProperties properties)
    {
        _properties = properties;
        Owner = owner;
        Title = "Document Properties";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _title.Text = properties.Title ?? string.Empty;
        _author.Text = properties.Author ?? string.Empty;
        _subject.Text = properties.Subject ?? string.Empty;
        _keywords.Text = properties.Keywords ?? string.Empty;
        _comments.Text = properties.Comments ?? string.Empty;
        AutomationProperties.SetAutomationId(_title, FocusPlan.InitialFocusTargetAutomationId);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Title:", _title);
        AddRow(grid, 1, "Author:", _author);
        AddRow(grid, 2, "Subject:", _subject);
        AddRow(grid, 3, "Keywords:", _keywords);
        AddRow(grid, 4, "Comments:", _comments);

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
        _properties.Title = Normalize(_title.Text);
        _properties.Author = Normalize(_author.Text);
        _properties.Subject = Normalize(_subject.Text);
        _properties.Keywords = Normalize(_keywords.Text);
        _properties.Comments = Normalize(_comments.Text);
        DialogResult = true;
    }

    private static string? Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
