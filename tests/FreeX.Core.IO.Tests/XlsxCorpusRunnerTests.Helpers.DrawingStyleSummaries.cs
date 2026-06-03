using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    private static TextBoxSummary CaptureTextBoxSummary(TextBoxModel textBox) =>
        new(
            textBox.Name ?? "",
            textBox.Text,
            textBox.Title ?? "",
            textBox.AltText ?? "",
            textBox.Anchor.Row,
            textBox.Anchor.Col,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.IsVisible,
            textBox.FillColor,
            textBox.OutlineColor,
            textBox.FillThemeColor,
            textBox.OutlineThemeColor);

    private static DrawingShapeSummary CaptureDrawingShapeSummary(DrawingShapeModel shape) =>
        new(
            shape.Name ?? "",
            shape.Kind,
            shape.Title ?? "",
            shape.AltText ?? "",
            shape.Anchor.Row,
            shape.Anchor.Col,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.IsVisible,
            shape.FillColor,
            shape.OutlineColor,
            shape.GradientFillEndColor,
            shape.FillThemeColor,
            shape.OutlineThemeColor,
            shape.HasShadowEffect);

    private static PictureSummary CapturePictureSummary(PictureModel picture) =>
        new(
            picture.Name ?? "",
            picture.Kind,
            picture.Title ?? "",
            picture.AltText ?? "",
            picture.Anchor.Row,
            picture.Anchor.Col,
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.IsVisible,
            picture.ContentType ?? "",
            picture.ImageBytes?.Length ?? 0,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.IsLinkedToSourceRange,
            picture.LinkedSourceRange is { } linkedSourceRange ? ToRangeSummary(linkedSourceRange) : null,
            picture.LinkedSourceSheetName ?? "",
            picture.SourceRowCount,
            picture.SourceColumnCount,
            picture.Cells
                .OrderBy(cell => cell.RowOffset)
                .ThenBy(cell => cell.ColumnOffset)
                .Select(cell => new PictureCellSummary(cell.RowOffset, cell.ColumnOffset, cell.Text))
                .ToArray());

    private static ConditionalFormatSummary CaptureConditionalFormatSummary(ConditionalFormat format) =>
        new(
            format.RuleType,
            format.Priority,
            format.Operator,
            format.Value1 ?? "",
            format.Value2 ?? "",
            CaptureStyleSummary(format.FormatIfTrue),
            format.MinColor,
            format.MidColor,
            format.MaxColor,
            format.UseThreeColorScale,
            format.MinThresholdType,
            format.MinThresholdValue ?? "",
            format.MidThresholdType,
            format.MidThresholdValue ?? "",
            format.MaxThresholdType,
            format.MaxThresholdValue ?? "",
            format.DataBarColor,
            format.DataBarMinThresholdType,
            format.DataBarMinThresholdValue ?? "",
            format.DataBarMaxThresholdType,
            format.DataBarMaxThresholdValue ?? "",
            format.DataBarShowValue,
            format.DataBarMinLength,
            format.DataBarMaxLength,
            format.DataBarGradient,
            format.DataBarBorder,
            format.DataBarAxisPosition ?? "",
            format.DataBarAxisColor,
            format.DataBarNegativeFillColor,
            format.DataBarNegativeBorderColor,
            format.AboveAverage,
            format.FormulaText ?? "",
            format.IconSetStyle ?? "",
            format.IconSetShowValue,
            format.IconSetReverse,
            format.IconSetThresholds.Select(threshold => new ConditionalFormatThresholdSummary(threshold.Type, threshold.Value ?? "")).ToArray(),
            format.TopBottomRank,
            format.TopBottomPercent,
            format.TextRuleText ?? "",
            format.DateOccurringPeriod ?? "",
            format.StopIfTrue,
            ToRangeSummary(format.AppliesTo));

    private static CellStyleSummary? CaptureStyleSummary(CellStyle? style) =>
        style is null
            ? null
            : new(
                style.FontName,
                style.FontSize,
                style.Bold,
                style.Italic,
                style.Underline,
                style.Strikethrough,
                style.FontColor,
                style.FillColor,
                NormalizeFillPatternStyle(style),
                style.FillPatternColor,
                style.NumberFormat);

    private static CellFillPatternStyle NormalizeFillPatternStyle(CellStyle style) =>
        style.FillColor.HasValue && style.FillPatternStyle == CellFillPatternStyle.None
            ? CellFillPatternStyle.Solid
            : style.FillPatternStyle;

}
