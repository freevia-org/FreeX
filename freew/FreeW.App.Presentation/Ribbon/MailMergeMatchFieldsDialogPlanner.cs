using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public readonly record struct MailMergeMatchFieldRolePlan(
    FieldRole Role,
    string Label,
    string SelectedChoice);

public static class MailMergeMatchFieldsDialogPlanner
{
    public const string NotMatchedChoice = "(not matched)";

    private static readonly FieldRole[] Roles = Enum.GetValues<FieldRole>();

    private static readonly IReadOnlyDictionary<FieldRole, string> RoleLabels =
        new Dictionary<FieldRole, string>
        {
            [FieldRole.Title] = "Title (Mr., Mrs., \u2026)",
            [FieldRole.FirstName] = "First Name",
            [FieldRole.MiddleName] = "Middle Name",
            [FieldRole.LastName] = "Last Name",
            [FieldRole.Suffix] = "Suffix (Jr., Sr., \u2026)",
            [FieldRole.Company] = "Company",
            [FieldRole.Address1] = "Address 1",
            [FieldRole.Address2] = "Address 2",
            [FieldRole.City] = "City",
            [FieldRole.State] = "State",
            [FieldRole.PostalCode] = "Postal Code",
            [FieldRole.Country] = "Country or Region",
        };

    private static string GetRoleLabel(FieldRole role) =>
        RoleLabels.TryGetValue(role, out var label) ? label : role.ToString();

    public static IReadOnlyList<string> GetColumnChoices(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return [NotMatchedChoice, .. header];
    }

    public static IReadOnlyList<MailMergeMatchFieldRolePlan> GetRolePlans(
        IReadOnlyList<string> header,
        FieldMapping current)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(current);

        return Roles
            .Select(role => new MailMergeMatchFieldRolePlan(
                role,
                GetRoleLabel(role),
                ResolveSelectedChoice(header, current[role])))
            .ToList();
    }

    public static FieldMapping CreateResult(IReadOnlyDictionary<FieldRole, string?> selectedChoices)
    {
        ArgumentNullException.ThrowIfNull(selectedChoices);

        var mapping = new FieldMapping();
        foreach (var role in Roles)
        {
            selectedChoices.TryGetValue(role, out var selected);
            mapping[role] = NormalizeSelectedChoice(selected);
        }

        return mapping;
    }

    private static string ResolveSelectedChoice(IReadOnlyList<string> header, string? mappedColumn)
    {
        if (string.IsNullOrWhiteSpace(mappedColumn))
            return NotMatchedChoice;

        return header.FirstOrDefault(
            column => column.Equals(mappedColumn, StringComparison.OrdinalIgnoreCase)) ?? NotMatchedChoice;
    }

    private static string? NormalizeSelectedChoice(string? selectedChoice)
    {
        if (string.IsNullOrWhiteSpace(selectedChoice) ||
            selectedChoice.Equals(NotMatchedChoice, StringComparison.OrdinalIgnoreCase))
            return null;

        return selectedChoice;
    }
}
