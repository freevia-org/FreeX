using System.Globalization;

namespace FreeX.App.Services;

/// <summary>
/// Portable parsing / validation / projection for the Options (Settings) dialog. The platform shells
/// (the WPF host's <c>OptionsDialog</c> and the Avalonia shell's Options window) collect raw user input
/// — text for the numeric fields, booleans for the toggles, an index for the enum pickers — and hand it
/// to this planner, which validates it and projects it onto a fresh <see cref="AppOptions"/>.
///
/// <para>
/// This keeps every decision (what counts as a valid font size, how the calculation-mode radio maps to
/// <see cref="AppOptions.AutoCalculate"/>, how the default-format picker index maps to the format string,
/// which fields the dialog does NOT edit and must therefore be carried over verbatim) in one shared place,
/// so the Avalonia view is pure UI and macOS inherits identical behaviour. The numeric validation mirrors
/// the host's <c>OptionsInputParser</c>; the projection reuses the <see cref="AppOptions"/> normalizers.
/// </para>
/// </summary>
public static class OptionsDialogPlanner
{
    /// <summary>The fixed outer window width used by the WPF Options dialog.</summary>
    public const double WindowWidth = 760;

    /// <summary>The fixed outer window height used by the WPF Options dialog.</summary>
    public const double WindowHeight = 560;

    /// <summary>
    /// The fixed client-frame width captured for non-Formulas Options pages in WPF parity evidence.
    /// Avalonia renders this content frame directly so both shells produce the same PNG dimensions.
    /// </summary>
    public const double CaptureWidth = 744;

    /// <summary>The fixed client-frame height captured for non-Formulas Options pages in WPF parity evidence.</summary>
    public const double CaptureHeight = 520.5;

    /// <summary>The fixed client-frame height captured for the taller Formulas Options page.</summary>
    public const double FormulasCaptureHeight = 776.5;

    // Shared WPF/Avalonia frame and Advanced-page metrics.
    public const double CategoryColumnWidth = 220;
    public const double CategoryTopMargin = 8;
    public const double CategoryItemHorizontalPadding = 16;
    public const double CategoryItemVerticalPadding = 9;
    public const double CategoryItemHeight = 37.36;
    public const double ContentPaddingHorizontal = 28;
    public const double ContentPaddingVertical = 20;
    public const double FooterPaddingHorizontal = 16;
    public const double FooterPaddingVertical = 10;
    public const double FooterHeight = 46;
    public const double FooterButtonWidth = 80;
    public const double ButtonHeight = 26;
    public const double ControlHeight = 24;
    public const double GeneralContentWidth = CaptureWidth - CategoryColumnWidth - (ContentPaddingHorizontal * 2);
    public const double GeneralLabelWidth = 230;
    public const double GeneralFontFieldWidth = 200;
    public const double GeneralSmallFieldWidth = 80;
    public const double GeneralFieldSpacing = 0;
    public const double GeneralDescriptionBottomMargin = 18;
    public const double GeneralSectionBottomMargin = 12;
    public const double GeneralSectionTopMargin = 18;
    public const double GeneralCheckBoxHeight = 18;
    public const double GeneralFieldBottomMargin = 9;
    public const double GeneralUserNameBottomMargin = 6;
    public const double LanguageFieldWidth = 240;
    public const double LanguageSectionTopMargin = 0;
    public const double LanguageSectionBottomMargin = 14;
    public const double LanguageFieldBottomMargin = 9;
    public const double LanguageDescriptionTopMargin = 4;
    public const double ProofingContentWidth = CaptureWidth - CategoryColumnWidth - (ContentPaddingHorizontal * 2);
    public const double ProofingWordsListHeight = 108;
    public const double ProofingAddWordLabelWidth = 94;
    public const double ProofingAddWordButtonWidth = 78;
    public const double ProofingRemoveWordButtonWidth = 92;
    public const double ProofingClearWordsButtonWidth = 82;
    public const double ProofingAutoCorrectButtonWidth = 150;
    public const double AdvancedDirectionLeftMargin = 18;
    public const double AdvancedDirectionLabelWidth = 160;
    public const double AdvancedDirectionControlWidth = 140;
    public const double AdvancedObjectsLabelWidth = 230;
    public const double AdvancedObjectsControlWidth = 220;
    public const double AdvancedMoveAfterEnterBottomMargin = 8;
    public const double AdvancedDirectionBottomMargin = 9;
    public const double AdvancedDisabledFillHandleBottomMargin = 6;
    public const double AdvancedAutoCompleteBottomMargin = 6;
    public const double AdvancedDisplaySectionTopMargin = 18;
    public const double AdvancedGridlinesBottomMargin = 6;
    public const double AdvancedHeadingsBottomMargin = 8;
    public const double AdvancedObjectsBottomMargin = 9;

    /// <summary>Why a numeric Options field could not be parsed.</summary>
    public enum OptionsInputError
    {
        None,
        InvalidFontSize,
        InvalidSheetCount,
    }

