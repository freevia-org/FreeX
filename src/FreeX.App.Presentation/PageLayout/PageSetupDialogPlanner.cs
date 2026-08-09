using System.Globalization;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Renderer-neutral Page Setup dialog surface metadata. Platform shells own control construction;
/// this planner owns shared dimensions, automation contracts, and combo catalogs.
/// </summary>
public sealed record PageSetupChoicePlan<T>(IReadOnlyList<PageSetupChoice<T>> Choices, T FallbackValue)
{
    public int IndexOf(T value) =>
        PageSetupDialogModel.ChoiceIndex(Choices, value, FallbackValue);

    public T ValueAt(int selectedIndex) =>
        PageSetupDialogModel.ChoiceValue(Choices, selectedIndex, FallbackValue);
}

public sealed record PageSetupDialogOpenPlan(
    PageSetupInitialFocusTarget InitialFocusTarget,
    PageSetupValidationRoute InitialRoute);

public enum PageSetupDialogFocusTarget
{
    Orientation,
    PaperSize,
    Margins,
    LeftMargin,
    RightMargin,
    TopMargin,
    BottomMargin,
    HeaderMargin,
    FooterMargin,
    ScalePercent,
    FitPagesWide,
    FitPagesTall,
    FirstPageNumber,
    PrintQuality,
    PrintArea,
    RepeatRows,
    RepeatColumns,
    PageOrder,
    PrintErrorValue,
    PrintComments
}

public sealed record PageSetupDialogFocusPlan(
    PageSetupValidationRoute Route,
    PageSetupDialogFocusTarget Target);

public sealed record PageSetupMarginTextFields(
    string Left,
    string Right,
    string Top,
    string Bottom);

public sealed record PageSetupDialogChoiceIndexes
{
    public int Orientation { get; init; }
    public int PaperSize { get; init; }
    public int PageOrder { get; init; }
    public int PrintErrorValue { get; init; }
    public int PrintComments { get; init; }
    public int HeaderPreset { get; init; } = -1;
    public int FooterPreset { get; init; } = -1;
}

public sealed record PageSetupDialogScalingSurface
{
    public PageSetupScalingMode Mode { get; init; } = PageSetupScalingMode.AdjustToPercent;
    public string ScalePercentText { get; init; } = "100";
    public string FitToWideText { get; init; } = "1";
    public string FitToTallText { get; init; } = "1";
    public bool IsAdjustToPercent => Mode == PageSetupScalingMode.AdjustToPercent;
    public bool IsFitToPages => Mode == PageSetupScalingMode.FitToPages;
}

public sealed record PageSetupDialogSurfacePlan
{
    public PageSetupDialogFields Fields { get; init; } = new();
    public PageSetupDialogChoiceIndexes ChoiceIndexes { get; init; } = new();
    public PageSetupMarginTextFields Margins { get; init; } = new("0.5", "0.5", "0.5", "0.5");
    public PageSetupDialogScalingSurface Scaling { get; init; } = new();
    public string HeaderMarginText { get; init; } = "0.3";
    public string FooterMarginText { get; init; } = "0.3";
    public string FirstPageNumberText { get; init; } = "";
    public string PrintQualityDpiText { get; init; } = "";
    public string PrintAreaText { get; init; } = "";
    public string RepeatRowsText { get; init; } = "";
    public string RepeatColumnsText { get; init; } = "";
}

