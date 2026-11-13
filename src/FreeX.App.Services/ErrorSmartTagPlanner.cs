using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum ErrorSmartTagAction
{
    None,
    ConvertToNumber,
    ShowCalculationSteps,
    HelpOnThisError,
    IgnoreError,
    EditInFormulaBar,
    ErrorCheckingOptions,
}

/// <summary>One row of the error smart tag's menu: the error's name (a disabled title), a separator or a command.</summary>
public sealed record ErrorSmartTagMenuItem(
    string Header,
    ErrorSmartTagAction Action,
    bool IsTitle = false,
    bool IsSeparator = false)
{
    public static ErrorSmartTagMenuItem Separator { get; } = new(string.Empty, ErrorSmartTagAction.None, IsSeparator: true);
}

/// <summary>
/// Excel's error smart tag: the warning button beside an active cell that shows the green error
/// indicator, and the menu it opens (error name, the fix for that error, Help, Ignore Error,
/// Edit in Formula Bar, Error Checking Options).
/// </summary>
public static class ErrorSmartTagPlanner
{
    public const string ButtonAutomationId = "ErrorSmartTagButton";
    public const string ButtonToolTip = "Error Checking";
    public const double ButtonSize = 18;

    /// <summary>Excel's name for the error, the first (disabled) row of the menu.</summary>
    public static string Title(string errorCode) => errorCode switch
    {
        FormulaAuditingService.NumberStoredAsTextErrorCode => "Number Stored as Text",
        FormulaAuditingService.TwoDigitYearTextDateErrorCode => "Text Date with 2-Digit Year",
        FormulaAuditingService.FormulaStoredAsTextErrorCode => "Formula Stored as Text",
        FormulaAuditingService.InconsistentFormulaErrorCode => "Inconsistent Formula",
        FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode => "Inconsistent Calculated Column Formula",
        FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode => "Formula Omits Adjacent Cells",
        FormulaAuditingService.UnlockedFormulaCellsErrorCode => "Unprotected Formula",
        FormulaAuditingService.FormulaRefersToBlankCellsErrorCode => "Formula Refers to Empty Cells",
        _ when errorCode == ErrorValue.DivByZero.Code => "Divide by Zero Error",
        _ when errorCode == ErrorValue.Value.Code => "Error in Value",
        _ when errorCode == ErrorValue.Ref.Code => "Invalid Cell Reference Error",
        _ when errorCode == ErrorValue.Name.Code => "Invalid Name Error",
        _ when errorCode == ErrorValue.NA.Code => "Value Not Available Error",
        _ when errorCode == ErrorValue.Num.Code => "Invalid Number Error",
        _ when errorCode == ErrorValue.Null.Code => "Null Error",
        _ when errorCode == ErrorValue.Spill.Code => "Spill Range Isn't Blank",
        _ when errorCode == ErrorValue.Calc.Code => "Calculation Error",
        _ => "Error",
    };

    public static IReadOnlyList<ErrorSmartTagMenuItem> BuildMenu(string errorCode)
    {
        var items = new List<ErrorSmartTagMenuItem> { new(Title(errorCode), ErrorSmartTagAction.None, IsTitle: true) };
        if (string.Equals(errorCode, FormulaAuditingService.NumberStoredAsTextErrorCode, StringComparison.Ordinal))
            items.Add(new("Convert to Number", ErrorSmartTagAction.ConvertToNumber));
        else if (errorCode.StartsWith('#'))
            items.Add(new("Show Calculation Steps...", ErrorSmartTagAction.ShowCalculationSteps));

        items.Add(new("Help on this Error", ErrorSmartTagAction.HelpOnThisError));
        items.Add(new("Ignore Error", ErrorSmartTagAction.IgnoreError));
        items.Add(new("Edit in Formula Bar", ErrorSmartTagAction.EditInFormulaBar));
        items.Add(ErrorSmartTagMenuItem.Separator);
        items.Add(new("Error Checking Options...", ErrorSmartTagAction.ErrorCheckingOptions));
        return items;
    }
}
