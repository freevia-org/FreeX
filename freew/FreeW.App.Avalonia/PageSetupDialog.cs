using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Page Setup dialog: a modal <see cref="Window"/> that lets the user inspect and
/// change the document's page geometry (size, orientation, margins).
/// </summary>
public sealed class PageSetupDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private static readonly CultureInfo DialogCulture = CultureInfo.InvariantCulture;

    private readonly TextBox _topBox = MakeNumericBox();
    private readonly TextBox _bottomBox = MakeNumericBox();
    private readonly TextBox _leftBox = MakeNumericBox();
    private readonly TextBox _rightBox = MakeNumericBox();
    private readonly RadioButton _portraitRadio = new() { Content = PageSetupDialogPlanner.OrientationNames[0], IsChecked = true, Margin = new Thickness(0, 4, 12, 0), GroupName = "Orientation" };
    private readonly RadioButton _landscapeRadio = new() { Content = PageSetupDialogPlanner.OrientationNames[1], IsChecked = false, Margin = new Thickness(0, 4, 12, 0), GroupName = "Orientation" };
    private readonly ComboBox _paperSizeBox;
    private readonly TextBox _paperWidthBox = MakeNumericBox();
    private readonly TextBox _paperHeightBox = MakeNumericBox();
    private readonly StackPanel _customSizePanel;
    private readonly TextBlock _status = new();

    public PageSetupDialog(PageSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = PageSetupDialogPlanner.Title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = PageSetupDialogPlanner.BuildInitialState(
            current,
            SectionBreakKind.NextPage,
            PageSetupDialogPlanner.AvaloniaPaperOptions,
            PageSetupGeometryMode.NormalizeToOrientation,
            DialogCulture);

        _topBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.MarginTopPt, DialogCulture);
        _bottomBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.MarginBottomPt, DialogCulture);
        _leftBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.MarginLeftPt, DialogCulture);
        _rightBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.MarginRightPt, DialogCulture);

        _portraitRadio.IsChecked = state.OrientationIndex != 1;
        _landscapeRadio.IsChecked = state.OrientationIndex == 1;

        _paperSizeBox = new ComboBox { MinWidth = 200 };
        AvaloniaCompactDialogChrome.ApplyComboBox(_paperSizeBox, DialogChromeStyle);
        _paperSizeBox.ItemsSource = PageSetupDialogPlanner.AvaloniaPaperOptions
            .Select(option => option.AvaloniaLabel)
            .ToArray();
        _paperSizeBox.SelectedIndex = state.PaperSizeIndex;

        _paperWidthBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.WidthPt, DialogCulture);
        _paperHeightBox.Text = PageSetupDialogPlanner.FormatCompactPoints(current.HeightPt, DialogCulture);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_portraitRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_landscapeRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(0, 6, 0, 0));

        var customIndex = PageSetupDialogPlanner.CustomIndex(PageSetupDialogPlanner.AvaloniaPaperOptions);
        _customSizePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _customSizePanel.Children.Add(new TextBlock { Text = PageSetupDialogPlanner.CustomWidthLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _customSizePanel.Children.Add(_paperWidthBox);
        _customSizePanel.Children.Add(new TextBlock { Text = PageSetupDialogPlanner.CustomHeightLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        _customSizePanel.Children.Add(_paperHeightBox);
        _customSizePanel.IsVisible = state.PaperSizeIndex == customIndex;

        _paperSizeBox.SelectionChanged += (_, _) =>
        {
            _customSizePanel.IsVisible = _paperSizeBox.SelectedIndex == customIndex;
        };

        var content = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        content.Children.Add(SectionLabel(PageSetupDialogPlanner.MarginsSectionLabel));
        var marginGrid = new Grid();
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marginGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        marginGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledCell(marginGrid, PageSetupDialogPlanner.TopMarginLabel, _topBox, 0, 0);
        AddLabeledCell(marginGrid, PageSetupDialogPlanner.BottomMarginLabel, _bottomBox, 0, 2);
        AddLabeledCell(marginGrid, PageSetupDialogPlanner.LeftMarginLabel, _leftBox, 1, 0);
        AddLabeledCell(marginGrid, PageSetupDialogPlanner.RightMarginLabel, _rightBox, 1, 2);
        content.Children.Add(marginGrid);

        content.Children.Add(SectionLabel(PageSetupDialogPlanner.OrientationSectionLabel));
        var orientRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        orientRow.Children.Add(_portraitRadio);
        orientRow.Children.Add(_landscapeRadio);
        content.Children.Add(orientRow);

        content.Children.Add(SectionLabel(PageSetupDialogPlanner.PaperSizeSectionLabel));
        content.Children.Add(_paperSizeBox);
        content.Children.Add(_customSizePanel);

        content.Children.Add(_status);
        var ok = new Button { Content = PageSetupDialogPlanner.OkButton, IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        var cancel = new Button { Content = PageSetupDialogPlanner.CancelButton, MinWidth = 84, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 84);
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0)));

        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>Result produced by the dialog on OK.</summary>
    public sealed record PageSetupDialogResult(
        double MarginTopPt,
        double MarginBottomPt,
        double MarginLeftPt,
        double MarginRightPt,
        bool Landscape,
        double WidthPt,
        double HeightPt);

    private void OnOk()
    {
        _status.IsVisible = false;

        var input = new PageSetupDialogInput(
            MarginTopText: _topBox.Text,
            MarginBottomText: _bottomBox.Text,
            MarginLeftText: _leftBox.Text,
            MarginRightText: _rightBox.Text,
            GutterText: "0",
            OrientationIndex: _landscapeRadio.IsChecked == true ? 1 : 0,
            MultiplePagesIndex: 0,
            WidthText: _paperWidthBox.Text,
            HeightText: _paperHeightBox.Text,
            PaperSizeIndex: _paperSizeBox.SelectedIndex,
            SectionStartIndex: 1,
            DifferentFirstPage: false,
            DifferentOddEvenPages: false,
            HeaderDistanceText: "0",
            FooterDistanceText: "0",
            VerticalAlignmentIndex: 0,
            UseSelectedPaperPreset: true,
            GeometryMode: PageSetupGeometryMode.NormalizeToOrientation,
            ValidationProfile: PageSetupValidationProfile.CompactDialog);

        if (!PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.AvaloniaPaperOptions,
                DialogCulture,
                out var planned,
                out var error))
        {
            ShowError(error ?? PageSetupDialogPlanner.UnifiedValidationMessage);
            return;
        }

        Close(new PageSetupDialogResult(
            MarginTopPt: planned!.MarginTopPt,
            MarginBottomPt: planned.MarginBottomPt,
            MarginLeftPt: planned.MarginLeftPt,
            MarginRightPt: planned.MarginRightPt,
            Landscape: planned.Landscape,
            WidthPt: planned.WidthPt,
            HeightPt: planned.HeightPt));
    }

    /// <summary>
    /// Build a <see cref="PageSettings"/> from <paramref name="result"/>, copying it over the
    /// current <see cref="TextDocument.Page"/> via <see cref="DocumentView.SetPageSettings"/>.
    /// </summary>
    public static void ApplyResult(DocumentView editor, PageSetupDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        var settings = editor.Document.Page.Clone();
        settings.MarginTopPt = result.MarginTopPt;
        settings.MarginBottomPt = result.MarginBottomPt;
        settings.MarginLeftPt = result.MarginLeftPt;
        settings.MarginRightPt = result.MarginRightPt;
        settings.Landscape = result.Landscape;
        settings.WidthPt = result.WidthPt;
        settings.HeightPt = result.HeightPt;

        editor.SetPageSettings(settings);
    }

    /// <summary>
    /// Shows the Page Setup dialog modally and, on OK, applies the changes to the document.
    /// Must be called from the UI thread.
    /// </summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var current = editor.Document.Page;
        var dialog = new PageSetupDialog(current);
        var result = await dialog.ShowDialog<PageSetupDialogResult?>(owner);
        if (result is null)
            return;

        ApplyResult(editor, result);
    }

    private void ShowError(string msg)
    {
        _status.Text = msg;
        _status.IsVisible = true;
    }

    private static TextBox MakeNumericBox()
    {
        var box = new TextBox { Width = 80, Margin = new Thickness(0, 4, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 2),
    };

    private static void AddLabeledCell(Grid grid, string label, Control ctrl, int row, int col)
    {
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 2),
        };
        var cell = new StackPanel { Children = { lbl, ctrl } };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        grid.Children.Add(cell);
    }
}
