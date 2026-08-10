using System.Globalization;
using System.Linq;
using FreeX.App.Presentation.Localization;
using FreeX.App.Presentation.Options;

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
/// the shared parser entry points below; the projection reuses the <see cref="AppOptions"/> normalizers.
/// </para>
/// </summary>
public static class OptionsDialogPlanner
{
    public static ValidationPresentationDescriptor<OptionsValidationFocusTarget> DescribeInputError(
        OptionsInputError error,
        OptionsValidationTextProfile profile) =>
        OptionsValidationPresentationPlanner.DescribeGeneralInput(
            error == OptionsInputError.InvalidFontSize,
            profile);

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
    public const double EaseSectionHeaderTopMargin = 0;
    public const double EaseSectionHeaderBottomMargin = 0;
    public const double EaseSectionRuleTopMargin = 6;
    public const double EaseSectionRuleBottomMargin = 13;
    public const double EaseCheckBoxBottomMargin = 6;
    public const double EaseCheckBoxHeight = 15;
    public const double LanguageFieldWidth = 240;
    public const double LanguageSectionTopMargin = 0;
    public const double LanguageSectionBottomMargin = 14;
    public const double LanguageFieldBottomMargin = 9;
    public const double LanguageDescriptionTopMargin = 4;
    public const double AddInsSectionHeaderTopMargin = 0;
    public const double AddInsSectionHeaderBottomMargin = 0;
    public const double AddInsSectionRuleTopMargin = 5;
    public const double AddInsSectionRuleBottomMargin = 12;
    public const double AddInsDescriptionBottomMargin = 8;
    public const double AddInsGoButtonWidth = 70;
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

