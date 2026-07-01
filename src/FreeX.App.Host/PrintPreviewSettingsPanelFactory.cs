using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;
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
        Action? showCustomMargins = null,
        string? fixturePrinterName = null)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 10),
            Orientation = Orientation.Vertical
        };

        // Tracks the current backstage settings; rebuilt whenever a control changes.
        var currentSettings = new PrintPreviewSettings();
        var railPlan = PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
            sheet,
            totalPages: 1,
            printerName: fixturePrinterName ?? string.Empty,
            currentSettings,
            hasSelection,
            setPrintPreviewSettings is not null,
            WpfPrintSettingsTextResolver.Instance,
            stripMnemonics: false);
        var panelPlan = railPlan.Settings;

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

        void ApplyAction(PrintPreviewSettingsPanelActionPlan action)
        {
            switch (action.Kind)
            {
                case PrintPreviewSettingsPanelActionKind.UpdatePreviewSettings:
                    if (setPrintPreviewSettings is null || action.Settings is null)
                        return;

                    ApplySettings(action.Settings);
                    break;

                case PrintPreviewSettingsPanelActionKind.ExecuteCommand:
                    if (executeCommand is null || action.Command is null)
                        return;

                    executeCommand(action.Command);
                    if (action.RefreshPreview)
                        refreshPreview();
                    break;

                case PrintPreviewSettingsPanelActionKind.OpenCustomMargins:
                    showCustomMargins?.Invoke();
                    break;

                case PrintPreviewSettingsPanelActionKind.OpenPageSetup:
                    showPageSetup?.Invoke();
                    break;
            }
        }

        // 1. Copies
        var copiesUpDown = new TextBox
        {
            Text = panelPlan.Copies.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = railPlan.CopiesBoxWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(copiesUpDown, UiText.Get("PrintPreview_CopiesSectionAutomationName"));
        AutomationProperties.SetHelpText(copiesUpDown, UiText.Get("PrintPreview_CopiesSectionHelpText"));
        AddLabel(railPlan.CopiesSectionText, copiesUpDown);
        copiesUpDown.TextChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreateCopiesAction(
                currentSettings,
                copiesUpDown.Text));
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
        WpfPrintPreviewToolbarPlanner.PopulatePrinterBox(
            printerBox,
            UiText.Get("PrintPreview_NoInstalledPrintersToolTip"),
            UiText.Get("PrintPreview_NoInstalledPrintersHelpText"),
            fixturePrinterName);
        AddLabel(railPlan.PrinterSectionText, printerBox);
        printerBox.SelectionChanged += (_, _) =>
        {
            var name = printerBox.SelectedItem switch
            {
                PrintQueue q => q.FullName,
                string fixtureName => fixtureName,
                _ => null
            };
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePrinterAction(currentSettings, name));
        };
        panel.Children.Add(printerBox);

        var printerPropertiesBtn = new Button
        {
            Content = railPlan.PrinterPropertiesButtonText,
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
        AddLabel(railPlan.PrintWhatLabelText, printWhatBox);
        printWhatBox.SelectionChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePrintWhatAction(
                panelPlan,
                currentSettings,
                printWhatBox.SelectedIndex));
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
            Text = railPlan.PageRange.FromPageText,
            Width = railPlan.PageRange.PageBoxWidth,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_PageRangeFromHelpText")
        };
        AutomationProperties.SetName(fromBox, UiText.Get("PrintPreview_PageRangeFromAutomationName"));
        AutomationProperties.SetHelpText(fromBox, UiText.Get("PrintPreview_PageRangeFromHelpText"));
        var toLabel = new Label
        {
            Content = railPlan.PageRange.ToSeparatorText,
            Target = null,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var toBox = new TextBox
        {
            Text = railPlan.PageRange.ToPageText,
            Width = railPlan.PageRange.PageBoxWidth,
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
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePageRangeAction(
                currentSettings,
                fromBox.Text,
                toBox.Text));
        }

        fromBox.TextChanged += (_, _) => ApplyPageRange();
        toBox.TextChanged += (_, _) => ApplyPageRange();

        AddLabel(railPlan.PagesLabelText, fromBox);
        panel.Children.Add(pageRangePanel);

        // 5. Print sides
        var sidesBox = MakeComboBox(
            panelPlan.SidesOptions,
            panelPlan.SidesSelectedIndex);
        AutomationProperties.SetName(sidesBox, UiText.Get("PrintPreview_SidesAutomationName"));
        AutomationProperties.SetHelpText(sidesBox, UiText.Get("PrintPreview_SidesHelpText"));
        AddLabel(railPlan.SidesSectionText, sidesBox);
        sidesBox.SelectionChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreateSidesAction(
                panelPlan,
                currentSettings,
                sidesBox.SelectedIndex));
        };
        panel.Children.Add(sidesBox);

        // 6. Collation
        var collatedBox = MakeComboBox(
            panelPlan.CollationOptions,
            panelPlan.CollationSelectedIndex);
        AutomationProperties.SetName(collatedBox, UiText.Get("PrintPreview_CollatedSectionAutomationName"));
        AutomationProperties.SetHelpText(collatedBox, UiText.Get("PrintPreview_CollatedSectionHelpText"));
        AddLabel(railPlan.CollationSectionText, collatedBox);
        collatedBox.SelectionChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreateCollationAction(
                panelPlan,
                currentSettings,
                collatedBox.SelectedIndex));
        };
        panel.Children.Add(collatedBox);

        // 7. Orientation
        var orientBox = MakeComboBox(panelPlan.OrientationOptions, panelPlan.OrientationSelectedIndex);
        AddLabel(railPlan.OrientationLabelText, orientBox);
        orientBox.SelectionChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreateOrientationAction(
                sheetId,
                panelPlan,
                orientBox.SelectedIndex));
        };
        panel.Children.Add(orientBox);

        // 7b. Paper size
        var paperBox = MakeComboBox(panelPlan.PaperSizeOptions, panelPlan.PaperSizeSelectedIndex);
        AddLabel(railPlan.PaperSizeLabelText, paperBox);
        paperBox.SelectionChanged += (_, _) =>
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePaperSizeAction(
                sheetId,
                panelPlan,
                paperBox.SelectedIndex));
        };
        panel.Children.Add(paperBox);

        // 7c. Margins with Custom Margins option
        var marginsBox = MakeComboBox(panelPlan.MarginOptions, panelPlan.MarginsSelectedIndex);
        AddLabel(railPlan.MarginsLabelText, marginsBox);
        marginsBox.SelectionChanged += (_, _) =>
        {
            var action = PrintPreviewSettingsPanelPlanner.CreateMarginsAction(
                sheetId,
                panelPlan,
                marginsBox.SelectedIndex);
            ApplyAction(action);
            if (action.ResetSelection)
                marginsBox.SelectedIndex = panelPlan.MarginsSelectedIndex;
        };
        panel.Children.Add(marginsBox);

        // 8. Scaling
        var scaleBox = MakeComboBox(panelPlan.ScalingOptions, panelPlan.ScalingSelectedIndex);
        AddLabel(railPlan.ScalingLabelText, scaleBox);
        scaleBox.SelectionChanged += (_, _) =>
        {
            var action = PrintPreviewSettingsPanelPlanner.CreateScalingAction(
                sheetId,
                panelPlan,
                scaleBox.SelectedIndex);
            ApplyAction(action);
            if (action.ResetSelection)
                scaleBox.SelectedIndex = panelPlan.ScalingSelectedIndex;
        };
        panel.Children.Add(scaleBox);

        // Ignore print area
        var ignorePrintAreaBox = new CheckBox
        {
            Content = railPlan.IgnorePrintAreaText,
            IsChecked = panelPlan.IgnorePrintAreaChecked,
            IsEnabled = panelPlan.IgnorePrintAreaEnabled,
            Margin = new Thickness(0, 6, 0, 4),
            ToolTip = UiText.Get("PrintPreview_IgnorePrintAreaToolTip")
        };
        AutomationProperties.SetName(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaAutomationName"));
        AutomationProperties.SetHelpText(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaHelpText"));
        ignorePrintAreaBox.Checked += (_, _) => ApplyAction(
            PrintPreviewSettingsPanelPlanner.CreateIgnorePrintAreaAction(currentSettings, true));
        ignorePrintAreaBox.Unchecked += (_, _) => ApplyAction(
            PrintPreviewSettingsPanelPlanner.CreateIgnorePrintAreaAction(currentSettings, false));
        panel.Children.Add(ignorePrintAreaBox);

        // Print options
        AddSectionLabel(railPlan.PrintOptionsSectionText);
        var gridlinesBox = new CheckBox
        {
            Content = railPlan.PrintGridlinesText,
            IsChecked = panelPlan.PrintGridlines,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var headingsBox = new CheckBox
        {
            Content = railPlan.PrintHeadingsText,
            IsChecked = panelPlan.PrintHeadings,
            Margin = new Thickness(0, 0, 0, 4)
        };

        void ApplyPrintOptions(bool printGridlines, bool printHeadings)
        {
            ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePrintOptionsAction(
                sheetId,
                printGridlines,
                printHeadings));
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
            Content = railPlan.PageSetupLinkText,
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

}
