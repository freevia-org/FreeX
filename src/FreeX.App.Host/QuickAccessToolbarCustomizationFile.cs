using System.IO;
using System.Text.Json;

namespace FreeX.App.Host;

internal sealed record QuickAccessToolbarCustomization(
    bool QuickAccessToolbarBelowRibbon,
    IReadOnlyList<string> CommandIds);

internal sealed record QuickAccessToolbarCustomizationFileResult(
    bool Success,
    QuickAccessToolbarCustomization? Customization,
    string? ErrorMessage)
{
    public static QuickAccessToolbarCustomizationFileResult Ok(QuickAccessToolbarCustomization customization) =>
        new(true, customization, null);

    public static QuickAccessToolbarCustomizationFileResult Fail(string errorMessage) =>
        new(false, null, errorMessage);
}

internal static class QuickAccessToolbarCustomizationFile
{
    public const string FileFormat = "FreeX.QuickAccessToolbarCustomization";
    public const int CurrentVersion = 1;
    public const string DefaultExtension = ".freex-qat.json";
    public const string DefaultFileName = "FreeX Quick Access Toolbar.freex-qat.json";
    public const string DialogFilter =
        "FreeX Quick Access Toolbar (*.freex-qat.json)|*.freex-qat.json|JSON files (*.json)|*.json|All files (*.*)|*.*";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize(
        IEnumerable<string>? commandIds,
        bool quickAccessToolbarBelowRibbon)
    {
        var payload = new Payload
        {
            Format = FileFormat,
            Version = CurrentVersion,
            QuickAccessToolbarBelowRibbon = quickAccessToolbarBelowRibbon,
            Commands = QuickAccessToolbarCatalog.NormalizeCommandIds(commandIds)
                .Select(commandId => (string?)commandId)
                .ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static bool TrySave(
        string path,
        IEnumerable<string>? commandIds,
        bool quickAccessToolbarBelowRibbon,
        out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Choose a destination file for the Quick Access Toolbar customization.";
            return false;
        }

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                Serialize(commandIds, quickAccessToolbarBelowRibbon));
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to export Quick Access Toolbar customization to '{path}': {ex.Message}";
            return false;
        }
    }

    public static QuickAccessToolbarCustomizationFileResult TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "Choose a FreeX Quick Access Toolbar customization file to import.");

        try
        {
            return TryDeserialize(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            return QuickAccessToolbarCustomizationFileResult.Fail(
                $"Failed to import Quick Access Toolbar customization from '{path}': {ex.Message}");
        }
    }

    public static QuickAccessToolbarCustomizationFileResult TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "The selected Quick Access Toolbar customization file is empty.");

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return QuickAccessToolbarCustomizationFileResult.Fail(
                $"The selected file is not valid FreeX Quick Access Toolbar JSON: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return QuickAccessToolbarCustomizationFileResult.Fail(
                $"The selected file is not valid FreeX Quick Access Toolbar JSON: {ex.Message}");
        }

        if (payload is null)
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "The selected Quick Access Toolbar customization file is empty.");

        if (!string.Equals(payload.Format?.Trim(), FileFormat, StringComparison.Ordinal))
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "The selected file is not a FreeX Quick Access Toolbar customization file.");

        if (payload.Version != CurrentVersion)
            return QuickAccessToolbarCustomizationFileResult.Fail(
                $"Unsupported Quick Access Toolbar customization version: {payload.Version}.");

        if (payload.Commands is null || payload.Commands.Count == 0)
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "The selected file does not list any Quick Access Toolbar commands.");

        var commandIds = NormalizeImportedCommandIds(payload.Commands);
        if (commandIds.Count == 0)
            return QuickAccessToolbarCustomizationFileResult.Fail(
                "The selected file does not contain any commands FreeX can add to the Quick Access Toolbar.");

        return QuickAccessToolbarCustomizationFileResult.Ok(
            new QuickAccessToolbarCustomization(
                payload.QuickAccessToolbarBelowRibbon,
                commandIds));
    }

    private static IReadOnlyList<string> NormalizeImportedCommandIds(IEnumerable<string?> commandIds)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var commandId in commandIds)
        {
            if (string.IsNullOrWhiteSpace(commandId) ||
                !QuickAccessToolbarCatalog.TryGet(commandId.Trim(), out var command) ||
                !seen.Add(command.Id))
            {
                continue;
            }

            result.Add(command.Id);
        }

        return result;
    }

    private sealed class Payload
    {
        public string? Format { get; set; } = FileFormat;
        public int Version { get; set; } = CurrentVersion;
        public bool QuickAccessToolbarBelowRibbon { get; set; }
        public List<string?>? Commands { get; set; }
    }
}
