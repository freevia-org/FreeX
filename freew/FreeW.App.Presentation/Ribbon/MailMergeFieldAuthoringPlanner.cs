using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record MailMergeFieldInsertionPlan(
    ComplexField Field,
    string CachedLabel);

public static class MailMergeFieldAuthoringPlanner
{
    public static MailMergeFieldInsertionPlan? CreateMergeFieldPlan(string? fieldName)
    {
        var normalized = MailMerge.NormalizeMergeFieldName(fieldName ?? string.Empty);
        if (normalized.Length == 0)
            return null;

        return CreateNativeFieldPlan(
            new ComplexField(MailMerge.BuildMergeFieldInstruction(normalized)),
            normalized);
    }

    public static MailMergeFieldInsertionPlan? CreateSpecialFieldPlan(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName) ||
            !MailMerge.TryGetNativeSpecialFieldInstruction(fieldName, out var instruction))
        {
            return null;
        }

        return CreateNativeFieldPlan(new ComplexField(instruction), fieldName.Trim());
    }

    public static MailMergeFieldInsertionPlan CreateAddressBlockPlan() =>
        CreateNativeFieldPlan(
            new ComplexField(MailMerge.AddressBlockInstruction),
            "AddressBlock");

    public static MailMergeFieldInsertionPlan CreateGreetingLinePlan() =>
        CreateNativeFieldPlan(
            new ComplexField(MailMerge.GreetingLineInstruction),
            "GreetingLine");

    public static MailMergeFieldInsertionPlan CreateNativeFieldPlan(
        ComplexField field,
        string displayLabel)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new MailMergeFieldInsertionPlan(
            field,
            $"{MailMerge.FieldOpen}{displayLabel}{MailMerge.FieldClose}");
    }
}
