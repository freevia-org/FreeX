using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DataValidationErrorMessages
{
    public static string BuildNumericErrorMessage(DataValidation dv, double v1, double v2)
    {
        return dv.Operator switch
        {
            DvOperator.Between => $"Value must be between {v1} and {v2}.",
            DvOperator.NotBetween => $"Value must not be between {v1} and {v2}.",
            DvOperator.Equal => $"Value must equal {v1}.",
            DvOperator.NotEqual => $"Value must not equal {v1}.",
            DvOperator.GreaterThan => $"Value must be greater than {v1}.",
            DvOperator.LessThan => $"Value must be less than {v1}.",
            DvOperator.GreaterThanOrEqual => $"Value must be greater than or equal to {v1}.",
            DvOperator.LessThanOrEqual => $"Value must be less than or equal to {v1}.",
            _ => "Invalid value."
        };
    }
}