public sealed record PageSetupDialogSurfaceInput
{
    public int OrientationIndex { get; init; }
    public int PaperSizeIndex { get; init; }
    public string MarginsText { get; init; } = "";
    public string? LeftMarginText { get; init; }
    public string? RightMarginText { get; init; }
    public string? TopMarginText { get; init; }
    public string? BottomMarginText { get; init; }
    public string HeaderMarginText { get; init; } = "";
    public string FooterMarginText { get; init; } = "";
    public bool CenterHorizontally { get; init; }
    public bool CenterVertically { get; init; }
    public PageSetupScalingMode ScalingMode { get; init; } = PageSetupScalingMode.AdjustToPercent;
    public string ScalePercentText { get; init; } = "";
    public string FitToWideText { get; init; } = "";
    public string FitToTallText { get; init; } = "";
    public string FirstPageNumberText { get; init; } = "";
    public string PrintQualityDpiText { get; init; } = "";
    public string PrintAreaText { get; init; } = "";
    public string RepeatRowsText { get; init; } = "";
    public string RepeatColumnsText { get; init; } = "";
    public bool PrintGridlines { get; init; }
    public bool PrintHeadings { get; init; }
    public bool PrintBlackAndWhite { get; init; }
    public bool PrintDraftQuality { get; init; }
    public int PrintErrorValueIndex { get; init; }
    public int PrintCommentsIndex { get; init; }
    public int PageOrderIndex { get; init; }
    public WorksheetHeaderFooter Header { get; init; } = new("", "", "");
    public WorksheetHeaderFooter Footer { get; init; } = new("", "", "");
    public WorksheetHeaderFooter FirstPageHeader { get; init; } = new("", "", "");
    public WorksheetHeaderFooter FirstPageFooter { get; init; } = new("", "", "");
    public WorksheetHeaderFooter EvenPageHeader { get; init; } = new("", "", "");
    public WorksheetHeaderFooter EvenPageFooter { get; init; } = new("", "", "");
    public WorksheetHeaderFooterPictureSet HeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FirstPageHeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FirstPageFooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet EvenPageHeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet EvenPageFooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public bool DifferentFirstPage { get; init; }
    public bool DifferentOddEvenPages { get; init; }
    public bool ScaleHeaderFooterWithDocument { get; init; } = true;
    public bool AlignHeaderFooterWithMargins { get; init; } = true;
}

public sealed record PageSetupDialogValidationFocusState
{
    public bool HasSeparateMarginFields { get; init; }
    public string MarginsText { get; init; } = "";
    public string LeftMarginText { get; init; } = "";
    public string RightMarginText { get; init; } = "";
    public string TopMarginText { get; init; } = "";
    public string BottomMarginText { get; init; } = "";
    public string HeaderMarginText { get; init; } = "";
    public string FooterMarginText { get; init; } = "";
    public PageSetupScalingMode ScalingMode { get; init; } = PageSetupScalingMode.AdjustToPercent;
    public string FitToWideText { get; init; } = "";
    public string RepeatRowsText { get; init; } = "";
}

public static class PageSetupDialogPlanner
{
    public const string TitleResourceKey = "PageSetup_Title";
    public const string DialogAutomationId = "PageSetupDialog";
    public const string TabsAutomationId = "PageSetupTabs";

    public const string OrientationBoxAutomationId = "PageSetupOrientationBox";
    public const string PaperSizeBoxAutomationId = "PageSetupPaperSizeBox";
    public const string LeftMarginBoxAutomationId = "PageSetupLeftMarginBox";
    public const string RightMarginBoxAutomationId = "PageSetupRightMarginBox";
    public const string TopMarginBoxAutomationId = "PageSetupTopMarginBox";
    public const string BottomMarginBoxAutomationId = "PageSetupBottomMarginBox";
    public const string HeaderMarginBoxAutomationId = "PageSetupHeaderMarginBox";
    public const string FooterMarginBoxAutomationId = "PageSetupFooterMarginBox";
    public const string HeaderPresetBoxAutomationId = "PageSetupHeaderPresetBox";
    public const string FooterPresetBoxAutomationId = "PageSetupFooterPresetBox";
    public const string PageOrderBoxAutomationId = "PageSetupPageOrderBox";
    public const string CellErrorsBoxAutomationId = "PageSetupCellErrorsBox";
    public const string CommentsBoxAutomationId = "PageSetupCommentsBox";
    public const string ValidationTextAutomationId = "PageSetupValidationText";
    public const string OkButtonAutomationId = "PageSetupOkButton";
    public const string CancelButtonAutomationId = "PageSetupCancelButton";
    public const string PrintButtonAutomationId = "PageSetupPrintButton";
    public const string PrintPreviewButtonAutomationId = "PageSetupPrintPreviewButton";
    public const string OptionsButtonAutomationId = "PageSetupOptionsButton";

    public const double WindowWidth = 600;
    public const double WindowHeight = 560;
    public const double MinWindowWidth = 580;
    public const double MinWindowHeight = 520;
    public const double FieldMinWidth = 220;
    public const double HeaderFooterPresetMinWidth = 260;
    public const double FooterButtonMinWidth = 84;
    public const double PrintPreviewButtonMinWidth = 100;

    public static PageSetupChoicePlan<WorksheetPageOrientation> OrientationChoices { get; } =
        new(PageSetupDialogModel.OrientationChoices, WorksheetPageOrientation.Portrait);

