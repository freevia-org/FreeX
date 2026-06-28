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
        var panelPlan = PrintPreviewSettingsPanelPlanner.Build(
            sheet,
            currentSettings,
            hasSelection,
            setPrintPreviewSettings is not null,
            WpfPrintSettingsTextResolver.Instance);

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

        static ComboBox MakeComboBox<TValue>(
            IReadOnlyList<PrintPreviewChoice<TValue>> items,
            int selectedIndex)
        {
            var box = new ComboBox { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var item in items)
            {
                box.Items.Add(new ComboBoxItem
                {
                    Content = item.Text,
                    IsEnabled = item.IsEnabled
                });
            }

            box.SelectedIndex = selectedIndex;
            return box;
        }

        void ApplySettings(PrintPreviewSettings updated)
        {
            currentSettings = updated;
            setPrintPreviewSettings?.Invoke(updated);
            refreshPreview();
        }

        // 1. Copies
        var copiesUpDown = new TextBox
        {
            Text = panelPlan.Copies.ToString(CultureInfo.InvariantCulture),
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

        // 2. Printer
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

        // 3. Print what
        var printWhatBox = MakeComboBox(
            panelPlan.PrintWhatOptions,
            panelPlan.PrintWhatSelectedIndex);
        AutomationProperties.SetName(printWhatBox, UiText.Get("PrintPreview_PrintWhatAutomationName"));
        AutomationProperties.SetHelpText(printWhatBox, UiText.Get("PrintPreview_PrintWhatHelpText"));
        AddLabel(UiText.Get("PrintPreview_PrintWhatLabel"), printWhatBox);
        printWhatBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || printWhatBox.SelectedIndex < 0)
                return;

            var option = panelPlan.PrintWhatOptions[printWhatBox.SelectedIndex];
            if (!option.IsEnabled)
                return;

            ApplySettings(currentSettings with { PrintWhat = option.Value });
        };
        panel.Children.Add(printWhatBox);

        // 4. Page range
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

        // 5. Print sides
        var sidesBox = MakeComboBox(
            panelPlan.SidesOptions,
            panelPlan.SidesSelectedIndex);
        AutomationProperties.SetName(sidesBox, UiText.Get("PrintPreview_SidesAutomationName"));
        AutomationProperties.SetHelpText(sidesBox, UiText.Get("PrintPreview_SidesHelpText"));
        AddLabel(UiText.Get("PrintPreview_SidesSectionLabel"), sidesBox);
        sidesBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || sidesBox.SelectedIndex < 0)
                return;

            ApplySettings(currentSettings with { Sides = panelPlan.SidesOptions[sidesBox.SelectedIndex].Value });
        };
        panel.Children.Add(sidesBox);

        // 6. Collation
        var collatedBox = MakeComboBox(
            panelPlan.CollationOptions,
            panelPlan.CollationSelectedIndex);
        AutomationProperties.SetName(collatedBox, UiText.Get("PrintPreview_CollatedSectionAutomationName"));
        AutomationProperties.SetHelpText(collatedBox, UiText.Get("PrintPreview_CollatedSectionHelpText"));
        AddLabel(UiText.Get("PrintPreview_CollatedSectionLabel"), collatedBox);
        collatedBox.SelectionChanged += (_, _) =>
        {
            if (setPrintPreviewSettings is null || collatedBox.SelectedIndex < 0)
                return;

            ApplySettings(currentSettings with { Collated = panelPlan.CollationOptions[collatedBox.SelectedIndex].Value });
        };
        panel.Children.Add(collatedBox);

        // 7. Orientation
        var orientBox = MakeComboBox(panelPlan.OrientationOptions, panelPlan.OrientationSelectedIndex);
        AddLabel(UiText.Get("PrintPreview_OrientationLabel"), orientBox);
        orientBox.SelectionChanged += (_, _) =>
        {
            if (orientBox.SelectedIndex < 0 || executeCommand is null)
                return;

            var orient = panelPlan.OrientationOptions[orientBox.SelectedIndex].Value;
            executeCommand(new SetPageOrientationCommand(sheetId, orient));
            refreshPreview();
        };
        panel.Children.Add(orientBox);

        // 7b. Paper size
        var paperBox = MakeComboBox(panelPlan.PaperSizeOptions, panelPlan.PaperSizeSelectedIndex);
        AddLabel(UiText.Get("PageSetup_PaperSize"), paperBox);
        paperBox.SelectionChanged += (_, _) =>
        {
            if (paperBox.SelectedIndex < 0 || executeCommand is null)
                return;

            var size = panelPlan.PaperSizeOptions[paperBox.SelectedIndex].Value;
            executeCommand(new SetPaperSizeCommand(sheetId, size));
            refreshPreview();
        };
        panel.Children.Add(paperBox);

        // 7c. Margins with Custom Margins option
        var marginsBox = MakeComboBox(panelPlan.MarginOptions, panelPlan.MarginsSelectedIndex);
        AddLabel(UiText.Get("PageSetup_Margins"), marginsBox);
        marginsBox.SelectionChanged += (_, _) =>
        {
            if (marginsBox.SelectedIndex < 0 || executeCommand is null)
                return;

            // The placeholder opens Page Setup on the Margins tab and then resets the combo.
            var option = panelPlan.MarginOptions[marginsBox.SelectedIndex];
            if (option.IsPlaceholder)
            {
                showCustomMargins?.Invoke();
                // Reset to neutral selection so if user cancels, combo doesn't stay on placeholder.
                marginsBox.SelectedIndex = panelPlan.MarginsSelectedIndex;
                return;
            }

            executeCommand(new SetPageMarginsCommand(sheetId, option.Value));
            refreshPreview();
        };
        panel.Children.Add(marginsBox);

        // 8. Scaling
        var scaleBox = MakeComboBox(panelPlan.ScalingOptions, panelPlan.ScalingSelectedIndex);
        AddLabel(UiText.Get("PrintPreview_ScalingLabel"), scaleBox);
        scaleBox.SelectionChanged += (_, _) =>
        {
            if (scaleBox.SelectedIndex < 0 || executeCommand is null)
                return;

            // The placeholder opens Page Setup and then resets the combo.
            var option = panelPlan.ScalingOptions[scaleBox.SelectedIndex];
            if (option.IsPlaceholder)
            {
                showPageSetup?.Invoke();
                scaleBox.SelectedIndex = panelPlan.ScalingSelectedIndex;
                return;
            }

            executeCommand(new SetScaleToFitCommand(sheetId, option.Value));
            refreshPreview();
        };
        panel.Children.Add(scaleBox);

        // Ignore print area
        var ignorePrintAreaBox = new CheckBox
        {
            Content = UiText.Get("PrintPreview_IgnorePrintArea"),
            IsChecked = panelPlan.IgnorePrintAreaChecked,
            IsEnabled = panelPlan.IgnorePrintAreaEnabled,
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

        // Print options
        AddSectionLabel(UiText.Get("PrintPreview_PrintOptionsSection"));
        var gridlinesBox = new CheckBox
        {
            Content = UiText.Get("PageSetup_PrintGridlines"),
            IsChecked = panelPlan.PrintGridlines,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var headingsBox = new CheckBox
        {
            Content = UiText.Get("PageSetup_PrintRowAndColumnHeadings"),
            IsChecked = panelPlan.PrintHeadings,
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

        // Page Setup link
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
