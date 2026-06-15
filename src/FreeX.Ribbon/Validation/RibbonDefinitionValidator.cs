namespace FreeX.Ribbon;

/// <summary>
/// Structural validation of a <see cref="RibbonDefinition"/>, independent of its origin
/// (typed builder or deserialized JSON). Returns diagnostics; never throws.
/// </summary>
public static class RibbonDefinitionValidator
{
    public static RibbonDiagnostics Validate(RibbonDefinition definition)
    {
        var items = new List<RibbonDiagnostic>();

        foreach (var dup in Duplicates(definition.Tabs.Select(t => t.Id)))
            items.Add(new RibbonDiagnostic("RBN001", RibbonDiagnosticSeverity.Error,
                $"Duplicate tab id '{dup}'."));

        foreach (var tab in definition.Tabs)
        {
            foreach (var dup in Duplicates(tab.Groups.Select(g => g.Id)))
                items.Add(new RibbonDiagnostic("RBN002", RibbonDiagnosticSeverity.Error,
                    $"Duplicate group id '{dup}' in tab '{tab.Id}'."));

            foreach (var group in tab.Groups)
            {
                if (!group.Sizing.SupportedVariants.Contains(RibbonAdaptiveGroupState.Full))
                    items.Add(new RibbonDiagnostic("RBN003", RibbonDiagnosticSeverity.Error,
                        $"Group '{group.Id}' must support the Full variant."));

                foreach (var dup in Duplicates(group.Controls
                             .Where(c => c is not RibbonSeparator)
                             .Select(c => c.KeyTip)
                             .Where(k => !string.IsNullOrEmpty(k))!))
                    items.Add(new RibbonDiagnostic("RBN004", RibbonDiagnosticSeverity.Warning,
                        $"Duplicate keytip '{dup}' in group '{group.Id}'."));
            }
        }

        return new RibbonDiagnostics(items);
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values.GroupBy(v => v, StringComparer.Ordinal)
              .Where(g => g.Count() > 1)
              .Select(g => g.Key);
}