    public static PageSetupChoicePlan<WorksheetPaperSize> PaperSizeChoices { get; } =
        new(PageSetupDialogModel.PaperSizeChoices, WorksheetPaperSize.A4);

    public static PageSetupChoicePlan<WorksheetPageOrder> PageOrderChoices { get; } =
        new(PageSetupDialogModel.PageOrderChoices, WorksheetPageOrder.DownThenOver);

    public static PageSetupChoicePlan<WorksheetPrintErrorValue> PrintErrorValueChoices { get; } =
        new(PageSetupDialogModel.PrintErrorValueChoices, WorksheetPrintErrorValue.Displayed);

    public static PageSetupChoicePlan<WorksheetPrintComments> PrintCommentChoices { get; } =
        new(PageSetupDialogModel.PrintCommentChoices, WorksheetPrintComments.None);

    public static PageSetupDialogOpenPlan PlanOpen(PageLayoutPageSetupOpenSource source) =>
        PlanOpen(ResolveInitialFocusTarget(source));

    public static PageSetupDialogOpenPlan PlanOpen(PageSetupInitialFocusTarget initialFocusTarget) =>
        new(initialFocusTarget, ResolveInitialFocusRoute(initialFocusTarget));

    public static PageSetupDialogSurfacePlan PlanSurface(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return PlanSurface(sheet, PageSetupDialogModel.FromSheet(sheet));
    }

    public static PageSetupDialogSurfacePlan PlanSurface(Sheet sheet, PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fields);

