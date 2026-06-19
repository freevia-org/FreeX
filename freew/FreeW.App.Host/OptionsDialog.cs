using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's modal settings editor, backed by <see cref="FreeWOptions"/>. It edits the real persisted
/// settings the model exposes today — the recent-files cap, the default save format, and the UI language
/// override — and nothing it cannot persist. On OK it builds a normalized <see cref="Result"/> options
/// object; the host then applies it live and saves it through the shared <c>JsonSettingsStore</c>.
///
/// <para>
/// Code-only to match the rest of the FreeW window style (see <see cref="PropertiesDialog"/>). The button
/// row, automatic content sizing, and initial focus come from the shared dialog helpers in
/// <c>Free.Shared.Shell</c>, and the surface itself from <c>Free.Shared.Ribbon.Wpf.DialogWindow</c>, so no
/// chrome is re-authored here.
/// </para>
/// </summary>
internal sealed class OptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly FreeWOptions _options;

    private readonly TextBox _recentFilesCap = new() { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>The normalized options produced on OK; equals the input options on Cancel.</summary>
    public FreeWOptions Result { get; private set; }

    public OptionsDialog(Window owner, FreeWOptions options)
    {
        _options = options ?? new FreeWOptions();
        Result = _options;

        Owner = owner;
        Title = "FreeW Options";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // The single .docx format FreeW ships today, surfaced as a (currently single-entry) picker so the
        // setting reads honestly and is ready to grow. The Tag carries the persisted extension value.
        _defaultFormat.Items.Add(new ComboBoxItem { Content = "Word Document (*.docx)", Tag = FreeWOptions.DocxDefaultFormat });
        _defaultFormat.SelectedIndex = 0;

        _recentFilesCap.Text = _options.RecentFilesCap.ToString(CultureInfo.CurrentCulture);
        _uiLanguage.Text = _options.UiLanguage;

        var grid = new Grid { Margin = new Thickness(16, 16, 16, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Recent files to keep:", _recentFilesCap);
        AddRow(grid, 1, "Default save format:", _defaultFormat);
        AddRow(grid, 2, "UI language:", _uiLanguage, hint: $"Empty = follow the system culture (currently {SystemLanguageLabel()}).");

        var buttons = DialogButtonRowFactory.Create(Commit, buttonWidth: 84, rowMargin: new Thickness(16, 0, 16, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(buttons);
        Content = outer;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_recentFilesCap);
    }

    private void Commit()
    {
        if (!OptionsDialogPlanner.TryParseRecentFilesCap(_recentFilesCap.Text, out var cap))
        {
            DialogMessageHelper.ShowWarning(
                this,
                $"Enter a whole number between {FreeWOptions.MinRecentFilesCap} and {FreeWOptions.MaxRecentFilesCap} for the recent-files count.",
                Title);
            DialogFocus.FocusAndSelect(_recentFilesCap);
            return;
        }

        var format = (_defaultFormat.SelectedItem as ComboBoxItem)?.Tag as string;
        Result = OptionsDialogPlanner.BuildResult(cap, format, _uiLanguage.Text);
        DialogResult = true;
    }

    private static string SystemLanguageLabel()
    {
        var name = CultureInfo.CurrentCulture.Name;
        return string.IsNullOrEmpty(name) ? "invariant" : name;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement field, string? hint = null)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 12, 0)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        field.Margin = new Thickness(0, 8, 0, 0);

        if (hint is null)
        {
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(field);
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
    }
}
