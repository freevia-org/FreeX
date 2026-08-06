using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    private static string CellStylePresetHeader(CellStylePreset preset) =>
        preset switch
        {
            CellStylePreset.CheckCell => "Check Cell",
            CellStylePreset.LinkedCell => "Linked Cell",
            CellStylePreset.ExplanatoryText => "Explanatory Text",
            CellStylePreset.Heading1 => "Heading 1",
            CellStylePreset.Heading2 => "Heading 2",
            CellStylePreset.WarningText => "Warning Text",
            CellStylePreset.Accent1_20 => "20% - Accent 1",
            CellStylePreset.Accent2_20 => "20% - Accent 2",
            CellStylePreset.Accent3_20 => "20% - Accent 3",
            CellStylePreset.Accent4_20 => "20% - Accent 4",
            CellStylePreset.Accent5_20 => "20% - Accent 5",
            CellStylePreset.Accent6_20 => "20% - Accent 6",
            CellStylePreset.Accent1_40 => "40% - Accent 1",
            CellStylePreset.Accent2_40 => "40% - Accent 2",
            CellStylePreset.Accent3_40 => "40% - Accent 3",
            CellStylePreset.Accent4_40 => "40% - Accent 4",
            CellStylePreset.Accent5_40 => "40% - Accent 5",
            CellStylePreset.Accent6_40 => "40% - Accent 6",
            CellStylePreset.Accent1_60 => "60% - Accent 1",
            CellStylePreset.Accent2_60 => "60% - Accent 2",
            CellStylePreset.Accent3_60 => "60% - Accent 3",
            CellStylePreset.Accent4_60 => "60% - Accent 4",
            CellStylePreset.Accent5_60 => "60% - Accent 5",
            CellStylePreset.Accent6_60 => "60% - Accent 6",
            _ => preset.ToString()
        };

    private static bool ContainsExcludedStatus(string? value) =>
        ResolveLocalizedValue(value)?.Contains("excluded", StringComparison.OrdinalIgnoreCase) == true;

    private static XElement StyleByKey(XDocument document, XNamespace presentation, XNamespace x, string key) =>
        document
            .Descendants(presentation + "Style")
            .Single(style => style.Attribute(x + "Key")?.Value == key);

    private static string? SetterValue(XElement style, XNamespace presentation, string property) =>
        style
            .Elements(presentation + "Setter")
            .Single(setter => setter.Attribute("Property")?.Value == property)
            .Attribute("Value")
            ?.Value;

    private static string? CommandName(XElement element, XNamespace ribbonWpf) =>
        element.Attribute(ribbonWpf + "RibbonMetadata.CommandName")?.Value ??
        LocalizedAttribute(element, ribbonWpf + "RibbonTooltip.Title");

    private static string? LocalizedAttribute(XElement element, XName name) =>
        ResolveLocalizedValue(element.Attribute(name)?.Value);

    private static string? LocalizedAttribute(XElement element, string name) =>
        ResolveLocalizedValue(element.Attribute(name)?.Value);

    private static string? ResolveLocalizedValue(string? value)
    {
        const string locPrefix = "{local:Loc Key=";
        if (value is not { Length: > 0 } ||
            !value.StartsWith(locPrefix, StringComparison.Ordinal) ||
            !value.EndsWith("}", StringComparison.Ordinal))
        {
            return value;
        }

        var key = value[locPrefix.Length..^1];
        return UiText.Get(key);
    }

    private static string? GetButtonText(XElement button, XNamespace presentation)
    {
        if (LocalizedAttribute(button, "Content") is { } content)
            return content;

        return button
            .Descendants()
            .Where(element => element.Name == presentation + "TextBlock" || element.Name == presentation + "AccessText")
            .Select(element => LocalizedAttribute(element, "Text") ?? element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static XElement FindTab(XDocument document, string header)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Single(element => LocalizedAttribute(element, "Header") == header);
    }

    private static string ReadPivotCommandSource()
    {
        return DialogSourceTestSupport.ReadHostSources(
            "MainWindow.PivotCommands.cs",
            "MainWindow.PivotAdvancedCommands.cs",
            "MainWindow.PivotChartCommands.cs",
            "MainWindow.PivotDesignCommands.cs",
            "MainWindow.PivotSlicerTimeline.cs");
    }
}
