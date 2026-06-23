using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

using AvaloniaControlShapesLine = Avalonia.Controls.Shapes.Line;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Print Preview window for the Avalonia shell (realignment R7), built on the portable page-content
/// model. For the active sheet it resolves the print range, paginates via
/// <see cref="PagePaginationPlanner"/>, and — one page at a time — builds a
/// <see cref="PageContentLayout"/> through <see cref="PageContentRenderModelBuilder"/> (measuring text
/// with the shared <see cref="AvaloniaTextMeasurer"/>) and paints it onto a Canvas of Avalonia
/// rectangles, lines, and text blocks positioned from the layout's device-independent points.
///
/// The window shows Prev / Next buttons with a "Page X of N" caption (navigation math lives in the
/// pure <see cref="PrintPreviewPageNavigator"/>), and a <see cref="Viewbox"/> zooms each rendered page
/// to fit the available area. The flattening of a page layout into ordered paint primitives lives in
/// the unit-tested <see cref="PrintPreviewInstructionBuilder"/>; this file only turns each primitive
/// into an Avalonia control.
///
/// Deferred: the OS print dialog (no system print is invoked yet — this is a preview/export surface),
/// and drawing objects / charts on the page (the page-content model omits them by design).
/// </summary>
public sealed partial class MainWindow
{
    private static readonly ITextMeasurer PrintPreviewTextMeasurer = new AvaloniaTextMeasurer();
    private static readonly IBrush PrintPreviewSurfaceBackground = Brush(82, 86, 92);

    private async Task ShowPrintPreviewDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        if (!PrintPreviewPaginationContext.TryCreate(_session.Workbook, sheet, PrintPreviewTextMeasurer, out var context))
        {
            ShowEditIssue(UiText.Get("ShellLoc_NothingToPreview"));
            return;
        }

