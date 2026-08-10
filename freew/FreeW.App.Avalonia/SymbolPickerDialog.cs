using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>Compact modal glyph picker matching FreeW's WPF Symbol dialog.</summary>
internal sealed class SymbolPickerDialog : FreeWDialogWindow
{
    private static readonly IBrush GlyphBackground = new ImmutableSolidColorBrush(Color.FromRgb(221, 221, 221));
    private static readonly IBrush GlyphBorder = new ImmutableSolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush GlyphHoverBackground = new ImmutableSolidColorBrush(Color.FromRgb(229, 243, 255));
    private static readonly IBrush GlyphHoverBorder = new ImmutableSolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush GlyphPressedBackground = new ImmutableSolidColorBrush(Color.FromRgb(204, 232, 255));
    private static readonly FuncControlTemplate<Button> GlyphButtonTemplate = new((button, _) =>
    {
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = button });
        presenter.Bind(Layoutable.HorizontalAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = button });
        presenter.Bind(Layoutable.VerticalAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = button });

        var border = new Border { CornerRadius = new CornerRadius(1), Child = presenter };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = button });
        border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = button });
        border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = button });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = button });
        return border;
    });

    private readonly List<Button> _glyphButtons = [];

    public string? Result { get; private set; }

    public SymbolPickerDialog()
    {
        Title = FreeWSymbolPickerDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, FreeWSymbolPickerDialogPlanner.DialogAutomationId);

        var panel = new StackPanel { Margin = new Thickness(FreeWSymbolPickerDialogPlanner.OuterMargin) };
        var grid = new UniformGrid { Columns = FreeWSymbolPickerDialogPlanner.Columns };
        foreach (var glyph in FreeWSymbolPickerDialogPlanner.Glyphs)
        {
            var semantic = FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph);
            var button = new Button
            {
                Content = glyph,
                MinWidth = FreeWSymbolPickerDialogPlanner.ButtonSize,
                Height = FreeWSymbolPickerDialogPlanner.ButtonSize,
                FontSize = FreeWSymbolPickerDialogPlanner.ButtonFontSize,
                Margin = new Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(button, semantic.CodePointLabel);
            AutomationProperties.SetName(button, semantic.AutomationName);
            AutomationProperties.SetAutomationId(button, semantic.AutomationId);
            button.Click += (_, _) => SelectGlyph(glyph, close: true);
            _glyphButtons.Add(button);
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);

        var cancel = new Button
        {
            Content = FreeWSymbolPickerDialogPlanner.CancelText,
            IsCancel = true,
            MinWidth = FreeWSymbolPickerDialogPlanner.FooterButtonMinWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(
                FreeWSymbolPickerDialogPlanner.ButtonMargin,
                FreeWSymbolPickerDialogPlanner.FooterTopMargin,
                FreeWSymbolPickerDialogPlanner.ButtonMargin,
                0),
            Padding = new Thickness(8, 2),
        };
        AutomationProperties.SetAutomationId(cancel, FreeWSymbolPickerDialogPlanner.CancelAutomationId);
        cancel.Click += (_, _) => Close();
        panel.Children.Add(cancel);

        Content = panel;
        Opened += (_, _) =>
        {
            ApplyGlyphButtonChrome(grid);
        };
    }

    internal IReadOnlyList<Button> GlyphButtonsForTest => _glyphButtons;

    internal string? SelectGlyphForTest(string glyph)
    {
        SelectGlyph(glyph, close: false);
        return Result;
    }

    private void SelectGlyph(string glyph, bool close)
    {
        if (!FreeWSymbolPickerDialogPlanner.Glyphs.Contains(glyph, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(glyph));

        Result = glyph;
        if (close)
            Close();
    }

    private static void ApplyGlyphButtonChrome(UniformGrid grid)
    {
        foreach (var button in grid.Children.OfType<Button>())
        {
            // Shared dialog chrome normalizes generic buttons after Opened; restore the WPF tile
            // metrics here while retaining that chrome for the dialog and footer.
            button.MinWidth = FreeWSymbolPickerDialogPlanner.ButtonSize;
            button.Height = FreeWSymbolPickerDialogPlanner.ButtonSize;
            button.MinHeight = FreeWSymbolPickerDialogPlanner.ButtonSize;
            button.MaxHeight = FreeWSymbolPickerDialogPlanner.ButtonSize;
            button.Padding = new Thickness(0);
            button.FontSize = FreeWSymbolPickerDialogPlanner.ButtonFontSize;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.Background = GlyphBackground;
            button.BorderBrush = GlyphBorder;
            button.BorderThickness = new Thickness(1);
            button.Template = GlyphButtonTemplate;
        }

        grid.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, GlyphHoverBackground),
                new Setter(TemplatedControl.BorderBrushProperty, GlyphHoverBorder),
            },
        });
        grid.Styles.Add(new Style(x => x.OfType<Button>().Class(":focus"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderBrushProperty, GlyphHoverBorder),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2)),
            },
        });
        grid.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, GlyphPressedBackground),
                new Setter(TemplatedControl.BorderBrushProperty, GlyphHoverBorder),
            },
        });
    }
}
