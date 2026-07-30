using System.Text.Json;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private int _nameBoxDropdownPhysicalEvidenceSequence;

    private void InitializeNameBoxDropdownPhysicalEvidence()
    {
        RecordNameBoxDropdownPhysicalEvidence(item: null, stage: "fixture-seeded");
    }

    private void RecordNameBoxDropdownPhysicalEvidence(NameBoxNavigationItem? item, string stage)
    {
        var path = FindNameBoxDropdownPhysicalEvidencePath(App.StartupArguments);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var selectedKind = _selectedDrawingObjectKind?.ToString();
            var payload = new
            {
                sequence = ++_nameBoxDropdownPhysicalEvidenceSequence,
                utc = DateTimeOffset.UtcNow,
                stage,
                itemName = item?.Name,
                itemKind = item?.Kind.ToString(),
                itemObjectKind = item?.ObjectKind?.ToString(),
                nameBoxText = _cellAddressText.Text ?? string.Empty,
                selectedObjectKind = selectedKind,
                selectedObjectId = _selectedDrawingObjectId?.ToString(),
                activeSheetId = _session.ActiveSheet.Id.ToString(),
                activeCell = _session.ActiveCell.ToA1(),
                contextualState = selectedKind ?? "None",
            };
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Physical evidence is opt-in and must never alter normal worksheet behavior.
        }
    }

    private static string? FindNameBoxDropdownPhysicalEvidencePath(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(
                    arguments[index],
                    InteractionValidationOptions.NameBoxDropdownPhysicalEvidenceArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
