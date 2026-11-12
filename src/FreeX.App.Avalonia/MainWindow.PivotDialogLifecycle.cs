using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── WPF PivotDialogLayout equivalents ─────────────────────────────────────
    // The WPF pivot dialog family shares PivotDialogLayout: a 16px root margin, labels with zero
    // padding and a 0,3,0,4 margin, a right-aligned OK/Cancel row 12px below the content with 76px
    // minimum button widths and an 8px gap, and reference editors whose 28px picker sits LEFT of the
    // reference box with a 6px gap (DialogReferencePicker.CreateEditor docks the picker first).
    private const double PivotDialogButtonMinWidth = 76;

    private static TextBlock CreatePivotDialogLabel(string text, Thickness? margin = null) => new()
    {
        Text = StripDisplayMnemonic(text),
        FontSize = 12,
        FontFamily = FormulaBarFontFamily,
        Margin = margin ?? new Thickness(0, 3, 0, 4),
    };

    private static StackPanel CreatePivotDialogButtonRow(Button accept, Button cancel, Thickness? margin = null)
    {
        accept.Margin = new Thickness(0, 0, 8, 0);
        cancel.Margin = new Thickness(0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            Margin = margin ?? new Thickness(0, 12, 0, 0),
            Children = { accept, cancel },
        };
    }

    // WPF PivotDialogLayout.CreateGroupBox + CreateGroupPanel: a group box 12px above the next block
    // whose content panel is inset 10,8,10,10.
    private static GroupBox CreatePivotDialogGroupBox(string header, Control content, Thickness? margin = null)
    {
        var groupBox = new GroupBox
        {
            Header = StripDisplayMnemonic(header),
            Content = content,
            Margin = margin ?? new Thickness(0, 0, 0, 12),
            Padding = new Thickness(10, 8, 10, 10),
        };
        AvaloniaCompactDialogChrome.ApplyGroupBox(groupBox, PivotDialogChromeStyle);
        return groupBox;
    }

    // WPF PivotDialogLayout.AddLabeledControl: the label above its control, the control 8px above the
    // next row unless the caller overrides the margin.
    private static void AddPivotDialogLabeledControl(Panel stack, string label, Control control, Thickness? margin = null)
    {
        stack.Children.Add(CreatePivotDialogLabel(label));
        control.Margin = margin ?? new Thickness(0, 0, 0, 8);
        stack.Children.Add(control);
    }

    private static Grid CreatePivotDialogReferenceEditor(Button picker, TextBox referenceBox, Thickness margin = default)
    {
        ApplyPivotButtonChrome(picker, 28);
        picker.Width = 28;
        picker.Padding = new Thickness(0);
        picker.Margin = new Thickness(0, 0, 6, 0);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = margin,
        };
        Grid.SetColumn(picker, 0);
        Grid.SetColumn(referenceBox, 1);
        row.Children.Add(picker);
        row.Children.Add(referenceBox);
        return row;
    }

    /// <summary>
    /// Applies the WPF keyboard contract shared by the Pivot dialog family. Avalonia's default traversal can
    /// skip the initial control or leave a nested tab/panel scope when the dialog is built dynamically, so the
    /// production dialog owns a bounded, visible focus graph for both Tab directions.
    /// </summary>
    private static void ConfigurePivotDialogLifecycle(
        Window dialog,
        Control initialFocus,
        bool selectAllText = false)
    {
        var root = dialog.Content as Control ?? dialog;
        ConfigureDialogTabCycle(dialog, root);

        EventHandler? layoutUpdated = null;
        var focusEstablished = false;

        void FocusInitialControl()
        {
            if (focusEstablished ||
                !dialog.IsVisible ||
                !initialFocus.IsVisible ||
                !initialFocus.IsEffectivelyEnabled)
            {
                return;
            }

            if (!ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), initialFocus) &&
                !FocusDialogControl(initialFocus))
            {
                return;
            }

            if (selectAllText && initialFocus is TextBox textBox && textBox.IsFocused)
                textBox.SelectAll();

            focusEstablished = true;
            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        }

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Tab ||
                    (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
                {
                    return;
                }

                // A user Tab is authoritative navigation. Do not let a queued activation/layout retry
                // reassert the opener's initial control after the user has already moved focus.
                focusEstablished = true;
                if (layoutUpdated is not null)
                    dialog.LayoutUpdated -= layoutUpdated;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        layoutUpdated = (_, _) => FocusInitialControl();
        dialog.Opened += (_, _) =>
        {
            FocusInitialControl();
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Background);
        };
        dialog.Activated += (_, _) =>
        {
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
        };
        dialog.LayoutUpdated += layoutUpdated;
        dialog.Closed += (_, _) =>
        {
            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        };
    }
}
