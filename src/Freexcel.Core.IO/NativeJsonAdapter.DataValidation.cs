using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static DataValidation? TryLoadDataValidation(DataValidationDto? validationDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(validationDto?.AppliesTo))
            return null;
        if (!IsSupportedDataValidation(validationDto))
            return null;

        try
        {
            return new DataValidation
            {
                AppliesTo = GridRange.Parse(validationDto.AppliesTo, sheetId),
                Type = validationDto.Type,
                Operator = validationDto.Operator,
                Formula1 = validationDto.Formula1,
                Formula2 = validationDto.Formula2,
                AllowBlank = validationDto.AllowBlank,
                ShowDropdown = validationDto.ShowDropdown,
                AlertStyle = validationDto.AlertStyle,
                ShowInputMessage = validationDto.ShowInputMessage,
                ShowErrorMessage = validationDto.ShowErrorMessage,
                ErrorTitle = validationDto.ErrorTitle,
                ErrorMessage = validationDto.ErrorMessage,
                PromptTitle = validationDto.PromptTitle,
                PromptMessage = validationDto.PromptMessage,
                NativeAttributes = validationDto.NativeAttributes,
                NativeChildXmls = validationDto.NativeChildXmls,
                NativeContainerAttributes = validationDto.NativeContainerAttributes,
                NativeContainerChildXmls = validationDto.NativeContainerChildXmls
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DataValidationDto ToDataValidationDto(DataValidation validation) => new()
    {
        AppliesTo = validation.AppliesTo.ToString(),
        Type = validation.Type,
        Operator = validation.Operator,
        Formula1 = validation.Formula1,
        Formula2 = validation.Formula2,
        AllowBlank = validation.AllowBlank,
        ShowDropdown = validation.ShowDropdown,
        AlertStyle = validation.AlertStyle,
        ShowInputMessage = validation.ShowInputMessage,
        ShowErrorMessage = validation.ShowErrorMessage,
        ErrorTitle = validation.ErrorTitle,
        ErrorMessage = validation.ErrorMessage,
        PromptTitle = validation.PromptTitle,
        PromptMessage = validation.PromptMessage,
        NativeAttributes = validation.NativeAttributes is null ? null : new Dictionary<string, string>(validation.NativeAttributes),
        NativeChildXmls = validation.NativeChildXmls is null ? null : [.. validation.NativeChildXmls],
        NativeContainerAttributes = validation.NativeContainerAttributes is null ? null : new Dictionary<string, string>(validation.NativeContainerAttributes),
        NativeContainerChildXmls = validation.NativeContainerChildXmls is null ? null : [.. validation.NativeContainerChildXmls]
    };

    private static bool IsSupportedDataValidation(DataValidation validation) =>
        Enum.IsDefined(validation.Type) &&
        Enum.IsDefined(validation.Operator) &&
        Enum.IsDefined(validation.AlertStyle);

    private static bool IsSupportedDataValidation(DataValidationDto validation) =>
        Enum.IsDefined(validation.Type) &&
        Enum.IsDefined(validation.Operator) &&
        Enum.IsDefined(validation.AlertStyle);
}
