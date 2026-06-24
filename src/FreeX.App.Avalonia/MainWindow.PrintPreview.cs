using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            Padding = new Thickness(0),
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(pageHost, PrintPreviewDialogPlanner.PageHostAutomationId);

        var pageNumberBox = new TextBox
        {
            Text = "1",
            Width = 44,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };

        var pageStatusText = new TextBlock
        {
            Text = PrintPreviewNavigationState.Create(1, context.PageCount).StatusText,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetAutomationId(pageStatusText, PrintPreviewDialogPlanner.PageLabelAutomationId);

        var firstButton = CreatePreviewToolbarButton("|<");
        var prevButton = CreatePreviewToolbarButton("<");
        var nextButton = CreatePreviewToolbarButton(">");
        var lastButton = CreatePreviewToolbarButton(">|");
        AutomationProperties.SetAutomationId(prevButton, PrintPreviewDialogPlanner.PreviousButtonAutomationId);
        AutomationProperties.SetAutomationId(nextButton, PrintPreviewDialogPlanner.NextButtonAutomationId);

        var exportButton = new Button
        {
            Content = PrintPreviewText("PrintPreview_PrintButton", "Print..."),
            MinWidth = 68,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(10, 1),
            Background = Brushes.White,
            BorderBrush = Brush(0, 120, 215),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(exportButton, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId);
        exportButton.IsEnabled = StorageProvider.CanSave;

        var closeButton = new Button
        {
            Content = PrintPreviewText("PrintPreview_CloseButton", "Close"),
            MinWidth = 68,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(10, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            IsCancel = true,
        };
        AutomationProperties.SetAutomationId(closeButton, PrintPreviewDialogPlanner.CloseButtonAutomationId);

        void Render()
        {
            pageHost.Child = BuildPreviewDocumentViewerSurface(context, navigator.CurrentIndex);
            pageNumberBox.Text = (navigator.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture);
            pageStatusText.Text = PrintPreviewNavigationState.Create(navigator.CurrentIndex + 1, context.PageCount).StatusText;
            firstButton.IsEnabled = navigator.CanGoPrevious;
            prevButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
            lastButton.IsEnabled = navigator.CanGoNext;
        }

        firstButton.Click += (_, _) =>
        {
            navigator = navigator.JumpTo(0);
            Render();
        };
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
        lastButton.Click += (_, _) =>
        {
            navigator = navigator.JumpTo(context.PageCount - 1);
            Render();
        };
        pageNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            if (PrintPreviewDialogPlanner.TryParsePageNumber(pageNumberBox.Text, context.PageCount, out var pageNumber))
            {
                navigator = navigator.JumpTo(pageNumber - 1);
                Render();
            }

            e.Handled = true;
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

        var documentToolbar = CreatePreviewDocumentToolbar(
            firstButton,
            prevButton,
            nextButton,
            lastButton,
            pageNumberBox,
            pageStatusText);
        var previewPane = CreatePrintPreviewPane(documentToolbar, pageHost);
        var settingsRail = CreatePrintPreviewSettingsRail(context.PageCount);
        var topToolbar = CreatePrintPreviewTopToolbar(context.PageCount, exportButton, closeButton);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("220,*"),
        };
        Grid.SetRow(topToolbar, 0);
        Grid.SetColumnSpan(topToolbar, 2);
        Grid.SetRow(settingsRail, 1);
        Grid.SetColumn(settingsRail, 0);
        Grid.SetRow(previewPane, 1);
        Grid.SetColumn(previewPane, 1);
        layout.Children.Add(topToolbar);
        layout.Children.Add(settingsRail);
        layout.Children.Add(previewPane);

        dialog.Content = layout;
        dialog.Opened += (_, _) =>
        {
            Render();
            exportButton.Focus();
        };

        await dialog.ShowDialog(this);
    }

    private static Border CreatePrintPreviewTopToolbar(int totalPages, Button printButton, Button closeButton)
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(4),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                printButton,
                new TextBlock { Text = PrintPreviewText("PrintPreview_PrinterLabel", "Printer:"), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                CreatePreviewComboBox(190, "HP30138B4D655D(HP Color Laser MFP 178 179)"),
                new TextBlock { Text = PrintPreviewText("PrintPreview_CopiesLabel", "Copies:"), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                new TextBox
                {
                    Text = "1",
                    Width = 44,
                    Height = 24,
                    MinHeight = 24,
                    MaxHeight = 24,
                    Padding = new Thickness(4, 1),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    BorderBrush = Brush(130, 130, 130),
                    BorderThickness = new Thickness(1),
                    VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
                },
                new CheckBox
                {
                    Content = PrintPreviewText("PrintPreview_CollatedLabel", "Collated"),
                    IsChecked = true,
                    MinHeight = 20,
                    MaxHeight = 20,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
                new TextBlock { Text = PrintPreviewText("PrintPreview_SidesLabel", "Sides:"), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                CreatePreviewComboBox(178, PrintPreviewText("PrintPreview_SidesOneSided", "Print One Sided")),
                new TextBlock
                {
                    Text = PrintPreviewToolbarStatePlanner.CreateStatusText(
                        "HP30138B4D655D(HP Color Laser MFP 178 179)",
                        1,
                        totalPages),
                    MaxWidth = 280,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
                CreatePreviewComboBox(96, PrintPreviewText("PrintPreview_AllPagesLabel", "All pages")),
                closeButton,
            },
        };

        return new Border
        {
            Background = Brush(235, 244, 253),
            BorderBrush = Brush(190, 204, 220),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar,
        };
    }

    private static ScrollViewer CreatePrintPreviewSettingsRail(int totalPages)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(10),
            Children =
            {
                CreateSettingsSection(PrintPreviewText("PrintPreview_CopiesSectionLabel", "Copies:")),
                new TextBox
                {
                    Text = "1",
                    Width = 60,
                    Height = 24,
                    MinHeight = 24,
                    MaxHeight = 24,
                    Padding = new Thickness(4, 1),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    BorderBrush = Brush(130, 130, 130),
                    BorderThickness = new Thickness(1),
                    VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                },
                CreateSettingsSection(PrintPreviewText("PrintPreview_PrinterSectionLabel", "Printer:")),
                CreatePreviewComboBox(183, "HP30138B4D655D(HP Color Laser MFP 178 179)"),
                new Button
                {
                    Content = PrintPreviewText("PrintPreview_PrinterPropertiesButton", "Printer Properties"),
                    Height = 24,
                    MinHeight = 24,
                    MaxHeight = 24,
                    Padding = new Thickness(6, 1),
                    Background = Brushes.White,
                    BorderBrush = Brush(112, 112, 112),
                    BorderThickness = new Thickness(1),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                },
                CreateSettingsSection(PrintPreviewText("PrintPreview_PrintWhatLabel", "Print What:")),
                CreatePreviewComboBox(183, PrintPreviewText("PrintPreview_PrintWhatActiveSheets", "Print Active Sheets")),
                CreateSettingsSection(PrintPreviewText("PrintPreview_PagesLabel", "Pages:")),
                CreatePageRangeRow(totalPages),
                CreateSettingsSection(PrintPreviewText("PrintPreview_SidesSectionLabel", "Print Sides:")),
                CreatePreviewComboBox(183, PrintPreviewText("PrintPreview_SidesOneSided", "Print One Sided")),
                CreateSettingsSection(PrintPreviewText("PrintPreview_CollatedSectionLabel", "Collation:")),
                CreatePreviewComboBox(183, PrintPreviewText("PrintPreview_CollatedOption", "Collated")),
                CreateSettingsSection(PrintPreviewText("PrintPreview_OrientationLabel", "Orientation:")),
                CreatePreviewComboBox(183, "Portrait"),
                CreateSettingsSection(PrintPreviewText("PageSetup_PaperSize", "Paper size:")),
                CreatePreviewComboBox(183, "A4"),
                CreateSettingsSection(PrintPreviewText("PrintPreview_MarginsButton", "Margins")),
                CreatePreviewComboBox(183, "Narrow"),
                CreateSettingsSection(PrintPreviewText("PrintPreview_ScalingLabel", "Scaling:")),
                CreatePreviewComboBox(183, PrintPreviewText("PrintPreview_ScaleNoScaling", "No Scaling")),
                new CheckBox { Content = PrintPreviewText("PrintPreview_IgnorePrintArea", "Ignore print area"), IsChecked = false, MinHeight = 20, MaxHeight = 20, FontSize = 12, FontFamily = FormulaBarFontFamily },
                CreateSettingsSection(PrintPreviewText("PrintPreview_PrintOptionsSection", "Print Options")),
                new CheckBox { Content = PrintPreviewText("PageSetup_PrintGridlines", "Print gridlines"), IsChecked = false, MinHeight = 20, MaxHeight = 20, FontSize = 12, FontFamily = FormulaBarFontFamily },
            },
        };

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brush(245, 245, 245),
            Content = panel,
        };
    }

    private static Grid CreatePageRangeRow(int totalPages)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto"),
        };
        var fromBox = new TextBox
        {
            Text = "1",
            Width = 44,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        var toLabel = new TextBlock { Text = PrintPreviewText("PrintPreview_PageRangeToText", "To:"), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(6, 0), VerticalAlignment = AvaloniaVerticalAlignment.Center };
        var toBox = new TextBox
        {
            Text = totalPages.ToString(CultureInfo.InvariantCulture),
            Width = 44,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toLabel, 1);
        Grid.SetColumn(toBox, 2);
        row.Children.Add(fromBox);
        row.Children.Add(toLabel);
        row.Children.Add(toBox);
        return row;
    }

    private static TextBlock CreateSettingsSection(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 4, 0, -4),
        };

    private static Border CreatePrintPreviewPane(Control documentToolbar, Border pageHost)
    {
        var findBar = new Grid
        {
            Background = Brush(235, 244, 253),
            ColumnDefinitions = new ColumnDefinitions("240,Auto,Auto,*"),
            MinHeight = 26,
        };
        var findBox = new TextBox
        {
            PlaceholderText = "Type text to find...",
            Margin = new Thickness(4, 2),
            Height = 22,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontStyle = FontStyle.Italic,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
        };
        var previous = CreatePreviewToolbarButton("<");
        var next = CreatePreviewToolbarButton(">");
        Grid.SetColumn(findBox, 0);
        Grid.SetColumn(previous, 1);
        Grid.SetColumn(next, 2);
        findBar.Children.Add(findBox);
        findBar.Children.Add(previous);
        findBar.Children.Add(next);

        var pane = new DockPanel();
        DockPanel.SetDock(documentToolbar, Dock.Top);
        DockPanel.SetDock(findBar, Dock.Bottom);
        pane.Children.Add(documentToolbar);
        pane.Children.Add(findBar);
        pane.Children.Add(pageHost);

        return new Border
        {
            Background = Brushes.White,
            Child = pane,
        };
    }

    private static Border CreatePreviewDocumentToolbar(
        Button firstButton,
        Button prevButton,
        Button nextButton,
        Button lastButton,
        TextBox pageNumberBox,
        TextBlock pageStatusText)
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(6, 4),
            Children =
            {
                firstButton,
                prevButton,
                nextButton,
                lastButton,
                CreatePreviewToolbarSeparator(),
                new TextBlock { Text = PrintPreviewText("PrintPreview_PageLabel", "Page:"), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                pageNumberBox,
                pageStatusText,
                CreatePreviewToolbarSeparator(),
                new TextBlock { Text = PrintPreviewText("PrintPreview_ZoomLabel", "Zoom:"), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                CreatePreviewComboBox(82, "100%"),
                CreatePreviewToolbarSeparator(),
                CreatePreviewToolbarButton(PrintPreviewText("PrintPreview_MarginsButton", "Margins")),
                CreatePreviewToolbarButton(PrintPreviewText("PrintPreview_PageSetupButton", "Page Setup")),
            },
        };

        return new Border
        {
            Background = Brush(235, 244, 253),
            BorderBrush = Brush(190, 204, 220),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar,
        };
    }

    private static TextBlock CreatePreviewToolbarSeparator() =>
        new()
        {
            Text = "|",
            Foreground = Brush(170, 180, 190),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static Button CreatePreviewToolbarButton(string text) =>
        new()
        {
            Content = text,
            MinWidth = 26,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(6, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static ComboBox CreatePreviewComboBox(double width, string selectedText) =>
        new()
        {
            Width = width,
            ItemsSource = new[] { selectedText },
            SelectedIndex = 0,
        };

    private static string PrintPreviewText(string key, string fallback)
    {
        var text = UiText.Get(key);
        return text.StartsWith("[[", StringComparison.Ordinal) && text.EndsWith("]]", StringComparison.Ordinal)
            ? fallback
            : text;
    }

    private static Control BuildPreviewDocumentViewerSurface(PrintPreviewPaginationContext context, int pageIndex)
    {
        var surface = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = new Thickness(84, 8, 84, 8),
            Child = BuildPreviewPageView(context, pageIndex),
        };

        return surface;
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
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Child = canvas,
        };

        return pageBorder;
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