    /// <summary>The validated, ready-to-collect input the Options dialog gathers from the user.</summary>
    public sealed record OptionsDialogInput(
        string DefaultFontName,
        int DefaultFontSize,
        int DefaultSheetCount,
        string UserName,
        bool AutoCalculate,
        bool UseR1C1ReferenceStyle,
        bool ErrorCheckingEnabled,
        bool ProofingIgnoreUppercase,
        bool ProofingIgnoreNumbers,
        bool ShowFormulaBar,
        bool ShowGridlines,
        bool ShowHeadings,
        string DefaultFormat,
        bool ShowScreenTips,
        bool MoveSelectionAfterEnter,
        AppOptionsEnterDirection AfterEnterDirection,
        AppOptionsObjectDisplay? ObjectsDisplay = null,
        bool? CollapseRibbonAutomatically = null,
        string? AppLanguage = null,
        bool? CrashAnalyticsEnabled = null);

    /// <summary>Font names offered in the Options dialog's default-font picker (parity with the WPF host).</summary>
    public static IReadOnlyList<string> FontNames { get; } =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    /// <summary>Font sizes offered in the Options dialog's default-size picker (parity with the WPF host).</summary>
    public static IReadOnlyList<string> FontSizes { get; } =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public static bool TryParseDefaultFontSize(string? input, out int fontSize)
    {
        fontSize = 0;
        if (!int.TryParse((input ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0
            || parsed > AppOptions.MaxDefaultFontSize)
        {
            return false;
        }

        fontSize = parsed;
        return true;
    }

    public static bool TryParseDefaultSheetCount(string? input, out int sheetCount)
    {
        sheetCount = 0;
        if (!int.TryParse((input ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < AppOptions.MinDefaultSheetCount
            || parsed > AppOptions.MaxDefaultSheetCount)
        {
            return false;
        }

        sheetCount = parsed;
        return true;
    }

    /// <summary>
    /// Validates the raw numeric fields and assembles an <see cref="OptionsDialogInput"/>. Booleans and the
    /// already-resolved string fields are passed through; only the two numeric text fields can fail.
    /// </summary>
    public static bool TryBuildInput(
        string? defaultFontName,
        string? defaultFontSizeText,
        string? defaultSheetCountText,
        string? userName,
        bool autoCalculate,
        bool useR1C1ReferenceStyle,
        bool errorCheckingEnabled,
        bool proofingIgnoreUppercase,
        bool proofingIgnoreNumbers,
        bool showFormulaBar,
        bool showGridlines,
        bool showHeadings,
        string? defaultFormat,
        bool showScreenTips,
        bool moveSelectionAfterEnter,
        AppOptionsEnterDirection afterEnterDirection,
        out OptionsDialogInput input,
        out OptionsInputError error,
        AppOptionsObjectDisplay? objectsDisplay = null,
        bool? collapseRibbonAutomatically = null,
        string? appLanguage = null,
        bool? crashAnalyticsEnabled = null)
    {
        input = null!;

        if (!TryParseDefaultFontSize(defaultFontSizeText, out var fontSize))
        {
            error = OptionsInputError.InvalidFontSize;
            return false;
        }

        if (!TryParseDefaultSheetCount(defaultSheetCountText, out var sheetCount))
        {
            error = OptionsInputError.InvalidSheetCount;
            return false;
        }

        error = OptionsInputError.None;
        input = new OptionsDialogInput(
            AppOptions.NormalizeDefaultFontName(defaultFontName),
            fontSize,
            sheetCount,
            AppOptions.NormalizeUserName(userName),
            autoCalculate,
            useR1C1ReferenceStyle,
            errorCheckingEnabled,
            proofingIgnoreUppercase,
            proofingIgnoreNumbers,
            showFormulaBar,
            showGridlines,
            showHeadings,
            AppOptions.NormalizeDefaultFormat(defaultFormat),
            showScreenTips,
            moveSelectionAfterEnter,
            afterEnterDirection,
            objectsDisplay,
            collapseRibbonAutomatically,
            appLanguage,
            crashAnalyticsEnabled);
        return true;
    }

    /// <summary>
    /// Projects validated dialog input onto a fresh <see cref="AppOptions"/>, carrying over every field the
    /// dialog does not surface (status-bar layout, language, custom dictionary, quick-access toolbar, PDF
    /// export language, …) from <paramref name="existing"/> so saving the dialog never clears
    /// settings the user could not see. The result is normalized and ready to persist via
    /// <see cref="AppOptionsStore.SaveToPath"/>.
    /// </summary>
    public static AppOptions Project(AppOptions existing, OptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(input);

        var options = new AppOptions
        {
            // Dialog-managed fields.
            DefaultFontName = input.DefaultFontName,
            DefaultFontSize = input.DefaultFontSize,
            DefaultSheetCount = input.DefaultSheetCount,
            UserName = input.UserName,
            ShowScreenTips = input.ShowScreenTips,
            AutoCalculate = input.AutoCalculate,
            UseR1C1ReferenceStyle = input.UseR1C1ReferenceStyle,
            ErrorCheckingEnabled = input.ErrorCheckingEnabled,
            ProofingIgnoreUppercase = input.ProofingIgnoreUppercase,
            ProofingIgnoreNumbers = input.ProofingIgnoreNumbers,
            ShowFormulaBar = input.ShowFormulaBar,
            ShowGridlines = input.ShowGridlines,
            ShowHeadings = input.ShowHeadings,
            DefaultFormat = input.DefaultFormat,
            MoveSelectionAfterEnter = input.MoveSelectionAfterEnter,
            AfterEnterDirection = input.AfterEnterDirection,
            ObjectsDisplay = input.ObjectsDisplay ?? existing.ObjectsDisplay,

            // Remaining values are carried over because this dialog does not surface them.
            CollapseRibbonAutomatically = input.CollapseRibbonAutomatically ?? existing.CollapseRibbonAutomatically,
            AppLanguage = input.AppLanguage ?? existing.AppLanguage,
            SpellCheckCustomDictionaryWords = existing.SpellCheckCustomDictionaryWords,
            FormulaBarExpanded = existing.FormulaBarExpanded,
            StatusBarShowCellMode = existing.StatusBarShowCellMode,
            StatusBarShowEndMode = existing.StatusBarShowEndMode,
            StatusBarShowSelectionMode = existing.StatusBarShowSelectionMode,
            StatusBarShowPageNumber = existing.StatusBarShowPageNumber,
            StatusBarShowAverage = existing.StatusBarShowAverage,
            StatusBarShowCount = existing.StatusBarShowCount,
            StatusBarShowNumericalCount = existing.StatusBarShowNumericalCount,
            StatusBarShowMinimum = existing.StatusBarShowMinimum,
            StatusBarShowMaximum = existing.StatusBarShowMaximum,
            StatusBarShowSum = existing.StatusBarShowSum,
            StatusBarShowViewShortcuts = existing.StatusBarShowViewShortcuts,
            StatusBarShowZoom = existing.StatusBarShowZoom,
            StatusBarShowZoomSlider = existing.StatusBarShowZoomSlider,
            QuickAccessToolbarBelowRibbon = existing.QuickAccessToolbarBelowRibbon,
            QuickAccessToolbarCommands = existing.QuickAccessToolbarCommands,
            CrashAnalyticsEnabled = input.CrashAnalyticsEnabled ?? existing.CrashAnalyticsEnabled,
            CrashAnalyticsPrompted = existing.CrashAnalyticsPrompted || input.CrashAnalyticsEnabled == true,
            PdfExportLanguage = existing.PdfExportLanguage,
        };

        options.NormalizePersistedCollections();
        return options;
    }

    /// <summary>
    /// Resolves the picker index (0-based) for a default-format choice. Index 0 is the .xlsx default,
    /// index 1 is the native FreeX (.fxl) format.
    /// </summary>
    public static int DefaultFormatToIndex(string? format) =>
        string.Equals(
            AppOptions.NormalizeDefaultFormat(format),
            AppOptions.FreeXWorkbookDefaultFormat,
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    /// <summary>Maps a default-format picker index back to its format string.</summary>
    public static string IndexToDefaultFormat(int index) =>
        index == 1 ? AppOptions.FreeXWorkbookDefaultFormat : AppOptions.XlsxDefaultFormat;

    /// <summary>
    /// Maps an <see cref="AppOptionsEnterDirection"/> to its 0-based index in the Advanced tab's
    /// "After pressing Enter, move selection" direction picker (Down, Right, Up, Left — matching the
    /// WPF host's OptionsDialog).
    /// </summary>
    public static int AfterEnterDirectionToIndex(AppOptionsEnterDirection direction) => direction switch
    {
        AppOptionsEnterDirection.Right => 1,
        AppOptionsEnterDirection.Up => 2,
        AppOptionsEnterDirection.Left => 3,
        _ => 0,
    };

    /// <summary>Maps an Advanced-tab direction picker index back to its <see cref="AppOptionsEnterDirection"/>.</summary>
    public static AppOptionsEnterDirection IndexToAfterEnterDirection(int index) => index switch
    {
        1 => AppOptionsEnterDirection.Right,
        2 => AppOptionsEnterDirection.Up,
        3 => AppOptionsEnterDirection.Left,
        _ => AppOptionsEnterDirection.Down,
    };

    /// <summary>Resolves the default-font picker index, falling back to Calibri when the saved font is custom.</summary>
    public static int DefaultFontToIndex(string? fontName)
    {
        var normalized = AppOptions.NormalizeDefaultFontName(fontName);
        for (var index = 0; index < FontNames.Count; index++)
        {
            if (string.Equals(FontNames[index], normalized, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        for (var index = 0; index < FontNames.Count; index++)
        {
            if (string.Equals(FontNames[index], AppOptions.DefaultFontNameFallback, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }
}
