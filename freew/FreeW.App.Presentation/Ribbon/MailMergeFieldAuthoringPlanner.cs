using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public readonly record struct MailMergeFieldAuthoringPlan(
    string Instruction,
    string CachedLabel);

public static class MailMergeFieldAuthoringPlanner
{
    public static bool TryCreate(string? fieldName, out MailMergeFieldAuthoringPlan plan)
    {
        var normalized = MailMerge.NormalizeMergeFieldName(fieldName ?? string.Empty);
        if (normalized.Length == 0)
        {
            plan = default;
            return false;
        }

        plan = new MailMergeFieldAuthoringPlan(
            MailMerge.BuildMergeFieldInstruction(normalized),
            $"{MailMerge.FieldOpen}{normalized}{MailMerge.FieldClose}");
        return true;
    }
}
