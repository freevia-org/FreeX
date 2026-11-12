using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Stable dimensions belong to the live renderer; external validation hosts exercise these same surfaces.
    internal const int NameBoxDropdownWidth = 208;
    internal const int NameBoxDropdownHeight = 136;
    private const int ForecastSheetDialogWidth = 320 - WpfNoResizeFrameWidth;
    private const int ForecastSheetDialogHeight = 150 - WpfNoResizeClientFrameHeight;
    private const int ForecastSheetErrorBandHeight = 40;
    private const int SubtotalDialogWidth = 380 - WpfNoResizeFrameWidth;
    // WPF SubtotalDialog is a 380x390 *outer* NoResize window (see the frame note below).
    private const int SubtotalDialogHeight = 390 - WpfNoResizeClientFrameHeight;
    // WPF Window.Width/Height are OUTER sizes (caption and frame included); Avalonia's are client
    // sizes. A WPF dialog size copied verbatim is therefore too large here by the Windows frame.
    // Subtract the allowance so the client area matches what the WPF dialog actually lays its
    // content out in.
    //
    // The WPF parity capture shows each dialog as a real window and renders its content on a canvas
    // of the outer size, so the uncovered right/bottom band is the real frame. ResizeMode.NoResize
    // dialogs lose the same 16 x 39 as resizable ones on Windows 10/11: PivotValueFieldSettings
    // (430x430) and PivotFieldFilter (380x470), both NoResize, fill exactly 414x391 and 364x431;
    // PageSetup, GoTo, GoToSpecial, GoalSeek, Subtotal, AddWatch and AllowEditRanges show the same
    // 15 x 37 excess over their content margin as the resizable FindReplace and FormatCells. The thin
    // 6 x 29 frame of a classic fixed dialog never appears, so every NoResize dialog uses
    // WpfNoResizeFrameWidth / WpfNoResizeClientFrameHeight (the old 29px height allowance is gone).
    // Exceptions: references captured with a pinned CLIENT size (TryGetFixedDialogClientCaptureSize in
    // the WPF capture: About, Scenario Manager, Zoom) already are client sizes, and a dialog whose
    // content would not fit the Windows client height keeps its height (or follows its content).
    internal const int WpfResizableFrameWidth = 16;
    internal const int WpfResizableFrameHeight = 39;
    private const int WpfNoResizeFrameWidth = WpfResizableFrameWidth;
    private const int WpfNoResizeClientFrameHeight = WpfResizableFrameHeight;
    private const int TextToColumnsDialogWidth = (int)TextToColumnsDialogMetrics.WindowWidth - WpfResizableFrameWidth;
    private const int TextToColumnsDialogHeight = (int)TextToColumnsDialogMetrics.WindowHeight - WpfResizableFrameHeight;

    private static TextBlock CreateExportOptionsSectionLabel(string resourceKey, double topMargin = 0) =>
        new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            FontWeight = FontWeight.SemiBold,
            // WPF: the first section caption sits 6px above its options, the later ones 4px.
            Margin = new Thickness(0, topMargin, 0, topMargin == 0 ? 6 : 4),
        };

    private static StackPanel CreateExportOptionsLabeledControl(
        string resourceKey,
        Control control,
        double leftIndent = 0) =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(leftIndent, 2, 0, 0),
            Spacing = 6,
            Children =
            {
                new Label
                {
                    Content = StripDisplayMnemonic(UiText.Get(resourceKey)),
                    Target = control,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                },
                control,
            },
        };

    private static bool IsFocusInside(Window dialog, IInputElement? element) =>
        element is Visual visual && ReferenceEquals(TopLevel.GetTopLevel(visual), dialog);
}
