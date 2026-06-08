using System.Xml;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonDisabledCommandGuardrailTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Local = "clr-namespace:FreeX.App.Host";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void VisibleRibbonCommands_DoNotShipAsStaticDisabledOrDeferredPlaceholdersWithoutReason()
    {
        var path = DialogSourceTestSupport.FindHostSourceFile("MainWindow.xaml");
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        var ribbonTabs = document
            .Descendants(Presentation + "TabControl")
            .Single(element => AttributeValue(element, Xaml + "Name") == "RibbonTabs");

        var violations = ribbonTabs
            .Descendants()
            .Where(IsRibbonCommandElement)
            .Where(command => !IsHiddenOutsideContextualTab(command))
            .Select(ReadCommand)
            .Where(command => command.IsStaticDisabled || command.HasDeferredOrExcludedTooltip)
            .Where(command => !AllowedRibbonCommandReasons.ContainsKey(command.Key))
            .Select(command => command.FormatViolation())
            .ToArray();

        violations.Should().BeEmpty(
            "visible ribbon commands should not remain as disabled/deferred/excluded placeholders unless this guardrail documents an explicit allowed runtime-state or temporary integration reason; violations: {0}",
            string.Join("; ", violations));
    }

    [Fact]
    public void AllowedRibbonCommandReasons_AreExplicit()
    {
        AllowedRibbonCommandReasons.Should().OnlyContain(
            pair => !string.IsNullOrWhiteSpace(pair.Value),
            "allowing a visible disabled/deferred ribbon command requires a reason that future cleanup can audit");
    }

    private static bool IsRibbonCommandElement(XElement element)
    {
        if (!CommandElementNames.Contains(element.Name.LocalName))
            return false;

        return AttributeValue(element, Local + "RibbonMetadata.CommandName") is not null;
    }

    private static bool IsHiddenOutsideContextualTab(XElement element) =>
        element
            .AncestorsAndSelf()
            .Any(ancestor =>
                string.Equals(AttributeValue(ancestor, "Visibility"), "Collapsed", StringComparison.Ordinal) &&
                ancestor.Name != Presentation + "TabItem");

    private static GuardedRibbonCommand ReadCommand(XElement element)
    {
        var tab = element
            .Ancestors(Presentation + "TabItem")
            .Select(tabElement => AttributeValue(tabElement, "Header") ?? "")
            .FirstOrDefault() ?? "";
        var group = element
            .Ancestors(Presentation + "Grid")
            .Where(IsRibbonGroupPanel)
            .Select(ReadGroupName)
            .FirstOrDefault() ?? "";
        var commandName = AttributeValue(element, Local + "RibbonMetadata.CommandName") ?? "";
        var description = AttributeValue(element, Local + "RibbonTooltip.Description") ?? "";
        var path = BuildCommandPath(element, commandName);
        var lineNumber = element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : 0;

        return new GuardedRibbonCommand(
            new RibbonCommandGuardKey(tab, group, path),
            element.Name.LocalName,
            lineNumber,
            string.Equals(AttributeValue(element, "IsEnabled"), "False", StringComparison.OrdinalIgnoreCase),
            HasDeferredOrExcludedTooltip(description));
    }

    private static string ReadGroupName(XElement group) =>
        group
            .Descendants(Presentation + "TextBlock")
            .Where(IsGroupLabel)
            .Select(label => AttributeValue(label, "Text"))
            .FirstOrDefault() ?? "";

    private static string BuildCommandPath(XElement element, string commandName)
    {
        var ancestorCommands = element
            .Ancestors()
            .Reverse()
            .Where(IsRibbonCommandElement)
            .Select(ancestor => AttributeValue(ancestor, Local + "RibbonMetadata.CommandName"))
            .Where(name => !string.IsNullOrWhiteSpace(name));

        return string.Join(" > ", ancestorCommands.Append(commandName));
    }

    private static bool IsRibbonGroupPanel(XElement element) =>
        string.Equals(AttributeValue(element, "Style"), "{StaticResource RibbonGroupPanel}", StringComparison.Ordinal);

    private static bool IsGroupLabel(XElement element) =>
        string.Equals(AttributeValue(element, "Style"), "{StaticResource GroupLbl}", StringComparison.Ordinal);

    private static bool HasDeferredOrExcludedTooltip(string description) =>
        description.Contains("Deferred", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Excluded", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("out of scope", StringComparison.OrdinalIgnoreCase);

    private static string? AttributeValue(XElement element, XName name) =>
        LocalizedXamlTestSupport.ResolveLocalizedValue(element.Attribute(name)?.Value);

    private static readonly HashSet<string> CommandElementNames = new(StringComparer.Ordinal)
    {
        "AutomationInvokeButton",
        "Button",
        "CheckBox",
        "ComboBox",
        "MenuItem",
        "ToggleButton"
    };

    private static readonly IReadOnlyDictionary<RibbonCommandGuardKey, string> AllowedRibbonCommandReasons =
        new Dictionary<RibbonCommandGuardKey, string>
        {
            [new("Data", "Get & Transform Data", "Get Data")] =
                "Allowed partial-scope copy: the command imports local CSV data; its tooltip only calls out excluded connector families.",
            [new("Data", "Queries & Connections", "Refresh All")] =
                "Allowed partial-scope copy: the command recalculates and refreshes FreeX-managed workbook data; its tooltip only calls out excluded external query families.",
            [new("Insert", "Illustrations", "Pictures > Place in Cell > This Device Picture in Cell")] =
                "Temporary Excel-parity placeholder: in-cell picture anchoring needs model/rendering support before local file insertion can be enabled.",
            [new("Insert", "Illustrations", "Pictures > Place in Cell > Stock Images in Cell")] =
                "Temporary Excel-parity placeholder: stock image service integration is intentionally unavailable until connector policy and licensing are defined.",
            [new("Insert", "Illustrations", "Pictures > Place in Cell > Online Pictures in Cell")] =
                "Temporary Excel-parity placeholder: online image search/source connectors are intentionally unavailable until connector policy is defined.",
            [new("Insert", "Illustrations", "Pictures > Place over Cells > Stock Images over Cells")] =
                "Temporary Excel-parity placeholder: stock image service integration is intentionally unavailable until connector policy and licensing are defined.",
            [new("Insert", "Illustrations", "Pictures > Place over Cells > Online Pictures over Cells")] =
                "Temporary Excel-parity placeholder: online image search/source connectors are intentionally unavailable until connector policy is defined.",
            [new("Page Layout", "Page Setup", "Print Area > Add to Print Area")] =
                "Temporary Excel-parity placeholder: additive print areas need multi-range print-area model and persistence support before enabling.",
            [new("Data", "Outline", "Group > Auto Outline")] =
                "Temporary Excel-parity placeholder: automatic outline inference is not implemented; manual group/ungroup remains the supported path."
        };

    private sealed record GuardedRibbonCommand(
        RibbonCommandGuardKey Key,
        string ElementName,
        int LineNumber,
        bool IsStaticDisabled,
        bool HasDeferredOrExcludedTooltip)
    {
        public string FormatViolation()
        {
            var reasons = new List<string>();
            if (IsStaticDisabled)
                reasons.Add("IsEnabled=\"False\"");
            if (HasDeferredOrExcludedTooltip)
                reasons.Add("Deferred/Excluded tooltip copy");

            var line = LineNumber > 0 ? $" line {LineNumber}" : "";
            return $"{Key.Tab}/{Key.Group}/{Key.Path} ({ElementName}{line}: {string.Join(", ", reasons)})";
        }
    }

    private sealed record RibbonCommandGuardKey(string Tab, string Group, string Path);
}
