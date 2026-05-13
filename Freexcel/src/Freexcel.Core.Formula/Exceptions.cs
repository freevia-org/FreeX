namespace Freexcel.Core.Formula;

/// <summary>
/// Exception thrown when a formula cannot be parsed or evaluated.
/// </summary>
public class FormulaParseException : Exception
{
    public FormulaParseException(string message) : base(message) { }
    public FormulaParseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when a formula encounters an evaluation error.
/// </summary>
public class FormulaEvalException : Exception
{
    public string ErrorCode { get; }

    public FormulaEvalException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
