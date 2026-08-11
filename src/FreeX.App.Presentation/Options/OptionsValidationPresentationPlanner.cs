using Free.Shared.Localization;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Options;

public enum OptionsValidationTextProfile
{
    Wpf,
    Avalonia
}

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
        bool invalidFontSize,
        OptionsValidationTextProfile profile) =>
        invalidFontSize
            ? new(
                LocalizedTextDescriptor.Resource(
                    profile == OptionsValidationTextProfile.Wpf
                        ? "Options_InvalidDefaultFontSizeMessage"
                        : "Options_InvalidFontSizeMessage"),
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
