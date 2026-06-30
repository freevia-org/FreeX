using System;
using System.Windows;
using System.Windows.Media;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Base class for code-built app dialogs. Gives every dialog one clean, consistent Word-style surface:
/// a white background, Segoe UI typography, crisp text rendering, owner-centred placement, and the shared
/// modern flat control theme (<c>DialogResources.xaml</c>) merged into the window's OWN resource scope —
/// so the styling applies to the dialog's plain controls without touching the main window / ribbon.
///
/// A dialog opts in simply by deriving from <see cref="DialogWindow"/> instead of <see cref="Window"/>.
/// All defaults are plain property sets, so a dialog's constructor can still override any of them (size,
/// resize mode, a non-modal placement, etc.).
/// </summary>
public abstract class DialogWindow : Window
{
    protected DialogWindow()
    {
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Free.Shared.Shell.Wpf;component/DialogResources.xaml", UriKind.Relative)
        });
    }
}
