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
    private const int ForecastSheetDialogWidth = 320;
    private const int ForecastSheetDialogHeight = 150;
    private const int SubtotalDialogWidth = 380;
    private const int SubtotalDialogHeight = 390;
    private const int TextToColumnsDialogWidth = (int)TextToColumnsDialogMetrics.WindowWidth;
    private const int TextToColumnsDialogHeight = (int)TextToColumnsDialogMetrics.WindowHeight;

    private static TextBlock CreateExportOptionsSectionLabel(string resourceKey, double topMargin = 0) =>
        new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 4),
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
