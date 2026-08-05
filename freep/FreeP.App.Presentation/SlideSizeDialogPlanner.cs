using System.Globalization;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public enum SlideSizeDialogPreset
{
    Standard43,
    Widescreen169,
    Custom
}

public enum SlideSizeDialogUnit
{
    Inches,
    Centimeters
}

public enum SlideSizeDialogField
{
    None,
    Width,
    Height
}

public sealed record SlideSizeDialogDisplayState(
    string WidthText,
    string HeightText,
    string UnitLabel);

public sealed record SlideSizeDialogInitialState(
    SlideSizeDialogPreset Preset,
    SlideSizeDialogDisplayState Display);

public sealed record SlideSizeDialogValidationMessage(
    string Caption,
    string Message,
    SlideSizeDialogField FocusField);

public sealed record SlideSizeDialogParsePlan(
    bool IsValid,
    long CxEmu,
    long CyEmu,
    SlideSizeDialogField FocusField);

public sealed record SlideSizeDialogResultPlan(
    bool ShouldApply,
    long CxEmu,
    long CyEmu,
    SlideSizeDialogValidationMessage? Validation);

public static class SlideSizeDialogPlanner
{
    public const long EmuPerInch = DrawingMlCoordinateUnits.EmuPerInch;
    public const long EmuPerCm = 360_000L;
    public const long MinimumSlideSizeEmu = EmuPerInch / 2;

    public const string InvalidSizeCaption = "Invalid Size";
    public const string InvalidPositiveNumbersMessage =
        "Please enter valid positive numbers for width and height.";
    public const string MinimumSizeMessage =
        "Slide dimensions must be at least 0.5 inches (1.27 cm).";

    private const long Standard43CxEmu = EmuPerInch * 10;
    private const long Standard43CyEmu = EmuPerInch * 15 / 2;
    private const long Widescreen169CxEmu = EmuPerInch * 40 / 3;
    private const long Widescreen169CyEmu = Standard43CyEmu;

    private const NumberStyles UnitChangeNumberStyles =
        NumberStyles.Float | NumberStyles.AllowThousands;

    public static (long CxEmu, long CyEmu) Standard43Emu
        => (Standard43CxEmu, Standard43CyEmu);

    public static (long CxEmu, long CyEmu) Widescreen169Emu
        => (Widescreen169CxEmu, Widescreen169CyEmu);

    public static double EmuToInches(long emu) => emu / (double)EmuPerInch;

    public static double EmuToCm(long emu) => emu / (double)EmuPerCm;

    public static long InchesToEmu(double inches) => (long)Math.Round(inches * EmuPerInch);

    public static long CmToEmu(double cm) => (long)Math.Round(cm * EmuPerCm);

    public static SlideSizeDialogPreset ClassifySize(long cxEmu, long cyEmu)
    {
        if (cxEmu == Standard43CxEmu && cyEmu == Standard43CyEmu)
        {
            return SlideSizeDialogPreset.Standard43;
        }

        if (cxEmu == Widescreen169CxEmu && cyEmu == Widescreen169CyEmu)
        {
            return SlideSizeDialogPreset.Widescreen169;
        }

        return SlideSizeDialogPreset.Custom;
    }

    public static SlideSizeDialogInitialState BuildInitialState(
        long cxEmu,
        long cyEmu,
        SlideSizeDialogUnit unit,
        CultureInfo? culture = null)
    {
        return new(
            ClassifySize(cxEmu, cyEmu),
            FormatSize(cxEmu, cyEmu, unit, culture));
    }

    public static SlideSizeDialogDisplayState? BuildPresetSelectionDisplay(
        SlideSizeDialogPreset preset,
        SlideSizeDialogUnit unit,
        CultureInfo? culture = null)
    {
        return preset switch
        {
            SlideSizeDialogPreset.Standard43 => FormatSize(
                Standard43CxEmu,
                Standard43CyEmu,
                unit,
                culture),
            SlideSizeDialogPreset.Widescreen169 => FormatSize(
                Widescreen169CxEmu,
                Widescreen169CyEmu,
                unit,
                culture),
            _ => null
        };
    }

