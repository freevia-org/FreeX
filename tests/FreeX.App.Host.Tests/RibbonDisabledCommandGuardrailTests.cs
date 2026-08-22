using System.Xml;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonDisabledCommandGuardrailTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace RibbonWpf =
        "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void VisibleRibbonCommands_DoNotShipAsStaticDisabledOrDeferredPlaceholdersWithoutReason()
    {
        var violations = ReadVisibleRibbonCommands()
            .Where(command => command.IsStaticDisabled || command.HasDeferredOrExcludedTooltip)
            .Where(command => !AllowedRibbonCommandReasons.ContainsKey(command.Key))
            .Select(command => command.FormatViolation())
            .ToArray();

        violations.Should().BeEmpty(
            "visible ribbon commands should not remain as disabled/deferred/excluded placeholders unless this guardrail documents an explicit allowed runtime-state or temporary integration reason; violations: {0}",
            string.Join("; ", violations));
    }

    [Fact]
    public void NotImplementedOrExcludedRibbonCommands_AreNotSurfacedAsActionableCommands()
    {
        var violations = ReadVisibleRibbonCommands()
            .Where(command => HiddenRibbonCommandPaths.Contains(command.Key.Path))
            .Select(command => command.FormatViolation())
            .ToArray();

        violations.Should().BeEmpty(
            "ribbon commands without implemented behavior must remain hidden until they have a real command path; violations: {0}",
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

        return AttributeValue(element, RibbonWpf + "RibbonMetadata.CommandName") is not null;
    }

    private static IEnumerable<GuardedRibbonCommand> ReadVisibleRibbonCommands()
    {
        var path = DialogSourceTestSupport.FindHostSourceFile("MainWindow.xaml");
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        var ribbonTabs = document
            .Descendants(Presentation + "TabControl")
            .Single(element => AttributeValue(element, Xaml + "Name") == "RibbonTabs");

        return ribbonTabs
            .Descendants()
            .Where(IsRibbonCommandElement)
            .Where(command => !IsHiddenOutsideContextualTab(command))
            .Select(ReadCommand)
            .ToArray();
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
        var commandName = AttributeValue(element, RibbonWpf + "RibbonMetadata.CommandName") ?? "";
        var description = AttributeValue(element, RibbonWpf + "RibbonTooltip.Description") ?? "";
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
            .Select(ancestor => AttributeValue(ancestor, RibbonWpf + "RibbonMetadata.CommandName"))
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
                "Allowed partial-scope copy: the command recalculates and refreshes FreeX-managed workbook data; its tooltip only calls out excluded external query families."
        };

    private static readonly HashSet<string> HiddenRibbonCommandPaths = new(StringComparer.Ordinal)
    {
        "Recommended PivotTables",
        "Pictures > Place in Cell",
        "Pictures > Place in Cell > This Device Picture in Cell",
        "Pictures > Place in Cell > Stock Images in Cell",
        "Pictures > Place in Cell > Online Pictures in Cell",
        "Pictures > Place over Cells > Stock Images over Cells",
        "Pictures > Place over Cells > Online Pictures over Cells",
        "Print Area > Add to Print Area",
        "Group > Auto Outline"
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