    public sealed record OptionsDialogSupplementalInput(
        bool EnableFillHandleAndCellDragAndDrop,
        bool EnableAutoCompleteForCellValues,
        bool QuickAccessToolbarBelowRibbon,
        IReadOnlyList<string> QuickAccessToolbarCommands,
        IReadOnlyList<string> SpellCheckCustomDictionaryWords,
        bool? FormulaBarExpanded = null);

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
            GenerateGetPivotData = existing.GenerateGetPivotData,
            EnableAutoCompleteForCellValues = existing.EnableAutoCompleteForCellValues,
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
            PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(existing.PdfExportLanguage),
        };

        options.NormalizePersistedCollections();
        return options;
    }

    public static AppOptions Project(
        AppOptions existing,
        OptionsDialogInput input,
        OptionsDialogSupplementalInput supplemental)
    {
        ArgumentNullException.ThrowIfNull(supplemental);
        ArgumentNullException.ThrowIfNull(supplemental.QuickAccessToolbarCommands);
        ArgumentNullException.ThrowIfNull(supplemental.SpellCheckCustomDictionaryWords);

        var options = Project(existing, input);
        options.EnableFillHandleAndCellDragAndDrop = supplemental.EnableFillHandleAndCellDragAndDrop;
        options.EnableAutoCompleteForCellValues = supplemental.EnableAutoCompleteForCellValues;
        options.QuickAccessToolbarBelowRibbon = supplemental.QuickAccessToolbarBelowRibbon;
        options.QuickAccessToolbarCommands = supplemental.QuickAccessToolbarCommands.ToList();
        options.SpellCheckCustomDictionaryWords = supplemental.SpellCheckCustomDictionaryWords.ToList();
        if (supplemental.FormulaBarExpanded is { } formulaBarExpanded)
            options.FormulaBarExpanded = input.ShowFormulaBar && formulaBarExpanded;
        options.NormalizePersistedCollections();
        return options;
    }

    /// <summary>
    /// Reload-and-diff-merge for the Options dialog's OK handler. <see cref="AppOptions"/> (options.json)
    /// is a single whole-document store shared by every open window/process -- the Avalonia shell has no
    /// single-instance enforcement, and View &gt; New Window opens an independent <c>MainWindow</c> that
    /// loads its own <see cref="AppOptions"/> snapshot. <see cref="Project"/> builds <paramref name="edited"/>
    /// purely from <paramref name="openTimeSnapshot"/> (the snapshot this dialog loaded when it opened), so
    /// saving <paramref name="edited"/> as the whole document would silently discard any change another
    /// window (or this window's own right-click "Add to Quick Access Toolbar"/"Customize Status Bar" menus,
    /// which already reload-before-mutate) persisted while this dialog was open (last-writer-wins / lost
    /// update). Instead, reload the freshest on-disk state (<paramref name="freshFromDisk"/>) and apply each
    /// field from <paramref name="edited"/> onto it only when that field actually differs from
    /// <paramref name="openTimeSnapshot"/> -- i.e. only when the user actually changed it in this dialog
    /// session. A field this dialog exposes no control for at all (status-bar visibility toggles,
    /// PdfExportLanguage, ...) is always equal between <paramref name="edited"/> and
    /// <paramref name="openTimeSnapshot"/>, so it is left alone and keeps whatever is freshest on disk
    /// instead of reverting to this window's stale value. Mirrors the WPF host's OK handler
    /// (<c>OptionsDialog.xaml.cs</c>, "Reload the current on-disk options immediately before saving...").
    /// </summary>
    public static AppOptions MergeOntoFreshLoad(AppOptions freshFromDisk, AppOptions openTimeSnapshot, AppOptions edited)
    {
        ArgumentNullException.ThrowIfNull(freshFromDisk);
        ArgumentNullException.ThrowIfNull(openTimeSnapshot);
        ArgumentNullException.ThrowIfNull(edited);

        var merged = freshFromDisk;

        if (edited.DefaultFontName != openTimeSnapshot.DefaultFontName) merged.DefaultFontName = edited.DefaultFontName;
        if (edited.DefaultFontSize != openTimeSnapshot.DefaultFontSize) merged.DefaultFontSize = edited.DefaultFontSize;
        if (edited.DefaultSheetCount != openTimeSnapshot.DefaultSheetCount) merged.DefaultSheetCount = edited.DefaultSheetCount;
        if (edited.UserName != openTimeSnapshot.UserName) merged.UserName = edited.UserName;
        if (edited.CollapseRibbonAutomatically != openTimeSnapshot.CollapseRibbonAutomatically) merged.CollapseRibbonAutomatically = edited.CollapseRibbonAutomatically;
        if (edited.ShowScreenTips != openTimeSnapshot.ShowScreenTips) merged.ShowScreenTips = edited.ShowScreenTips;
        if (!string.Equals(edited.AppLanguage, openTimeSnapshot.AppLanguage, StringComparison.Ordinal)) merged.AppLanguage = edited.AppLanguage;
        if (edited.AutoCalculate != openTimeSnapshot.AutoCalculate) merged.AutoCalculate = edited.AutoCalculate;
        if (edited.UseR1C1ReferenceStyle != openTimeSnapshot.UseR1C1ReferenceStyle) merged.UseR1C1ReferenceStyle = edited.UseR1C1ReferenceStyle;
        if (edited.GenerateGetPivotData != openTimeSnapshot.GenerateGetPivotData) merged.GenerateGetPivotData = edited.GenerateGetPivotData;
        if (edited.ErrorCheckingEnabled != openTimeSnapshot.ErrorCheckingEnabled) merged.ErrorCheckingEnabled = edited.ErrorCheckingEnabled;
        if (edited.ProofingIgnoreUppercase != openTimeSnapshot.ProofingIgnoreUppercase) merged.ProofingIgnoreUppercase = edited.ProofingIgnoreUppercase;
        if (edited.ProofingIgnoreNumbers != openTimeSnapshot.ProofingIgnoreNumbers) merged.ProofingIgnoreNumbers = edited.ProofingIgnoreNumbers;
        if (edited.ShowFormulaBar != openTimeSnapshot.ShowFormulaBar) merged.ShowFormulaBar = edited.ShowFormulaBar;
        if (edited.FormulaBarExpanded != openTimeSnapshot.FormulaBarExpanded) merged.FormulaBarExpanded = edited.FormulaBarExpanded;
        if (edited.MoveSelectionAfterEnter != openTimeSnapshot.MoveSelectionAfterEnter) merged.MoveSelectionAfterEnter = edited.MoveSelectionAfterEnter;
        if (edited.AfterEnterDirection != openTimeSnapshot.AfterEnterDirection) merged.AfterEnterDirection = edited.AfterEnterDirection;
        if (edited.EnableFillHandleAndCellDragAndDrop != openTimeSnapshot.EnableFillHandleAndCellDragAndDrop) merged.EnableFillHandleAndCellDragAndDrop = edited.EnableFillHandleAndCellDragAndDrop;
        if (edited.EnableAutoCompleteForCellValues != openTimeSnapshot.EnableAutoCompleteForCellValues) merged.EnableAutoCompleteForCellValues = edited.EnableAutoCompleteForCellValues;
        if (edited.ShowGridlines != openTimeSnapshot.ShowGridlines) merged.ShowGridlines = edited.ShowGridlines;
        if (edited.ShowHeadings != openTimeSnapshot.ShowHeadings) merged.ShowHeadings = edited.ShowHeadings;
        if (edited.ObjectsDisplay != openTimeSnapshot.ObjectsDisplay) merged.ObjectsDisplay = edited.ObjectsDisplay;
        if (!string.Equals(edited.DefaultFormat, openTimeSnapshot.DefaultFormat, StringComparison.Ordinal)) merged.DefaultFormat = edited.DefaultFormat;
        if (edited.QuickAccessToolbarBelowRibbon != openTimeSnapshot.QuickAccessToolbarBelowRibbon) merged.QuickAccessToolbarBelowRibbon = edited.QuickAccessToolbarBelowRibbon;
        if (!edited.QuickAccessToolbarCommands.SequenceEqual(openTimeSnapshot.QuickAccessToolbarCommands)) merged.QuickAccessToolbarCommands = edited.QuickAccessToolbarCommands;
        if (!edited.SpellCheckCustomDictionaryWords.SequenceEqual(openTimeSnapshot.SpellCheckCustomDictionaryWords)) merged.SpellCheckCustomDictionaryWords = edited.SpellCheckCustomDictionaryWords;
        if (edited.CrashAnalyticsEnabled != openTimeSnapshot.CrashAnalyticsEnabled) merged.CrashAnalyticsEnabled = edited.CrashAnalyticsEnabled;
        // Monotonic flag (never reverts once true); OR the freshest on-disk value with this dialog
        // session's raw checked state (edited.CrashAnalyticsEnabled always reflects the checkbox as-is --
        // see Project's `input.CrashAnalyticsEnabled ?? existing.CrashAnalyticsEnabled` where the Avalonia
        // caller always passes a non-null value), instead of the open-time snapshot's possibly-stale flag.
        merged.CrashAnalyticsPrompted = merged.CrashAnalyticsPrompted || edited.CrashAnalyticsEnabled;
        // StatusBarShow*, PdfExportLanguage and any other field this dialog exposes no control for are
        // deliberately left untouched: Project() always carries them straight through from
        // openTimeSnapshot, so edited == openTimeSnapshot for those fields and the diff above is always a
        // no-op -- merged already holds whatever is freshest on disk for them.

        merged.NormalizePersistedCollections();
        return merged;
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

    public static int ObjectDisplayToIndex(AppOptionsObjectDisplay display) => display switch
    {
        AppOptionsObjectDisplay.Placeholders => 1,
        AppOptionsObjectDisplay.Nothing => 2,
        _ => 0,
    };

    public static AppOptionsObjectDisplay IndexToObjectDisplay(int index) => index switch
    {
        1 => AppOptionsObjectDisplay.Placeholders,
        2 => AppOptionsObjectDisplay.Nothing,
        _ => AppOptionsObjectDisplay.All,
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
