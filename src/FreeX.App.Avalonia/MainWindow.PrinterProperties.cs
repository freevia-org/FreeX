using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, PrinterOptionSelection> _printerJobOptions =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _printerPropertiesOpen;

    private bool CanShowPrinterProperties(string? printerName) =>
        _printService.IsSupported
        && _printService is IPlatformPrinterOptionsService
        && !string.IsNullOrWhiteSpace(printerName);

    private PrinterOptionSelection? GetPrinterJobOptions(string? printerName) =>
        printerName is null ? null : _printerJobOptions.GetValueOrDefault(printerName);

    private Button? CreatePrintDialogPrinterPropertiesButton(Window owner, ComboBox printer)
    {
        if (!CanShowPrinterProperties(printer.SelectedItem as string))
            return null;

        var button = new Button
        {
            Content = UiText.Get("PrintPreview_PrinterPropertiesButton"),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        };
        StripContentMnemonic(button);
        AvaloniaCompactDialogChrome.ApplyButton(button, PrintDialogChromeStyle, minWidth: 72);
        ConfigurePrinterPropertiesButton(
            button, "PrintPrinterPropertiesButton", owner, printer, () => printer.SelectedItem as string);
        return button;
    }

    private void ConfigurePrinterPropertiesButton(
        Button button,
        string automationId,
        Window owner,
        ComboBox printer,
        Func<string?> resolvePrinterName)
    {
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, UiText.Get("PrintPreview_PrinterPropertiesAutomationName"));
        AutomationProperties.SetHelpText(button, UiText.Get("PrintPreview_PrinterPropertiesHelpText"));

        void UpdateAvailability() => button.IsEnabled = CanShowPrinterProperties(resolvePrinterName());

        UpdateAvailability();
        printer.SelectionChanged += (_, _) => UpdateAvailability();
        button.Click += (_, _) =>
        {
            if (resolvePrinterName() is { } printerName && CanShowPrinterProperties(printerName))
                RunGuarded(() => ShowPrinterPropertiesAsync(owner, printerName));
        };
    }

    private async Task ShowPrinterPropertiesAsync(Window owner, string printerName)
    {
        if (!CanShowPrinterProperties(printerName)
            || _printService is not IPlatformPrinterOptionsService printerOptions
            || _printerPropertiesOpen
            || !owner.IsVisible)
        {
            return;
        }

        _printerPropertiesOpen = true;
        try
        {
            var selection = await ShowPrinterOptionsDialogAsync(
                owner, printerName, printerOptions, GetPrinterJobOptions(printerName));
            if (selection is null)
                return;

            if (selection.Values.Count == 0)
                _printerJobOptions.Remove(printerName);
            else
                _printerJobOptions[printerName] = selection;
        }
        finally
        {
            _printerPropertiesOpen = false;
        }
    }

    private static async Task<PrinterOptionSelection?> ShowPrinterOptionsDialogAsync(
        Window owner,
        string printerName,
        IPlatformPrinterOptionsService service,
        PrinterOptionSelection? savedSelection)
    {
        var dialog = new Window
        {
            Title = UiText.Get("PrintPreview_PrinterPropertiesAutomationName"),
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, PrintDialogChromeStyle);
        AutomationProperties.SetAutomationId(dialog, "PrinterPropertiesDialog");

        var optionsPanel = new StackPanel { Spacing = 10 };
        var optionsScroll = new ScrollViewer
        {
            Content = optionsPanel,
            MaxHeight = 360,
            IsVisible = false,
        };
        var status = new TextBlock
        {
            Text = UiText.Get("Print_ConnectingToPrinter"),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(status, "PrinterPropertiesStatus");

        var editors = new List<(PrinterOptionDefinition Option, ComboBox Selector)>();
        var ok = AvaloniaCompactDialogChrome.CreateActionButton(
            UiText.Get("Common_OkText"),
            () =>
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (option, selector) in editors)
                {
                    if (selector.SelectedIndex > 0)
                        values.Add(option.Key, option.Choices[selector.SelectedIndex - 1].Value);
                }

                dialog.Close(new PrinterOptionSelection(printerName, new ReadOnlyDictionary<string, string>(values)));
            },
            minWidth: 84,
            isDefault: true,
            style: PrintDialogChromeStyle);
        ok.IsEnabled = false;
        AutomationProperties.SetAutomationId(ok, "PrinterPropertiesOkButton");
        var cancel = AvaloniaCompactDialogChrome.CreateActionButton(
            UiText.Get("Common_CancelText"), dialog.Close, minWidth: 84,
            isCancel: true, style: PrintDialogChromeStyle);
        AutomationProperties.SetAutomationId(cancel, "PrinterPropertiesCancelButton");

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = printerName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                status,
                optionsScroll,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]),
            },
        };

        using var cancellation = new CancellationTokenSource();
        dialog.Closed += (_, _) => cancellation.Cancel();
        dialog.Opened += async (_, _) =>
        {
            cancel.Focus();
            PrinterOptionsResult result;
            try
            {
                result = await service.GetOptionsAsync(printerName, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result = new PrinterOptionsResult(PrinterOptionsStatus.Failed, [], ex.Message);
            }

            if (cancellation.IsCancellationRequested)
                return;

            if (result.Status == PrinterOptionsStatus.Available)
            {
                foreach (var option in result.Options)
                {
                    if (option.Choices.Count == 0 ||
                        (option.Choices.Count == 1 && option.Choices[0].Value == option.DefaultValue))
                    {
                        continue;
                    }

                    var selector = CreatePrinterOptionSelector(option, savedSelection);
                    editors.Add((option, selector));
                    optionsPanel.Children.Add(new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap },
                            selector,
                        },
                    });
                }
            }

            if (editors.Count == 0)
            {
                status.Text = result.Status is PrinterOptionsStatus.Available or PrinterOptionsStatus.NoOptions
                    ? UiText.Get("Print_PrinterOptionsEmpty")
                    : UiText.Get("Print_PrinterOptionsUnavailable");
                if (!string.IsNullOrWhiteSpace(result.Message))
                    ToolTip.SetTip(status, result.Message);

                if (savedSelection is not null)
                {
                    var resetText = UiText.Get("Print_UsePrinterDefaults");
                    ok.Content = resetText;
                    AutomationProperties.SetName(ok, resetText);
                    ok.IsEnabled = true;
                }
                return;
            }

            optionsScroll.IsVisible = true;
            status.IsVisible = false;
            ok.IsEnabled = true;
            editors[0].Selector.Focus();
        };

        return await dialog.ShowDialog<PrinterOptionSelection?>(owner);
    }

    private static ComboBox CreatePrinterOptionSelector(PrinterOptionDefinition option, PrinterOptionSelection? savedSelection)
    {
        var defaultChoice = option.Choices.FirstOrDefault(choice => choice.Value == option.DefaultValue);
        var defaultLabel = defaultChoice is null
            ? UiText.Get("Print_PrinterOptionDefault")
            : UiText.Format("Print_PrinterOptionDefaultWithValue", defaultChoice.Label);
        int selectedIndex = 0;
        if (savedSelection?.Values.TryGetValue(option.Key, out var savedValue) == true)
        {
            while (selectedIndex < option.Choices.Count && option.Choices[selectedIndex].Value != savedValue)
                selectedIndex++;
            selectedIndex = selectedIndex == option.Choices.Count ? 0 : selectedIndex + 1;
        }

        var selector = new ComboBox
        {
            ItemsSource = new[] { defaultLabel }.Concat(option.Choices.Select(choice => choice.Label)).ToArray(),
            SelectedIndex = selectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(selector, PrintDialogChromeStyle);
        AutomationProperties.SetName(selector, option.Label);
        AutomationProperties.SetAutomationId(selector, $"PrinterPropertiesOption-{option.Key}");
        return selector;
    }
}
