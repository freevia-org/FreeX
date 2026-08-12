using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DataTools;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private sealed record SubtotalDialogCaptureState(
        GridRange SelectedRange,
        IReadOnlyList<SubtotalDialogColumnChoice> Columns,
        uint GroupColumnOffset,
        IReadOnlyList<uint> SubtotalColumnOffsets,
        string FunctionText,
        bool ReplaceCurrentSubtotals,
        bool PageBreakBetweenGroups,
        bool SummaryBelowData);

    private sealed record PrintPreviewCaptureTextRun(
        string Text,
        double Left,
        double Top,
        double FontSize,
        bool Bold,
        PresentationRgb Color);

    private sealed record PrintPreviewCapturePage(
        string Title,
        string Subtitle,
        int PageNumber,
        IReadOnlyList<PrintPreviewCaptureTextRun> TextRuns)
    {
        public const double Width = 696;
        public const double Height = 768;
    }

    // Stable renderer dimensions are shared by the live dialogs and the external capture host.
    internal const int NameBoxDropdownParityCaptureWidth = 208;
    internal const int NameBoxDropdownParityCaptureHeight = 136;
    private const int ForecastSheetParityDialogWidth = 320;
    private const int ForecastSheetParityDialogHeight = 150;
    private const int SubtotalParityDialogWidth = 380;
    private const int SubtotalParityDialogHeight = 390;
    private const int TextToColumnsParityDialogWidth = (int)TextToColumnsDialogMetrics.WindowWidth;
    private const int TextToColumnsParityDialogHeight = (int)TextToColumnsDialogMetrics.WindowHeight;

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
