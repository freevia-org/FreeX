using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace FreeX.App.Host;

internal static class PrintPreviewSettingsPanelFactory
{
    public static StackPanel Build(
        SheetId sheetId,
        Sheet? sheet,
        Action<IWorkbookCommand>? executeCommand,
        Action refreshPreview,
        Action<PrintPreviewSettings>? setPrintPreviewSettings = null,
        bool hasSelection = false,
        Action? showPageSetup = null,
        Action? showCustomMargins = null)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 10),
            Orientation = Orientation.Vertical
        };

        // Tracks the current backstage settings; rebuilt whenever a control changes.
        var currentSettings = new PrintPreviewSettings();

        void AddSectionLabel(string text) =>
            panel.Children.Add(new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 10, 0, 2),
                FontWeight = FontWeights.SemiBold
            });

        void AddLabel(string text, Control target) =>
            panel.Children.Add(new Label
            {
                Content = text,
                Target = target,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 10, 0, 2),
                FontWeight = FontWeights.SemiBold
            });

        static ComboBox MakeComboBox(string[] items, int selectedIndex)
        {
            var box = new ComboBox { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var item in items)
                box.Items.Add(item);
            box.SelectedIndex = selectedIndex;
            return box;
        }

        void ApplySettings(PrintPreviewSettings updated)
        {
            currentSettings = updated;
            setPrintPreviewSettings?.Invoke(updated);
            refreshPreview();
        }

        // ── 1. COPIES ─────────────────────────────────────────────────────────
        var copiesUpDown = new TextBox
        {
            Text = currentSettings.Copies.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = 60,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(copiesUpDown, UiText.Get("PrintPreview_CopiesSectionAutomationName"));
        AutomationProperties.SetHelpText(copiesUpDown, UiText.Get("PrintPreview_CopiesSectionHelpText"));
        AddLabel(UiText.Get("PrintPreview_CopiesSectionLabel"), copiesUpDown);
        copiesUpDown.TextChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null)
                return;
            if (int.TryParse(copiesUpDown.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= 1 and <= 999)
            {
                ApplySettings(currentSettings with { Copies = parsed });
            }
        };
        panel.Children.Add(copiesUpDown);

        // ── 2. PRINTER ────────────────────────────────────────────────────────
        var printerBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = UiText.Get("PrintPreview_PrinterToolTip")
        };
        AutomationProperties.SetName(printerBox, UiText.Get("PrintPreview_PrinterAutomationName"));
        AutomationProperties.SetHelpText(printerBox, UiText.Get("PrintPreview_PrinterHelpText"));
        PopulatePrinterBox(printerBox);
        AddLabel(UiText.Get("PrintPreview_PrinterSectionLabel"), printerBox);
        printerBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null)
                return;
            var name = printerBox.SelectedItem is PrintQueue q ? q.FullName : null;
            ApplySettings(currentSettings with { PrinterName = name });
        };
        panel.Children.Add(printerBox);

        var printerPropertiesBtn = new Button
        {
            Content = UiText.Get("PrintPreview_PrinterPropertiesButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 4),
            Padding = new Thickness(4, 2, 4, 2)
        };
        AutomationProperties.SetAutomationId(printerPropertiesBtn, "BackstagePrinterPropertiesButton");
        AutomationProperties.SetName(printerPropertiesBtn, UiText.Get("PrintPreview_PrinterPropertiesAutomationName"));
        AutomationProperties.SetHelpText(printerPropertiesBtn, UiText.Get("PrintPreview_PrinterPropertiesHelpText"));
        printerPropertiesBtn.Click += (_, _) =>
            NativePrintDialogService.ShowPrinterOptionsDialog();
        panel.Children.Add(printerPropertiesBtn);

        // ── 3. PRINT WHAT ─────────────────────────────────────────────────────
        var printWhatBox = MakeComboBox(
            [
                UiText.Get("PrintPreview_PrintWhatActiveSheets"),
                UiText.Get("PrintPreview_PrintWhatEntireWorkbook"),
                UiText.Get("PrintPreview_PrintWhatSelection")
            ],
            (int)currentSettings.PrintWhat);
        // Disable "Print Selection" when there is no current selection.
        if (!hasSelection && printWhatBox.Items.Count > 2)
        {
            if (printWhatBox.ItemContainerGenerator.ContainerFromIndex(2) is ComboBoxItem selItem)
                selItem.IsEnabled = false;
        }
        // Post-render hook so container is ready.
        printWhatBox.Loaded += (_, _) =>
        {
            if (!hasSelection && printWhatBox.ItemContainerGenerator.ContainerFromIndex(2) is ComboBoxItem selItem)
                selItem.IsEnabled = false;
        };
        AutomationProperties.SetName(printWhatBox, UiText.Get("PrintPreview_PrintWhatAutomationName"));
        AutomationProperties.SetHelpText(printWhatBox, UiText.Get("PrintPreview_PrintWhatHelpText"));
        AddLabel(UiText.Get("PrintPreview_PrintWhatLabel"), printWhatBox);
        printWhatBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || printWhatBox.SelectedIndex < 0)
                return;
            var what = (PrintWhat)printWhatBox.SelectedIndex;
            ApplySettings(currentSettings with { PrintWhat = what });
        };
        panel.Children.Add(printWhatBox);

        // ── 4. PAGE RANGE ─────────────────────────────────────────────────────
        var pageRangePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var fromBox = new TextBox
        {
            Width = 44,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_PageRangeFromHelpText")
        };
        AutomationProperties.SetName(fromBox, UiText.Get("PrintPreview_PageRangeFromAutomationName"));
        AutomationProperties.SetHelpText(fromBox, UiText.Get("PrintPreview_PageRangeFromHelpText"));
        var toLabel = new Label
        {
            Content = UiText.Get("PrintPreview_PageRangeToLabel"),
            Target = null,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var toBox = new TextBox
        {
            Width = 44,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_PageRangeToHelpText")
        };
        AutomationProperties.SetName(toBox, UiText.Get("PrintPreview_PageRangeToAutomationName"));
        AutomationProperties.SetHelpText(toBox, UiText.Get("PrintPreview_PageRangeToHelpText"));
        pageRangePanel.Children.Add(fromBox);
        pageRangePanel.Children.Add(toLabel);
        pageRangePanel.Children.Add(toBox);

        void ApplyPageRange()
        {
            if (setPrintPreviewSettings is null)
                return;
            var fromText = fromBox.Text?.Trim();
            var toText = toBox.Text?.Trim();
            int? from = string.IsNullOrEmpty(fromText) ? null :
                int.TryParse(fromText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fv) ? fv : null;
            int? to = string.IsNullOrEmpty(toText) ? null :
                int.TryParse(toText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tv) ? tv : null;
            ApplySettings(currentSettings with { PageFrom = from, PageTo = to });
        }

        fromBox.TextChanged += (_, _) => ApplyPageRange();
        toBox.TextChanged += (_, _) => ApplyPageRange();

        AddLabel(UiText.Get("PrintPreview_PageRangeFromLabel"), fromBox);
        panel.Children.Add(pageRangePanel);

        // ── 5. PRINT SIDES ────────────────────────────────────────────────────
        var sidesBox = MakeComboBox(
            [
                UiText.Get("PrintPreview_SidesOneSided"),
                UiText.Get("PrintPreview_SidesFlipLongEdge"),
                UiText.Get("PrintPreview_SidesFlipShortEdge")
            ],
            PrintSettingsPlanner.SidesModeToIndex(currentSettings.Sides));
        AutomationProperties.SetName(sidesBox, UiText.Get("PrintPreview_SidesAutomationName"));
        AutomationProperties.SetHelpText(sidesBox, UiText.Get("PrintPreview_SidesHelpText"));
        AddLabel(UiText.Get("PrintPreview_SidesSectionLabel"), sidesBox);
        sidesBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || sidesBox.SelectedIndex < 0)
                return;
            ApplySettings(currentSettings with { Sides = PrintSettingsPlanner.SidesIndexToMode(sidesBox.SelectedIndex) });
        };
        panel.Children.Add(sidesBox);

        // ── 6. COLLATION ──────────────────────────────────────────────────────
        var collatedBox = MakeComboBox(
            [
                UiText.Get("PrintPreview_CollatedOption"),
                UiText.Get("PrintPreview_UncollatedOption")
            ],
            currentSettings.Collated ? 0 : 1);
        AutomationProperties.SetName(collatedBox, UiText.Get("PrintPreview_CollatedSectionAutomationName"));
        AutomationProperties.SetHelpText(collatedBox, UiText.Get("PrintPreview_CollatedSectionHelpText"));
        AddLabel(UiText.Get("PrintPreview_CollatedSectionLabel"), collatedBox);
        collatedBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || collatedBox.SelectedIndex < 0)
                return;
            ApplySettings(currentSettings with { Collated = collatedBox.SelectedIndex == 0 });
        };
        panel.Children.Add(collatedBox);

        // ── 7. ORIENTATION ────────────────────────────────────────────────────
        var orientIndex = sheet?.PageOrientation == WorksheetPageOrientation.Landscape ? 1 : 0;
        var orientBox = MakeComboBox([UiText.Get("PageSetup_Portrait"), UiText.Get("PageSetup_Landscape")], orientIndex);
        AddLabel(UiText.Get("PrintPreview_OrientationLabel"), orientBox);
        orientBox.SelectionChanged += (_, _) =>
        {
            if (orientBox.SelectedIndex < 0 || executeCommand is null)
                return;

            var orient = orientBox.SelectedIndex == 1
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait;
            executeCommand(new SetPageOrientationCommand(sheetId, orient));
            refreshPreview();
        };
        panel.Children.Add(orientBox);

        // ── 7b. PAPER SIZE ────────────────────────────────────────────────────
        var paperIndex = sheet?.PaperSize switch
        {
            WorksheetPaperSize.Letter => 1,
            WorksheetPaperSize.Legal => 2,
            _ => 0
        };
        var paperBox = MakeComboBox(
            [
                UiText.Get("MainWindow_Header_A4"),
                UiText.Get("MainWindow_Header_Letter"),
                UiText.Get("MainWindow_Header_Legal")
            ],
            paperIndex);
        AddLabel(UiText.Get("PageSetup_PaperSize"), paperBox);
        paperBox.SelectionChanged += (_, _) =>
        {
            if (paperBox.SelectedIndex < 0 || executeCommand is null)
                return;

            var size = paperBox.SelectedIndex switch
            {
                1 => WorksheetPaperSize.Letter,
                2 => WorksheetPaperSize.Legal,
                _ => WorksheetPaperSize.A4
            };
            executeCommand(new SetPaperSizeCommand(sheetId, size));
            refreshPreview();
        };
        panel.Children.Add(paperBox);

        // ── 7c. MARGINS (with Custom Margins…) ────────────────────────────────
        var marginsIndex = sheet?.PageMargins == WorksheetPageMargins.Normal
            ? 1
            : sheet?.PageMargins == WorksheetPageMargins.Wide
                ? 2
                : 0;
        var marginsBox = MakeComboBox(
            [
                UiText.Get("MainWindow_Header_Narrow"),
                UiText.Get("MainWindow_Header_Normal"),
                UiText.Get("MainWindow_Header_Wide"),
                UiText.Get("PrintPreview_CustomMarginsOption")
            ],
            marginsIndex);
        AddLabel(UiText.Get("PageSetup_Margins"), marginsBox);
        marginsBox.SelectionChanged += (_, _) =>
        {
            if (marginsBox.SelectedIndex < 0 || executeCommand is null)
                return;

            // Index 3 = "Custom Margins..." — open Page Setup on Margins tab and reset combo.
            if (marginsBox.SelectedIndex == 3)
            {
                showCustomMargins?.Invoke();
                // Reset to neutral selection so if user cancels, combo doesn't stay on placeholder.
                marginsBox.SelectedIndex = marginsIndex;
                return;
            }

            var margins = marginsBox.SelectedIndex switch
            {
                1 => WorksheetPageMargins.Normal,
                2 => WorksheetPageMargins.Wide,
                _ => WorksheetPageMargins.Narrow
            };
            executeCommand(new SetPageMarginsCommand(sheetId, margins));
            refreshPreview();
        };
        panel.Children.Add(marginsBox);

        // ── 8. SCALING (expanded) ─────────────────────────────────────────────
        var stf = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        var scaleIndex = PrintSettingsPlanner.ScaleToFitToIndex(stf);
        var scaleBox = MakeComboBox(
            [
                UiText.Get("PrintPreview_ScaleNoScaling"),
                UiText.Get("PrintPreview_ScaleFitSheet"),
                UiText.Get("PrintPreview_ScaleFitColumns"),
                UiText.Get("PrintPreview_ScaleFitRows"),
                UiText.Get("PrintPreview_ScaleCustomOptions")
            ],
            scaleIndex);
        AddLabel(UiText.Get("PrintPreview_ScalingLabel"), scaleBox);
        scaleBox.SelectionChanged += (_, _) =>
        {
            if (scaleBox.SelectedIndex < 0 || executeCommand is null)
                return;

            // Index 4 = "Custom Scaling Options…" — open Page Setup, reset combo.
            if (scaleBox.SelectedIndex == 4)
            {
                showPageSetup?.Invoke();
                scaleBox.SelectedIndex = scaleIndex;
                return;
            }

            var scale = PrintSettingsPlanner.ScaleIndexToScaleToFit(scaleBox.SelectedIndex);
            executeCommand(new SetScaleToFitCommand(sheetId, scale));
            refreshPreview();
        };
        panel.Children.Add(scaleBox);

        // ── IGNORE PRINT AREA ─────────────────────────────────────────────────
        var ignorePrintAreaBox = new CheckBox
        {
            Content = UiText.Get("PrintPreview_IgnorePrintArea"),
            IsChecked = currentSettings.IgnorePrintArea,
            IsEnabled = sheet?.PrintArea is not null && setPrintPreviewSettings is not null,
            Margin = new Thickness(0, 6, 0, 4),
            ToolTip = UiText.Get("PrintPreview_IgnorePrintAreaToolTip")
        };
        AutomationProperties.SetName(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaAutomationName"));
        AutomationProperties.SetHelpText(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaHelpText"));
        ignorePrintAreaBox.Checked += (_, _) =>
        {
            if (setPrintPreviewSettings is null)
                return;
            ApplySettings(currentSettings with { IgnorePrintArea = true });
        };
        ignorePrintAreaBox.Unchecked += (_, _) =>
        {
            if (setPrintPreviewSettings is null)
                return;
            ApplySettings(currentSettings with { IgnorePrintArea = false });
        };
        panel.Children.Add(ignorePrintAreaBox);

        // ── PRINT OPTIONS ─────────────────────────────────────────────────────
        AddSectionLabel(UiText.Get("PrintPreview_PrintOptionsSection"));
        var gridlinesBox = new CheckBox
        {
            Content = UiText.Get("PageSetup_PrintGridlines"),
            IsChecked = sheet?.PrintGridlines ?? false,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var headingsBox = new CheckBox
        {
            Content = UiText.Get("PageSetup_PrintRowAndColumnHeadings"),
            IsChecked = sheet?.PrintHeadings ?? false,
            Margin = new Thickness(0, 0, 0, 4)
        };

        void ApplyPrintOptions(bool printGridlines, bool printHeadings)
        {
            if (executeCommand is null)
                return;
            executeCommand(new SetPrintOptionsCommand(sheetId, printGridlines, printHeadings));
            refreshPreview();
        }

        gridlinesBox.Checked += (_, _) => ApplyPrintOptions(true, headingsBox.IsChecked == true);
        gridlinesBox.Unchecked += (_, _) => ApplyPrintOptions(false, headingsBox.IsChecked == true);
        headingsBox.Checked += (_, _) => ApplyPrintOptions(gridlinesBox.IsChecked == true, true);
        headingsBox.Unchecked += (_, _) => ApplyPrintOptions(gridlinesBox.IsChecked == true, false);
        panel.Children.Add(gridlinesBox);
        panel.Children.Add(headingsBox);

        // ── PAGE SETUP LINK ───────────────────────────────────────────────────
        var pageSetupLink = new Button
        {
            Content = UiText.Get("PrintPreview_PageSetupLink"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 4),
            Padding = new Thickness(4, 2, 4, 2)
        };
        AutomationProperties.SetAutomationId(pageSetupLink, "BackstagePageSetupLink");
        AutomationProperties.SetName(pageSetupLink, UiText.Get("PrintPreview_PageSetupLinkAutomationName"));
        AutomationProperties.SetHelpText(pageSetupLink, UiText.Get("PrintPreview_PageSetupLinkHelpText"));
        pageSetupLink.Click += (_, _) =>
        {
            showPageSetup?.Invoke();
            refreshPreview();
        };
        panel.Children.Add(pageSetupLink);

        return panel;
    }

    private static void PopulatePrinterBox(ComboBox printerBox)
    {
        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
                printerBox.Items.Add(queue);

            if (printerBox.Items.Count > 0)
            {
                printerBox.DisplayMemberPath = nameof(PrintQueue.FullName);
                printerBox.SelectedItem = null;
                foreach (var item in printerBox.Items)
                {
                    if (item is not PrintQueue queue)
                        continue;
                    if (string.Equals(
                            queue.FullName,
                            server.DefaultPrintQueue.FullName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        printerBox.SelectedItem = queue;
                        break;
                    }
                }
                if (printerBox.SelectedItem is null)
                    printerBox.SelectedIndex = 0;
                return;
            }
        }
        catch (PrintSystemException)
        {
        }

        printerBox.IsEnabled = false;
        printerBox.ToolTip = UiText.Get("PrintPreview_NoInstalledPrintersToolTip");
        AutomationProperties.SetHelpText(printerBox, UiText.Get("PrintPreview_NoInstalledPrintersHelpText"));
    }
}
