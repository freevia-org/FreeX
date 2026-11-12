using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly ExportPlannerTextResolver AvaloniaExportPlannerTextResolver = new(
        UiText.Get,
        (key, args) => UiText.Format(key, args));

    private async Task<ExportOptions?> ShowExportOptionsDialogAsync(
        ExportContentScope defaultScope,
        ExportFormat format)
    {
        var availability = ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(format);
        ExportOptions? result = null;

        var dialog = new Window
        {
            Title = UiText.Get(ExportOptionsDialogSurfacePlanner.TitleResourceKey),
            // WPF Width/MaxHeight are outer sizes; Avalonia sizes the client, so the measured Windows
            // frame comes off both.
            Width = ExportOptionsDialogSurfacePlanner.Width - WpfNoResizeFrameWidth,
            SizeToContent = SizeToContent.Height,
            MaxHeight = ExportOptionsDialogSurfacePlanner.MaxHeight - WpfNoResizeClientFrameHeight,
            // WPF ExportOptionsDialog is ResizeMode.NoResize.
            CanResize = false,
            // WPF renders every caption, option and field in the 12px dialog font; without this the
            // radio buttons, labels and drop-downs fell back to the 14px theme default.
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, ExportOptionsDialogSurfacePlanner.DialogAutomationId);

        var activeSheetButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_ActiveSheetS")),
            IsChecked = defaultScope == ExportContentScope.ActiveSheet
        };
        var selectionButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_SelectedRange")),
            IsChecked = defaultScope == ExportContentScope.Selection,
            IsEnabled = true
        };
        if (!selectionButton.IsEnabled)
        {
            var help = UiText.Get("ExportOptions_SelectACellRangeBeforeExportingTheSelection");
            ToolTip.SetTip(selectionButton, help);
            AutomationProperties.SetHelpText(selectionButton, help);
        }

        var workbookButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Workbook")),
            IsChecked = defaultScope == ExportContentScope.EntireWorkbook
        };
        if (selectionButton.IsChecked == true && !selectionButton.IsEnabled)
            activeSheetButton.IsChecked = true;

        var allPagesButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_All")),
            GroupName = "PageRange",
            IsChecked = true
        };
        var pagesButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Pages")),
            GroupName = "PageRange"
        };
        var fromPageBox = CreateExportOptionsTextBox(width: 56, isEnabled: false);
        var toPageBox = CreateExportOptionsTextBox(width: 56, isEnabled: false);
        AutomationProperties.SetName(fromPageBox, UiText.Get("ExportOptions_FromPage"));
        AutomationProperties.SetName(toPageBox, UiText.Get("ExportOptions_ToPage"));

        var documentPropertiesBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_IncludeDocumentProperties"))
        };
        var ignorePrintAreasBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_IgnorePrintAreas"))
        };
        var bookmarksBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_CreatePdfBookmarks")),
            IsEnabled = availability.PdfBookmarksEnabled
        };
        var bookmarkModeBox = CreateExportOptionsComboBox(180, availability.PdfBookmarksEnabled && bookmarksBox.IsChecked == true);
        AddComboItems(
            bookmarkModeBox,
            "ExportOptions_SheetNames",
            "ExportOptions_PrintTitles",
            "ExportOptions_PageNumbers");
        var initialViewBox = CreateExportOptionsComboBox(180, availability.PdfInitialViewEnabled);
        AddComboItems(
            initialViewBox,
            "ExportOptions_SinglePage",
            "ExportOptions_OneContinuousColumn",
            "ExportOptions_TwoColumnsOddPagesLeft",
            "ExportOptions_TwoColumnsOddPagesRight");
        var openModeBox = CreateExportOptionsComboBox(180, availability.PdfOpenModeEnabled);
        AddComboItems(
            openModeBox,
            "ExportOptions_Normal",
            "ExportOptions_BookmarksVisible",
            "ExportOptions_FullScreen");
        var pdfLanguageBox = CreateExportOptionsTextBox(88, availability.PdfLanguageEnabled, ExportPlanner.DefaultPdfLanguage);
        var bitmapTextBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_BitmapTextWhenFontsMayNotBeEmbedded")),
            IsEnabled = availability.PdfBitmapTextEnabled
        };
        var pdfABox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_PdfACompliantNotSupported")),
            IsEnabled = false
        };
        var structureTagsBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_DocumentStructureTagsNotSupported")),
            IsEnabled = false
        };
        var standardQualityButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Standard")),
            IsChecked = true
        };
        var minimumSizeButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_MinimumSize")),
            IsEnabled = availability.MinimumSizeEnabled
        };
        var openAfterPublishBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_OpenAfterPublishing")),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var errorBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        bookmarksBox.IsCheckedChanged += (_, _) =>
            bookmarkModeBox.IsEnabled = bookmarksBox.IsChecked == true && availability.PdfBookmarksEnabled;
        pagesButton.IsCheckedChanged += (_, _) =>
        {
            var enabled = pagesButton.IsChecked == true;
            fromPageBox.IsEnabled = enabled;
            toPageBox.IsEnabled = enabled;
            if (enabled)
                fromPageBox.Focus();
        };
        allPagesButton.IsCheckedChanged += (_, _) =>
        {
            if (allPagesButton.IsChecked == true)
            {
                fromPageBox.IsEnabled = false;
                toPageBox.IsEnabled = false;
            }
        };

        void ShowError(string? message, Control? focusTarget = null)
        {
            errorBlock.Text = message ?? string.Empty;
            errorBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
            focusTarget?.Focus();
        }

        // WPF stacks the options with no extra spacing inside a 16px margin; the row gaps come from
        // the per-row margins below. The bottom margin moves to the docked button row.
        var stack = new StackPanel { Margin = new Thickness(16, 16, 16, 0) };
        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PublishWhat"));
        stack.Children.Add(activeSheetButton);
        stack.Children.Add(selectionButton);
        stack.Children.Add(workbookButton);
        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PageRange", topMargin: 12));
        stack.Children.Add(allPagesButton);

        // WPF: Pages, then each Label with Margin (8, 0, 6, 0) before its 56px box.
        var pageRangePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };
        pageRangePanel.Children.Add(pagesButton);
        pageRangePanel.Children.Add(CreateExportOptionsLabel("ExportOptions_From", fromPageBox, new Thickness(8, 0, 6, 0)));
        pageRangePanel.Children.Add(fromPageBox);
        pageRangePanel.Children.Add(CreateExportOptionsLabel("ExportOptions_To", toPageBox, new Thickness(8, 0, 6, 0)));
        pageRangePanel.Children.Add(toPageBox);
        stack.Children.Add(pageRangePanel);

        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PdfXpsOptions", topMargin: 14));
        stack.Children.Add(documentPropertiesBox);
        stack.Children.Add(ignorePrintAreasBox);
        stack.Children.Add(bookmarksBox);
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_BookmarkMode", bookmarkModeBox, leftIndent: 22));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_InitialView", initialViewBox, topMargin: 4));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_OpenMode", openModeBox));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_PdfLanguage", pdfLanguageBox));
        stack.Children.Add(bitmapTextBox);
        stack.Children.Add(pdfABox);
        stack.Children.Add(structureTagsBox);
        stack.Children.Add(standardQualityButton);
        stack.Children.Add(minimumSizeButton);
        stack.Children.Add(openAfterPublishBox);
        stack.Children.Add(errorBlock);
        foreach (var toggle in new ToggleButton[]
                 {
                     activeSheetButton, selectionButton, workbookButton, allPagesButton, pagesButton,
                     documentPropertiesBox, ignorePrintAreasBox, bookmarksBox, bitmapTextBox, pdfABox,
                     structureTagsBox, standardQualityButton, minimumSizeButton, openAfterPublishBox,
                 })
        {
            // The compact toggle template is only as tall as its 12px text line (14px); WPF's
            // default CheckBox / RadioButton rows are 15px, which sets the option pitch here.
            toggle.MinHeight = ExportOptionsWpfToggleHeight;
            toggle.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
        }

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true };
        // WPF: 80px OK/Cancel buttons 8px apart, 18px below the last option and 16px above the edge.
        ApplyDialogButtonChrome(okButton, 80, isDefault: true);
        ApplyDialogButtonChrome(cancelButton, 80);
        okButton.Click += (_, _) =>
        {
            ShowError(null);
            ExportPageRange? pageRange = null;
            if (pagesButton.IsChecked == true &&
                !ExportPlanner.TryCreatePageRange(
                    fromPageBox.Text ?? string.Empty,
                    toPageBox.Text ?? string.Empty,
                    out pageRange,
                    out var pageRangeError,
                    AvaloniaExportPlannerTextResolver))
            {
                var focusTarget = ExportOptionsDialogSurfacePlanner.ResolveInvalidPageRangeFocusTarget(
                    pageRangeError,
                    fromPageBox.Text,
                    UiText.Get("Export_PageRangeFromLessThanToError")) == ExportOptionsDialogFocusTarget.ToPage
                    ? toPageBox
                    : fromPageBox;
                pagesButton.IsChecked = true;
                ShowError(pageRangeError, focusTarget);
                return;
            }

            if (!ExportPlanner.TryNormalizePdfLanguage(
                    pdfLanguageBox.Text,
                    out var pdfLanguage,
                    out var pdfLanguageError,
                    AvaloniaExportPlannerTextResolver))
            {
                ShowError(pdfLanguageError, pdfLanguageBox);
                return;
            }

            result = ExportOptionsDialogSurfacePlanner.CreateResult(
                workbookButton.IsChecked == true
                    ? ExportContentScope.EntireWorkbook
                    : selectionButton.IsChecked == true
                        ? ExportContentScope.Selection
                        : ExportContentScope.ActiveSheet,
                documentPropertiesBox.IsChecked == true,
                openAfterPublishBox.IsChecked == true,
                ignorePrintAreasBox.IsChecked == true,
                pageRange,
                minimumSizeButton.IsChecked == true ? ExportQuality.MinimumSize : ExportQuality.Standard,
                bookmarksBox.IsChecked == true,
                ExportOptionsDialogSurfacePlanner.BookmarkModeFromIndex(bookmarkModeBox.SelectedIndex),
                ExportOptionsDialogSurfacePlanner.InitialViewFromIndex(initialViewBox.SelectedIndex),
                ExportOptionsDialogSurfacePlanner.OpenModeFromIndex(openModeBox.SelectedIndex),
                bitmapTextBox.IsChecked == true,
                pdfLanguage,
                format: format);
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            result = null;
            dialog.Close();
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(16, 18, 16, 16),
            Spacing = 8,
            Children = { okButton, cancelButton },
        };

        var root = new DockPanel();
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        dialog.Content = root;
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                dialog.Close();
        };
        dialog.Opened += (_, _) => activeSheetButton.Focus();
        await dialog.ShowDialog(this);

        return result;
    }

    private async Task TryOpenExportedPdfAsync(string path)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        var result = await DesktopPathLauncher.OpenFileAsync(
            path,
            launcher is null
                ? null
                : target => launcher.LaunchUriAsync(target.LaunchUri));

        if (result.Outcome == DesktopPathLaunchOutcome.Launched)
            return;

        if (result.Outcome == DesktopPathLaunchOutcome.LauncherUnavailable)
        {
            ShowExportIssue("Export completed, but no platform launcher is available to open the PDF.");
            return;
        }

        if (result.Error is not null)
        {
            ShowExportIssue($"Export completed, but the PDF could not be opened: {result.Error.Message}");
            return;
        }

        ShowExportIssue("Export completed, but the platform launcher did not open the PDF.");
    }

    private static TextBox CreateExportOptionsTextBox(double width, bool isEnabled, string? text = null) =>
        new()
        {
            Width = width,
            Height = 24,
            MinHeight = 24,
            Padding = new Thickness(4, 2, 4, 2),
            Text = text,
            IsEnabled = isEnabled
        };

    private static ComboBox CreateExportOptionsComboBox(double width, bool isEnabled) =>
        new()
        {
            Width = width,
            // WPF's default combo boxes are 22px tall at the 12px dialog font.
            Height = 22,
            MinHeight = 22,
            Padding = new Thickness(6, 0, 4, 0),
            FontSize = 12,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            IsEnabled = isEnabled,
            SelectedIndex = 0
        };

    private static void AddComboItems(ComboBox comboBox, params string[] resourceKeys)
    {
        foreach (var resourceKey in resourceKeys)
            comboBox.Items.Add(UiText.Get(resourceKey));
        comboBox.SelectedIndex = 0;
    }
}
