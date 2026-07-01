using FreeX.Core.Model;

namespace FreeX.App.Presentation.SheetUI;

public sealed record SheetNameDialogResult(string SheetName);

public sealed record UnhideSheetDialogResult(string SheetName);

public sealed record ActivateSheetDialogResult(SheetId SheetId);

public sealed record SheetDialogTarget(string DisplayName, SheetId SheetId)
{
    public override string ToString() => DisplayName;
}

public enum SheetNameValidationError
{
    Blank,
    TooLong,
    InvalidCharacters,
    InvalidApostrophe
}

public static class SheetDialogPlanner
{
    public static SheetNameDialogResult CreateSheetNameResult(string? sheetName) =>
        new((sheetName ?? string.Empty).Trim());

    public static bool TryCreateSheetNameResult(
        string? sheetName,
        out SheetNameDialogResult result,
        out SheetNameValidationError? error)
    {
        result = CreateSheetNameResult(sheetName);

        if (string.IsNullOrWhiteSpace(result.SheetName))
        {
            error = SheetNameValidationError.Blank;
            return false;
        }

        if (result.SheetName.Length > 31)
        {
            error = SheetNameValidationError.TooLong;
            return false;
        }

        if (Workbook.ContainsInvalidSheetNameCharacter(result.SheetName))
        {
            error = SheetNameValidationError.InvalidCharacters;
            return false;
        }

        if (Workbook.ValidateSheetNameStructure(result.SheetName) is not null)
        {
            error = SheetNameValidationError.InvalidApostrophe;
            return false;
        }

        error = null;
        return true;
    }

    public static IReadOnlyList<SheetDialogTarget> BuildActivateSheetTargets(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return workbook.Sheets
            .Where(sheet => !sheet.IsHidden)
            .Select(sheet => new SheetDialogTarget(sheet.Name, sheet.Id))
            .ToList();
    }

    public static SheetDialogTarget? FindInitialActivateSheetTarget(
        IReadOnlyList<SheetDialogTarget> targets,
        SheetId activeSheetId)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            return null;

        foreach (var target in targets)
        {
            if (target.SheetId == activeSheetId)
                return target;
        }

        return targets[0];
    }

    public static ActivateSheetDialogResult CreateActivateSheetResult(SheetId sheetId) =>
        new(sheetId);

    public static IReadOnlyList<string> BuildUnhideSheetTargets(IEnumerable<string> hiddenSheetNames)
    {
        ArgumentNullException.ThrowIfNull(hiddenSheetNames);
        return hiddenSheetNames.ToList();
    }

    public static UnhideSheetDialogResult CreateUnhideSheetResult(string? sheetName) =>
        new((sheetName ?? string.Empty).Trim());

    public static bool CanAcceptUnhideSheetTarget(string? sheetName) =>
        !string.IsNullOrWhiteSpace(sheetName);
}
