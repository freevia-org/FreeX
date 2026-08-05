using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private sealed record ChartStyleDialogSubmission(bool Accepted, int? StyleId);

    private static AvaloniaCompactDialogChromeStyle MissingParityDialogChromeStyle =>
        new(FormulaBarFontFamily);

    private async Task ShowChartStyleDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        if (!TryGetSelectedChart("Chart Styles", out var chart))
            return;

        var submission = await ShowChartStyleDialogCoreAsync(chart);
        if (submission is not { Accepted: true })
            return;

        var result = _session.ExecuteReviewCommand(
            new SetChartStyleCommand(_session.ActiveSheet.Id, chart.Id, submission.StyleId));
        RefreshShell(result.Success
            ? submission.StyleId is { } styleId
                ? UiText.Format("ChartLoc_AppliedChartStyle", styleId)
                : UiText.Get("ChartStyle_AutomaticOption")
            : result.ErrorMessage ?? UiText.Get("ChartLoc_ChartStylesFailed"));
    }

    private async Task<ChartStyleDialogSubmission?> ShowChartStyleDialogCoreAsync(ChartModel chart)
    {
        var descriptors = ChartStylePlanner.GetStyleOptions();
        var initialStyleId = ChartStylePlanner.Read(chart).StyleId;
        int? selectedStyleId = initialStyleId;

        var gallery = new UniformGrid
        {
            Columns = 4,
            Rows = 0,
        };

        foreach (var descriptor in descriptors)
        {
            var displayName = descriptor.ResourceValue is { } displayValue
                ? UiText.Format(descriptor.DisplayNameResourceKey, displayValue)
                : UiText.Get(descriptor.DisplayNameResourceKey);
            var previewLabel = descriptor.ResourceValue is { } previewValue
                ? UiText.Format(descriptor.PreviewLabelResourceKey, previewValue)
                : UiText.Get(descriptor.PreviewLabelResourceKey);
            var option = new RadioButton
            {
                GroupName = "ChartStyleGallery",
                IsChecked = descriptor.StyleId == initialStyleId,
                Margin = new Thickness(2),
                Padding = new Thickness(2),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                Content = CreateChartStyleOptionContent(displayName, previewLabel),
            };
            AvaloniaCompactDialogChrome.ApplyRadioButton(option, MissingParityDialogChromeStyle);
            AutomationProperties.SetAutomationId(
                option,
                descriptor.StyleId is { } styleId ? $"ChartStyleOption{styleId}" : "ChartStyleAutomaticOption");
            AutomationProperties.SetName(option, displayName);
            option.IsCheckedChanged += (_, _) =>
            {
                if (option.IsChecked == true)
                    selectedStyleId = descriptor.StyleId;
            };
            gallery.Children.Add(option);
        }

        AutomationProperties.SetAutomationId(gallery, "ChartStyleGallery");
        AutomationProperties.SetName(gallery, UiText.Get("ChartStyle_GalleryAutomationName"));

        var scroll = new ScrollViewer
        {
            Content = gallery,
            Height = 230,
            Focusable = true,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        var dialog = NewChartDialog(UiText.Get("ChartStyle_Title"), "ChartStyleDialog");
        dialog.Width = 480;
        dialog.Height = 350;
        dialog.SizeToContent = SizeToContent.Manual;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartStyle");
        okButton.Click += (_, _) => dialog.Close(new ChartStyleDialogSubmission(true, selectedStyleId));
        cancelButton.Click += (_, _) => dialog.Close((ChartStyleDialogSubmission?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("ChartStyle_StyleLabel"),
                    FontFamily = FormulaBarFontFamily,
                },
                scroll,
                buttonRow,
            },
        };
        // WPF focuses the gallery control itself. Focusing a checked child inside the scroll host can
        // trigger a re-entrant bring-into-view/layout pass while the headless capture is rendering.
        dialog.Opened += (_, _) => scroll.Focus();

        return await dialog.ShowDialog<ChartStyleDialogSubmission?>(this);
    }

    private static Control CreateChartStyleOptionContent(string displayName, string previewLabel)
    {
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
            Spacing = 6,
            Margin = new Thickness(0, 2),
        };
        foreach (var height in new[] { 12d, 22d, 17d })
        {
            bars.Children.Add(new Border
            {
                Width = 10,
                Height = height,
                Background = Brush(68, 114, 196),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
            });
        }

        return new StackPanel
        {
            Width = 96,
            Children =
            {
                new Border
                {
                    Height = 30,
                    Background = Brushes.White,
                    BorderBrush = Brush(166, 166, 166),
                    BorderThickness = new Thickness(1),
                    Child = bars,
                },
                new TextBlock
                {
                    Text = displayName,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 10,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0),
                },
                new TextBlock
                {
                    Text = previewLabel,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 9,
                    Foreground = Brush(96, 96, 96),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                },
            },
        };
    }

    private async Task<WorksheetHeaderFooterPictureSet?> ShowHeaderFooterPictureSetFormatDialogAsync(
        WorksheetHeaderFooterPictureSet pictures,
        HeaderFooterEditorSection preferredSection = HeaderFooterEditorSection.Center)
    {
        var section = ResolveHeaderFooterPictureSection(pictures, preferredSection);
        var picture = HeaderFooterEditorPlanner.GetPicture(pictures, section);
        if (picture is null)
            return null;

        var formatted = await ShowHeaderFooterPictureFormatDialogAsync(picture);
        return formatted is null
            ? null
            : HeaderFooterEditorPlanner.SetPicture(pictures, section, formatted);
    }

    private async Task<WorksheetHeaderFooterPicture?> ShowHeaderFooterPictureFormatDialogAsync(
        WorksheetHeaderFooterPicture picture)
    {
        var normalized = HeaderFooterPictureFormatPlanner.NormalizePictureSize(picture.DeepClone());
        var state = HeaderFooterPictureFormatPlanner.CreateState(
            picture,
            UiText.Get("HeaderFooterPicture_DefaultFileName"),
            CultureInfo.InvariantCulture);
        var updatingSize = false;

        var widthBox = new TextBox { Text = state.WidthText };
        var heightBox = new TextBox { Text = state.HeightText };
        var lockAspectRatio = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("FormatPicture_LockAspectRatio")),
            IsChecked = state.LockAspectRatio,
        };
        var warning = new TextBlock
        {
            Text = UiText.Get("FormatPicture_InvalidSizeMessage"),
            Foreground = Brush(180, 32, 37),
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };

        AvaloniaCompactDialogChrome.ApplyTextBox(widthBox, MissingParityDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(heightBox, MissingParityDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(lockAspectRatio, MissingParityDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(warning, MissingParityDialogChromeStyle);
        SetHeaderFooterPictureAutomation(widthBox, "HeaderFooterPictureWidthBox", "HeaderFooterPicture_WidthAutomationName", "HeaderFooterPicture_WidthHelpText");
        SetHeaderFooterPictureAutomation(heightBox, "HeaderFooterPictureHeightBox", "HeaderFooterPicture_HeightAutomationName", "HeaderFooterPicture_HeightHelpText");
        AutomationProperties.SetAutomationId(lockAspectRatio, "HeaderFooterPictureLockAspectRatioCheckBox");
        AutomationProperties.SetName(lockAspectRatio, UiText.Get("HeaderFooterPicture_LockAspectRatioAutomationName"));
        AutomationProperties.SetHelpText(lockAspectRatio, UiText.Get("HeaderFooterPicture_LockAspectRatioHelpText"));

        void SetDimension(TextBox box, double value)
        {
            updatingSize = true;
            box.Text = HeaderFooterPictureFormatPlanner.FormatSize(value, CultureInfo.InvariantCulture);
            updatingSize = false;
        }

        widthBox.TextChanged += (_, _) =>
        {
            if (updatingSize || lockAspectRatio.IsChecked != true)
                return;
            if (HeaderFooterPictureFormatPlanner.SyncHeightFromWidth(widthBox.Text, state.OriginalSize) is { } height)
                SetDimension(heightBox, height);
        };
        heightBox.TextChanged += (_, _) =>
        {
            if (updatingSize || lockAspectRatio.IsChecked != true)
                return;
            if (HeaderFooterPictureFormatPlanner.SyncWidthFromHeight(heightBox.Text, state.OriginalSize) is { } width)
                SetDimension(widthBox, width);
        };

        var resetButton = new Button { Content = UiText.Get("HeaderFooterPicture_ResetButton") };
        AvaloniaCompactDialogChrome.ApplyButton(resetButton, MissingParityDialogChromeStyle, 72);
        AutomationProperties.SetAutomationId(resetButton, "HeaderFooterPictureResetSizeButton");
        AutomationProperties.SetName(resetButton, UiText.Get("HeaderFooterPicture_ResetSizeAutomationName"));
        AutomationProperties.SetHelpText(resetButton, UiText.Get("HeaderFooterPicture_ResetSizeHelpText"));
        resetButton.Click += (_, _) =>
        {
            var reset = HeaderFooterPictureFormatPlanner.ResetSize(state);
            updatingSize = true;
            widthBox.Text = HeaderFooterPictureFormatPlanner.FormatSize(reset.Width, CultureInfo.InvariantCulture);
            heightBox.Text = HeaderFooterPictureFormatPlanner.FormatSize(reset.Height, CultureInfo.InvariantCulture);
            updatingSize = false;
            warning.IsVisible = false;
        };

        var dialog = new Window
        {
            Title = UiText.Get("FormatPicture_Title"),
            Width = 360,
            Height = 270,
            CanResize = false,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "HeaderFooterPictureFormatDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, MissingParityDialogChromeStyle, 72, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, MissingParityDialogChromeStyle, 72);
        okButton.Click += (_, _) =>
        {
            if (!HeaderFooterPictureFormatPlanner.TryCreateResult(
                    normalized,
                    widthBox.Text,
                    heightBox.Text,
                    out var result,
                    out var invalidField))
            {
                warning.IsVisible = true;
                var invalidBox = invalidField == ObjectSizeDialogField.Width ? widthBox : heightBox;
                invalidBox.Focus();
                invalidBox.SelectAll();
                return;
            }

            dialog.Close(result);
        };
        cancelButton.Click += (_, _) => dialog.Close((WorksheetHeaderFooterPicture?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = state.FileName, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 8) },
                CreateMissingParityLabel(UiText.Get("FormatPicture_WidthLabel")),
                widthBox,
                CreateMissingParityLabel(UiText.Get("FormatPicture_HeightLabel")),
                heightBox,
                lockAspectRatio,
                resetButton,
                warning,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 4, 0, 0)),
            },
        };
        dialog.Opened += (_, _) =>
        {
            var initial = state.InitialFocusField == ObjectSizeDialogField.Width ? widthBox : heightBox;
            initial.Focus();
            initial.SelectAll();
        };

        return await dialog.ShowDialog<WorksheetHeaderFooterPicture?>(this);
    }

    private async Task ShowUnhideWindowDialogAsync()
    {
        if (HiddenWindows.Count == 0)
        {
            RefreshShell(UiText.Get("MainWindowMessage_UnhideNoHiddenWindows"));
            return;
        }

        var allWindows = AllTopLevelWindows.ToList();
        var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(
            HiddenWindows.Select(window =>
            {
                var index = Math.Max(0, allWindows.IndexOf(window));
                var displayName = window is MainWindow workbookWindow
                    ? WorkbookWindowSelectionPlanner.FormatDisplayName(workbookWindow._session.Workbook.Name, "")
                    : string.IsNullOrWhiteSpace(window.Title) ? "Workbook" : window.Title;
                return new WorkbookWindowSelectionEntry<Window>(window, index, displayName);
            }),
            _session.Workbook.Name,
            allWindows.Count);

        var selected = await ShowUnhideWindowDialogCoreAsync(targets);
        if (selected is null || !HiddenWindows.Remove(selected))
            return;

        selected.Show();
        selected.Activate();
        RefreshShell(UiText.Get("MainWindowMessage_UnhideWindowTitle"));
    }

    private async Task<Window?> ShowUnhideWindowDialogCoreAsync(
        IReadOnlyList<WorkbookWindowSelectionTarget<Window>> targets)
    {
        var list = new ListBox
        {
            ItemsSource = targets,
            SelectedItem = targets.FirstOrDefault(),
            SelectionMode = SelectionMode.Single,
            MinHeight = 64,
        };
        AvaloniaCompactDialogChrome.ApplyListBox(list, MissingParityDialogChromeStyle);
        AutomationProperties.SetAutomationId(list, "UnhideWindowList");
        AutomationProperties.SetName(list, UiText.Get("UnhideWindow_ListAutomationName"));
        AutomationProperties.SetHelpText(list, UiText.Get("UnhideWindow_ListHelpText"));

        var dialog = new Window
        {
            Title = UiText.Get("UnhideWindow_Title"),
            Width = 340,
            Height = 160,
            CanResize = false,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "UnhideWindowDialog");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, MissingParityDialogChromeStyle, 72, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, MissingParityDialogChromeStyle, 72);
        AutomationProperties.SetAutomationId(okButton, "UnhideWindowOkButton");
        AutomationProperties.SetName(okButton, UiText.Get("UnhideWindow_OkAutomationName"));
        AutomationProperties.SetHelpText(okButton, UiText.Get("UnhideWindow_OkHelpText"));
        AutomationProperties.SetAutomationId(cancelButton, "UnhideWindowCancelButton");
        AutomationProperties.SetName(cancelButton, UiText.Get("UnhideWindow_CancelAutomationName"));
        AutomationProperties.SetHelpText(cancelButton, UiText.Get("UnhideWindow_CancelHelpText"));
        okButton.IsEnabled = list.SelectedItem is WorkbookWindowSelectionTarget<Window>;
        list.SelectionChanged += (_, _) =>
            okButton.IsEnabled = list.SelectedItem is WorkbookWindowSelectionTarget<Window>;

        void Accept()
        {
            if (list.SelectedItem is WorkbookWindowSelectionTarget<Window> target)
                dialog.Close(target.Window);
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close((Window?)null);
        list.PointerPressed += (_, args) =>
        {
            if (args.ClickCount >= 2 && list.SelectedItem is WorkbookWindowSelectionTarget<Window>)
            {
                Accept();
                args.Handled = true;
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
            Children =
            {
                CreateMissingParityLabel(UiText.Get("UnhideWindow_WindowLabel")),
                list,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 8, 0, 0)),
            },
        };
        dialog.Opened += (_, _) => list.Focus();

        return await dialog.ShowDialog<Window?>(this);
    }

    private async Task ShowHeaderFooterPictureFormatParityDialogAsync()
    {
        var picture = new WorksheetHeaderFooterPicture(
            [],
            "image/png",
            "QuarterlyHeader.png",
            Width: 160,
            Height: 80);
        await ShowHeaderFooterPictureFormatDialogAsync(picture);
    }

    private async Task ShowUnhideWindowParityDialogAsync()
    {
        var hidden = new Window
        {
            Title = "Parity Demo:2",
            Width = 320,
            Height = 200,
            ShowInTaskbar = false,
        };
        hidden.Show();
        hidden.Hide();
        HiddenWindows.Add(hidden);
        try
        {
            await ShowUnhideWindowDialogAsync();
        }
        finally
        {
            HiddenWindows.Remove(hidden);
            hidden.Close();
        }
    }

    private static HeaderFooterEditorSection ResolveHeaderFooterPictureSection(
        WorksheetHeaderFooterPictureSet pictures,
        HeaderFooterEditorSection preferredSection)
    {
        if (HeaderFooterEditorPlanner.GetPicture(pictures, preferredSection) is not null)
            return preferredSection;
        if (pictures.Left is not null)
            return HeaderFooterEditorSection.Left;
        if (pictures.Center is not null)
            return HeaderFooterEditorSection.Center;
        return HeaderFooterEditorSection.Right;
    }

    private static TextBlock CreateMissingParityLabel(string text) => new()
    {
        Text = StripDisplayMnemonic(text),
        FontFamily = FormulaBarFontFamily,
    };

    private static void SetHeaderFooterPictureAutomation(
        TextBox box,
        string id,
        string nameKey,
        string helpKey)
    {
        AutomationProperties.SetAutomationId(box, id);
        AutomationProperties.SetName(box, UiText.Get(nameKey));
        AutomationProperties.SetHelpText(box, UiText.Get(helpKey));
    }
}
