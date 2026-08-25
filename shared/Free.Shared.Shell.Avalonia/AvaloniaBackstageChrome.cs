using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaBackstageChromeStyle(IBrush PrimaryInk, IBrush SecondaryInk)
{
    public IBrush? ActionInk { get; init; }
    public IBrush? ScrollTrackBrush { get; init; }
    public IBrush? ScrollThumbBrush { get; init; }
    public IBrush SeparatorBrush { get; init; } = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    public double HeadingFontSize { get; init; } = BackstageVisualContract.Pane.HeadingFontSize;
    public Thickness HeadingMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.HeadingMargin);
    public double DescriptionFontSize { get; init; } = BackstageVisualContract.Pane.DescriptionFontSize;
    public double SectionHeaderFontSize { get; init; } = BackstageVisualContract.Pane.SectionHeaderFontSize;
    public Thickness SectionHeaderMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.SectionHeaderMargin);
    public Thickness DetailGridMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.DetailGridMargin);
    public Thickness DetailLabelMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.DetailGridMargin);
    public Thickness DetailValueMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.DetailGridMargin);
    public double DetailLabelColumnWidth { get; init; } = BackstageVisualContract.Pane.DetailLabelColumnWidth;
    public double DetailFontSize { get; init; } = BackstageVisualContract.Pane.DetailFontSize;
    public double ActionFontSize { get; init; } = BackstageVisualContract.Pane.ActionFontSize;
    public double ActionDescriptionFontSize { get; init; } = BackstageVisualContract.Pane.ActionDescriptionFontSize;
    public Thickness ActionRowMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.ActionRowMargin);
    public Thickness ActionDescriptionMargin { get; init; } = ToThickness(BackstageVisualContract.Pane.ActionDescriptionMargin);
    public VerticalAlignment DetailLabelVerticalAlignment { get; init; } = VerticalAlignment.Stretch;
    public double? NoteLineHeight { get; init; }

    public static AvaloniaBackstageChromeStyle FromContract() => new(
        new SolidColorBrush(ToColor(BackstageVisualContract.Theme.PrimaryText)),
        new SolidColorBrush(ToColor(BackstageVisualContract.Theme.SecondaryText)));

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static Color ToColor(BackstageVisualColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);
}

