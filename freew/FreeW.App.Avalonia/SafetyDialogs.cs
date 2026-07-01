using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed record InspectorRemovalChoice(bool Comments, bool Revisions, bool Properties, bool Bookmarks)
{
    public bool HasAnySelection => Comments || Revisions || Properties || Bookmarks;
}

internal sealed class RestrictEditingDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private static readonly IReadOnlyList<ModeChoice> Choices =
    [
        new("No protection", ProtectionMode.None),
        new("Read-only (no changes)", ProtectionMode.ReadOnly),
        new("Track changes only", ProtectionMode.TrackChangesOnly),
    ];

    private readonly ComboBox _modeBox = new() { Width = 210 };

    public ProtectionSettings? Result { get; private set; }

    public RestrictEditingDialog(ProtectionSettings current)
    {
        Title = "Restrict Editing";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _modeBox.ItemsSource = Choices;
        _modeBox.SelectedIndex = Math.Max(0, Choices.ToList().FindIndex(choice => choice.Mode == current.Mode));
        AvaloniaCompactDialogChrome.ApplyComboBox(_modeBox, DialogChromeStyle);

        var grid = new Grid
        {
            Margin = new Thickness(16, 16, 16, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var label = new TextBlock
        {
            Text = "Editing restrictions:",
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 0);
        Grid.SetRow(_modeBox, 0);
        Grid.SetColumn(_modeBox, 1);
        grid.Children.Add(label);
        grid.Children.Add(_modeBox);

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            Result = new ProtectionSettings(((_modeBox.SelectedItem as ModeChoice) ?? Choices[0]).Mode);
            Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { LastChildFill = true, Children = { buttons, grid } };
    }

    private sealed record ModeChoice(string Label, ProtectionMode Mode)
    {
        public override string ToString() => Label;
    }
}

internal sealed class DocumentInspectorDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly CheckBox _comments;
    private readonly CheckBox _revisions;
    private readonly CheckBox _properties;
    private readonly CheckBox _bookmarks;

    public InspectorRemovalChoice? Choice { get; private set; }

    public DocumentInspectorDialog(InspectionResult result)
    {
        Title = "Document Inspector";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var body = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Spacing = 6,
        };

        if (result.IsClean)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No comments, revisions, document properties, or bookmarks were found.",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        _comments = AddCheck(body, "Comments", result.Comments);
        _revisions = AddCheck(body, "Revisions", result.Revisions);
        _properties = AddCheck(body, "Document properties", result.NonEmptyProperties);
        _bookmarks = AddCheck(body, "Bookmarks", result.Bookmarks);

        var remove = new Button { Content = result.IsClean ? "OK" : "Remove", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(remove, DialogChromeStyle, minWidth: 72, isDefault: true);
        remove.Click += (_, _) =>
        {
            Choice = result.IsClean
                ? new InspectorRemovalChoice(false, false, false, false)
                : new InspectorRemovalChoice(
                    _comments.IsChecked == true,
                    _revisions.IsChecked == true,
                    _properties.IsChecked == true,
                    _bookmarks.IsChecked == true);
            Close();
        };

        IReadOnlyList<Control> controls = result.IsClean
            ? [remove]
            : [remove, CreateCancelButton()];
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(controls, new Thickness(16, 12, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel { LastChildFill = true, Children = { buttons, body } };
    }

    private static CheckBox AddCheck(Panel body, string label, int count)
    {
        var box = new CheckBox
        {
            Content = $"{label}: {count}",
            IsChecked = count > 0,
            IsEnabled = count > 0,
        };
        AvaloniaCompactDialogChrome.ApplyCheckBox(box, DialogChromeStyle);
        body.Children.Add(box);
        return box;
    }

    private Button CreateCancelButton()
    {
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();
        return cancel;
    }
}

internal sealed class AccessibilityReportDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    public AccessibilityReportDialog(AccessibilityReport report)
    {
        Title = "Accessibility Checker";
        Width = 480;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        var body = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Spacing = 8,
        };

        body.Children.Add(new TextBlock
        {
            Text = report.IsClean
                ? "No accessibility issues were found."
                : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.TipCount} tip(s)",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var issue in report.Issues)
            body.Children.Add(new TextBlock
            {
                Text = $"{issue.Severity}: {issue.Message}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Close();
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok], new Thickness(16, 12, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                buttons,
                new ScrollViewer
                {
                    Content = body,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            },
        };
    }
}