    public static SlideSizeDialogDisplayState BuildUnitChangeDisplay(
        string widthText,
        string heightText,
        SlideSizeDialogUnit oldUnit,
        SlideSizeDialogUnit newUnit,
        CultureInfo? culture = null)
    {
        if (oldUnit == newUnit)
        {
            return new(widthText, heightText, UnitLabel(newUnit));
        }

        culture ??= CultureInfo.CurrentCulture;

        if (!double.TryParse(widthText, UnitChangeNumberStyles, culture, out double width))
        {
            width = 0;
        }

        if (!double.TryParse(heightText, UnitChangeNumberStyles, culture, out double height))
        {
            height = 0;
        }

        long cxEmu = ToEmu(width, oldUnit);
        long cyEmu = ToEmu(height, oldUnit);

        return FormatSize(cxEmu, cyEmu, newUnit, culture);
    }

    public static SlideSizeDialogDisplayState BuildInputDisplay(
        string? widthText,
        string? heightText,
        SlideSizeDialogUnit unit) =>
        new(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            UnitLabel(unit));

    public static SlideSizeDialogParsePlan TryParsePositiveSize(
        string widthText,
        string heightText,
        SlideSizeDialogUnit unit,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (!double.TryParse(widthText, NumberStyles.Any, culture, out double width) || width <= 0)
        {
            return new(false, 0, 0, SlideSizeDialogField.Width);
        }

        if (!double.TryParse(heightText, NumberStyles.Any, culture, out double height) || height <= 0)
        {
            return new(false, 0, 0, SlideSizeDialogField.Height);
        }

        return new(
            true,
            ToEmu(width, unit),
            ToEmu(height, unit),
            SlideSizeDialogField.None);
    }

    public static SlideSizeDialogResultPlan BuildOkResult(
        string widthText,
        string heightText,
        SlideSizeDialogUnit unit,
        CultureInfo? culture = null)
    {
        var parse = TryParsePositiveSize(widthText, heightText, unit, culture);
        if (!parse.IsValid)
        {
            return Invalid(
                InvalidPositiveNumbersMessage,
                parse.FocusField);
        }

        if (parse.CxEmu < MinimumSlideSizeEmu || parse.CyEmu < MinimumSlideSizeEmu)
        {
            return Invalid(
                MinimumSizeMessage,
                parse.CxEmu < MinimumSlideSizeEmu
                    ? SlideSizeDialogField.Width
                    : SlideSizeDialogField.Height);
        }

        return new(true, parse.CxEmu, parse.CyEmu, null);
    }

    public static bool TryApplyResult(
        EditingSession editor,
        SlideSizeDialogResultPlan result)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (!result.ShouldApply)
        {
            return false;
        }

        editor.SetSlideSize(result.CxEmu, result.CyEmu);
        return true;
    }

    public static SlideSizeDialogDisplayState FormatSize(
        long cxEmu,
        long cyEmu,
        SlideSizeDialogUnit unit,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        string format = unit == SlideSizeDialogUnit.Inches ? "F3" : "F2";
        return new(
            FromEmu(cxEmu, unit).ToString(format, culture),
            FromEmu(cyEmu, unit).ToString(format, culture),
            UnitLabel(unit));
    }

    private static SlideSizeDialogResultPlan Invalid(
        string message,
        SlideSizeDialogField focusField)
    {
        return new(
            false,
            0,
            0,
            new SlideSizeDialogValidationMessage(
                InvalidSizeCaption,
                message,
                focusField));
    }

    private static long ToEmu(double value, SlideSizeDialogUnit unit)
        => unit == SlideSizeDialogUnit.Inches
            ? InchesToEmu(value)
            : CmToEmu(value);

    private static double FromEmu(long emu, SlideSizeDialogUnit unit)
        => unit == SlideSizeDialogUnit.Inches
            ? EmuToInches(emu)
            : EmuToCm(emu);

    private static string UnitLabel(SlideSizeDialogUnit unit)
        => unit == SlideSizeDialogUnit.Inches ? "in" : "cm";
}