public sealed record AvaloniaBackstageActionButtonSpec(string Text, string AutomationId, Action Action)
{
    public Thickness Padding { get; init; } = new(10, 4);
    public double? MinWidth { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public string? AutomationName { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed record AvaloniaBackstageStackedActionButtonSpec(
    string Label,
    string Description,
    string AutomationId,
    Action Action);

public sealed record AvaloniaBackstageDescribedActionRowSpec(
    string ButtonText,
    string Description,
    string AutomationId)
{
    public Action? Action { get; init; }
    public bool IsEnabled { get; init; } = true;
    public Thickness RowMargin { get; init; } = new(0, 0, 0, 8);
    public Thickness ButtonPadding { get; init; } = new(12, 6);
    public Thickness DescriptionMargin { get; init; } = new(12, 0, 0, 0);
}

public sealed record AvaloniaBackstageContentAreaSpec(Control Content, IBrush Background)
{
    public Thickness Padding { get; init; } = new(32, 24);
}

public sealed record AvaloniaBackstageDialogLayoutSpec(Control Content, Control BottomContent)
{
    public Thickness RootMargin { get; init; } = new(18);
    public Thickness ContentMargin { get; init; } = new(0, 0, 0, 12);
}

public sealed record AvaloniaBackstagePaneSpec(IReadOnlyList<AvaloniaBackstagePaneElementSpec> Elements)
{
    public double Spacing { get; init; } = 14;
}

public abstract record AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageHeadingElementSpec(string Text) : AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageSectionHeaderElementSpec(string Text) : AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageNoteElementSpec(string Text, string AutomationId) : AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageDetailRowsElementSpec(IReadOnlyList<AvaloniaBackstageDetailRowSpec> Rows)
    : AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageDetailRowSpec(string Label, string Value, string ValueAutomationId);

public sealed record AvaloniaBackstageActionRowElementSpec(IReadOnlyList<AvaloniaBackstageActionButtonSpec> Actions)
    : AvaloniaBackstagePaneElementSpec
{
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    public double Spacing { get; init; } = 8;
}

public sealed record AvaloniaBackstageRadioGroupElementSpec(
    string GroupName,
    IReadOnlyList<AvaloniaBackstageRadioOptionSpec> Options)
    : AvaloniaBackstagePaneElementSpec;

public sealed record AvaloniaBackstageRadioOptionSpec(
    string Text,
    string AutomationId,
    Action Select)
{
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
    public Thickness Margin { get; init; } = new(0, 2);
}

/// <summary>
/// Shared chrome builders for Avalonia Backstage panes. Apps supply pane plans, text, callbacks,
/// sizing, and app colors; this class only centralizes common visual construction.
/// </summary>
public static class AvaloniaBackstageChrome
{
    public static StackPanel CreatePane(AvaloniaBackstagePaneSpec spec, AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Elements);
        ArgumentNullException.ThrowIfNull(style);

        var pane = new StackPanel { Spacing = spec.Spacing };
        foreach (var element in spec.Elements)
        {
            pane.Children.Add(element switch
            {
                AvaloniaBackstageHeadingElementSpec heading => CreateHeading(heading.Text, style),
                AvaloniaBackstageSectionHeaderElementSpec section => CreateSectionHeader(section.Text, style),
                AvaloniaBackstageNoteElementSpec note => CreateNote(note.Text, style, note.AutomationId),
                AvaloniaBackstageDetailRowsElementSpec details => CreateDetailRows(details, style),
                AvaloniaBackstageActionRowElementSpec actions => CreateActionRow(actions),
                AvaloniaBackstageRadioGroupElementSpec group => CreateRadioGroup(group),
                _ => throw new ArgumentOutOfRangeException(nameof(element), element, null),
            });
        }

        return pane;
    }

    public static Border CreateContentArea(AvaloniaBackstageContentAreaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Content);
        ArgumentNullException.ThrowIfNull(spec.Background);

        return new Border
        {
            Background = spec.Background,
            Child = CreateContentScroll(spec.Content, margin: default, padding: spec.Padding),
        };
    }

    public static DockPanel CreateDialogLayout(AvaloniaBackstageDialogLayoutSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Content);
        ArgumentNullException.ThrowIfNull(spec.BottomContent);

        var root = new DockPanel { Margin = spec.RootMargin };
        DockPanel.SetDock(spec.BottomContent, Dock.Bottom);
        root.Children.Add(spec.BottomContent);
        root.Children.Add(CreateContentScroll(spec.Content, spec.ContentMargin, padding: default));
        return root;
    }