        return new PageSetupDialogSurfacePlan
        {
            Fields = fields,
            ChoiceIndexes = new PageSetupDialogChoiceIndexes
            {
                Orientation = OrientationChoices.IndexOf(fields.Orientation),
                PaperSize = PaperSizeChoices.IndexOf(fields.PaperSize),
                PageOrder = PageOrderChoices.IndexOf(fields.PageOrder),
                PrintErrorValue = PrintErrorValueChoices.IndexOf(fields.PrintErrorValue),
                PrintComments = PrintCommentChoices.IndexOf(fields.PrintComments),
                HeaderPreset = PageSetupDialogModel.HeaderFooterPresetExactIndex(
                    PageSetupDialogModel.HeaderPresetChoices,
                    fields.Header.Center),
                FooterPreset = PageSetupDialogModel.HeaderFooterPresetExactIndex(
                    PageSetupDialogModel.FooterPresetChoices,
                    fields.Footer.Center),
            },
            Margins = ParseMarginTextFields(fields.MarginsText),
            Scaling = BuildScalingSurface(fields),
            HeaderMarginText = fields.HeaderMarginText,
            FooterMarginText = fields.FooterMarginText,
            FirstPageNumberText = fields.FirstPageNumberText,
            PrintQualityDpiText = fields.PrintQualityDpiText,
            PrintAreaText = sheet.PrintArea is { } printArea
                ? PageSetupRangeSelectionFormatter.Format(
                    PageSetupRangeSelectionTarget.PrintArea,
                    printArea,
                    useR1C1ReferenceStyle: false)
                : fields.PrintAreaText,
            RepeatRowsText = sheet.PrintTitleRows is { } repeatRows
                ? PageSetupRangeSelectionFormatter.FormatRepeatRows(repeatRows, useR1C1ReferenceStyle: false)
                : fields.RepeatRowsText,
            RepeatColumnsText = sheet.PrintTitleColumns is { } repeatColumns
                ? PageSetupRangeSelectionFormatter.FormatRepeatColumns(repeatColumns, useR1C1ReferenceStyle: false)
                : fields.RepeatColumnsText,
        };
    }

    public static PageSetupDialogFields BuildFields(
        PageSetupDialogFields initial,
        PageSetupDialogSurfaceInput input)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(input);

        return initial with
        {
            Orientation = OrientationChoices.ValueAt(input.OrientationIndex),
            PaperSize = PaperSizeChoices.ValueAt(input.PaperSizeIndex),
            MarginsText = input.LeftMarginText is not null &&
                input.RightMarginText is not null &&
                input.TopMarginText is not null &&
                input.BottomMarginText is not null
                    ? BuildMarginsText(new PageSetupMarginTextFields(
                        input.LeftMarginText,
                        input.RightMarginText,
                        input.TopMarginText,
                        input.BottomMarginText))
                    : input.MarginsText,
            HeaderMarginText = input.HeaderMarginText,
            FooterMarginText = input.FooterMarginText,
            CenterHorizontally = input.CenterHorizontally,
            CenterVertically = input.CenterVertically,
            ScalingMode = input.ScalingMode,
            ScalePercentText = input.ScalePercentText,
            FitToWideText = input.FitToWideText,
            FitToTallText = input.FitToTallText,
            FirstPageNumberText = input.FirstPageNumberText,
            PrintQualityDpiText = input.PrintQualityDpiText,
            PrintAreaText = input.PrintAreaText,
            RepeatRowsText = input.RepeatRowsText,
            RepeatColumnsText = input.RepeatColumnsText,
            PrintGridlines = input.PrintGridlines,
            PrintHeadings = input.PrintHeadings,
            PrintBlackAndWhite = input.PrintBlackAndWhite,
            PrintDraftQuality = input.PrintDraftQuality,
            PrintErrorValue = PrintErrorValueChoices.ValueAt(input.PrintErrorValueIndex),
            PrintComments = PrintCommentChoices.ValueAt(input.PrintCommentsIndex),
            PageOrder = PageOrderChoices.ValueAt(input.PageOrderIndex),
            Header = input.Header,
            Footer = input.Footer,
            FirstPageHeader = input.FirstPageHeader,
            FirstPageFooter = input.FirstPageFooter,
            EvenPageHeader = input.EvenPageHeader,
            EvenPageFooter = input.EvenPageFooter,
            HeaderPictures = input.HeaderPictures.DeepClone(),
            FooterPictures = input.FooterPictures.DeepClone(),
            FirstPageHeaderPictures = input.FirstPageHeaderPictures.DeepClone(),
            FirstPageFooterPictures = input.FirstPageFooterPictures.DeepClone(),
            EvenPageHeaderPictures = input.EvenPageHeaderPictures.DeepClone(),
            EvenPageFooterPictures = input.EvenPageFooterPictures.DeepClone(),
            DifferentFirstPage = input.DifferentFirstPage,
            DifferentOddEvenPages = input.DifferentOddEvenPages,
            ScaleHeaderFooterWithDocument = input.ScaleHeaderFooterWithDocument,
            AlignHeaderFooterWithMargins = input.AlignHeaderFooterWithMargins
        };
    }

    public static string BuildMarginsText(PageSetupMarginTextFields margins)
    {
        ArgumentNullException.ThrowIfNull(margins);
        return string.Join(
            ",",
            NormalizeMarginToken(margins.Left),
            NormalizeMarginToken(margins.Right),
            NormalizeMarginToken(margins.Top),
            NormalizeMarginToken(margins.Bottom));
    }

    /// <summary>
    /// Each margin TextBox is unfiltered free text, so a comma-decimal locale (de-DE, fr-FR, es-ES, ...)
    /// naturally produces values like "1,91" -- using the very character BuildMarginsText joins the four
    /// fields with. Text that already parses under InvariantCulture (the common case: a plain '.'-decimal,
    /// or a locale whose decimal separator already is '.') is passed through byte-for-byte so formatting
    /// (e.g. a trailing ".0") is preserved exactly as typed. Only text that needs CurrentCulture to parse
    /// (matching NumericInputParser's convention used elsewhere in this dialog for the header/footer
    /// margins) gets re-emitted as an invariant token before joining, so the join/split round-trip through
    /// PageMarginInputParser never sees a locale decimal comma. Text that fails to parse under either
    /// culture is passed through unchanged so the downstream parser still reports a "not a number" error
    /// instead of a silently wrong field count.
    /// </summary>
    private static string NormalizeMarginToken(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;

        if (NumericInputParser.TryParseFiniteDouble(trimmed, CultureInfo.InvariantCulture, out _))
            return trimmed;

        return NumericInputParser.TryParseFiniteDouble(trimmed, CultureInfo.CurrentCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : trimmed;
    }

    public static WorksheetHeaderFooter ApplyHeaderPreset(WorksheetHeaderFooter header, int selectedIndex) =>
        HeaderFooterEditorPlanner.ApplyCenterPreset(
            header,
            PageSetupDialogModel.HeaderFooterPresetValue(
                PageSetupDialogModel.HeaderPresetChoices,
                selectedIndex));

    public static WorksheetHeaderFooter ApplyFooterPreset(WorksheetHeaderFooter footer, int selectedIndex) =>
        HeaderFooterEditorPlanner.ApplyCenterPreset(
            footer,
            PageSetupDialogModel.HeaderFooterPresetValue(
                PageSetupDialogModel.FooterPresetChoices,
                selectedIndex));

    public static int ResolveHeaderPresetIndex(WorksheetHeaderFooter header) =>
        PageSetupDialogModel.HeaderFooterPresetExactIndex(
            PageSetupDialogModel.HeaderPresetChoices,
            header.Center);

    public static int ResolveFooterPresetIndex(WorksheetHeaderFooter footer) =>
        PageSetupDialogModel.HeaderFooterPresetExactIndex(
            PageSetupDialogModel.FooterPresetChoices,
            footer.Center);

    public static PageSetupDialogFocusPlan PlanInitialFocus(
        PageSetupDialogOpenPlan openPlan,
        PageSetupScalingMode scalingMode)
    {
        ArgumentNullException.ThrowIfNull(openPlan);

        var target = openPlan.InitialFocusTarget switch
        {
            PageSetupInitialFocusTarget.Margins => PageSetupDialogFocusTarget.LeftMargin,
            PageSetupInitialFocusTarget.PaperSize => PageSetupDialogFocusTarget.PaperSize,
            PageSetupInitialFocusTarget.ScaleToFit => scalingMode == PageSetupScalingMode.FitToPages
                ? PageSetupDialogFocusTarget.FitPagesWide
                : PageSetupDialogFocusTarget.ScalePercent,
            PageSetupInitialFocusTarget.PrintArea => PageSetupDialogFocusTarget.PrintArea,
            PageSetupInitialFocusTarget.RepeatRows => PageSetupDialogFocusTarget.RepeatRows,
            _ => PageSetupDialogFocusTarget.Orientation,
        };

        return new PageSetupDialogFocusPlan(openPlan.InitialRoute, target);
    }

    public static PageSetupDialogFocusPlan PlanValidationFocus(
        PageSetupValidationTarget? target,
        PageSetupDialogValidationFocusState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var route = PageSetupDialogModel.GetValidationRoute(target);
        return new PageSetupDialogFocusPlan(route, target switch
        {
            PageSetupValidationTarget.Margins => ResolveMarginFocusTarget(state),
            PageSetupValidationTarget.HeaderMargin => PageSetupDialogFocusTarget.HeaderMargin,
            PageSetupValidationTarget.FooterMargin => ResolveHeaderFooterMarginFocusTarget(state),
            PageSetupValidationTarget.Scaling => ResolveScalingFocusTarget(state),
            PageSetupValidationTarget.FirstPageNumber => PageSetupDialogFocusTarget.FirstPageNumber,
            PageSetupValidationTarget.PrintQuality => PageSetupDialogFocusTarget.PrintQuality,
            PageSetupValidationTarget.PrintArea => PageSetupDialogFocusTarget.PrintArea,
            PageSetupValidationTarget.RepeatRows => PageSetupDialogFocusTarget.RepeatRows,
            PageSetupValidationTarget.RepeatColumns => ResolvePrintTitlesFocusTarget(state),
            PageSetupValidationTarget.PaperSize => PageSetupDialogFocusTarget.PaperSize,
            PageSetupValidationTarget.PageOrder => PageSetupDialogFocusTarget.PageOrder,
            PageSetupValidationTarget.PrintErrorValue => PageSetupDialogFocusTarget.PrintErrorValue,
            PageSetupValidationTarget.PrintComments => PageSetupDialogFocusTarget.PrintComments,
            _ => PageSetupDialogFocusTarget.Orientation,
        });
    }

    public static PageSetupInitialFocusTarget ResolveInitialFocusTarget(PageLayoutPageSetupOpenSource source) =>
        source switch
        {
            PageLayoutPageSetupOpenSource.CustomMargins => PageSetupInitialFocusTarget.Margins,
            PageLayoutPageSetupOpenSource.ExtendedPaperSize => PageSetupInitialFocusTarget.PaperSize,
            PageLayoutPageSetupOpenSource.ScaleToFit => PageSetupInitialFocusTarget.ScaleToFit,
            PageLayoutPageSetupOpenSource.PrintArea => PageSetupInitialFocusTarget.PrintArea,
            PageLayoutPageSetupOpenSource.PrintTitles => PageSetupInitialFocusTarget.RepeatRows,
            _ => PageSetupInitialFocusTarget.PageOrientation
        };

    public static PageSetupValidationRoute ResolveInitialFocusRoute(PageSetupInitialFocusTarget initialFocusTarget) =>
        initialFocusTarget switch
        {
            PageSetupInitialFocusTarget.Margins =>
                new(PageSetupDialogTab.Margins, PageSetupDialogField.Margins),
            PageSetupInitialFocusTarget.PaperSize =>
                new(PageSetupDialogTab.Page, PageSetupDialogField.PaperSize),
            PageSetupInitialFocusTarget.ScaleToFit =>
                new(PageSetupDialogTab.Page, PageSetupDialogField.Scaling),
            PageSetupInitialFocusTarget.PrintArea =>
                new(PageSetupDialogTab.Sheet, PageSetupDialogField.PrintArea),
            PageSetupInitialFocusTarget.RepeatRows =>
                new(PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatRows),
            _ => new(PageSetupDialogTab.Page, PageSetupDialogField.Orientation)
        };

    public static IReadOnlyList<string> ResolveChoiceLabels<T>(
        PageSetupChoicePlan<T> plan,
        Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textProvider);

        return ResolveChoiceLabels(plan.Choices, textProvider);
    }

    public static IReadOnlyList<string> ResolveChoiceLabels<T>(
        IReadOnlyList<PageSetupChoice<T>> choices,
        Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(textProvider);

        return choices.Select(choice => textProvider(choice.LabelResourceKey)).ToArray();
    }

    private static PageSetupMarginTextFields ParseMarginTextFields(string marginsText) =>
        PageMarginInputParser.TryParse(marginsText, out var margins, out _)
            ? new PageSetupMarginTextFields(
                FormatMargin(margins.Left),
                FormatMargin(margins.Right),
                FormatMargin(margins.Top),
                FormatMargin(margins.Bottom))
            : new PageSetupMarginTextFields("0.5", "0.5", "0.5", "0.5");

    private static PageSetupDialogScalingSurface BuildScalingSurface(PageSetupDialogFields fields) =>
        fields.ScalingMode == PageSetupScalingMode.AdjustToPercent
            ? new PageSetupDialogScalingSurface
            {
                Mode = PageSetupScalingMode.AdjustToPercent,
                ScalePercentText = fields.ScalePercentText,
                FitToWideText = "1",
                FitToTallText = "1",
            }
            : new PageSetupDialogScalingSurface
            {
                Mode = PageSetupScalingMode.FitToPages,
                ScalePercentText = "100",
                FitToWideText = fields.FitToWideText,
                FitToTallText = fields.FitToTallText,
            };

    private static PageSetupDialogFocusTarget ResolveMarginFocusTarget(
        PageSetupDialogValidationFocusState state)
    {
        if (!state.HasSeparateMarginFields)
            return PageSetupDialogFocusTarget.Margins;

        foreach (var (text, target) in new[]
        {
            (state.LeftMarginText, PageSetupDialogFocusTarget.LeftMargin),
            (state.RightMarginText, PageSetupDialogFocusTarget.RightMargin),
            (state.TopMarginText, PageSetupDialogFocusTarget.TopMargin),
            (state.BottomMarginText, PageSetupDialogFocusTarget.BottomMargin),
        })
        {
            if (!PageLayoutInputParser.TryParseMarginDistance(text, out _))
                return target;
        }

        return PageSetupDialogFocusTarget.LeftMargin;
    }

    private static PageSetupDialogFocusTarget ResolveHeaderFooterMarginFocusTarget(
        PageSetupDialogValidationFocusState state) =>
        PageLayoutInputParser.TryParseMarginDistance(state.HeaderMarginText, out _)
            ? PageSetupDialogFocusTarget.FooterMargin
            : PageSetupDialogFocusTarget.HeaderMargin;

    private static PageSetupDialogFocusTarget ResolveScalingFocusTarget(
        PageSetupDialogValidationFocusState state)
    {
        if (state.ScalingMode != PageSetupScalingMode.FitToPages)
            return PageSetupDialogFocusTarget.ScalePercent;

        return int.TryParse(
                   state.FitToWideText.Trim(),
                   System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var wide) &&
               wide > 0
            ? PageSetupDialogFocusTarget.FitPagesTall
            : PageSetupDialogFocusTarget.FitPagesWide;
    }

    private static PageSetupDialogFocusTarget ResolvePrintTitlesFocusTarget(
        PageSetupDialogValidationFocusState state) =>
        PageLayoutInputParser.TryParseRepeatRows(state.RepeatRowsText, out _)
            ? PageSetupDialogFocusTarget.RepeatColumns
            : PageSetupDialogFocusTarget.RepeatRows;

    private static string FormatMargin(double margin) =>
        margin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
