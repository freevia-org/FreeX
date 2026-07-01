using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Compact Avalonia editor for the FreeW options that the cross-platform shell consumes today.
/// Parsing and normalization stay in <see cref="OptionsDialogPlanner"/>.
/// </summary>
internal sealed class OptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly FreeWOptions _seed;
    private readonly TextBox _recentFilesCap = new() { Width = 72 };
    private readonly ComboBox _defaultFormat = new() { Width = 180 };
    private readonly TextBox _uiLanguage = new() { Width = 180 };
    private readonly TextBlock _status = new();

    public FreeWOptions? Result { get; private set; }

    public OptionsDialog(FreeWOptions options)
    {
        _seed = options ?? new FreeWOptions();

        Title = "FreeW Options";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _recentFilesCap.Text = _seed.RecentFilesCap.ToString();
        _defaultFormat.ItemsSource = new[] { new FormatChoice("Word Document (*.docx)", FreeWOptions.DocxDefaultFormat) };
        _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _seed.UiLanguage;

        AvaloniaCompactDialogChrome.ApplyTextBox(_recentFilesCap, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_defaultFormat, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_uiLanguage, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));

        var grid = new Grid
        {
            Margin = new Thickness(16, 16, 16, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        AddRow(grid, 0, "Recent files to keep:", _recentFilesCap);
        AddRow(grid, 1, "Default save format:", _defaultFormat);
        AddRow(grid, 2, "UI language:", _uiLanguage);

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                buttons,
                new StackPanel { Children = { grid, _status } },
            },
        };
    }

    private void Accept()
    {
        _status.IsVisible = false;
        if (!OptionsDialogPlanner.TryParseRecentFilesCap(_recentFilesCap.Text, out var cap))
        {
            _status.Text = $"Enter a whole number between {FreeWOptions.MinRecentFilesCap} and {FreeWOptions.MaxRecentFilesCap}.";
            _status.IsVisible = true;
            _recentFilesCap.Focus();
            return;
        }

        var format = (_defaultFormat.SelectedItem as FormatChoice)?.Extension;
        Result = OptionsDialogPlanner.BuildResult(
            cap,
            format,
            _uiLanguage.Text,
            _seed.AutoCorrectEnabled,
            _seed.AutoFormat ?? AutoFormatOptions.Default,
            _seed.AutoCorrect ?? AutoCorrectOptions.Default);
        Close();
    }

    private static void AddRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);

        field.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);

        grid.Children.Add(text);
        grid.Children.Add(field);
    }

    private sealed record FormatChoice(string Label, string Extension)
    {
        public override string ToString() => Label;
    }
}
