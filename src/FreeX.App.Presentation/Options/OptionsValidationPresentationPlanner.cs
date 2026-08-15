using Free.Shared.Localization;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Options;

public enum OptionsValidationFocusTarget
{
    DefaultFontSize,
    DefaultSheetCount,
    MaxIterations,
    MaxChange
}

public static class OptionsValidationPresentationPlanner
{
    public static ValidationPresentationDescriptor<OptionsValidationFocusTarget> DescribeGeneralInput(
        bool invalidFontSize) =>
        invalidFontSize
            ? new(
                LocalizedTextDescriptor.Resource("Options_InvalidDefaultFontSizeMessage"),
                OptionsValidationFocusTarget.DefaultFontSize)
            : new(
                LocalizedTextDescriptor.Resource("Options_InvalidSheetCountMessage"),
                OptionsValidationFocusTarget.DefaultSheetCount);

    public static ValidationPresentationDescriptor<OptionsValidationFocusTarget> DescribeCalculationInput(
        CalculationOptionsInputError error) =>
        error == CalculationOptionsInputError.InvalidMaxIterations
            ? new(
                LocalizedTextDescriptor.Resource("Options_InvalidMaxIterationsMessage"),
                OptionsValidationFocusTarget.MaxIterations)
            : new(
                LocalizedTextDescriptor.Resource("Options_InvalidMaxChangeMessage"),
                OptionsValidationFocusTarget.MaxChange);
}
