using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreeX.App.Host;

public static class ComboBoxDropDownWheelBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ComboBoxDropDownWheelBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty DropDownScrollViewerProperty =
        DependencyProperty.RegisterAttached(
            "DropDownScrollViewer",
            typeof(ScrollViewer),
            typeof(ComboBoxDropDownWheelBehavior));

    private static readonly List<WeakReference<ComboBox>> OpenComboBoxes = [];
    private static bool _isInputHooked;

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ComboBox comboBox)
            return;

        comboBox.DropDownOpened -= ComboBox_DropDownOpened;
        comboBox.DropDownClosed -= ComboBox_DropDownClosed;
        comboBox.Unloaded -= ComboBox_Unloaded;
        comboBox.PreviewMouseWheel -= ComboBox_PreviewMouseWheel;

        if (e.NewValue is true)
        {
            comboBox.DropDownOpened += ComboBox_DropDownOpened;
            comboBox.DropDownClosed += ComboBox_DropDownClosed;
            comboBox.Unloaded += ComboBox_Unloaded;
            comboBox.PreviewMouseWheel += ComboBox_PreviewMouseWheel;
        }
        else
        {
            RemoveOpenComboBox(comboBox);
            DetachDropDownScrollViewer(comboBox);
            UnhookInputManagerIfIdle();
        }
    }

    private static void ComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        AddOpenComboBox(comboBox);
        HookInputManager();
        WireDropDownScrollViewer(comboBox);
        comboBox.Dispatcher.BeginInvoke(
            () => WireDropDownScrollViewer(comboBox),
            DispatcherPriority.Loaded);
    }

    private static void ComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        RemoveOpenComboBox(comboBox);
        DetachDropDownScrollViewer(comboBox);
        UnhookInputManagerIfIdle();
    }

    private static void ComboBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        RemoveOpenComboBox(comboBox);
        DetachDropDownScrollViewer(comboBox);
        UnhookInputManagerIfIdle();
    }

    private static void ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true } comboBox)
            return;

        if (FindDropDownScrollViewer(comboBox) is { } scrollViewer)
            ScrollDropDown(scrollViewer, e);
    }

    private static void DropDownScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ScrollDropDown(scrollViewer, e);
    }

    private static void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not MouseWheelEventArgs wheelArgs)
            return;

        foreach (var comboBox in GetOpenComboBoxes())
        {
            if (!comboBox.IsDropDownOpen)
                continue;

            if (FindDropDownScrollViewer(comboBox) is { } scrollViewer)
            {
                ScrollDropDown(scrollViewer, wheelArgs);
                return;
            }
        }
    }

    private static void WireDropDownScrollViewer(ComboBox comboBox)
    {
        DetachDropDownScrollViewer(comboBox);
        if (FindDropDownScrollViewer(comboBox) is not { } scrollViewer)
            return;

        scrollViewer.PreviewMouseWheel -= DropDownScrollViewer_PreviewMouseWheel;
        scrollViewer.PreviewMouseWheel += DropDownScrollViewer_PreviewMouseWheel;
        comboBox.SetValue(DropDownScrollViewerProperty, scrollViewer);
    }

    private static void DetachDropDownScrollViewer(ComboBox comboBox)
    {
        if (comboBox.GetValue(DropDownScrollViewerProperty) is not ScrollViewer scrollViewer)
            return;

        scrollViewer.PreviewMouseWheel -= DropDownScrollViewer_PreviewMouseWheel;
        comboBox.ClearValue(DropDownScrollViewerProperty);
    }

    private static ScrollViewer? FindDropDownScrollViewer(ComboBox comboBox)
    {
        if (comboBox.Template.FindName("PART_Popup", comboBox) is Popup { Child: DependencyObject popupChild })
            return FindVisualDescendant<ScrollViewer>(popupChild);

        return FindVisualDescendant<ScrollViewer>(comboBox);
    }

    private static void ScrollDropDown(ScrollViewer scrollViewer, MouseWheelEventArgs e)
    {
        var lineCount = Math.Max(1, Math.Abs(e.Delta) / 120);
        for (var i = 0; i < lineCount; i++)
        {
            if (e.Delta < 0)
                scrollViewer.LineDown();
            else
                scrollViewer.LineUp();
        }

        e.Handled = true;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            if (FindVisualDescendant<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }

    private static void AddOpenComboBox(ComboBox comboBox)
    {
        RemoveOpenComboBox(comboBox);
        OpenComboBoxes.Add(new WeakReference<ComboBox>(comboBox));
    }

    private static void RemoveOpenComboBox(ComboBox comboBox)
    {
        OpenComboBoxes.RemoveAll(reference =>
            !reference.TryGetTarget(out var target) ||
            ReferenceEquals(target, comboBox));
    }

    private static IReadOnlyList<ComboBox> GetOpenComboBoxes()
    {
        OpenComboBoxes.RemoveAll(reference => !reference.TryGetTarget(out _));
        return OpenComboBoxes
            .Select(reference => reference.TryGetTarget(out var comboBox) ? comboBox : null)
            .OfType<ComboBox>()
            .Reverse()
            .ToArray();
    }

    private static void HookInputManager()
    {
        if (_isInputHooked)
            return;

        InputManager.Current.PreProcessInput += InputManager_PreProcessInput;
        _isInputHooked = true;
    }

    private static void UnhookInputManagerIfIdle()
    {
        OpenComboBoxes.RemoveAll(reference => !reference.TryGetTarget(out var comboBox) || !comboBox.IsDropDownOpen);
        if (OpenComboBoxes.Count > 0 || !_isInputHooked)
            return;

        InputManager.Current.PreProcessInput -= InputManager_PreProcessInput;
        _isInputHooked = false;
    }
}