        await ShowPrintPreviewWindowCoreAsync(context);
    }

    private async Task ShowPrintPreviewWindowCoreAsync(PrintPreviewPaginationContext context)
    {
        var navigator = PrintPreviewPageNavigator.Create(context.PageCount);

        var dialog = new Window
        {
            Title = UiText.Format(
                PrintPreviewDialogPlanner.TitleFormatResourceKey,
                PrintPreviewDialogPlanner.NormalizeWorkbookName(_session.DisplayName)),
            Width = PrintPreviewDialogPlanner.WindowWidth,
            Height = PrintPreviewDialogPlanner.WindowHeight,
            MinWidth = PrintPreviewDialogPlanner.MinWindowWidth,
            MinHeight = PrintPreviewDialogPlanner.MinWindowHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PrintPreviewDialogPlanner.DialogAutomationId);

        var pageHost = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = new Thickness(16),
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(pageHost, PrintPreviewDialogPlanner.PageHostAutomationId);

        var pageLabel = new TextBlock
        {
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            MinWidth = 120,
            TextAlignment = TextAlignment.Center,
        };
        AutomationProperties.SetAutomationId(pageLabel, PrintPreviewDialogPlanner.PageLabelAutomationId);

        var prevButton = new Button { Content = UiText.Get("ShellLoc_PrintPreviewPrev"), MinWidth = 84, Padding = new Thickness(10, 4) };
        var nextButton = new Button { Content = UiText.Get("ShellLoc_PrintPreviewNext"), MinWidth = 84, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(prevButton, PrintPreviewDialogPlanner.PreviousButtonAutomationId);
        AutomationProperties.SetAutomationId(nextButton, PrintPreviewDialogPlanner.NextButtonAutomationId);

        var exportButton = new Button { Content = UiText.Get("ShellLoc_PrintPreviewExportPdf"), MinWidth = 120, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(exportButton, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId);
        exportButton.IsEnabled = StorageProvider.CanSave;

        var closeButton = new Button { Content = UiText.Get("Common_Close"), MinWidth = 84, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(closeButton, PrintPreviewDialogPlanner.CloseButtonAutomationId);

        void Render()
        {
            pageHost.Child = BuildPreviewPageView(context, navigator.CurrentIndex);
            pageLabel.Text = navigator.Caption;
            prevButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
        }

        prevButton.Click += (_, _) =>
        {
            navigator = navigator.Previous();
            Render();
        };
        nextButton.Click += (_, _) =>
        {
            navigator = navigator.Next();
            Render();
        };
        exportButton.Click += async (_, _) =>
        {
            dialog.Close();
            await ExportActiveSheetPdfAsync();
        };
        closeButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    dialog.Close();
                    e.Handled = true;
                    break;
                case Key.Left or Key.PageUp:
                    navigator = navigator.Previous();
                    Render();
                    e.Handled = true;
                    break;
                case Key.Right or Key.PageDown:
                    navigator = navigator.Next();
                    Render();
                    e.Handled = true;
                    break;
            }
        };

        var navBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { prevButton, pageLabel, nextButton },
        };

        var actionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { exportButton, closeButton },
        };

        var toolbar = new Grid
        {
            Margin = new Thickness(12, 8),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(navBar, 0);
        Grid.SetColumn(actionBar, 2);
        toolbar.Children.Add(navBar);
        toolbar.Children.Add(actionBar);

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Bottom);
        layout.Children.Add(toolbar);
        layout.Children.Add(pageHost);

        dialog.Content = layout;
        dialog.Opened += (_, _) =>
        {
            Render();
            nextButton.Focus();
        };

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Builds the zoom-to-fit view for one preview page: a <see cref="Viewbox"/> wrapping a Canvas the
    /// size of the page rectangle, onto which the page's flattened paint primitives are rendered.
    /// </summary>
    private static Control BuildPreviewPageView(PrintPreviewPaginationContext context, int pageIndex)
    {
        var layout = context.BuildPage(pageIndex);
        if (layout is null)
        {
            return new TextBlock
            {
                Text = UiText.Get("ShellLoc_PageCouldNotRender"),
                Foreground = Brushes.White,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
        }

        var painting = PrintPreviewInstructionBuilder.Build(layout);
        var canvas = new Canvas
        {
            Width = painting.PageBounds.Width,
            Height = painting.PageBounds.Height,
            Background = Brushes.White,
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(canvas, PrintPreviewDialogPlanner.PageCanvasAutomationId);

        RenderPreviewInstructions(canvas, painting.Instructions);

        var pageBorder = new Border
        {
            Width = painting.PageBounds.Width,
            Height = painting.PageBounds.Height,
            Background = Brushes.White,
            BorderBrush = Brush(160, 160, 160),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(64, 0, 0, 0),
            }),
            Child = canvas,
        };

        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Child = pageBorder,
        };
    }

    private static void RenderPreviewInstructions(
        Canvas canvas,
        IReadOnlyList<PrintPreviewPaintInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            switch (instruction.Kind)
            {
                case PrintPreviewPaintKind.Rectangle:
                    AddPreviewRectangle(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Line:
                    AddPreviewLine(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Text:
                    AddPreviewText(canvas, instruction);
                    break;
            }
        }
    }

    private static void AddPreviewRectangle(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        var rect = new AvaloniaRectangle
        {
            Width = Math.Max(0, instruction.Width),
            Height = Math.Max(0, instruction.Height),
        };
        if (instruction.Fill is { } fill)
            rect.Fill = PreviewBrush(fill);
        if (instruction.Stroke is { } stroke && instruction.StrokeThickness > 0)
        {
            rect.Stroke = PreviewBrush(stroke);
            rect.StrokeThickness = instruction.StrokeThickness;
        }

        Canvas.SetLeft(rect, instruction.Left);
        Canvas.SetTop(rect, instruction.Top);
        canvas.Children.Add(rect);
    }

    private static void AddPreviewLine(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (instruction.Stroke is not { } stroke)
            return;

        var line = new AvaloniaControlShapesLine
        {
            StartPoint = new Point(instruction.X1, instruction.Y1),
            EndPoint = new Point(instruction.X2, instruction.Y2),
            Stroke = PreviewBrush(stroke),
            StrokeThickness = instruction.StrokeThickness,
        };
        canvas.Children.Add(line);
    }

    private static void AddPreviewText(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (string.IsNullOrEmpty(instruction.Text))
            return;

        var font = instruction.Font;
        var text = new TextBlock
        {
            Text = instruction.Text,
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font.FontFamily)
                ? AvaloniaTextMeasurer.DefaultFontFamily
                : font.FontFamily),
            FontSize = font.FontSize > 0 ? font.FontSize : 9,
            FontWeight = font.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = font.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = PreviewBrush(font.Color),
            Width = Math.Max(0, instruction.Width),
            TextAlignment = ToTextAlignment(instruction.Alignment),
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
        };

        Canvas.SetLeft(text, instruction.Left);
        Canvas.SetTop(text, instruction.Top);
        canvas.Children.Add(text);
    }

    private static TextAlignment ToTextAlignment(PageTextAlignment alignment) =>
        alignment switch
        {
            PageTextAlignment.Center => TextAlignment.Center,
            PageTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

    private static IBrush PreviewBrush(PresentationRgb color) =>
        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
}
