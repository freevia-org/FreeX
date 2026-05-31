using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class PrintPreviewSettingsPanelFactory
{
    public static StackPanel Build(
        SheetId sheetId,
        Sheet? sheet,
        Action<IWorkbookCommand>? executeCommand,
        Action refreshPreview,
        Action<PrintPreviewSettings>? setPrintPreviewSettings = null)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 10),
            Orientation = Orientation.Vertical
        };

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

        void ApplyPrintOptions(bool printGridlines, bool printHeadings)
        {
            if (executeCommand is null)
                return;

            executeCommand(new SetPrintOptionsCommand(sheetId, printGridlines, printHeadings));
            refreshPreview();
        }

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

        var marginsIndex = sheet?.PageMargins == WorksheetPageMargins.Normal
            ? 1
            : sheet?.PageMargins == WorksheetPageMargins.Wide
                ? 2
                : 0;
        var marginsBox = MakeComboBox(
            [
                UiText.Get("MainWindow_Header_Narrow"),
                UiText.Get("MainWindow_Header_Normal"),
                UiText.Get("MainWindow_Header_Wide")
            ],
            marginsIndex);
        AddLabel(UiText.Get("PageSetup_Margins"), marginsBox);
        marginsBox.SelectionChanged += (_, _) =>
        {
            if (marginsBox.SelectedIndex < 0 || executeCommand is null)
                return;

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

        var stf = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        var scaleIndex = stf switch
        {
            { FitToPagesWide: 1, FitToPagesTall: 1 } => 1,
            { FitToPagesWide: 1, FitToPagesTall: null } => 2,
            _ => 0
        };
        var scaleBox = MakeComboBox(
            [
                UiText.Get("PrintPreview_Scale100Percent"),
                UiText.Get("PrintPreview_FitToOnePage"),
                UiText.Get("PrintPreview_FitToOnePageWide")
            ],
            scaleIndex);
        AddLabel(UiText.Get("PrintPreview_ScalingLabel"), scaleBox);
        scaleBox.SelectionChanged += (_, _) =>
        {
            if (scaleBox.SelectedIndex < 0 || executeCommand is null)
                return;

            var scale = scaleBox.SelectedIndex switch
            {
                1 => new WorksheetScaleToFit(null, 1, 1),
                2 => new WorksheetScaleToFit(null, 1, null),
                _ => WorksheetScaleToFit.Default
            };
            executeCommand(new SetScaleToFitCommand(sheetId, scale));
            refreshPreview();
        };
        panel.Children.Add(scaleBox);

        var ignorePrintAreaBox = new CheckBox
        {
            Content = UiText.Get("PrintPreview_IgnorePrintArea"),
            IsChecked = false,
            IsEnabled = sheet?.PrintArea is not null && setPrintPreviewSettings is not null,
            Margin = new Thickness(0, 6, 0, 4),
            ToolTip = UiText.Get("PrintPreview_IgnorePrintAreaToolTip")
        };
        AutomationProperties.SetName(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaAutomationName"));
        AutomationProperties.SetHelpText(ignorePrintAreaBox, UiText.Get("PrintPreview_IgnorePrintAreaHelpText"));

        void ApplyPrintPreviewSettings()
        {
            if (setPrintPreviewSettings is null)
                return;

            setPrintPreviewSettings(new PrintPreviewSettings(ignorePrintAreaBox.IsChecked == true));
            refreshPreview();
        }

        ignorePrintAreaBox.Checked += (_, _) => ApplyPrintPreviewSettings();
        ignorePrintAreaBox.Unchecked += (_, _) => ApplyPrintPreviewSettings();
        panel.Children.Add(ignorePrintAreaBox);

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
        gridlinesBox.Checked += (_, _) => ApplyPrintOptions(true, headingsBox.IsChecked == true);
        gridlinesBox.Unchecked += (_, _) => ApplyPrintOptions(false, headingsBox.IsChecked == true);
        headingsBox.Checked += (_, _) => ApplyPrintOptions(gridlinesBox.IsChecked == true, true);
        headingsBox.Unchecked += (_, _) => ApplyPrintOptions(gridlinesBox.IsChecked == true, false);
        panel.Children.Add(gridlinesBox);
        panel.Children.Add(headingsBox);

        return panel;
    }
}
