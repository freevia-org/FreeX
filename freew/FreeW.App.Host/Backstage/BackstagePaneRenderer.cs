using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeW.App.Presentation.Backstage;

namespace FreeW.App.Host.Backstage;

internal static class BackstagePaneRenderer
{
    public static UIElement BuildAccountPane(
        BackstageVisualKit kit,
        BackstageAccountPaneSurfaceSpec surface)
    {
        var metrics = surface.VisualMetrics;
        var panel = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(BuildHeader(kit, surface.Title, surface.Description, metrics));

        foreach (var group in surface.Groups)
        {
            panel.Children.Add(BuildSectionHeader(kit, group.Heading, metrics));
            foreach (var field in group.Fields)
                panel.Children.Add(BuildField(kit, field.Label, field.Value, metrics));
        }

        var options = kit.LinkButton(
            surface.OptionsAction.Label,
            surface.OptionsAction.Invoke ?? (() => { }));
        options.FontSize = metrics.OptionsFontSize;
        options.Margin = ToThickness(metrics.OptionsMargin);
        options.IsEnabled = surface.OptionsAction.IsEnabled;
        options.SetCurrentValue(
            AutomationProperties.AutomationIdProperty,
            surface.OptionsAction.AutomationId);
        panel.Children.Add(options);

        return kit.Scroll(panel);
    }

    public static UIElement BuildActionPane(
        BackstageVisualKit kit,
        BackstageActionPaneSurfaceSpec surface)
    {
        var metrics = surface.VisualMetrics;
        var panel = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(BuildHeader(kit, surface.Title, surface.Description, metrics));

        foreach (var group in surface.Groups)
        {
            panel.Children.Add(BuildSectionHeader(kit, group.Heading, metrics));
            foreach (var action in group.Actions)
                panel.Children.Add(BuildActionRow(kit, action, metrics));
        }

        return kit.Scroll(panel);
    }

    private static UIElement BuildHeader(
        BackstageVisualKit kit,
        string title,
        string description,
        BackstageActionPaneVisualMetrics metrics) =>
        BuildHeaderCore(
            kit,
            title,
            description,
            metrics.HeadingFontSize,
            metrics.HeadingBottomMargin,
            metrics.DescriptionFontSize,
            metrics.DescriptionBottomMargin);

    private static UIElement BuildHeader(
        BackstageVisualKit kit,
        string title,
        string description,
        BackstageAccountPaneVisualMetrics metrics) =>
        BuildHeaderCore(
            kit,
            title,
            description,
            metrics.HeadingFontSize,
            metrics.HeadingBottomMargin,
            metrics.DescriptionFontSize,
            metrics.DescriptionBottomMargin);

    private static UIElement BuildHeaderCore(
        BackstageVisualKit kit,
        string title,
        string description,
        double headingFontSize,
        BackstageThickness headingMargin,
        double descriptionFontSize,
        BackstageThickness descriptionMargin)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = headingFontSize,
            FontWeight = FontWeights.Light,
            Foreground = kit.Heading,
            Margin = ToThickness(headingMargin),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = kit.Muted,
                FontSize = descriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(descriptionMargin),
            });
        }

        return panel;
    }

    private static TextBlock BuildSectionHeader(
        BackstageVisualKit kit,
        string text,
        BackstageAccountPaneVisualMetrics metrics) =>
        new()
        {
            Text = text,
            FontSize = metrics.SectionHeaderFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = kit.Heading,
            Margin = ToThickness(metrics.SectionHeaderMargin),
        };

    private static TextBlock BuildSectionHeader(
        BackstageVisualKit kit,
        string text,
        BackstageActionPaneVisualMetrics metrics) =>
        new()
        {
            Text = text,
            FontSize = metrics.SectionHeaderFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = kit.Heading,
            Margin = ToThickness(metrics.SectionHeaderMargin),
        };

    private static UIElement BuildField(
        BackstageVisualKit kit,
        string label,
        string value,
        BackstageAccountPaneVisualMetrics metrics)
    {
        var grid = new Grid { Margin = ToThickness(metrics.FieldRowMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(metrics.FieldLabelColumnWidth),
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = kit.Muted,
            FontSize = metrics.FieldFontSize,
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = kit.Heading,
            FontSize = metrics.FieldFontSize,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
        return grid;
    }

    private static UIElement BuildActionRow(
        BackstageVisualKit kit,
        BackstageActionRow action,
        BackstageActionPaneVisualMetrics metrics)
    {
        var row = new StackPanel { Margin = ToThickness(metrics.ActionRowMargin) };
        var button = kit.LinkButton(action.Label, action.Invoke ?? (() => { }));
        button.FontSize = metrics.ActionFontSize;
        button.IsEnabled = action.Invoke is not null;
        button.SetCurrentValue(
            AutomationProperties.AutomationIdProperty,
            $"BackstageAction_{action.Label.Replace(' ', '_')}");
        row.Children.Add(button);

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = kit.Muted,
                FontSize = metrics.DescriptionTextFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.ActionDescriptionMargin),
            });
        }

        return row;
    }

    private static Thickness ToThickness(BackstageThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}
