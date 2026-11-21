using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Test hook: opens the dedicated rule dialog WPF's <c>ShowCfDialog(ruleType)</c> opens (or the Edit
    /// Formatting Rule dialog when <paramref name="existingRule"/> is supplied) with an inspection callback
    /// that runs once the dialog is open, and returns the built rule (null when cancelled).
    /// </summary>
    internal async Task<ConditionalFormat?> ShowConditionalFormatRuleDialogForTestAsync(
        string ruleType,
        ConditionalFormat? existingRule,
        Action<ConditionalFormatRuleDialogInspection> inspect)
    {
        var previous = _conditionalFormatRuleDialogInspectionCallback;
        _conditionalFormatRuleDialogInspectionCallback = inspect;
        try
        {
            return existingRule is null
                ? await ShowConditionalFormatRuleDialogAsync(ruleType)
                : await ShowConditionalFormatRuleEditorAsync(existingRule);
        }
        finally
        {
            _conditionalFormatRuleDialogInspectionCallback = previous;
        }
    }
}
