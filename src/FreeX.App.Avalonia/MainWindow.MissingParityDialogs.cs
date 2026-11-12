using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
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
            ChartCommandWorkflowPlanner.BuildStyleCommand(
                _session.ActiveSheet.Id,
                chart,
                submission.StyleId));
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

        // WPF ChartStyleDialog: a 230px ListBox whose items panel is a four-column UniformGrid of
        // 96px tiles (4px item margin), so all four columns fit beside the scroll bar and the selected
        // tile carries the list selection instead of a radio glyph.
        var gallery = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Height = 230,
            Margin = new Thickness(0, 0, 0, 16),
            ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = 4 }),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(gallery, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AvaloniaCompactDialogChrome.ApplyListBox(
            gallery,
            MissingParityDialogChromeStyle with { ListBoxItemPadding = new Thickness(0) });

        var styleIds = new List<int?>();
        foreach (var descriptor in descriptors)
        {
            var displayName = descriptor.ResourceValue is { } displayValue
                ? UiText.Format(descriptor.DisplayNameResourceKey, displayValue)
                : UiText.Get(descriptor.DisplayNameResourceKey);
            var previewLabel = descriptor.ResourceValue is { } previewValue
                ? UiText.Format(descriptor.PreviewLabelResourceKey, previewValue)
                : UiText.Get(descriptor.PreviewLabelResourceKey);
            var option = new ListBoxItem
            {
                Content = CreateChartStyleOptionContent(displayName, previewLabel),
            };
            AutomationProperties.SetAutomationId(
                option,
                descriptor.StyleId is { } styleId ? $"ChartStyleOption{styleId}" : "ChartStyleAutomaticOption");
            AutomationProperties.SetName(option, displayName);
            styleIds.Add(descriptor.StyleId);
            gallery.Items.Add(option);
        }

        gallery.SelectedIndex = styleIds.IndexOf(initialStyleId);
        gallery.SelectionChanged += (_, _) =>
        {
            if (gallery.SelectedIndex >= 0 && gallery.SelectedIndex < styleIds.Count)
                selectedStyleId = styleIds[gallery.SelectedIndex];
        };

        AutomationProperties.SetAutomationId(gallery, "ChartStyleGallery");
        AutomationProperties.SetName(gallery, UiText.Get("ChartStyle_GalleryAutomationName"));

        var dialog = NewChartDialog(UiText.Get("ChartStyle_Title"), "ChartStyleDialog");
        dialog.Width = 480;
        dialog.Height = 350;
        dialog.SizeToContent = SizeToContent.Manual;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartStyle");
        okButton.Click += (_, _) => dialog.Close(new ChartStyleDialogSubmission(true, selectedStyleId));
        cancelButton.Click += (_, _) => dialog.Close((ChartStyleDialogSubmission?)null);

        // WPF ChartStyleDialog: the label sits 4px above the 230px gallery, which leaves 16px above the
        // button row.
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("ChartStyle_StyleLabel")),
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                gallery,
                buttonRow,
            },
        };
        // WPF focuses the gallery control itself.
        dialog.Opened += (_, _) => gallery.Focus();

        return await dialog.ShowDialog<ChartStyleDialogSubmission?>(this);
    }

    // WPF ChartStyleDialog.CreateStyleGalleryTemplate: a 96px stack with a 4px margin holding a 42px
    // white swatch (three 10px bars, 3px apart on each side, 5px above the bottom), the style name
    // 4px below it and a 10px grey preview caption.
    private static Control CreateChartStyleOptionContent(string displayName, string previewLabel)
    {
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 5),
        };
        foreach (var height in new[] { 18d, 28d, 22d })
        {
            bars.Children.Add(new Border
            {
                Width = 10,
                Height = height,
                Margin = new Thickness(3, 0),
                Background = Brush(0, 120, 215),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
            });
        }

        return new StackPanel
        {
            Width = 96,
            Margin = new Thickness(4),
            Children =
            {
                new Border
                {
                    Height = 42,
                    Background = Brushes.White,
                    BorderBrush = Brush(160, 160, 160),
                    BorderThickness = new Thickness(1),
                    Child = bars,
                },
                new TextBlock
                {
                    Text = displayName,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 12,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                },
                new TextBlock
                {
                    Text = previewLabel,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 10,
                    Foreground = Brush(109, 109, 109),
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
            // WPF dialogs inherit the 12px UI font; the Avalonia window default is 14px.
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
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

        // WPF HeaderFooterPictureFormatDialog.CreateContent: 16px root margin, the file name 12px above
        // the first label, each label 4px above its box, boxes 8px apart, a left-aligned 72px Reset
        // button with an (0, 8, 0, 12) margin, then OK/Cancel. The window keeps the WPF 360x270 as its
        // client size (the Avalonia controls are taller per row than the WPF ones, so the content
        // would not fit the 241px the WPF client area leaves).
        TextBlock CreateFieldLabel(string resourceKey)
        {
            var label = CreateMissingParityLabel(UiText.Get(resourceKey));
            label.Margin = new Thickness(0, 0, 0, 4);
            return label;
        }

        widthBox.Margin = new Thickness(0, 0, 0, 8);
        heightBox.Margin = new Thickness(0, 0, 0, 8);
        resetButton.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        resetButton.Margin = new Thickness(0, 8, 0, 12);
        warning.Margin = new Thickness(0, 0, 0, 4);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = state.FileName, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 12) },
                CreateFieldLabel("FormatPicture_WidthLabel"),
                widthBox,
                CreateFieldLabel("FormatPicture_HeightLabel"),
                heightBox,
                lockAspectRatio,
                resetButton,
                warning,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]),
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
        if (WindowRegistry.HiddenWindows.Count == 0)
        {
            RefreshShell(UiText.Get("MainWindowMessage_UnhideNoHiddenWindows"));
            return;
        }

        var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(
            WindowRegistry.HiddenWindows.Select(window =>
                new WorkbookWindowSelectionEntry<MainWindow>(
                    window,
                    WindowRegistry.IndexOf(window),
                    window.WindowMenuDisplayName)),
            _session.Workbook.Name,
            WindowRegistry.Count);

        var selected = await ShowUnhideWindowDialogCoreAsync(targets);
        if (selected is null || !WindowRegistry.Unhide(selected))
            return;

        RefreshShell(UiText.Get("MainWindowMessage_UnhideWindowTitle"));
    }

    private async Task<MainWindow?> ShowUnhideWindowDialogCoreAsync(
        IReadOnlyList<WorkbookWindowSelectionTarget<MainWindow>> targets)
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
            // WPF UnhideWindowDialog is 340x160: a 16px margin, the label 4px above the 64px list,
            // 12px above the 72px OK/Cancel row. With the Avalonia control heights that content
            // fills the 160 client height, so the WPF size is kept as the client size.
            Width = 340,
            Height = 160,
            CanResize = false,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            // WPF dialogs inherit the 12px UI font; the Avalonia window default is 14px.
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
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
        okButton.IsEnabled = list.SelectedItem is WorkbookWindowSelectionTarget<MainWindow>;
        list.SelectionChanged += (_, _) =>
            okButton.IsEnabled = list.SelectedItem is WorkbookWindowSelectionTarget<MainWindow>;

        void Accept()
        {
            if (list.SelectedItem is WorkbookWindowSelectionTarget<MainWindow> target)
                dialog.Close(target.Window);
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close((MainWindow?)null);
        list.PointerPressed += (_, args) =>
        {
            if (args.ClickCount >= 2 && list.SelectedItem is WorkbookWindowSelectionTarget<MainWindow>)
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

        return await dialog.ShowDialog<MainWindow?>(this);
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