    public static ScrollViewer CreateContentScroll(Control content, Thickness margin, Thickness padding)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = margin,
            Padding = padding,
            Content = content,
        };
    }

    public static Control CreatePaneHeader(string title, string description, AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = style.HeadingFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = style.PrimaryInk,
            Margin = style.HeadingMargin,
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = style.DescriptionFontSize,
            Foreground = style.SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(CreateSeparator(style, new Thickness(0, 4, 0, 0)));
        return stack;
    }

    public static TextBlock CreateHeading(string text, AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.Light,
            FontSize = style.HeadingFontSize,
            Foreground = style.PrimaryInk,
            Margin = style.HeadingMargin,
        };
    }

    public static TextBlock CreateSectionHeader(string text, AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = style.SectionHeaderFontSize,
            Foreground = style.PrimaryInk,
            Margin = style.SectionHeaderMargin,
        };
    }

    public static TextBlock CreateNote(
        string text,
        AvaloniaBackstageChromeStyle style,
        string? automationId = null,
        FontStyle fontStyle = FontStyle.Normal,
        Thickness margin = default)
    {
        ArgumentNullException.ThrowIfNull(style);

        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = style.SecondaryInk,
            FontStyle = fontStyle,
            Margin = margin,
        };
        if (style.NoteLineHeight is { } lineHeight)
            block.LineHeight = lineHeight;
        if (!string.IsNullOrWhiteSpace(automationId))
            AutomationProperties.SetAutomationId(block, automationId);
        return block;
    }

    public static Grid CreateDetailGrid() => CreateDetailGrid(AvaloniaBackstageChromeStyle.FromContract());

    public static Grid CreateDetailGrid(AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return
        new()
        {
            ColumnDefinitions = new ColumnDefinitions($"{style.DetailLabelColumnWidth},*"),
            Margin = style.DetailGridMargin,
        };
    }

    public static void AddDetailRow(
        Grid grid,
        string label,
        string value,
        string valueAutomationId,
        AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(style);

        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = style.SecondaryInk,
            FontSize = style.DetailFontSize,
            Margin = style.DetailLabelMargin,
            VerticalAlignment = style.DetailLabelVerticalAlignment,
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetRow(labelBlock, rowIndex);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = style.PrimaryInk,
            FontSize = style.DetailFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = style.DetailValueMargin,
        };
        AutomationProperties.SetAutomationId(valueBlock, valueAutomationId);
        Grid.SetColumn(valueBlock, 1);
        Grid.SetRow(valueBlock, rowIndex);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    public static Grid CreateDetailRows(
        AvaloniaBackstageDetailRowsElementSpec spec,
        AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Rows);
        ArgumentNullException.ThrowIfNull(style);

        var grid = CreateDetailGrid(style);
        foreach (var row in spec.Rows)
        {
            AddDetailRow(grid, row.Label, row.Value, row.ValueAutomationId, style);
        }

        return grid;
    }

    public static Button CreateActionButton(AvaloniaBackstageActionButtonSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Action);

        var button = new Button
        {
            Content = spec.Text,
            Padding = spec.Padding,
            IsEnabled = spec.IsEnabled,
        };
        if (spec.MinWidth is { } minWidth)
            button.MinWidth = minWidth;
        if (spec.HorizontalAlignment is { } horizontalAlignment)
            button.HorizontalAlignment = horizontalAlignment;
        if (!string.IsNullOrWhiteSpace(spec.AutomationName))
            AutomationProperties.SetName(button, spec.AutomationName);
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        button.Click += (_, _) => spec.Action();
        return button;
    }

    public static StackPanel CreateActionRow(AvaloniaBackstageActionRowElementSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Actions);

        var row = new StackPanel
        {
            Orientation = spec.Orientation,
            Spacing = spec.Spacing,
        };
        foreach (var action in spec.Actions)
        {
            row.Children.Add(CreateActionButton(action));
        }

        return row;
    }

    public static StackPanel CreateRadioGroup(AvaloniaBackstageRadioGroupElementSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Options);

        var group = new StackPanel();
        foreach (var option in spec.Options)
        {
            ArgumentNullException.ThrowIfNull(option.Select);

            var radio = new RadioButton
            {
                GroupName = spec.GroupName,
                Content = option.Text,
                IsEnabled = option.IsEnabled,
                IsChecked = option.IsChecked,
                Margin = option.Margin,
            };
            AutomationProperties.SetAutomationId(radio, option.AutomationId);
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    option.Select();
            };
            group.Children.Add(radio);
        }

        return group;
    }

    public static Button CreateStackedActionButton(
        AvaloniaBackstageStackedActionButtonSpec spec,
        AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Action);
        ArgumentNullException.ThrowIfNull(style);

        var label = new TextBlock
        {
            Text = spec.Label,
            Foreground = style.ActionInk ?? style.PrimaryInk,
            FontWeight = FontWeight.Medium,
            FontSize = style.ActionFontSize,
        };
        var desc = new TextBlock
        {
            Text = spec.Description,
            Foreground = style.SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            FontSize = style.ActionDescriptionFontSize,
            Margin = style.ActionDescriptionMargin,
        };
        var inner = new StackPanel { Children = { label, desc } };

        var button = new Button
        {
            Content = inner,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        button.Click += (_, _) => spec.Action();
        return button;
    }

    public static DockPanel CreateDescribedActionRow(
        AvaloniaBackstageDescribedActionRowSpec spec,
        AvaloniaBackstageChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(style);

        var button = new Button
        {
            Content = spec.ButtonText,
            Padding = spec.ButtonPadding,
            IsEnabled = spec.IsEnabled,
        };
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        if (spec.Action is { } action)
            button.Click += (_, _) => action();

        var row = new DockPanel { Margin = spec.RowMargin };
        DockPanel.SetDock(button, Dock.Left);
        row.Children.Add(button);
        row.Children.Add(new TextBlock
        {
            Text = spec.Description,
            Foreground = style.SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = spec.DescriptionMargin,
        });
        return row;
    }

    public static Border CreateSeparator(AvaloniaBackstageChromeStyle style, Thickness margin)
    {
        ArgumentNullException.ThrowIfNull(style);

        return new Border
        {
            Height = 1,
            Background = style.SeparatorBrush,
            Margin = margin,
        };
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
