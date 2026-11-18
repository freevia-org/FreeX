using System.Text.Json;
using System.Text.Json.Serialization;
using Free.Shared.Ribbon;
using FreeX.Ribbon.Definitions;

var check = args.Contains("--check", StringComparer.Ordinal);
var outputArgument = args.FirstOrDefault(argument => argument != "--check");
var outputPath = Path.GetFullPath(outputArgument ?? "docs/testing/freex-command-surface.jsonl");
var manifest = BuildManifest(FreeXRibbon.Build());
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
};
var lines = new List<string>
{
    JsonSerializer.Serialize(new CommandCoverageHeader(
        "manifest",
        "ribbon",
        "Generated from FreeXRibbon.Build(); pending fields require paired Excel/FreeX execution."), jsonOptions)
};
lines.AddRange(manifest.Commands.Select(command =>
    JsonSerializer.Serialize(command with { RecordType = "command" }, jsonOptions)));
var json = string.Join("\n", lines) + "\n";

if (check)
{
    if (!File.Exists(outputPath) || !string.Equals(File.ReadAllText(outputPath), json, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"Command surface manifest is stale: {outputPath}");
        return 1;
    }

    Console.WriteLine($"Command surface manifest is current: {manifest.Commands.Count} commands.");
    return 0;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, json);
Console.WriteLine($"Wrote {manifest.Commands.Count} commands to {outputPath}");
return 0;

static CommandCoverageManifest BuildManifest(RibbonDefinition definition)
{
    var commands = new Dictionary<string, List<CommandSurface>>(StringComparer.Ordinal);

    void Add(RibbonCommandId? commandId, CommandSurface surface)
    {
        if (commandId is not { } id || string.IsNullOrEmpty(id.Value))
            return;

        if (!commands.TryGetValue(id.Value, out var surfaces))
            commands.Add(id.Value, surfaces = []);
        surfaces.Add(surface);
    }

    foreach (var tab in definition.Tabs)
    foreach (var group in tab.Groups)
    {
        if (group.Launcher is { } launcher)
        {
            Add(launcher.CommandId, new CommandSurface(
                "GroupLauncher", tab.Id, tab.Header, tab.KeyTip, group.Id, group.Header, group.KeyTip,
                launcher.TooltipTitle, launcher.KeyTip, [], [], []));
        }

        foreach (var control in group.Controls)
        {
            Add(control.CommandId, new CommandSurface(
                control.GetType().Name, tab.Id, tab.Header, tab.KeyTip, group.Id, group.Header, group.KeyTip,
                control.Label, control.KeyTip, [], [], []));

            var menu = control switch
            {
                RibbonSplitButton split => split.Menu,
                RibbonDropdown dropdown => dropdown.Menu,
                _ => null,
            };
            if (menu is not null)
                AddMenuItems(menu.Items, tab, group, control.Label, [], [], []);
        }
    }

    void AddMenuItems(
        IReadOnlyList<RibbonMenuItem> items,
        RibbonTab tab,
        RibbonGroup group,
        string controlLabel,
        string[] parentPath,
        string?[] parentKeyTips,
        string[] parentInputGestures)
    {
        foreach (var item in items)
        {
            if (item.Kind == RibbonMenuItemKind.Separator)
                continue;

            var path = parentPath.Append(item.Header).ToArray();
            var keyTips = parentKeyTips.Append(item.KeyTip).ToArray();
            var inputGestures = parentInputGestures.Append(item.InputGesture ?? "").ToArray();
            Add(item.CommandId, new CommandSurface(
                "MenuItem", tab.Id, tab.Header, tab.KeyTip, group.Id, group.Header, group.KeyTip,
                controlLabel, null, path, keyTips, inputGestures, item.IsEnabled));
            AddMenuItems(item.Children, tab, group, controlLabel, path, keyTips, inputGestures);
        }
    }

    return new CommandCoverageManifest(commands.Select(pair => new CommandCoverageRow(
            "command",
            pair.Key,
            pair.Value,
            "pending",
            "pending",
            "pending",
            "pending",
            "pending")).ToArray());
}

sealed record CommandCoverageManifest(
    IReadOnlyList<CommandCoverageRow> Commands);

sealed record CommandCoverageHeader(string RecordType, string Scope, string EvidenceRule);

sealed record CommandCoverageRow(
    string RecordType,
    string CommandId,
    IReadOnlyList<CommandSurface> Surfaces,
    string MouseRoute,
    string KeyboardRoute,
    string ResultEvidence,
    string VisualEvidence,
    string WorkbookEffectEvidence);

sealed record CommandSurface(
    string Kind,
    string TabId,
    string Tab,
    string? TabKeyTip,
    string GroupId,
    string Group,
    string? GroupKeyTip,
    string Label,
    string? KeyTip,
    IReadOnlyList<string> MenuPath,
    IReadOnlyList<string?> MenuKeyTips,
    IReadOnlyList<string> MenuInputGestures,
    bool? DefinitionEnabled = null);
