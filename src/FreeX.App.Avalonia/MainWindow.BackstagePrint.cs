using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.PageLayout;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The working part of File &gt; Print, laid out like Excel's Print pane: a Print button and the print
/// settings on the left, the live page preview on the right. It reuses the Print Preview window's
/// settings rail, pagination and page painting, so both surfaces print and preview identically.
/// </summary>
public sealed partial class MainWindow
{
    private const double BackstagePrintPreviewHeight = 560;

    private Control BuildLiveBackstagePrintWorkspace()
    {
        var currentSettings = new PrintPreviewSettings();
        var canSpool = false;
        IReadOnlyList<string> printerNames = [];
        string? defaultPrinterName = null;
        var printerBoxes = new List<ComboBox>();

        if (!TryCreatePreviewContextForSettings(currentSettings, out var context))
            context = AvaloniaPrintPreviewPaginationContext.Empty();
        var navigator = PrintPreviewPageNavigator.Create(context.PageCount);

        var pageHost = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Height = BackstagePrintPreviewHeight,
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(pageHost, "BackstagePrintPreviewHost");

        var pageStatus = new TextBlock
        {
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        AutomationProperties.SetAutomationId(pageStatus, "BackstagePrintPageStatus");
        var previousButton = CreatePreviewToolbarButton("◀");
        var nextButton = CreatePreviewToolbarButton("▶");
        AutomationProperties.SetAutomationId(previousButton, "BackstagePrintPreviousPage");
        AutomationProperties.SetAutomationId(nextButton, "BackstagePrintNextPage");

        void Render()
        {
            pageHost.Child = context.PageCount > 0
                ? BuildPreviewDocumentViewerSurface(context, navigator.CurrentIndex)
                : new TextBlock
                {
                    Text = UiText.Get("ShellLoc_NothingToPreview"),
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    Margin = new Thickness(16),
                };
            pageStatus.Text = PrintPreviewNavigationState
                .Create(context.PageCount == 0 ? 0 : navigator.CurrentIndex + 1, context.PageCount)
                .StatusText;
            previousButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
        }

        void Repaginate()
        {
            if (!TryCreatePreviewContextForSettings(currentSettings, out context))
                context = AvaloniaPrintPreviewPaginationContext.Empty();
            navigator = PrintPreviewPageNavigator.Create(context.PageCount).JumpTo(navigator.CurrentIndex);
            Render();
        }

        previousButton.Click += (_, _) =>
        {
            navigator = navigator.Previous();
            Render();
        };
        nextButton.Click += (_, _) =>
        {
            navigator = navigator.Next();
            Render();
        };

        var printButton = CreateLiveBackstageActionButton(
            UiText.Get("MainWindow_Text_Print"),
            "BackstagePrintNowButton",
            () => PrintWithPreviewSettingsAsync(currentSettings, canSpool, printerNames, defaultPrinterName));
        printButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;

        var previewWindowButton = CreateLiveBackstageActionButton(
            UiText.Get("ShellLoc_PrintPreviewTitle"),
            "BackstagePrintPreviewButton",
            async () => await ShowPrintPreviewDialogAsync());
        previewWindowButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;

        var rail = CreatePrintPreviewSettingsRail(
            PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
                _session.ActiveSheet,
                context.PageCount,
                UiText.Get("Print_SaveAsPdfButton").Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.'),
                currentSettings,
                hasSelection: HasPrintSelection(_session.SelectedRange),
                canUpdatePrintPreviewSettings: true,
                PrintPreviewSettingsTextResolver),
            new PrintPreviewSettingsRailInteraction(
                _session.ActiveSheet.Id,
                () => currentSettings,
                updated => currentSettings = updated,
                Repaginate),
            printerBoxes.Add);
        rail.MaxHeight = BackstagePrintPreviewHeight;

        var leftColumn = new StackPanel { Spacing = 10, Width = PrintPreviewSurfacePlanner.SettingsRailWidth + 24 };
        leftColumn.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { printButton, previewWindowButton },
        });
        leftColumn.Children.Add(rail);

        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { previousButton, pageStatus, nextButton },
        };
        var rightColumn = new DockPanel { Margin = new Thickness(24, 0, 0, 0) };
        DockPanel.SetDock(navigation, Dock.Bottom);
        rightColumn.Children.Add(navigation);
        rightColumn.Children.Add(new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Child = pageHost,
        });

        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 16, 0, 0),
        };
        Grid.SetColumn(leftColumn, 0);
        Grid.SetColumn(rightColumn, 1);
        workspace.Children.Add(leftColumn);
        workspace.Children.Add(rightColumn);
        AutomationProperties.SetAutomationId(workspace, "BackstagePrintWorkspace");

        Render();

        // Fill the printer list without delaying the pane; lpstat can take a moment.
        if (_printService.IsSupported)
        {
            workspace.AttachedToVisualTree += (_, _) => RunGuarded(async () =>
            {
                var discovery = await _printService.DiscoverAsync();
                if (!discovery.IsAvailable)
                    return;

                printerNames = discovery.Printers.Select(printer => printer.Name).ToArray();
                defaultPrinterName = printerNames.FirstOrDefault(
                        name => string.Equals(name, discovery.DefaultPrinter, StringComparison.Ordinal))
                    ?? printerNames[0];
                canSpool = true;
                foreach (var printerBox in printerBoxes)
                {
                    printerBox.ItemsSource = printerNames;
                    printerBox.SelectedItem = defaultPrinterName;
                }
            });
        }

        return workspace;
    }
}
